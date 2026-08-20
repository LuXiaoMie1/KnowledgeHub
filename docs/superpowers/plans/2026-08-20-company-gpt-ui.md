# 「公司專用 GPT」改版 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 KnowledgeHub 改成公司內部 ChatGPT：對話為主角、可保存切換、web/Teams 共用後端對話、Markdown 渲染、乾淨視覺＋RWD。

**Architecture:** 後端新增 Conversation/ConversationMessage 表為唯一事實來源，`POST /api/conversations/messages` 取代舊 `/api/chat`（後端自載歷史、SSE 串流、自動落庫）；Teams bot 接同一套 repository 實現多輪。前端引入 vue-router 重構為 `/chat`＋`/documents` 兩頁，ChatGPT 式版面。

**Tech Stack:** ASP.NET Core (net10)、EF Core（Azure SQL）、Semantic Kernel、Bot Framework、Vue 3＋Vite＋Tailwind v4、vue-router、markdown-it＋DOMPurify。

**Spec:** `docs/superpowers/specs/2026-08-20-company-gpt-ui-design.md`

## Global Constraints

- bot 安全隔離**不可動**：bot 一律用 `"bot"` keyed 服務（AllDepartmentsScope、無 EmailPlugin），見 `KnowledgeHubBotHandler` 類別註解
- 歷史上限沿用：近 10 則訊息（`MaxHistoryTurns = 10`）、單則 4000 字（`MaxContentLength = 4000`）
- 不動 RAG 檢索管線（RetrievalPlugin／ChunkRepository／embedding）
- 整合測試（`[Trait("Category","Integration")]`）自建自清資料，不留殘留；連線走 user-secrets id `3fc8ee2a-3351-4410-a176-d589385e97f1`
- 單元測試斷言業務值；註解、UI 文案一律繁體中文；命名與錯誤處理照同檔既有慣例
- commit 訊息格式照 repo 慣例（`feat:`／`chore:`…，結尾 `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`）
- **開工前置**：工作樹上有未提交的檢索門檻改動（RetrievalOptions 等 6 檔），先以 `feat: RAG 檢索加相似度門檻（實測 0.38）` 單獨 commit，再開始 Task 1
- 測試指令：單元 `dotnet test backend/KnowledgeHub.Tests --filter "Category!=Integration" --nologo`；整合另加 `--filter "Category=Integration"`（需 Azure SQL 醒著，冷啟 30–60s）

---

## Phase 1：後端資料層與 API

### Task 1: Conversation 實體＋DbContext＋migration

**Files:**
- Create: `backend/KnowledgeHub.Core/Entities/Conversation.cs`
- Create: `backend/KnowledgeHub.Core/Entities/ConversationMessage.cs`
- Create: `backend/KnowledgeHub.Core/ConversationChannels.cs`
- Modify: `backend/KnowledgeHub.Infrastructure/KnowledgeHubDbContext.cs`

**Interfaces:**
- Produces: `Conversation`／`ConversationMessage` 實體、`ConversationChannels.Web`/`Teams` 常數、DbContext 的 `Conversations`/`ConversationMessages` DbSet——後續所有任務依賴

- [ ] **Step 1: 建實體與常數**

`backend/KnowledgeHub.Core/Entities/Conversation.cs`：

```csharp
namespace KnowledgeHub.Core.Entities;

public class Conversation
{
    public Guid Id { get; set; }
    public required string UserKey { get; set; }
    public required string Channel { get; set; }        // ConversationChannels.Web | Teams
    public required string Title { get; set; }
    public string? TeamsConversationId { get; set; }    // 僅 teams：Bot Framework conversation id
    public DateTime? EndedAtUtc { get; set; }           // 僅 teams：「新對話」指令蓋章
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<ConversationMessage> Messages { get; set; } = [];
}
```

`backend/KnowledgeHub.Core/Entities/ConversationMessage.cs`：

```csharp
namespace KnowledgeHub.Core.Entities;

public class ConversationMessage
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public required string Role { get; set; }           // "user" | "assistant"
    public required string Content { get; set; }
    public string? SourcesJson { get; set; }            // assistant 訊息的檢索來源（與 SSE sources 事件同形）
    public DateTime CreatedAtUtc { get; set; }
}
```

`backend/KnowledgeHub.Core/ConversationChannels.cs`：

```csharp
namespace KnowledgeHub.Core;

public static class ConversationChannels
{
    public const string Web = "web";
    public const string Teams = "teams";
}
```

- [ ] **Step 2: DbContext 加 DbSet 與設定**

`KnowledgeHubDbContext.cs` 的 DbSet 區塊加：

```csharp
public DbSet<Conversation> Conversations => Set<Conversation>();
public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();
```

`OnModelCreating` 加（照既有 CompanyDocument 設定的寫法）：

```csharp
modelBuilder.Entity<Conversation>(e =>
{
    e.Property(c => c.UserKey).HasMaxLength(100);
    e.Property(c => c.Channel).HasMaxLength(10);
    e.Property(c => c.Title).HasMaxLength(100);
    e.Property(c => c.TeamsConversationId).HasMaxLength(200);
    e.HasIndex(c => new { c.UserKey, c.UpdatedAtUtc });   // 側欄清單查詢
    e.HasIndex(c => c.TeamsConversationId);               // bot 接續查詢
    e.HasMany(c => c.Messages).WithOne(m => m.Conversation)
        .HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
});

modelBuilder.Entity<ConversationMessage>(e =>
{
    e.Property(m => m.Role).HasMaxLength(10);
    e.HasIndex(m => new { m.ConversationId, m.CreatedAtUtc });
});
```

- [ ] **Step 3: 建 migration 並確認編譯**

```powershell
dotnet build backend/KnowledgeHub.Api
dotnet ef migrations add Conversations --project backend/KnowledgeHub.Infrastructure --startup-project backend/KnowledgeHub.Api
```

Expected: 產出 `Migrations/*_Conversations.cs`，內含兩張表、三個索引、cascade FK。人工檢查 migration 檔沒有動到既有表。

- [ ] **Step 4: 套用到 DB**

```powershell
dotnet ef database update --project backend/KnowledgeHub.Infrastructure --startup-project backend/KnowledgeHub.Api
```

Expected: 成功（Azure SQL 冷啟時先重試一次）。

- [ ] **Step 5: Commit**

```powershell
git add backend/KnowledgeHub.Core backend/KnowledgeHub.Infrastructure
git commit -m "feat: Conversation 資料模型與 migration（web/Teams 共用對話保存）"
```

### Task 2: ICurrentUser.UserKey

**Files:**
- Modify: `backend/KnowledgeHub.Core/Interfaces/ICurrentUser.cs`
- Modify: `backend/KnowledgeHub.Api/Auth/CurrentUser.cs`
- Modify: `backend/KnowledgeHub.Tests/CurrentUserTests.cs`（加測試）
- Modify: 所有 `FakeUser : ICurrentUser` 測試替身（`EmailPluginTests.cs`、`DocumentsControllerTests.cs`、`KernelFactoryTests.cs`、`MeControllerTests.cs`）補 `public string UserKey => "test-user";`

**Interfaces:**
- Produces: `ICurrentUser.UserKey`（string）——Entra 使用者為 oid claim、種子帳號為 sub claim。Task 5 依賴

- [ ] **Step 1: 寫失敗測試（`CurrentUserTests.cs` 追加，照該檔既有的 HttpContext 假造模式）**

```csharp
[Fact]
public void UserKey_有oid時優先用oid()
{
    var user = CreateUser(new Claim("oid", "entra-oid-123"), new Claim("sub", "alice"));
    Assert.Equal("entra-oid-123", user.UserKey);
}

[Fact]
public void UserKey_無oid時退用sub()
{
    var user = CreateUser(new Claim("sub", "alice"));
    Assert.Equal("alice", user.UserKey);
}
```

（`CreateUser` 為該檔既有的建構 helper；若名稱不同，沿用檔內現名。）

- [ ] **Step 2: 跑測試確認紅**

```powershell
dotnet test backend/KnowledgeHub.Tests --filter "FullyQualifiedName~UserKey" --nologo
```

Expected: 編譯失敗（介面沒有 UserKey）——這就是紅。

- [ ] **Step 3: 實作**

`ICurrentUser.cs` 加：

```csharp
/// <summary>對話歸戶用的穩定識別：Entra 使用者為 oid（object id），種子帳號為 sub（username）。
/// Teams activity 的 AadObjectId 是同一個 Entra oid，因此同一人 web/Teams 對話自然歸戶。</summary>
string UserKey { get; }
```

`CurrentUser.cs` 加：

```csharp
public string UserKey =>
    accessor.HttpContext?.User.FindFirst("oid")?.Value
    ?? accessor.HttpContext?.User.FindFirst("sub")?.Value
    ?? throw new InvalidOperationException("缺少 oid/sub claim");
```

四個測試檔的 FakeUser 各補一行 `public string UserKey => "test-user";`。

- [ ] **Step 4: 跑全部單元測試確認綠**

```powershell
dotnet test backend/KnowledgeHub.Tests --filter "Category!=Integration" --nologo
```

Expected: 全綠。

- [ ] **Step 5: Commit**

```powershell
git add backend
git commit -m "feat: ICurrentUser.UserKey（Entra oid／種子帳號 sub，對話歸戶鍵）"
```

### Task 3: ConversationRepository＋整合測試

**Files:**
- Create: `backend/KnowledgeHub.Core/Interfaces/IConversationRepository.cs`
- Create: `backend/KnowledgeHub.Core/ConversationTitle.cs`
- Create: `backend/KnowledgeHub.Infrastructure/Repositories/ConversationRepository.cs`
- Test: `backend/KnowledgeHub.Tests/Integration/ConversationRepositoryTests.cs`
- Test: `backend/KnowledgeHub.Tests/ConversationTitleTests.cs`
- Modify: `backend/KnowledgeHub.Api/Program.cs`（DI 註冊）

**Interfaces:**
- Consumes: Task 1 的實體與 DbContext
- Produces: 下列介面，Task 5、6 依賴：

```csharp
namespace KnowledgeHub.Core.Interfaces;

using KnowledgeHub.Core.Entities;

public record ConversationSummary(Guid Id, string Title, string Channel, DateTime UpdatedAtUtc);

public interface IConversationRepository
{
    Task<Conversation> CreateAsync(string userKey, string channel, string title,
        string? teamsConversationId = null, CancellationToken ct = default);
    /// <summary>只回本人擁有的對話；非本人或不存在都回 null（呼叫端一律轉 404，避免洩漏存在性）。</summary>
    Task<Conversation?> FindOwnedAsync(Guid id, string userKey, CancellationToken ct = default);
    Task<IReadOnlyList<ConversationSummary>> ListAsync(string userKey, CancellationToken ct = default);
    Task<IReadOnlyList<ConversationMessage>> GetMessagesAsync(Guid conversationId, CancellationToken ct = default);
    /// <summary>追加一則訊息並更新 Conversation.UpdatedAtUtc。</summary>
    Task AppendMessageAsync(Guid conversationId, string role, string content,
        string? sourcesJson = null, CancellationToken ct = default);
    Task<bool> DeleteOwnedAsync(Guid id, string userKey, CancellationToken ct = default);
    /// <summary>該 Teams 對話串中最新且未結束（EndedAtUtc 為空）的對話；沒有則 null。</summary>
    Task<Conversation?> FindActiveTeamsAsync(string teamsConversationId, CancellationToken ct = default);
    /// <summary>蓋 EndedAtUtc 章（bot「新對話」指令用）。</summary>
    Task EndAsync(Guid id, CancellationToken ct = default);
}
```

- [ ] **Step 1: ConversationTitle 單元測試（先紅）**

`backend/KnowledgeHub.Tests/ConversationTitleTests.cs`：

```csharp
using KnowledgeHub.Core;

public class ConversationTitleTests
{
    [Fact]
    public void 短訊息原樣_換行折成空白()
        => Assert.Equal("出差 交通費怎麼報", ConversationTitle.From("出差\n交通費怎麼報"));

    [Fact]
    public void 超過30字截斷()
    {
        var title = ConversationTitle.From(new string('問', 50));
        Assert.Equal(30, title.Length);
    }
}
```

- [ ] **Step 2: 實作 `ConversationTitle`（`backend/KnowledgeHub.Core/ConversationTitle.cs`）**

```csharp
namespace KnowledgeHub.Core;

public static class ConversationTitle
{
    private const int MaxLength = 30;

    public static string From(string firstMessage)
    {
        var t = firstMessage.ReplaceLineEndings(" ").Trim();
        return t.Length <= MaxLength ? t : t[..MaxLength];
    }
}
```

跑 `dotnet test backend/KnowledgeHub.Tests --filter "FullyQualifiedName~ConversationTitle" --nologo` → 綠。

- [ ] **Step 3: 寫整合測試（先紅——repository 還不存在，先確認編譯錯）**

`backend/KnowledgeHub.Tests/Integration/ConversationRepositoryTests.cs`（連線建構照 `ChunkRepositoryTests.cs:17-25` 的模式）：

```csharp
using KnowledgeHub.Core;
using KnowledgeHub.Infrastructure;
using KnowledgeHub.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KnowledgeHub.Tests.Integration;

[Trait("Category", "Integration")]
public class ConversationRepositoryTests : IAsyncLifetime
{
    private KnowledgeHubDbContext _db = null!;
    private ConversationRepository _repo = null!;
    private readonly string _userKey = $"test-{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets("3fc8ee2a-3351-4410-a176-d589385e97f1").Build();
        var options = new DbContextOptionsBuilder<KnowledgeHubDbContext>()
            .UseSqlServer(config.GetConnectionString("Default")).Options;
        _db = new KnowledgeHubDbContext(options);
        _repo = new ConversationRepository(_db);
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _db.Conversations.Where(c => c.UserKey == _userKey).ExecuteDeleteAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task 建立後可列出_追加訊息會更新排序時間()
    {
        var a = await _repo.CreateAsync(_userKey, ConversationChannels.Web, "對話A");
        var b = await _repo.CreateAsync(_userKey, ConversationChannels.Web, "對話B");
        await _repo.AppendMessageAsync(a.Id, "user", "hi");

        var list = await _repo.ListAsync(_userKey);

        Assert.Equal(2, list.Count);
        Assert.Equal("對話A", list[0].Title);   // 追加訊息後 A 的 UpdatedAtUtc 較新，排最前
        Assert.Equal("對話B", list[1].Title);
    }

    [Fact]
    public async Task 歸戶隔離_拿別人的對話回null_刪除回false()
    {
        var mine = await _repo.CreateAsync(_userKey, ConversationChannels.Web, "我的");

        Assert.Null(await _repo.FindOwnedAsync(mine.Id, "someone-else"));
        Assert.False(await _repo.DeleteOwnedAsync(mine.Id, "someone-else"));
        Assert.NotNull(await _repo.FindOwnedAsync(mine.Id, _userKey));
    }

    [Fact]
    public async Task 訊息依時序讀回_SourcesJson往返完整()
    {
        var conv = await _repo.CreateAsync(_userKey, ConversationChannels.Web, "T");
        await _repo.AppendMessageAsync(conv.Id, "user", "問題");
        await _repo.AppendMessageAsync(conv.Id, "assistant", "回答", """[{"fileName":"a.md"}]""");

        var messages = await _repo.GetMessagesAsync(conv.Id);

        Assert.Equal(2, messages.Count);
        Assert.Equal("user", messages[0].Role);
        Assert.Equal("""[{"fileName":"a.md"}]""", messages[1].SourcesJson);
    }

    [Fact]
    public async Task 刪除對話_訊息級聯刪除()
    {
        var conv = await _repo.CreateAsync(_userKey, ConversationChannels.Web, "T");
        await _repo.AppendMessageAsync(conv.Id, "user", "hi");

        Assert.True(await _repo.DeleteOwnedAsync(conv.Id, _userKey));
        Assert.Empty(await _db.ConversationMessages.Where(m => m.ConversationId == conv.Id).ToListAsync());
    }

    [Fact]
    public async Task Teams接續_只找未結束的_蓋章後找不到()
    {
        var teamsId = $"19:test-{Guid.NewGuid():N}";
        var conv = await _repo.CreateAsync(_userKey, ConversationChannels.Teams, "T", teamsId);

        var active = await _repo.FindActiveTeamsAsync(teamsId);
        Assert.Equal(conv.Id, active!.Id);

        await _repo.EndAsync(conv.Id);
        Assert.Null(await _repo.FindActiveTeamsAsync(teamsId));
    }
}
```

- [ ] **Step 4: 實作 `ConversationRepository`（`backend/KnowledgeHub.Infrastructure/Repositories/ConversationRepository.cs`）**

```csharp
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Infrastructure.Repositories;

public class ConversationRepository(KnowledgeHubDbContext db) : IConversationRepository
{
    public async Task<Conversation> CreateAsync(string userKey, string channel, string title,
        string? teamsConversationId = null, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(), UserKey = userKey, Channel = channel, Title = title,
            TeamsConversationId = teamsConversationId, CreatedAtUtc = now, UpdatedAtUtc = now
        };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(ct);
        return conversation;
    }

    public Task<Conversation?> FindOwnedAsync(Guid id, string userKey, CancellationToken ct = default)
        => db.Conversations.FirstOrDefaultAsync(c => c.Id == id && c.UserKey == userKey, ct);

    public async Task<IReadOnlyList<ConversationSummary>> ListAsync(string userKey, CancellationToken ct = default)
        => await db.Conversations.Where(c => c.UserKey == userKey)
            .OrderByDescending(c => c.UpdatedAtUtc)
            .Select(c => new ConversationSummary(c.Id, c.Title, c.Channel, c.UpdatedAtUtc))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ConversationMessage>> GetMessagesAsync(
        Guid conversationId, CancellationToken ct = default)
        => await db.ConversationMessages.Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAtUtc).ToListAsync(ct);

    public async Task AppendMessageAsync(Guid conversationId, string role, string content,
        string? sourcesJson = null, CancellationToken ct = default)
    {
        db.ConversationMessages.Add(new ConversationMessage
        {
            Id = Guid.NewGuid(), ConversationId = conversationId, Role = role,
            Content = content, SourcesJson = sourcesJson, CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        await db.Conversations.Where(c => c.Id == conversationId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UpdatedAtUtc, DateTime.UtcNow), ct);
    }

    public async Task<bool> DeleteOwnedAsync(Guid id, string userKey, CancellationToken ct = default)
        => await db.Conversations.Where(c => c.Id == id && c.UserKey == userKey)
            .ExecuteDeleteAsync(ct) > 0;

    public Task<Conversation?> FindActiveTeamsAsync(string teamsConversationId, CancellationToken ct = default)
        => db.Conversations
            .Where(c => c.TeamsConversationId == teamsConversationId && c.EndedAtUtc == null)
            .OrderByDescending(c => c.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

    public Task EndAsync(Guid id, CancellationToken ct = default)
        => db.Conversations.Where(c => c.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.EndedAtUtc, DateTime.UtcNow), ct);
}
```

DI 註冊（`Program.cs`，放在 `AddScoped<IDocumentJobQueue…` 附近的 repository 註冊區）：

```csharp
builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
```

- [ ] **Step 5: 跑整合測試確認綠**

```powershell
dotnet test backend/KnowledgeHub.Tests --filter "FullyQualifiedName~ConversationRepositoryTests" --nologo
```

Expected: 5 綠（Azure SQL 冷啟時第一次可能 timeout，重跑一次）。

- [ ] **Step 6: Commit**

```powershell
git add backend
git commit -m "feat: ConversationRepository（建立/清單/訊息/歸戶隔離/Teams 接續）"
```

### Task 4: ChatSseStreamer 回傳彙整結果＋conversation 事件

**Files:**
- Modify: `backend/KnowledgeHub.Api/Sse/ChatSseStreamer.cs`
- Modify: `backend/KnowledgeHub.Tests/ChatSseStreamerTests.cs`

**Interfaces:**
- Produces（Task 5 依賴）：
  - `Task<(string Answer, string? SourcesJson)?> StreamAsync(Stream, string, IReadOnlyList<ChatTurn>, CancellationToken)`——成功回（完整回答, 來源 JSON 或 null），失敗/取消回 null；SSE 事件行為（token/sources/done/error）不變
  - `Task WriteConversationEventAsync(Stream output, Guid id, string title, CancellationToken ct)`——寫 `event: conversation`，data `{"id":"…","title":"…"}`

- [ ] **Step 1: 改測試（`ChatSseStreamerTests.cs`）——照該檔既有斷言模式，加/改**

```csharp
[Fact]
public async Task 成功時回傳完整回答與來源json()
{
    // fake IChatService 吐 "你"、"好"，RetrievalContext 塞一筆來源（沿檔內既有 fake）
    var result = await streamer.StreamAsync(output, "q", [], CancellationToken.None);

    Assert.NotNull(result);
    Assert.Equal("你好", result.Value.Answer);
    Assert.Contains("fileName", result.Value.SourcesJson);   // web 命名（camelCase）
}

[Fact]
public async Task 失敗時回傳null_仍寫error事件()
{
    // fake IChatService 丟例外
    var result = await streamer.StreamAsync(output, "q", [], CancellationToken.None);
    Assert.Null(result);
    // 既有的 error 事件斷言保留
}

[Fact]
public async Task conversation事件格式正確()
{
    var id = Guid.NewGuid();
    await streamer.WriteConversationEventAsync(output, id, "標題", CancellationToken.None);
    var text = ReadOutput();   // 檔內既有的輸出讀取 helper
    Assert.Contains("event: conversation", text);
    Assert.Contains(id.ToString(), text);
    Assert.Contains("標題", text);
}
```

跑 → 編譯紅。

- [ ] **Step 2: 實作**

`StreamAsync` 改為（try 區塊累積 `StringBuilder`，sources 序列化字串抽成變數重用，結構與錯誤處理照原樣）：

```csharp
public async Task<(string Answer, string? SourcesJson)?> StreamAsync(Stream output, string message,
    IReadOnlyList<ChatTurn> history, CancellationToken ct)
{
    try
    {
        var answer = new StringBuilder();
        await foreach (var token in chat.StreamAnswerAsync(message, history, ct))
        {
            answer.Append(token);
            await WriteEventAsync(output, "token", JsonSerializer.Serialize(new { text = token }, JsonOpts), ct);
        }

        string? sourcesJson = null;
        if (context.Results.Count > 0)
        {
            sourcesJson = JsonSerializer.Serialize(context.Results.Select(r => new
                { r.FileName, r.SequenceNumber, r.Content, r.Distance }), JsonOpts);
            await WriteEventAsync(output, "sources", sourcesJson, ct);
        }
        await WriteEventAsync(output, "done", "{}", ct);
        return (answer.ToString(), sourcesJson);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        return null;   // client 斷線：原註解與行為不變
    }
    catch (Exception ex)
    {
        // 原有的 error 事件與 log 邏輯全部保留，最後 return null
        ...（原 catch 區塊原樣）...
        return null;
    }
}

public async Task WriteConversationEventAsync(Stream output, Guid id, string title, CancellationToken ct)
    => await WriteEventAsync(output, "conversation",
        JsonSerializer.Serialize(new { id, title }, JsonOpts), ct);
```

- [ ] **Step 3: 跑單元測試綠、Commit**

```powershell
dotnet test backend/KnowledgeHub.Tests --filter "Category!=Integration" --nologo
git add backend
git commit -m "feat: ChatSseStreamer 回傳彙整結果並支援 conversation 事件"
```

### Task 5: ConversationsController＋移除 /api/chat

**Files:**
- Create: `backend/KnowledgeHub.Api/Controllers/ConversationsController.cs`
- Delete: `backend/KnowledgeHub.Api/Controllers/ChatController.cs`
- Create: `backend/KnowledgeHub.Tests/ConversationsControllerTests.cs`
- Delete/搬移: `backend/KnowledgeHub.Tests/ChatControllerTests.cs`（長度驗證等仍適用的測試搬到新測試檔）

**Interfaces:**
- Consumes: Task 2 `ICurrentUser.UserKey`、Task 3 `IConversationRepository`、Task 4 streamer 新簽章
- Produces: `GET/DELETE /api/conversations…`、`POST /api/conversations/messages`（前端 Task 8/9 依賴，事件序：新對話時 `conversation` → `token`× n → `sources` → `done`）

- [ ] **Step 1: 寫失敗測試（fake `IConversationRepository`／`IChatService`／`ICurrentUser`，照 `ChatControllerTests.cs` 既有的 controller 單元測試模式）**

```csharp
[Fact]
public async Task 帶他人conversationId_回404不串流()
{
    // fake repo：FindOwnedAsync 回 null
    await controller.SendMessage(new(Guid.NewGuid(), "hi"), CancellationToken.None);
    Assert.Equal(404, controller.Response.StatusCode);
    // fake chat 未被呼叫
}

[Fact]
public async Task 新對話_先發conversation事件_落庫user與assistant訊息()
{
    // conversationId = null；fake chat 吐 "答"；fake repo 記錄呼叫
    await controller.SendMessage(new(null, "第一句問題"), CancellationToken.None);

    var body = ReadResponseBody();
    Assert.Contains("event: conversation", body);
    Assert.True(body.IndexOf("event: conversation") < body.IndexOf("event: token"));
    // repo 收到 CreateAsync(userKey, "web", "第一句問題")
    // repo 收到 AppendMessageAsync ×2：("user","第一句問題",null)、("assistant","答",…)
}

[Fact]
public async Task 歷史裁切_只送近10則給chat()
{
    // fake repo GetMessagesAsync 回 15 則；fake chat 捕捉收到的 history
    await controller.SendMessage(new(existingId, "新訊息"), CancellationToken.None);
    Assert.Equal(10, capturedHistory.Count);
    Assert.Equal("第6則", capturedHistory[0].Content);   // 15 則取後 10 則
}

[Fact]
public async Task 訊息超過4000字_回400()
{
    await controller.SendMessage(new(null, new string('字', 4001)), CancellationToken.None);
    Assert.Equal(400, controller.Response.StatusCode);
}

[Fact]
public async Task 串流失敗_不落庫assistant訊息()
{
    // fake chat 丟例外；驗 AppendMessageAsync 只收到 user 那筆
}
```

（Get/Delete 的 404 與成功路徑各一條，斷言回傳內容含 Role/Content/SourcesJson。）

- [ ] **Step 2: 實作 `ConversationsController.cs`**

```csharp
using KnowledgeHub.Api.Sse;
using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeHub.Api.Controllers;

public record SendMessageRequest(Guid? ConversationId, string Message);

[ApiController]
[Route("api/conversations")]
[Authorize]
public class ConversationsController(
    IConversationRepository conversations, IChatService chat, ICurrentUser user,
    RetrievalContext context, ILogger<ChatSseStreamer> logger) : ControllerBase
{
    // 沿用舊 ChatController 的上限（免費層 LLM 配額保護）
    private const int MaxHistoryTurns = 10;
    private const int MaxContentLength = 4000;

    [HttpGet]
    public async Task<IReadOnlyList<ConversationSummary>> List(CancellationToken ct)
        => await conversations.ListAsync(user.UserKey, ct);

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (await conversations.FindOwnedAsync(id, user.UserKey, ct) is null) return NotFound();
        var messages = await conversations.GetMessagesAsync(id, ct);
        return Ok(messages.Select(m => new { m.Role, m.Content, m.SourcesJson, m.CreatedAtUtc }));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => await conversations.DeleteOwnedAsync(id, user.UserKey, ct) ? NoContent() : NotFound();

    [HttpPost("messages")]
    public async Task SendMessage(SendMessageRequest request, CancellationToken ct)
    {
        if (request.Message.Length > MaxContentLength)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { error = $"訊息長度不可超過 {MaxContentLength} 字元" }, ct);
            return;
        }

        Conversation conversation;
        var isNew = request.ConversationId is null;
        if (request.ConversationId is Guid id)
        {
            var found = await conversations.FindOwnedAsync(id, user.UserKey, ct);
            if (found is null) { Response.StatusCode = StatusCodes.Status404NotFound; return; }
            conversation = found;
        }
        else
        {
            conversation = await conversations.CreateAsync(user.UserKey,
                ConversationChannels.Web, ConversationTitle.From(request.Message), ct: ct);
        }

        // 先載歷史再落 user 訊息，歷史才不會把當前訊息算進去
        var history = (await conversations.GetMessagesAsync(conversation.Id, ct))
            .TakeLast(MaxHistoryTurns)
            .Select(m => new ChatTurn(m.Role, m.Content))
            .ToList();
        await conversations.AppendMessageAsync(conversation.Id, "user", request.Message, ct: ct);

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        var streamer = new ChatSseStreamer(chat, context, logger);
        if (isNew)
            await streamer.WriteConversationEventAsync(Response.Body, conversation.Id, conversation.Title, ct);
        var result = await streamer.StreamAsync(Response.Body, request.Message, history, ct);
        // 失敗（null）不落 assistant 訊息：使用者重試時對話裡只有問句，與 ChatGPT 行為一致
        if (result is not null)
            await conversations.AppendMessageAsync(conversation.Id, "assistant",
                result.Value.Answer, result.Value.SourcesJson, ct);
    }
}
```

刪除 `ChatController.cs`；`ChatControllerTests.cs` 中仍適用的（長度 400）已搬入新測試檔後刪除。

- [ ] **Step 3: 跑全部單元測試綠**

```powershell
dotnet test backend/KnowledgeHub.Tests --filter "Category!=Integration" --nologo
```

- [ ] **Step 4: Commit**

```powershell
git add -A backend
git commit -m "feat: /api/conversations 對話保存 API，移除舊 /api/chat"
```

## Phase 2：Teams bot 多輪

### Task 6: bot 接對話保存

**Files:**
- Modify: `backend/KnowledgeHub.Api/Bot/KnowledgeHubBotHandler.cs`
- Modify: `backend/KnowledgeHub.Tests/KnowledgeHubBotHandlerTests.cs`

**Interfaces:**
- Consumes: Task 3 `IConversationRepository`
- Produces: bot 行為——接續未結束對話、「新對話」/`/new` 重開

- [ ] **Step 1: 寫失敗測試（沿 `KnowledgeHubBotHandlerTests.cs` 既有的 TurnContext/fake 模式，fake repo 記錄呼叫）**

```csharp
[Fact]
public async Task 一般訊息_接續未結束對話_歷史送入chat()
{
    // fake repo：FindActiveTeamsAsync 回既有對話，GetMessagesAsync 回 2 則舊訊息
    // 斷言：fake chat 收到的 history 有 2 則；AppendMessageAsync 收到 user+assistant 各一
}

[Fact]
public async Task 沒有活躍對話_自動建新的()
{
    // fake repo：FindActiveTeamsAsync 回 null
    // 斷言：CreateAsync(userKey=AadObjectId, channel="teams", teamsConversationId=對話id) 被呼叫
}

[Fact]
public async Task 新對話指令_蓋章結束並回確認_不進RAG()
{
    // 訊息 = "新對話"；fake repo：FindActiveTeamsAsync 回既有對話
    // 斷言：EndAsync 被呼叫；回覆文字含「新對話」；fake chat 完全沒被呼叫
}

[Fact]
public async Task slash_new_等同新對話指令()
{
    // 訊息 = "/new"，斷言同上
}
```

- [ ] **Step 2: 實作**

`KnowledgeHubBotHandler` 建構子加 `IConversationRepository conversations`，`OnMessageActivityAsync` 改為：

```csharp
protected override async Task OnMessageActivityAsync(
    ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
{
    var text = turnContext.Activity.Text?.Trim() ?? "";
    var teamsConversationId = turnContext.Activity.Conversation.Id;
    // Teams 的 AadObjectId 即 Entra oid，與 web 端 UserKey 同源（歸戶互通）；非 Teams 管道（Emulator）退用 From.Id
    var userKey = turnContext.Activity.From?.AadObjectId ?? turnContext.Activity.From?.Id ?? "unknown";

    if (text is "新對話" or "/new")
    {
        var active = await conversations.FindActiveTeamsAsync(teamsConversationId, cancellationToken);
        if (active is not null) await conversations.EndAsync(active.Id, cancellationToken);
        await turnContext.SendActivityAsync(
            MessageFactory.Text("好的，已為您開啟新對話，請直接提問。"), cancellationToken);
        return;
    }

    await turnContext.SendActivityAsync(new Activity { Type = ActivityTypes.Typing }, cancellationToken);

    string reply;
    try
    {
        var conversation = await conversations.FindActiveTeamsAsync(teamsConversationId, cancellationToken)
            ?? await conversations.CreateAsync(userKey, ConversationChannels.Teams,
                ConversationTitle.From(text), teamsConversationId, cancellationToken);
        var history = (await conversations.GetMessagesAsync(conversation.Id, cancellationToken))
            .TakeLast(MaxHistoryTurns)
            .Select(m => new ChatTurn(m.Role, m.Content))
            .ToList();

        var sb = new StringBuilder();
        await foreach (var token in chat.StreamAnswerAsync(text, history, cancellationToken))
            sb.Append(token);
        reply = sb.ToString();

        if (retrievalContext.Results.Count > 0)
        {
            var sources = retrievalContext.Results.Select(r => r.FileName).Distinct();
            reply += "\n\n來源：" + string.Join("、", sources);
        }

        await conversations.AppendMessageAsync(conversation.Id, "user", text, ct: cancellationToken);
        await conversations.AppendMessageAsync(conversation.Id, "assistant", sb.ToString(), ct: cancellationToken);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Bot RAG 問答處理失敗");
        reply = "抱歉，處理您的問題時發生錯誤，請稍後再試。";
    }

    await turnContext.SendActivityAsync(MessageFactory.Text(reply), cancellationToken);
}
```

類別頂加 `private const int MaxHistoryTurns = 10;`，並更新類別註解（單輪→多輪，安全前提段落原樣保留）。

- [ ] **Step 3: 跑單元測試綠、Commit**

```powershell
dotnet test backend/KnowledgeHub.Tests --filter "Category!=Integration" --nologo
git add backend
git commit -m "feat: Teams bot 多輪對話（接續/新對話指令，與 web 共用對話保存）"
```

## Phase 3：前端改版

### Task 7: vue-router 骨架＋文件頁

**Files:**
- Modify: `frontend/package.json`（加 vue-router）
- Create: `frontend/src/router.ts`
- Modify: `frontend/src/main.ts`、`frontend/src/App.vue`
- Create: `frontend/src/views/DocumentsView.vue`
- Create: `frontend/src/views/ChatView.vue`（本任務先是殼，Task 8 填肉）

**Interfaces:**
- Produces: 路由 `/chat`、`/chat/:id`、`/documents`；App.vue 保留「未登入→LoginView、無部門→提示頁」的既有攔截

- [ ] **Step 1: 安裝**

```powershell
npm install vue-router@4 --prefix frontend
```

- [ ] **Step 2: `frontend/src/router.ts`**

```ts
import { createRouter, createWebHistory } from 'vue-router'
import ChatView from './views/ChatView.vue'
import DocumentsView from './views/DocumentsView.vue'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/chat' },
    { path: '/chat/:id?', name: 'chat', component: ChatView },
    { path: '/documents', name: 'documents', component: DocumentsView },
  ],
})
```

`main.ts`：

```ts
import { createApp } from 'vue'
import './style.css'
import App from './App.vue'
import { router } from './router'

createApp(App).use(router).mount('#app')
```

- [ ] **Step 3: `App.vue` 改為攔截＋router-view（登入/無部門攔截照原樣，header 移除——各 view 自帶）**

```vue
<script setup lang="ts">
import { useAuth } from './composables/useAuth'
import LoginView from './views/LoginView.vue'

const { token, noDepartmentMessage, logout } = useAuth()
</script>

<template>
  <LoginView v-if="!token" />
  <div
    v-else-if="noDepartmentMessage"
    class="flex h-screen flex-col items-center justify-center gap-4 bg-slate-100 px-6 text-center"
  >
    <p class="max-w-md text-slate-700">{{ noDepartmentMessage }}</p>
    <button class="rounded bg-slate-900 px-4 py-2 text-white" @click="logout">返回登入頁</button>
  </div>
  <router-view v-else />
</template>
```

- [ ] **Step 4: `DocumentsView.vue`（DocumentPanel 原樣搬入置中卡片，上方帶回聊天的連結）**

```vue
<script setup lang="ts">
import DocumentPanel from '../components/DocumentPanel.vue'
</script>

<template>
  <div class="min-h-screen bg-slate-50">
    <header class="flex items-center justify-between border-b border-slate-200 bg-white px-6 py-3">
      <router-link to="/chat" class="text-sm text-slate-600 hover:text-slate-900">← 回對話</router-link>
      <h1 class="text-lg font-semibold text-slate-900">文件管理</h1>
      <span class="w-16"></span>
    </header>
    <main class="mx-auto max-w-3xl p-6">
      <div class="rounded-lg border border-slate-200 bg-white p-4">
        <DocumentPanel />
      </div>
    </main>
  </div>
</template>
```

`ChatView.vue` 本任務先放殼（Task 8 完整版取代）：

```vue
<template>
  <div class="flex h-screen">
    <main class="flex-1"><p class="p-6 text-slate-400">ChatView（Task 8 實作）</p></main>
  </div>
</template>
```

- [ ] **Step 5: 驗證＋Commit**

```powershell
npm run build --prefix frontend
```

Expected: vue-tsc＋vite build 通過。`npm run dev --prefix frontend` 手動確認：登入後進 `/chat` 殼頁、`/documents` 可上傳列表如舊、未登入看到 LoginView。

```powershell
git add frontend
git commit -m "feat: 前端引入 vue-router（/chat、/documents），文件管理移獨立頁"
```

### Task 8: 對話側欄＋ChatView 版面

**Files:**
- Create: `frontend/src/composables/useConversations.ts`
- Create: `frontend/src/components/ConversationSidebar.vue`
- Modify: `frontend/src/views/ChatView.vue`（完整版）
- Modify: `frontend/src/components/ChatPanel.vue`（改 ChatGPT 式排版＋textarea）

**Interfaces:**
- Consumes: Task 5 的 `GET /api/conversations`、`DELETE /api/conversations/{id}`
- Produces: `useConversations()` → `{ list, load, remove }`（`ConversationSummary { id, title, channel, updatedAtUtc }`）；`ChatView` 讀 route param `id` 決定開啟哪條對話（依賴 Task 9 的 `useChat.open/reset`）

- [ ] **Step 1: `useConversations.ts`**

```ts
import { ref } from 'vue'
import { useAuth } from './useAuth'

export interface ConversationSummary {
  id: string; title: string; channel: string; updatedAtUtc: string
}

// 模組層單例：側欄與 ChatView 共享同一份清單（照 useAuth/useChat 既有慣例）
const list = ref<ConversationSummary[]>([])

export function useConversations() {
  const { authHeader } = useAuth()

  async function load(): Promise<void> {
    const res = await fetch('/api/conversations', { headers: authHeader() })
    if (res.ok) list.value = await res.json()
  }

  async function remove(id: string): Promise<boolean> {
    const res = await fetch(`/api/conversations/${id}`, { method: 'DELETE', headers: authHeader() })
    if (res.ok) list.value = list.value.filter((c) => c.id !== id)
    return res.ok
  }

  return { list, load, remove }
}
```

- [ ] **Step 2: `ConversationSidebar.vue`**

```vue
<script setup lang="ts">
import { useAuth } from '../composables/useAuth'
import { useConversations } from '../composables/useConversations'

defineProps<{ activeId: string | null }>()
const emit = defineEmits<{ select: [id: string]; new: []; deleted: [id: string] }>()

const { department, departments, logout } = useAuth()
const { list, remove } = useConversations()

async function onDelete(id: string) {
  if (await remove(id)) emit('deleted', id)
}
</script>

<template>
  <div class="flex h-full flex-col bg-slate-50">
    <div class="p-3">
      <button
        class="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-left text-sm font-medium text-slate-900 hover:bg-slate-100"
        @click="emit('new')"
      >
        ＋ 新對話
      </button>
    </div>
    <nav class="flex-1 space-y-0.5 overflow-y-auto px-2">
      <div
        v-for="c in list"
        :key="c.id"
        class="group flex items-center gap-1 rounded-lg px-2 py-2 text-sm hover:bg-slate-200"
        :class="c.id === activeId ? 'bg-slate-200 font-medium' : 'text-slate-700'"
      >
        <button class="min-w-0 flex-1 truncate text-left" @click="emit('select', c.id)">
          {{ c.title }}
        </button>
        <span
          v-if="c.channel === 'teams'"
          class="shrink-0 rounded bg-indigo-100 px-1 text-[10px] text-indigo-700"
          >Teams</span
        >
        <button
          class="hidden shrink-0 text-slate-400 hover:text-red-600 group-hover:block"
          title="刪除對話"
          @click="onDelete(c.id)"
        >
          ✕
        </button>
      </div>
    </nav>
    <div class="border-t border-slate-200 p-3 text-sm">
      <p class="truncate font-medium text-slate-900">
        {{ departments.length > 0 ? departments.join('、') : department }}
      </p>
      <div class="mt-2 flex items-center justify-between">
        <router-link to="/documents" class="text-slate-600 hover:text-slate-900">文件管理</router-link>
        <button class="text-slate-600 hover:text-slate-900" @click="logout">登出</button>
      </div>
    </div>
  </div>
</template>
```

- [ ] **Step 3: `ChatView.vue` 完整版（含手機抽屜）**

```vue
<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ConversationSidebar from '../components/ConversationSidebar.vue'
import ChatPanel from '../components/ChatPanel.vue'
import { useChat } from '../composables/useChat'
import { useConversations } from '../composables/useConversations'

const route = useRoute()
const router = useRouter()
const { conversationId, open, reset } = useChat()
const { load } = useConversations()
const drawerOpen = ref(false)

onMounted(load)

// 網址 → 對話：進 /chat/:id 載入該對話；進 /chat 清空
watch(
  () => route.params.id,
  async (id) => {
    if (typeof id === 'string' && id) {
      if (id !== conversationId.value) await open(id)
    } else {
      reset()
    }
  },
  { immediate: true },
)

// 對話 → 網址：新對話拿到 id 後補網址並刷新側欄
watch(conversationId, (id) => {
  if (id && route.params.id !== id) {
    router.replace(`/chat/${id}`)
    load()
  }
})

function onNew() {
  drawerOpen.value = false
  router.push('/chat')
}

function onSelect(id: string) {
  drawerOpen.value = false
  router.push(`/chat/${id}`)
}

function onDeleted(id: string) {
  if (conversationId.value === id) router.push('/chat')
}
</script>

<template>
  <div class="flex h-screen overflow-hidden">
    <!-- 桌機常駐側欄 -->
    <aside class="hidden w-64 shrink-0 border-r border-slate-200 md:block">
      <ConversationSidebar :active-id="conversationId" @new="onNew" @select="onSelect" @deleted="onDeleted" />
    </aside>

    <!-- 手機抽屜 -->
    <div v-if="drawerOpen" class="fixed inset-0 z-20 md:hidden">
      <div class="absolute inset-0 bg-black/30" @click="drawerOpen = false"></div>
      <aside class="absolute inset-y-0 left-0 w-72 bg-slate-50 shadow-xl">
        <ConversationSidebar :active-id="conversationId" @new="onNew" @select="onSelect" @deleted="onDeleted" />
      </aside>
    </div>

    <main class="flex min-w-0 flex-1 flex-col">
      <header class="flex items-center gap-3 border-b border-slate-200 px-4 py-2 md:hidden">
        <button class="text-slate-600" aria-label="開啟選單" @click="drawerOpen = true">☰</button>
        <h1 class="text-base font-semibold text-slate-900">KnowledgeHub</h1>
      </header>
      <ChatPanel class="min-h-0 flex-1" />
    </main>
  </div>
</template>
```

- [ ] **Step 4: `ChatPanel.vue` 改版（置中 max-w-3xl、assistant 無泡泡、textarea＋Enter 送出）**

script 區改動：input 改 textarea ref、加 `onKeydown`（Enter 送出、Shift+Enter 換行）、自動長高：

```ts
function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault()
    onSubmit()
  }
}

function autoGrow(e: Event) {
  const el = e.target as HTMLTextAreaElement
  el.style.height = 'auto'
  el.style.height = `${Math.min(el.scrollHeight, 200)}px`
}
```

template 改為：

```vue
<template>
  <div class="flex h-full flex-col">
    <div ref="listEl" class="flex-1 overflow-y-auto">
      <div class="mx-auto max-w-3xl space-y-6 px-4 py-6">
        <p v-if="messages.length === 0" class="pt-16 text-center text-slate-400">
          有什麼想問公司知識庫的？
        </p>
        <div v-for="(m, i) in messages" :key="i">
          <!-- 使用者：右側泡泡 -->
          <div v-if="m.role === 'user'" class="flex justify-end">
            <div class="max-w-[85%] whitespace-pre-wrap rounded-2xl bg-slate-900 px-4 py-2 text-sm text-white">
              {{ m.content }}
            </div>
          </div>
          <!-- assistant：無泡泡直排版 -->
          <div v-else class="space-y-2">
            <div v-if="m.content" class="text-sm text-slate-900">
              {{ m.content }}<span v-if="isStreaming(m, i)" class="animate-pulse">▍</span>
            </div>
            <p v-else-if="isStreaming(m, i)" class="text-sm text-slate-400">思考中…</p>
            <div v-if="m.error" class="rounded-lg bg-red-100 px-3 py-2 text-sm text-red-700">
              {{ m.error }}
            </div>
            <div v-if="m.sources.length > 0" class="space-y-1">
              <SourceCard v-for="(s, si) in m.sources" :key="si" :source="s" />
            </div>
          </div>
        </div>
      </div>
    </div>

    <form class="border-t border-slate-200 bg-white" @submit.prevent="onSubmit">
      <div class="mx-auto flex max-w-3xl items-end gap-2 px-4 py-3">
        <textarea
          v-model="input"
          rows="1"
          placeholder="輸入問題…（Enter 送出，Shift+Enter 換行）"
          class="max-h-[200px] flex-1 resize-none rounded-xl border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-slate-400"
          :disabled="sending"
          @keydown="onKeydown"
          @input="autoGrow"
        ></textarea>
        <button
          type="submit"
          :disabled="sending || !input.trim()"
          class="rounded-xl bg-slate-900 px-4 py-2 text-sm text-white disabled:opacity-50"
        >
          送出
        </button>
      </div>
    </form>
  </div>
</template>
```

- [ ] **Step 5: 驗證＋Commit**

`npm run build --prefix frontend` 通過；dev 手動確認側欄清單（此時發話仍走舊 useChat——Task 9 前發話會 404，屬預期，先驗版面與清單/刪除）。

```powershell
git add frontend
git commit -m "feat: ChatGPT 式版面（對話側欄、置中對話流、手機抽屜）"
```

### Task 9: useChat 接新端點

**Files:**
- Modify: `frontend/src/composables/useChat.ts`

**Interfaces:**
- Consumes: Task 5 `POST /api/conversations/messages`（SSE：conversation/token/sources/done/error）、`GET /api/conversations/{id}`
- Produces: `useChat()` → `{ messages, sending, conversationId, send, open, reset, cancel }`（Task 8 的 ChatView 依賴）

- [ ] **Step 1: 改寫 `useChat.ts`（保留既有 AbortController 註解與 reactive proxy 註解，模組層加 `conversationId`）**

```ts
import { ref } from 'vue'
import { useAuth } from './useAuth'

export interface Source { fileName: string; sequenceNumber: number; content: string; distance: number }
export interface ChatMessage { role: 'user' | 'assistant'; content: string; sources: Source[]; error: string | null }

let controller: AbortController | null = null

// 模組層單例：目前開啟的對話（null＝新對話尚未建立）
const messages = ref<ChatMessage[]>([])
const conversationId = ref<string | null>(null)

export function useChat() {
  const { authHeader, checkNoDepartment } = useAuth()
  const sending = ref(false)

  /** 載入既有對話（側欄點選／網址帶 id 進入）。404（被刪或非本人）就回到空白新對話。 */
  async function open(id: string): Promise<void> {
    cancel()
    const res = await fetch(`/api/conversations/${id}`, { headers: authHeader() })
    if (!res.ok) { reset(); return }
    const rows: { role: 'user' | 'assistant'; content: string; sourcesJson: string | null }[] =
      await res.json()
    conversationId.value = id
    messages.value = rows.map((r) => ({
      role: r.role, content: r.content, error: null,
      sources: r.sourcesJson ? JSON.parse(r.sourcesJson) : [],
    }))
  }

  function reset(): void {
    cancel()
    conversationId.value = null
    messages.value = []
  }

  async function send(text: string): Promise<void> {
    sending.value = true
    messages.value.push({ role: 'user', content: text, sources: [], error: null })
    messages.value.push({ role: 'assistant', content: '', sources: [], error: null })
    const reply = messages.value[messages.value.length - 1]   // （保留原 reactive proxy 註解）

    controller = new AbortController()
    try {
      const res = await fetch('/api/conversations/messages', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...authHeader() },
        body: JSON.stringify({ conversationId: conversationId.value, message: text }),
        signal: controller.signal,
      })
      if (!res.ok) {
        if (await checkNoDepartment(res)) return
        throw new Error(`HTTP ${res.status}`)
      }
      if (!res.body) throw new Error(`HTTP ${res.status}`)

      const reader = res.body.pipeThrough(new TextDecoderStream()).getReader()
      let buffer = ''
      while (true) {
        const { value, done } = await reader.read()
        if (done) break
        buffer += value
        let sep: number
        while ((sep = buffer.indexOf('\n\n')) >= 0) {
          handleEvent(buffer.slice(0, sep), reply)
          buffer = buffer.slice(sep + 2)
        }
      }
    } catch (e) {
      if (!(e instanceof DOMException && e.name === 'AbortError')) {
        reply.error = e instanceof Error ? e.message : '連線失敗'
      }
    } finally {
      sending.value = false
      controller = null
    }
  }

  function cancel(): void {
    controller?.abort()
  }

  function handleEvent(block: string, reply: ChatMessage) {
    const event = /^event: (.+)$/m.exec(block)?.[1]
    const data = /^data: (.+)$/m.exec(block)?.[1]
    if (!event || !data) return
    if (event === 'conversation') conversationId.value = JSON.parse(data).id
    else if (event === 'token') reply.content += JSON.parse(data).text
    else if (event === 'sources') reply.sources = JSON.parse(data)
    else if (event === 'error') reply.error = JSON.parse(data).message
  }

  return { messages, sending, conversationId, send, open, reset, cancel }
}
```

- [ ] **Step 2: 驗證＋Commit**

`npm run build --prefix frontend` 通過。dev＋後端實跑手動驗收：新對話送出→網址變 `/chat/{id}`、側欄出現新條目；重新整理對話仍在；切換對話載入正確歷史與來源卡片；刪除當前對話回到空白。

```powershell
git add frontend
git commit -m "feat: useChat 接 /api/conversations/messages（對話保存、開啟、切換）"
```

## Phase 4：Markdown 渲染

### Task 10: MarkdownContent 元件

**Files:**
- Modify: `frontend/package.json`（markdown-it、dompurify、@tailwindcss/typography）
- Modify: `frontend/src/style.css`
- Create: `frontend/src/components/MarkdownContent.vue`
- Modify: `frontend/src/components/ChatPanel.vue`（assistant 內容改用元件）

**Interfaces:**
- Produces: `<MarkdownContent :content="…" />`——渲染＋DOMPurify 消毒後輸出

- [ ] **Step 1: 安裝**

```powershell
npm install markdown-it dompurify @tailwindcss/typography --prefix frontend
npm install -D @types/markdown-it --prefix frontend
```

`style.css` 加一行（Tailwind v4 的 plugin 寫法）：

```css
@plugin "@tailwindcss/typography";
```

- [ ] **Step 2: `MarkdownContent.vue`**

```vue
<script setup lang="ts">
import { computed } from 'vue'
import MarkdownIt from 'markdown-it'
import DOMPurify from 'dompurify'

const props = defineProps<{ content: string }>()

// LLM 輸出視為不可信內容：render 後必經 DOMPurify 才能 v-html（XSS 防線，不可拿掉）
const md = new MarkdownIt({ linkify: true, breaks: true })
const html = computed(() => DOMPurify.sanitize(md.render(props.content)))
</script>

<template>
  <div class="prose prose-sm prose-slate max-w-none" v-html="html" />
</template>
```

- [ ] **Step 3: `ChatPanel.vue` 的 assistant 內容區改用元件**

```vue
<div v-if="m.content" class="text-sm text-slate-900">
  <MarkdownContent :content="m.content" />
  <span v-if="isStreaming(m, i)" class="animate-pulse">▍</span>
</div>
```

（script 加 `import MarkdownContent from './MarkdownContent.vue'`；使用者訊息維持 `{{ }}` 純文字。）

- [ ] **Step 4: 驗證＋Commit**

`npm run build --prefix frontend` 通過。手動：問一題會回表格/清單的問題（例：「用表格列出出差費用種類」），確認渲染；貼 `<img src=x onerror=alert(1)>` 進對話確認不執行（消毒生效）。

```powershell
git add frontend
git commit -m "feat: assistant 回覆 Markdown 渲染（markdown-it + DOMPurify）"
```

## Phase 5：視覺打磨＋RWD

### Task 11: 品牌點綴與 RWD 收尾

**Files:**
- Modify: `frontend/src/style.css`（品牌色 token）
- Modify: `frontend/src/components/ConversationSidebar.vue`、`ChatPanel.vue`、`frontend/src/views/DocumentsView.vue`、`LoginView.vue`（點綴色套用）

- [ ] **Step 1: 定義品牌色 token（`style.css`）**

```css
@theme {
  --color-brand: #e4002b; /* 暫定值：套用前自 QBurger 官網取正式色票，經使用者確認後替換 */
  --color-brand-hover: #c50025;
}
```

**此步驟必須先向使用者確認正式色票再繼續**（spec §8）。

- [ ] **Step 2: 套用點綴（只動這些位置，其餘維持 slate）**

- 「＋ 新對話」按鈕：`border-slate-300 bg-white text-slate-900` → `bg-brand text-white hover:bg-brand-hover`
- 送出按鈕：`bg-slate-900` → `bg-brand hover:bg-brand-hover`
- textarea focus ring：`focus:ring-slate-400` → `focus:ring-brand`
- LoginView 主要按鈕與 sidebar 頂部標題（`KnowledgeHub` 字樣加 `text-brand` 的 logo 點）

- [ ] **Step 3: RWD 走查與微調**

以瀏覽器 DevTools iPhone 尺寸逐頁走：登入 → 對話（抽屜開合、輸入、捲動）→ 切換對話 → 文件頁（上傳清單不橫溢，必要處加 `overflow-x-auto`）。發現的擠壓問題就地修（限 spacing/breakpoint class，不動邏輯）。

- [ ] **Step 4: 驗證＋Commit**

```powershell
npm run build --prefix frontend
git add frontend
git commit -m "feat: 品牌點綴色與手機 RWD 收尾"
```

### Task 12: 總驗收與收尾

- [ ] **Step 1: 後端全測試**

```powershell
dotnet test backend/KnowledgeHub.Tests --nologo
```

Expected: 單元＋整合全綠（Azure SQL 要醒著）。

- [ ] **Step 2: 手動驗收清單（spec §9，逐條打勾）**

- [ ] 新對話／切換／刪除
- [ ] 重新整理後對話保留
- [ ] 兩個帳號互看不到對方對話（開無痕視窗用第二帳號）
- [ ] Teams bot 多輪接續（連問兩題有上下文）＋「新對話」重開
- [ ] Teams 的對話出現在 web 側欄（含 Teams 徽章）——驗證 OID 歸戶；若不通，記錄實際 AadObjectId 與 web oid 差異，按 spec §11 退化為各自成列並回報
- [ ] 手機尺寸：抽屜、輸入、對話、文件頁
- [ ] Markdown：表格／清單／粗體渲染，XSS 注入不執行
- [ ] 舊對話重開來源卡片重現

- [ ] **Step 3: 更新 `PROGRESS.md`（新增本改版段落：✓ 完成項／□ 未辦）＋最終 commit**

```powershell
git add PROGRESS.md
git commit -m "docs: 公司 GPT 改版進度更新"
```

---

## Self-Review 紀錄

- Spec 覆蓋：§2 範圍六項 → Task 7-9(版面)、1-5(保存)、6(bot)、10(Markdown)、11(視覺RWD)、7(文件頁)；§9 驗收 → Task 12。無缺口
- 型別一致性：`ConversationSummary(Id,Title,Channel,UpdatedAtUtc)` 前後端一致（web JSON 為 camelCase）；`useChat` 回傳簽章與 ChatView 消費一致；streamer 新簽章與 controller 呼叫一致
- 佔位掃描：Task 4 Step 2 的「原 catch 區塊原樣」係指保留現檔 `ChatSseStreamer.cs:34-49` 既有程式碼，非未定內容；Task 11 品牌色暫定值已標明確認程序
