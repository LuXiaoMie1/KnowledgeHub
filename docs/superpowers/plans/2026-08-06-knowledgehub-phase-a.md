# KnowledgeHub Phase A 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建出可實跑的企業知識庫問答系統：上傳 PDF/Markdown → 背景切片向量化 → 部門權限檢索 → Gemini Agent 串流回答附來源。

**Architecture:** 三層 .NET 方案（Api → Infrastructure → Core，Core 無 EF/AI 依賴），Azure SQL 原生 `VECTOR(1536)` 做向量檢索，Semantic Kernel 走 Gemini 的 OpenAI 相容端點（chat）＋原生 REST（embedding），Hangfire 背景解析，Vue 3 前端以 SSE 接串流。

**Tech Stack:** .NET 10、EF Core 10（`Microsoft.EntityFrameworkCore.SqlServer` 10.0.x、`Microsoft.Data.SqlClient` ≥ 6.1.1）、Semantic Kernel（GA 版 OpenAI 連接器）、Hangfire 1.8.x、PdfPig、xUnit、Vue 3 + Vite + Tailwind CSS。

## Global Constraints（每個任務隱含遵守）

- 依賴方向：`Api → Infrastructure → Core`；Core 只准引用 `Microsoft.Data.SqlClient`（為了 `SqlVector<float>` 型別），不得引用 EF 或任何 AI SDK。
- Gemini chat：OpenAI 相容端點 `https://generativelanguage.googleapis.com/v1beta/openai/`，模型 `gemini-2.5-flash`。
- Gemini embedding：原生 REST `models/gemini-embedding-001:batchEmbedContents`，`outputDimensionality: 1536`，回傳向量**必須手動 L2 正規化**（官方要求：非 3072 維不自動正規化）。
- 切片：Markdown 依標題分段＋標題路徑前綴（超長段落內部 500 字元、10% 重疊細切；無標題退回固定切片）；PDF 固定 500 字元、10% 重疊。檢索 TOP 5、cosine 距離；部門過濾前置且只查 `Completed` 文件。
- 上傳限制：只收 `.pdf` / `.md`，≤ 20MB；embedding 批次 ≤ 64 段/次 HTTP。
- SSE 事件名固定：`token`、`sources`、`done`、`error`。
- 機密（連線字串、`Gemini:ApiKey`、`Jwt:SigningKey`）一律 `dotnet user-secrets`；demo 使用者密碼是唯一例外，直接放 `appsettings.json`。**開發期只餵假資料（AI Studio 免費層會被拿去訓練）。**
- 測試：整合測試標 `[Trait("Category", "Integration")]`，CI 用 `--filter "Category!=Integration"` 排除；單元測試斷言業務值，不准只斷言 not-null。
- 所有指令在 repo 根目錄 `KnowledgeHub/` 下執行（已是 git repo，含 `docs/`）。

---

### Task 1: 方案骨架、gitignore、CI（spec 階段 0）

**Files:**
- Create: `backend/KnowledgeHub.sln`、四個專案、`.gitignore`、`README.md`、`.github/workflows/ci.yml`

**Interfaces:**
- Produces: 空的可編譯方案；後續所有任務都在這個結構上工作。

- [ ] **Step 1: 建方案與專案**

```powershell
mkdir backend; cd backend
dotnet new sln -n KnowledgeHub
dotnet new webapi -n KnowledgeHub.Api --use-controllers
dotnet new classlib -n KnowledgeHub.Core
dotnet new classlib -n KnowledgeHub.Infrastructure
dotnet new xunit -n KnowledgeHub.Tests
dotnet sln add KnowledgeHub.Api KnowledgeHub.Core KnowledgeHub.Infrastructure KnowledgeHub.Tests
dotnet add KnowledgeHub.Api reference KnowledgeHub.Infrastructure
dotnet add KnowledgeHub.Infrastructure reference KnowledgeHub.Core
dotnet add KnowledgeHub.Tests reference KnowledgeHub.Api
dotnet add KnowledgeHub.Tests reference KnowledgeHub.Infrastructure
dotnet add KnowledgeHub.Tests reference KnowledgeHub.Core
```

確認四個 csproj 的 `<TargetFramework>` 都是 `net10.0`；刪掉範本自帶的 `Class1.cs`、`WeatherForecast` 相關檔案。

- [ ] **Step 2: 寫 `.gitignore`（repo 根目錄）**

```gitignore
bin/
obj/
node_modules/
dist/
uploads/
.env
*.user
.vs/
```

- [ ] **Step 3: 寫 README 雛形（repo 根目錄 `README.md`）**

內容至少含：一句話系統簡介、技術棧清單、`docs/` 指向設計文件、「Azure SQL 免費層 serverless 閒置會自動暫停，首個請求冷啟動 30–60 秒，demo 前先打 `GET /api/health`」注意事項、user-secrets 設定指令範例（key 名：`ConnectionStrings:Default`、`Gemini:ApiKey`、`Jwt:SigningKey`）、「擴充方向」一節（照設計文件 §13：hybrid search（BM25＋RRF）、reranker、評估集；另註明向量索引 DiskANN）。

- [ ] **Step 4: 寫 CI（`.github/workflows/ci.yml`）**

```yaml
name: CI
on: [push, pull_request]
jobs:
  backend:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet build backend/KnowledgeHub.sln --configuration Release
      - run: dotnet test backend/KnowledgeHub.sln --configuration Release --filter "Category!=Integration"
```

（前端 build job 在 Task 12 加入。）

- [ ] **Step 5: 驗證 build**

Run: `dotnet build backend/KnowledgeHub.sln`
Expected: Build succeeded, 0 Warning（範本殘留警告要清掉）。

- [ ] **Step 6: Commit 並推上 GitHub 確認 CI 綠**

```powershell
git add -A; git commit -m "chore: 方案骨架、gitignore、CI"
```

推上 GitHub 後開 Actions 頁面確認綠勾（repo 還沒建 remote 的話，先請使用者建立 GitHub repo 並告知 URL——這是需要使用者操作的點，不要自己猜 repo 名）。

---

### Task 2: Core 實體、enum 與介面

**Files:**
- Create: `backend/KnowledgeHub.Core/Entities/CompanyDocument.cs`、`DocumentChunk.cs`、`OutboxEmail.cs`
- Create: `backend/KnowledgeHub.Core/DocumentStatus.cs`、`ChunkSearchResult.cs`
- Create: `backend/KnowledgeHub.Core/Interfaces/IChunkRepository.cs`、`IEmbeddingService.cs`、`IOutboxEmailRepository.cs`、`IDocumentRepository.cs`、`IDocumentJobQueue.cs`、`IDocumentTextExtractor.cs`、`ICurrentUser.cs`
- Modify: `backend/KnowledgeHub.Core/KnowledgeHub.Core.csproj`（加 `Microsoft.Data.SqlClient`）

**Interfaces:**
- Produces（後續所有任務依賴這些簽名，逐字照抄）：

```csharp
namespace KnowledgeHub.Core;

public enum DocumentStatus { Pending, Processing, Completed, Failed }

public record ChunkSearchResult(
    Guid ChunkId, Guid DocumentId, string FileName,
    int SequenceNumber, string Content, double Distance);
```

```csharp
namespace KnowledgeHub.Core.Entities;
using Microsoft.Data.SqlClient;

public class CompanyDocument
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = "";
    public string Department { get; set; } = "";
    public DocumentStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public int ChunkCount { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public List<DocumentChunk> Chunks { get; set; } = [];
}

public class DocumentChunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public CompanyDocument Document { get; set; } = null!;
    public int SequenceNumber { get; set; }
    public string Content { get; set; } = "";
    public SqlVector<float> Embedding { get; set; }
}

public class OutboxEmail
{
    public Guid Id { get; set; }
    public string To { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
```

```csharp
namespace KnowledgeHub.Core.Interfaces;

public interface IChunkRepository
{
    Task<IReadOnlyList<ChunkSearchResult>> SearchSimilarChunksAsync(
        float[] queryVector, string department, int topK = 5, CancellationToken ct = default);
}

public interface IEmbeddingService
{
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}

public interface IOutboxEmailRepository
{
    Task AddAsync(OutboxEmail email, CancellationToken ct = default);
}

public interface IDocumentRepository
{
    Task<CompanyDocument?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<CompanyDocument>> ListByDepartmentAsync(string department, CancellationToken ct = default);
    Task AddAsync(CompanyDocument doc, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task SaveChunksAndCompleteAsync(Guid docId, IReadOnlyList<DocumentChunk> chunks, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid docId, DocumentStatus status, string? errorMessage = null, CancellationToken ct = default);
}

public interface IDocumentJobQueue
{
    void Enqueue(Guid documentId);
}

public interface IDocumentTextExtractor
{
    bool CanHandle(string fileExtension);   // ".pdf" / ".md"（小寫含點）
    string ExtractText(string filePath);
}

public interface ICurrentUser
{
    string Department { get; }
}
```

- [ ] **Step 1: 加套件**

```powershell
dotnet add backend/KnowledgeHub.Core package Microsoft.Data.SqlClient
```

（版本需 ≥ 6.1.1，`SqlVector<float>` 是 6.1 才有的型別。）

- [ ] **Step 2: 照上面的簽名建立所有檔案**（純 POCO 與介面，無邏輯，不寫單元測試）

- [ ] **Step 3: 驗證 build 後 commit**

Run: `dotnet build backend/KnowledgeHub.sln`
Expected: Build succeeded。

```powershell
git add -A; git commit -m "feat: Core 實體與介面"
```

---

### Task 3: TextChunker 與 MarkdownChunker 切片器（TDD）

**Files:**
- Create: `backend/KnowledgeHub.Core/TextChunker.cs`、`backend/KnowledgeHub.Core/MarkdownChunker.cs`
- Test: `backend/KnowledgeHub.Tests/TextChunkerTests.cs`、`backend/KnowledgeHub.Tests/MarkdownChunkerTests.cs`

**Interfaces:**
- Produces: `public static IReadOnlyList<string> TextChunker.Split(string text, int chunkSize = 500, double overlapRatio = 0.1)`（namespace `KnowledgeHub.Core`）。
- Produces: `public static IReadOnlyList<string> MarkdownChunker.Split(string text, int chunkSize = 500, double overlapRatio = 0.1)`（namespace `KnowledgeHub.Core`）——依 Markdown 標題分段、每片前綴標題路徑 `【A > B】\n`，段落超長時內部用 `TextChunker.Split` 細切（每片都保留前綴，故片長可能略超 chunkSize＋前綴長度）；全文無標題時行為與 `TextChunker.Split` 完全一致。
- Task 11 的背景 job 依副檔名路由：`.md` → `MarkdownChunker`、其他 → `TextChunker`。

- [ ] **Step 1: 寫失敗測試**

```csharp
using KnowledgeHub.Core;

public class TextChunkerTests
{
    [Fact]
    public void 空字串回空清單()
        => Assert.Empty(TextChunker.Split(""));

    [Fact]
    public void 純空白也回空清單()
        => Assert.Empty(TextChunker.Split("   \n  "));

    [Fact]
    public void 短於chunkSize回單片且等於原文()
    {
        var chunks = TextChunker.Split("短文", chunkSize: 500);
        Assert.Single(chunks);
        Assert.Equal("短文", chunks[0]);
    }

    [Fact]
    public void 長文切片_片長與重疊正確()
    {
        // 1200 字元、chunkSize 500、overlap 10%(50) → step 450 → 起點 0/450/900 → 長度 500/500/300
        var text = string.Concat(Enumerable.Range(0, 1200).Select(i => (char)('A' + i % 26)));
        var chunks = TextChunker.Split(text, chunkSize: 500, overlapRatio: 0.1);

        Assert.Equal(3, chunks.Count);
        Assert.Equal(500, chunks[0].Length);
        Assert.Equal(500, chunks[1].Length);
        Assert.Equal(300, chunks[2].Length);
        // 重疊驗證：第 2 片的前 50 字 == 第 1 片的最後 50 字
        Assert.Equal(chunks[0][^50..], chunks[1][..50]);
        // 內容無遺漏：去重疊後串回原文
        Assert.Equal(text, chunks[0] + chunks[1][50..] + chunks[2][50..]);
    }

    [Fact]
    public void 中文以字元計數()
    {
        var text = string.Concat(Enumerable.Repeat("知識庫測試", 30)); // 150 字元
        var chunks = TextChunker.Split(text, chunkSize: 100, overlapRatio: 0.1);
        Assert.Equal(2, chunks.Count);
        Assert.Equal(100, chunks[0].Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void chunkSize非正數丟例外(int size)
        => Assert.Throws<ArgumentOutOfRangeException>(() => TextChunker.Split("x", chunkSize: size));
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test backend/KnowledgeHub.Tests --filter TextChunkerTests`
Expected: FAIL（`TextChunker` 不存在，編譯錯誤即算失敗確認）。

- [ ] **Step 3: 最小實作**

```csharp
namespace KnowledgeHub.Core;

public static class TextChunker
{
    public static IReadOnlyList<string> Split(string text, int chunkSize = 500, double overlapRatio = 0.1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(chunkSize, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(overlapRatio);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(overlapRatio, 1.0);

        text = (text ?? "").Trim();
        if (text.Length == 0) return [];

        var overlap = (int)(chunkSize * overlapRatio);
        var step = chunkSize - overlap;
        var chunks = new List<string>();
        for (var start = 0; start < text.Length; start += step)
        {
            var length = Math.Min(chunkSize, text.Length - start);
            chunks.Add(text.Substring(start, length));
            if (start + length >= text.Length) break;
        }
        return chunks;
    }
}
```

- [ ] **Step 4: 跑測試確認全綠**

Run: `dotnet test backend/KnowledgeHub.Tests --filter TextChunkerTests`
Expected: PASS ×7。

- [ ] **Step 5: 寫 MarkdownChunker 失敗測試**

```csharp
using KnowledgeHub.Core;

public class MarkdownChunkerTests
{
    [Fact]
    public void 空字串回空清單()
        => Assert.Empty(MarkdownChunker.Split(""));

    [Fact]
    public void 無標題退回固定切片()
    {
        var text = new string('字', 1200);
        Assert.Equal(TextChunker.Split(text), MarkdownChunker.Split(text));
    }

    [Fact]
    public void 依標題分段_各片帶標題路徑前綴()
    {
        var md = "# 系統\n總覽說明\n## 重開機流程\n步驟一步驟二\n## 錯誤代碼\nE01 代表斷線";
        var chunks = MarkdownChunker.Split(md);
        Assert.Equal(3, chunks.Count);
        Assert.StartsWith("【系統】\n", chunks[0]);
        Assert.Contains("總覽說明", chunks[0]);
        Assert.StartsWith("【系統 > 重開機流程】\n", chunks[1]);
        Assert.Contains("步驟一步驟二", chunks[1]);
        Assert.StartsWith("【系統 > 錯誤代碼】\n", chunks[2]);
        Assert.Contains("E01 代表斷線", chunks[2]);
    }

    [Fact]
    public void 低階標題出現時_路徑收斂到該階()
    {
        var md = "# A\n## B\n內容一\n# C\n內容二";
        var chunks = MarkdownChunker.Split(md);
        Assert.Equal(2, chunks.Count);
        Assert.StartsWith("【A > B】\n", chunks[0]);
        Assert.StartsWith("【C】\n", chunks[1]);
    }

    [Fact]
    public void 標題下無內容_不產生chunk()
    {
        var md = "# 只有標題\n## 也只有標題\n有內容";
        var chunks = MarkdownChunker.Split(md);
        Assert.Single(chunks);
        Assert.Contains("有內容", chunks[0]);
    }

    [Fact]
    public void 超長段落_細切且每片都帶前綴()
    {
        var md = "# 長章節\n" + new string('字', 1200);
        var chunks = MarkdownChunker.Split(md, chunkSize: 500, overlapRatio: 0.1);
        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, c => Assert.StartsWith("【長章節】\n", c));
    }
}
```

- [ ] **Step 6: 跑測試確認失敗**

Run: `dotnet test backend/KnowledgeHub.Tests --filter MarkdownChunkerTests`
Expected: FAIL（`MarkdownChunker` 不存在，編譯錯誤即算失敗確認）。

- [ ] **Step 7: 最小實作**

```csharp
namespace KnowledgeHub.Core;

/// <summary>Markdown 標題感知切片：依標題分段、每片前綴標題路徑；全文無標題時退回 TextChunker 固定切片。</summary>
public static class MarkdownChunker
{
    public static IReadOnlyList<string> Split(string text, int chunkSize = 500, double overlapRatio = 0.1)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return [];

        var lines = text.Split('\n');
        if (!lines.Any(IsHeading)) return TextChunker.Split(text, chunkSize, overlapRatio);

        var chunks = new List<string>();
        var path = new List<(int Level, string Title)>();
        var body = new List<string>();

        void Flush()
        {
            var content = string.Join("\n", body).Trim();
            body.Clear();
            if (content.Length == 0) return;
            var prefix = path.Count == 0 ? "" : $"【{string.Join(" > ", path.Select(p => p.Title))}】\n";
            foreach (var piece in TextChunker.Split(content, chunkSize, overlapRatio))
                chunks.Add(prefix + piece);
        }

        foreach (var line in lines)
        {
            if (IsHeading(line))
            {
                Flush();
                var trimmed = line.TrimStart();
                var level = trimmed.TakeWhile(c => c == '#').Count();
                path.RemoveAll(p => p.Level >= level);
                path.Add((level, trimmed[level..].Trim()));
            }
            else body.Add(line);
        }
        Flush();
        return chunks;
    }

    private static bool IsHeading(string line)
    {
        var t = line.TrimStart();
        var hashes = t.TakeWhile(c => c == '#').Count();
        return hashes is >= 1 and <= 6 && t.Length > hashes && t[hashes] == ' ';
    }
}
```

- [ ] **Step 8: 跑測試確認全綠**

Run: `dotnet test backend/KnowledgeHub.Tests --filter "TextChunkerTests|MarkdownChunkerTests"`
Expected: PASS ×13（TextChunker 7＋MarkdownChunker 6，兩者都跑，確認未互相影響）。

- [ ] **Step 9: Commit**

```powershell
git add -A; git commit -m "feat: TextChunker 固定切片與 MarkdownChunker 標題感知切片"
```

---

### Task 4: DbContext、EF 模型與 migration（需要 Azure SQL 連線）

**Files:**
- Create: `backend/KnowledgeHub.Infrastructure/KnowledgeHubDbContext.cs`
- Create: `backend/KnowledgeHub.Api/Migrations/`（`dotnet ef` 產生）
- Modify: `backend/KnowledgeHub.Api/Program.cs`（註冊 DbContext＋`GET /api/health`）

**Interfaces:**
- Consumes: Task 2 的實體。
- Produces: `KnowledgeHubDbContext`（`DbSet<CompanyDocument> Documents`、`DbSet<DocumentChunk> DocumentChunks`、`DbSet<OutboxEmail> OutboxEmails`），Task 5/8/10/11 依賴。

- [ ] **Step 1: 加套件**

```powershell
dotnet add backend/KnowledgeHub.Infrastructure package Microsoft.EntityFrameworkCore.SqlServer
dotnet add backend/KnowledgeHub.Api package Microsoft.EntityFrameworkCore.Design
dotnet tool install --global dotnet-ef --version 10.*
```

- [ ] **Step 2: 寫 DbContext**

```csharp
using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Infrastructure;

public class KnowledgeHubDbContext(DbContextOptions<KnowledgeHubDbContext> options) : DbContext(options)
{
    public DbSet<CompanyDocument> Documents => Set<CompanyDocument>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<OutboxEmail> OutboxEmails => Set<OutboxEmail>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CompanyDocument>(e =>
        {
            e.Property(d => d.FileName).HasMaxLength(260);
            e.Property(d => d.Department).HasMaxLength(50);
            e.HasIndex(d => d.Department);
            e.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
            e.HasMany(d => d.Chunks).WithOne(c => c.Document)
                .HasForeignKey(c => c.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DocumentChunk>(e =>
        {
            e.Property(c => c.Embedding).HasColumnType("vector(1536)");
            e.HasIndex(c => new { c.DocumentId, c.SequenceNumber }).IsUnique();
        });

        modelBuilder.Entity<OutboxEmail>(e =>
        {
            e.Property(m => m.To).HasMaxLength(320);
            e.Property(m => m.Subject).HasMaxLength(500);
        });
    }
}
```

- [ ] **Step 3: Program.cs 註冊＋health 端點**

在 `builder.Services` 加：

```csharp
builder.Services.AddDbContext<KnowledgeHubDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
```

在 app 管線加：

```csharp
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
```

- [ ] **Step 4: 設定 user-secrets 連線字串（需使用者提供 Azure SQL 連線字串）**

```powershell
cd backend/KnowledgeHub.Api
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Default" "<Azure SQL 連線字串>"
```

連線字串還沒拿到就停下問使用者，不要用假值繼續。

- [ ] **Step 5: 建 migration 並套用**

```powershell
cd backend
dotnet ef migrations add InitialCreate --project KnowledgeHub.Infrastructure --startup-project KnowledgeHub.Api
dotnet ef database update --project KnowledgeHub.Infrastructure --startup-project KnowledgeHub.Api
```

Expected: 無錯誤。注意 Azure SQL 免費層冷啟動——第一次連線逾時就等 60 秒重跑一次（只重試一次，仍失敗則回報）。

- [ ] **Step 6: 驗證資料表真的建出來**

```powershell
dotnet run --project KnowledgeHub.Api
# 另開視窗
curl -k https://localhost:<port>/api/health
```

Expected: `{"status":"ok"}`；並以任一 SQL 工具或 `sqlcmd` 確認 `Documents`、`DocumentChunks`（Embedding 欄型別為 `vector`）、`OutboxEmails` 三張表存在。

- [ ] **Step 7: Commit**

```powershell
git add -A; git commit -m "feat: DbContext、VECTOR(1536) 模型與初始 migration"
```

---

### Task 5: ChunkRepository 向量搜尋（整合測試，CI 排除）

**Files:**
- Create: `backend/KnowledgeHub.Infrastructure/Repositories/ChunkRepository.cs`
- Test: `backend/KnowledgeHub.Tests/Integration/ChunkRepositoryTests.cs`

**Interfaces:**
- Consumes: `KnowledgeHubDbContext`（Task 4）、`IChunkRepository`（Task 2）。
- Produces: `ChunkRepository : IChunkRepository`，Task 8 的 RetrievalPlugin 依賴。

- [ ] **Step 1: 寫整合測試（先寫，因為沒有 DB 就跑不了，標 Integration）**

```csharp
using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Infrastructure;
using KnowledgeHub.Infrastructure.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KnowledgeHub.Tests.Integration;

[Trait("Category", "Integration")]
public class ChunkRepositoryTests : IAsyncLifetime
{
    private KnowledgeHubDbContext _db = null!;
    private readonly List<Guid> _createdDocIds = [];

    public async Task InitializeAsync()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets("<KnowledgeHub.Api 的 UserSecretsId，開工時抄 csproj>").Build();
        var options = new DbContextOptionsBuilder<KnowledgeHubDbContext>()
            .UseSqlServer(config.GetConnectionString("Default")).Options;
        _db = new KnowledgeHubDbContext(options);
        await SeedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.Documents.Where(d => _createdDocIds.Contains(d.Id)).ExecuteDeleteAsync();
        await _db.DisposeAsync();
    }

    // 1536 維單位向量：只有 index 位置是 1，其餘 0 → 彼此 cosine 距離 = 1，自身 = 0
    private static SqlVector<float> BasisVector(int index)
    {
        var v = new float[1536];
        v[index] = 1f;
        return new SqlVector<float>(v);
    }

    private async Task SeedAsync()
    {
        var itDoc = NewDoc("it.md", "IT", DocumentStatus.Completed,
            (0, "IT 段落 0", BasisVector(0)), (1, "IT 段落 1", BasisVector(1)));
        var hrDoc = NewDoc("hr.md", "HR", DocumentStatus.Completed,
            (0, "HR 段落 0", BasisVector(0)));
        var pendingIt = NewDoc("pending.md", "IT", DocumentStatus.Pending,
            (0, "未完成文件的段落", BasisVector(0)));
        _db.Documents.AddRange(itDoc, hrDoc, pendingIt);
        await _db.SaveChangesAsync();
    }

    private CompanyDocument NewDoc(string name, string dept, DocumentStatus status,
        params (int Seq, string Content, SqlVector<float> Emb)[] chunks)
    {
        var doc = new CompanyDocument
        {
            Id = Guid.NewGuid(), FileName = name, Department = dept, Status = status,
            UploadedAtUtc = DateTime.UtcNow, ChunkCount = chunks.Length,
            Chunks = chunks.Select(c => new DocumentChunk
            {
                Id = Guid.NewGuid(), SequenceNumber = c.Seq,
                Content = c.Content, Embedding = c.Emb
            }).ToList()
        };
        _createdDocIds.Add(doc.Id);
        return doc;
    }

    [Fact]
    public async Task 依cosine距離排序_部門與狀態過濾生效()
    {
        var repo = new ChunkRepository(_db);
        var query = new float[1536];
        query[0] = 1f; // 與 BasisVector(0) 完全同向

        var results = await repo.SearchSimilarChunksAsync(query, "IT", topK: 5);

        // 只回 IT 且 Completed：命中 2 段（排除 HR 的與 Pending 的）
        Assert.Equal(2, results.Count);
        Assert.Equal("IT 段落 0", results[0].Content);   // 距離 0，排最前
        Assert.Equal("IT 段落 1", results[1].Content);   // 距離 1
        Assert.True(results[0].Distance < 0.0001);
        Assert.Equal("it.md", results[0].FileName);
        Assert.DoesNotContain(results, r => r.Content.Contains("HR"));
        Assert.DoesNotContain(results, r => r.Content.Contains("未完成"));
    }

    [Fact]
    public async Task topK限制回傳筆數()
    {
        var repo = new ChunkRepository(_db);
        var query = new float[1536];
        query[0] = 1f;

        var results = await repo.SearchSimilarChunksAsync(query, "IT", topK: 1);
        Assert.Single(results);
    }
}
```

- [ ] **Step 2: 寫實作**

```csharp
using KnowledgeHub.Core;
using KnowledgeHub.Core.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Infrastructure.Repositories;

public class ChunkRepository(KnowledgeHubDbContext db) : IChunkRepository
{
    public async Task<IReadOnlyList<ChunkSearchResult>> SearchSimilarChunksAsync(
        float[] queryVector, string department, int topK = 5, CancellationToken ct = default)
    {
        var qv = new SqlVector<float>(queryVector);
        return await db.DocumentChunks
            .Where(c => c.Document.Department == department
                     && c.Document.Status == DocumentStatus.Completed)
            .OrderBy(c => EF.Functions.VectorDistance("cosine", c.Embedding, qv))
            .Take(topK)
            .Select(c => new ChunkSearchResult(
                c.Id, c.DocumentId, c.Document.FileName, c.SequenceNumber, c.Content,
                EF.Functions.VectorDistance("cosine", c.Embedding, qv)))
            .ToListAsync(ct);
    }
}
```

- [ ] **Step 3: 本機對 Azure SQL 跑整合測試**

Run: `dotnet test backend/KnowledgeHub.Tests --filter "Category=Integration"`
Expected: PASS ×2。失敗常見原因：冷啟動逾時（等 60 秒重跑一次）、user-secrets id 沒對上。

- [ ] **Step 4: 確認 CI 過濾器真的排除它**

Run: `dotnet test backend/KnowledgeHub.Tests --filter "Category!=Integration"`
Expected: 整合測試 0 執行（輸出的測試數不含上面 2 個）。

- [ ] **Step 5: Commit**

```powershell
git add -A; git commit -m "feat: 向量相似度檢索 Repository（部門/狀態過濾＋整合測試）"
```

---

### Task 6: JWT 認證與種子使用者

**Files:**
- Create: `backend/KnowledgeHub.Api/Auth/TokenService.cs`、`Auth/SeedUser.cs`、`Controllers/AuthController.cs`、`Auth/CurrentUser.cs`
- Modify: `backend/KnowledgeHub.Api/Program.cs`、`appsettings.json`
- Test: `backend/KnowledgeHub.Tests/TokenServiceTests.cs`、`AuthControllerTests.cs`

**Interfaces:**
- Consumes: `ICurrentUser`（Task 2）。
- Produces: `POST /api/auth/login`（body `{ "username", "password" }` → `{ "token" }`／401）；JWT 內含 claim `department`；`CurrentUser : ICurrentUser`（從 HttpContext 取 claim），Task 8/10 依賴。

- [ ] **Step 1: 加套件與設定**

```powershell
dotnet add backend/KnowledgeHub.Api package Microsoft.AspNetCore.Authentication.JwtBearer
```

`appsettings.json` 加（demo 密碼是刻意公開的，README 已注明；SigningKey 不在此，走 user-secrets）：

```json
{
  "Jwt": { "Issuer": "KnowledgeHub", "Audience": "KnowledgeHub" },
  "SeedUsers": [
    { "Username": "hr-user",  "Password": "demo-hr-2026",  "Department": "HR" },
    { "Username": "it-user",  "Password": "demo-it-2026",  "Department": "IT" },
    { "Username": "fin-user", "Password": "demo-fin-2026", "Department": "Finance" }
  ]
}
```

```powershell
cd backend/KnowledgeHub.Api
dotnet user-secrets set "Jwt:SigningKey" "<至少 32 字元的隨機字串>"
```

- [ ] **Step 2: 寫失敗測試**

```csharp
using System.IdentityModel.Tokens.Jwt;
using KnowledgeHub.Api.Auth;
using Microsoft.AspNetCore.Mvc;

public class TokenServiceTests
{
    private static TokenService NewService() =>
        new(signingKey: new string('k', 32), issuer: "KnowledgeHub", audience: "KnowledgeHub");

    [Fact]
    public void Token含department與sub_claim()
    {
        var token = NewService().CreateToken(username: "it-user", department: "IT");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal("IT", jwt.Claims.Single(c => c.Type == "department").Value);
        Assert.Equal("it-user", jwt.Claims.Single(c => c.Type == "sub").Value);
        Assert.Equal("KnowledgeHub", jwt.Issuer);
    }
}

public class AuthControllerTests
{
    private static readonly SeedUser[] Users =
        [new() { Username = "it-user", Password = "demo-it-2026", Department = "IT" }];

    private static AuthController NewController() =>
        new(Users, new TokenService(new string('k', 32), "KnowledgeHub", "KnowledgeHub"));

    [Fact]
    public void 帳密正確回token()
    {
        var result = NewController().Login(new LoginRequest("it-user", "demo-it-2026"));
        var ok = Assert.IsType<OkObjectResult>(result);
        var token = Assert.IsType<LoginResponse>(ok.Value).Token;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("IT", jwt.Claims.Single(c => c.Type == "department").Value);
    }

    [Theory]
    [InlineData("it-user", "wrong")]
    [InlineData("nobody", "demo-it-2026")]
    public void 帳密錯誤回401(string user, string pass)
        => Assert.IsType<UnauthorizedResult>(NewController().Login(new LoginRequest(user, pass)));
}
```

- [ ] **Step 3: 跑測試確認失敗**

Run: `dotnet test backend/KnowledgeHub.Tests --filter "TokenServiceTests|AuthControllerTests"`
Expected: 編譯錯誤（型別不存在）。

- [ ] **Step 4: 實作**

```csharp
// Auth/SeedUser.cs
namespace KnowledgeHub.Api.Auth;
public class SeedUser
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Department { get; set; } = "";
}

// Auth/TokenService.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace KnowledgeHub.Api.Auth;

public class TokenService(string signingKey, string issuer, string audience)
{
    public string CreateToken(string username, string department)
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: issuer, audience: audience,
            claims: [new Claim("sub", username), new Claim("department", department)],
            expires: DateTime.UtcNow.AddHours(8), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

// Controllers/AuthController.cs
using KnowledgeHub.Api.Auth;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeHub.Api.Controllers;

public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token);

[ApiController]
[Route("api/auth")]
public class AuthController(IEnumerable<SeedUser> users, TokenService tokens) : ControllerBase
{
    [HttpPost("login")]
    public IActionResult Login(LoginRequest request)
    {
        var user = users.FirstOrDefault(u =>
            u.Username == request.Username && u.Password == request.Password);
        return user is null
            ? Unauthorized()
            : Ok(new LoginResponse(tokens.CreateToken(user.Username, user.Department)));
    }
}

// Auth/CurrentUser.cs
using KnowledgeHub.Core.Interfaces;

namespace KnowledgeHub.Api.Auth;

public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string Department =>
        accessor.HttpContext?.User.FindFirst("department")?.Value
        ?? throw new InvalidOperationException("缺少 department claim");
}
```

Program.cs 加（JWT bearer 驗證＋DI）：

```csharp
var jwtKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("缺少 Jwt:SigningKey（user-secrets）");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        // 保留 "department"/"sub" 原始 claim 名
        NameClaimType = "sub"
    });
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(builder.Configuration.GetSection("SeedUsers").Get<SeedUser[]>()!.AsEnumerable());
builder.Services.AddSingleton(new TokenService(jwtKey,
    builder.Configuration["Jwt:Issuer"]!, builder.Configuration["Jwt:Audience"]!));
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
// app 管線：app.UseAuthentication(); app.UseAuthorization();
```

注意：JwtBearer 預設會把 `sub` 映射成長 URI claim 名，若測到 claim 名不是 `department`/`sub`，在 AddJwtBearer 前加 `JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();`。

- [ ] **Step 5: 跑測試確認全綠**

Run: `dotnet test backend/KnowledgeHub.Tests --filter "TokenServiceTests|AuthControllerTests"`
Expected: PASS ×4。

- [ ] **Step 6: 實跑驗證**

```powershell
dotnet run --project backend/KnowledgeHub.Api
curl -k -X POST https://localhost:<port>/api/auth/login -H "Content-Type: application/json" -d '{"username":"it-user","password":"demo-it-2026"}'
```

Expected: 200 + token；錯密碼回 401。把輸出貼進回報。

- [ ] **Step 7: Commit**

```powershell
git add -A; git commit -m "feat: JWT 登入與種子使用者（department claim）"
```

---

### Task 7: GeminiEmbeddingService（原生 REST、1536 維、正規化、批次 ≤64）

**Files:**
- Create: `backend/KnowledgeHub.Infrastructure/Ai/GeminiEmbeddingService.cs`
- Test: `backend/KnowledgeHub.Tests/GeminiEmbeddingServiceTests.cs`

**Interfaces:**
- Consumes: `IEmbeddingService`（Task 2）。
- Produces: `GeminiEmbeddingService : IEmbeddingService`。建構子 `GeminiEmbeddingService(HttpClient http, string apiKey)`；HttpClient 的 BaseAddress 由 DI 設為 `https://generativelanguage.googleapis.com/`。Task 8/11 依賴。

- [ ] **Step 1: 寫失敗測試（fake HttpMessageHandler）**

```csharp
using System.Net;
using System.Text.Json;
using KnowledgeHub.Infrastructure.Ai;

public class GeminiEmbeddingServiceTests
{
    // 回錄請求並回傳指定向量的假 handler
    private sealed class FakeHandler(Func<int, float[]> vectorFactory) : HttpMessageHandler
    {
        public List<JsonDocument> CapturedBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            CapturedBodies.Add(body);
            var count = body.RootElement.GetProperty("requests").GetArrayLength();
            var embeddings = Enumerable.Range(0, count)
                .Select(i => new { values = vectorFactory(i) });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { embeddings }))
            };
        }
    }

    private static GeminiEmbeddingService NewService(FakeHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/") },
            apiKey: "test-key");

    [Fact]
    public async Task 請求含outputDimensionality1536與正確模型()
    {
        var handler = new FakeHandler(_ => [3f, 4f]);
        await NewService(handler).EmbedAsync(["哈囉"]);

        var req = handler.CapturedBodies.Single().RootElement
            .GetProperty("requests")[0];
        Assert.Equal(1536, req.GetProperty("outputDimensionality").GetInt32());
        Assert.Equal("models/gemini-embedding-001", req.GetProperty("model").GetString());
        Assert.Equal("哈囉", req.GetProperty("content").GetProperty("parts")[0]
            .GetProperty("text").GetString());
    }

    [Fact]
    public async Task 回傳向量有做L2正規化()
    {
        var handler = new FakeHandler(_ => [3f, 4f]); // 長度 5
        var result = await NewService(handler).EmbedAsync(["x"]);

        Assert.Equal(0.6f, result[0][0], precision: 5); // 3/5
        Assert.Equal(0.8f, result[0][1], precision: 5); // 4/5
    }

    [Fact]
    public async Task 超過64段自動分批()
    {
        var handler = new FakeHandler(_ => [1f]);
        var texts = Enumerable.Range(0, 130).Select(i => $"段{i}").ToList();

        var result = await NewService(handler).EmbedAsync(texts);

        Assert.Equal(130, result.Count);
        Assert.Equal(3, handler.CapturedBodies.Count); // 64+64+2
        Assert.Equal(64, handler.CapturedBodies[0].RootElement.GetProperty("requests").GetArrayLength());
        Assert.Equal(2,  handler.CapturedBodies[2].RootElement.GetProperty("requests").GetArrayLength());
    }

    [Fact]
    public async Task 空清單不打API()
    {
        var handler = new FakeHandler(_ => [1f]);
        var result = await NewService(handler).EmbedAsync([]);
        Assert.Empty(result);
        Assert.Empty(handler.CapturedBodies);
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test backend/KnowledgeHub.Tests --filter GeminiEmbeddingServiceTests`
Expected: 編譯錯誤（型別不存在）。

- [ ] **Step 3: 實作**

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeHub.Core.Interfaces;

namespace KnowledgeHub.Infrastructure.Ai;

public class GeminiEmbeddingService(HttpClient http, string apiKey) : IEmbeddingService
{
    private const int BatchSize = 64;
    private const int Dimensions = 1536;
    private const string Model = "models/gemini-embedding-001";

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var all = new List<float[]>(texts.Count);
        foreach (var batch in texts.Chunk(BatchSize))
        {
            var payload = new
            {
                requests = batch.Select(t => new
                {
                    model = Model,
                    content = new { parts = new[] { new { text = t } } },
                    outputDimensionality = Dimensions
                })
            };
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"v1beta/{Model}:batchEmbedContents");
            request.Headers.Add("x-goog-api-key", apiKey);
            request.Content = JsonContent.Create(payload);

            var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            foreach (var e in json.RootElement.GetProperty("embeddings").EnumerateArray())
            {
                var vector = e.GetProperty("values").EnumerateArray()
                    .Select(v => v.GetSingle()).ToArray();
                all.Add(Normalize(vector));
            }
        }
        return all;
    }

    // gemini-embedding-001 非 3072 維的輸出未正規化，官方要求自行做 L2 正規化
    private static float[] Normalize(float[] v)
    {
        var norm = MathF.Sqrt(v.Sum(x => x * x));
        return norm == 0 ? v : v.Select(x => x / norm).ToArray();
    }
}
```

- [ ] **Step 4: 跑測試確認全綠**

Run: `dotnet test backend/KnowledgeHub.Tests --filter GeminiEmbeddingServiceTests`
Expected: PASS ×4。

- [ ] **Step 5: Commit**

```powershell
git add -A; git commit -m "feat: Gemini embedding 服務（1536 維、L2 正規化、批次 64）"
```

---

### Task 8: RetrievalContext、RetrievalPlugin、EmailPlugin

**Files:**
- Create: `backend/KnowledgeHub.Core/RetrievalContext.cs`
- Create: `backend/KnowledgeHub.Infrastructure/Ai/RetrievalPlugin.cs`、`Ai/EmailPlugin.cs`
- Create: `backend/KnowledgeHub.Infrastructure/Repositories/OutboxEmailRepository.cs`
- Test: `backend/KnowledgeHub.Tests/RetrievalPluginTests.cs`、`EmailPluginTests.cs`

**Interfaces:**
- Consumes: `IEmbeddingService`、`IChunkRepository`、`IOutboxEmailRepository`、`ICurrentUser`（Task 2）。
- Produces:
  - `RetrievalContext`（Core）：`public class RetrievalContext { public List<ChunkSearchResult> Results { get; } = []; }`，per-request scoped。
  - `RetrievalPlugin.SearchKnowledgeBaseAsync(string query) : Task<string>`（KernelFunction 名 `search_knowledge_base`）
  - `EmailPlugin.SendEmailAsync(string to, string subject, string body) : Task<string>`（KernelFunction 名 `send_email`）
  - Task 9 的 Kernel 組裝依賴這兩個 plugin；SSE 的 `sources` 事件依賴 `RetrievalContext`。

- [ ] **Step 1: 寫失敗測試**

```csharp
using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using KnowledgeHub.Infrastructure.Ai;

public class RetrievalPluginTests
{
    private sealed class FakeEmbedding : IEmbeddingService
    {
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<float[]>>([new float[1536]]);
    }

    private sealed class FakeChunks(IReadOnlyList<ChunkSearchResult> results) : IChunkRepository
    {
        public string? QueriedDepartment;
        public Task<IReadOnlyList<ChunkSearchResult>> SearchSimilarChunksAsync(
            float[] queryVector, string department, int topK = 5, CancellationToken ct = default)
        {
            QueriedDepartment = department;
            return Task.FromResult(results);
        }
    }

    private sealed class FakeUser : ICurrentUser { public string Department => "IT"; }

    private static readonly ChunkSearchResult Hit =
        new(Guid.NewGuid(), Guid.NewGuid(), "sop.md", 3, "重開 POS 主機的步驟…", 0.12);

    [Fact]
    public async Task 命中時_回傳文字含來源與內容_且寫入context()
    {
        var context = new RetrievalContext();
        var chunks = new FakeChunks([Hit]);
        var plugin = new RetrievalPlugin(new FakeEmbedding(), chunks, context, new FakeUser());

        var answer = await plugin.SearchKnowledgeBaseAsync("POS 怎麼重開");

        Assert.Contains("sop.md", answer);
        Assert.Contains("重開 POS 主機的步驟", answer);
        Assert.Equal("IT", chunks.QueriedDepartment);   // 部門取自 claim，不是參數
        Assert.Single(context.Results);
        Assert.Equal(Hit, context.Results[0]);
    }

    [Fact]
    public async Task 無命中_回傳查無訊息_context保持空()
    {
        var context = new RetrievalContext();
        var plugin = new RetrievalPlugin(new FakeEmbedding(), new FakeChunks([]), context, new FakeUser());

        var answer = await plugin.SearchKnowledgeBaseAsync("完全無關的問題");

        Assert.Contains("找不到相關資料", answer);
        Assert.Empty(context.Results);
    }
}

public class EmailPluginTests
{
    private sealed class FakeOutbox : IOutboxEmailRepository
    {
        public readonly List<OutboxEmail> Saved = [];
        public Task AddAsync(OutboxEmail email, CancellationToken ct = default)
        {
            Saved.Add(email);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task 寄信寫入outbox_欄位正確()
    {
        var outbox = new FakeOutbox();
        var plugin = new EmailPlugin(outbox);

        var result = await plugin.SendEmailAsync("boss@qburger.com.tw", "週報", "本週進度…");

        var saved = Assert.Single(outbox.Saved);
        Assert.Equal("boss@qburger.com.tw", saved.To);
        Assert.Equal("週報", saved.Subject);
        Assert.Equal("本週進度…", saved.Body);
        Assert.Contains("已寄出", result);
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test backend/KnowledgeHub.Tests --filter "RetrievalPluginTests|EmailPluginTests"`
Expected: 編譯錯誤。

- [ ] **Step 3: 實作**

```csharp
// Core/RetrievalContext.cs
namespace KnowledgeHub.Core;
public class RetrievalContext
{
    public List<ChunkSearchResult> Results { get; } = [];
}

// Infrastructure/Ai/RetrievalPlugin.cs
using System.ComponentModel;
using System.Text;
using KnowledgeHub.Core;
using KnowledgeHub.Core.Interfaces;
using Microsoft.SemanticKernel;

namespace KnowledgeHub.Infrastructure.Ai;

public class RetrievalPlugin(
    IEmbeddingService embedding, IChunkRepository chunks,
    RetrievalContext context, ICurrentUser user)
{
    [KernelFunction("search_knowledge_base")]
    [Description("搜尋公司知識庫，回傳與問題最相關的文件段落。回答任何公司規章、SOP、文件相關問題前必須先呼叫。")]
    public async Task<string> SearchKnowledgeBaseAsync(
        [Description("要查詢的問題")] string query)
    {
        var vector = (await embedding.EmbedAsync([query]))[0];
        var results = await chunks.SearchSimilarChunksAsync(vector, user.Department, topK: 5);
        if (results.Count == 0) return "知識庫中找不到相關資料。";

        context.Results.AddRange(results);
        var sb = new StringBuilder();
        for (var i = 0; i < results.Count; i++)
            sb.AppendLine($"[來源{i + 1}] {results[i].FileName} 第{results[i].SequenceNumber}段：{results[i].Content}");
        return sb.ToString();
    }
}

// Infrastructure/Ai/EmailPlugin.cs
using System.ComponentModel;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using Microsoft.SemanticKernel;

namespace KnowledgeHub.Infrastructure.Ai;

public class EmailPlugin(IOutboxEmailRepository outbox)
{
    [KernelFunction("send_email")]
    [Description("寄送 email 通知給指定收件人")]
    public async Task<string> SendEmailAsync(
        [Description("收件人 email")] string to,
        [Description("主旨")] string subject,
        [Description("內文")] string body)
    {
        await outbox.AddAsync(new OutboxEmail
        {
            Id = Guid.NewGuid(), To = to, Subject = subject, Body = body,
            CreatedAtUtc = DateTime.UtcNow
        });
        return $"已寄出給 {to}。";
    }
}

// Infrastructure/Repositories/OutboxEmailRepository.cs
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;

namespace KnowledgeHub.Infrastructure.Repositories;

public class OutboxEmailRepository(KnowledgeHubDbContext db) : IOutboxEmailRepository
{
    public async Task AddAsync(OutboxEmail email, CancellationToken ct = default)
    {
        db.OutboxEmails.Add(email);
        await db.SaveChangesAsync(ct);
    }
}
```

需要套件：`dotnet add backend/KnowledgeHub.Infrastructure package Microsoft.SemanticKernel`

- [ ] **Step 4: 跑測試確認全綠**

Run: `dotnet test backend/KnowledgeHub.Tests --filter "RetrievalPluginTests|EmailPluginTests"`
Expected: PASS ×3。

- [ ] **Step 5: Commit**

```powershell
git add -A; git commit -m "feat: RetrievalPlugin、EmailPlugin 與 RetrievalContext"
```

---

### Task 9: Chat 服務、Kernel 組裝與 SSE 端點

**Files:**
- Create: `backend/KnowledgeHub.Core/Interfaces/IChatService.cs`（含 `ChatTurn`）
- Create: `backend/KnowledgeHub.Infrastructure/Ai/SemanticKernelChatService.cs`
- Create: `backend/KnowledgeHub.Api/Sse/ChatSseStreamer.cs`、`Controllers/ChatController.cs`
- Modify: `backend/KnowledgeHub.Api/Program.cs`（Kernel 與服務 DI）
- Test: `backend/KnowledgeHub.Tests/ChatSseStreamerTests.cs`

**Interfaces:**
- Consumes: Task 8 的兩個 plugin 與 `RetrievalContext`。
- Produces:

```csharp
namespace KnowledgeHub.Core.Interfaces;

public record ChatTurn(string Role, string Content);   // Role: "user" | "assistant"

public interface IChatService
{
    IAsyncEnumerable<string> StreamAnswerAsync(
        string message, IReadOnlyList<ChatTurn> history, CancellationToken ct = default);
}
```

  - `POST /api/chat`（`[Authorize]`，body `{ "message": string, "history": ChatTurn[] }`）→ `text/event-stream`，事件依序：多個 `token`（`data: {"text":"..."}`）→ 有檢索結果才發一個 `sources`（`data: [{fileName, sequenceNumber, content, distance}, ...]`）→ `done`（`data: {}`）；中途例外 → `error`（`data: {"message":"..."}`）後結束。Task 14 前端依賴此協定。

- [ ] **Step 1: 寫失敗測試（SSE 排程邏輯，用 fake IChatService＋MemoryStream）**

```csharp
using System.Text;
using KnowledgeHub.Api.Sse;
using KnowledgeHub.Core;
using KnowledgeHub.Core.Interfaces;

public class ChatSseStreamerTests
{
    private sealed class FakeChat(IEnumerable<string> tokens, Exception? throwAfter = null) : IChatService
    {
        public async IAsyncEnumerable<string> StreamAnswerAsync(
            string message, IReadOnlyList<ChatTurn> history,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var t in tokens) { yield return t; await Task.Yield(); }
            if (throwAfter is not null) throw throwAfter;
        }
    }

    private static async Task<string> RunAsync(IChatService chat, RetrievalContext context)
    {
        using var stream = new MemoryStream();
        await new ChatSseStreamer(chat, context).StreamAsync(
            stream, "問題", [], CancellationToken.None);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    [Fact]
    public async Task 有檢索結果_依序發token_sources_done()
    {
        var context = new RetrievalContext();
        context.Results.Add(new ChunkSearchResult(
            Guid.NewGuid(), Guid.NewGuid(), "sop.md", 2, "內容", 0.1));

        var output = await RunAsync(new FakeChat(["你", "好"]), context);

        Assert.Contains("event: token\ndata: {\"text\":\"你\"}", output);
        Assert.Contains("event: sources\n", output);
        Assert.Contains("\"fileName\":\"sop.md\"", output);
        Assert.Contains("event: done\n", output);
        // 順序：最後一個 token 在 sources 前，sources 在 done 前
        Assert.True(output.IndexOf("event: sources") > output.LastIndexOf("event: token"));
        Assert.True(output.IndexOf("event: done") > output.IndexOf("event: sources"));
    }

    [Fact]
    public async Task 模型沒查庫_不發sources()
    {
        var output = await RunAsync(new FakeChat(["嗨"]), new RetrievalContext());
        Assert.DoesNotContain("event: sources", output);
        Assert.Contains("event: done\n", output);
    }

    [Fact]
    public async Task 串流中途例外_發error後結束_不發done()
    {
        var output = await RunAsync(
            new FakeChat(["前半"], throwAfter: new InvalidOperationException("boom")), new RetrievalContext());
        Assert.Contains("event: token\ndata: {\"text\":\"前半\"}", output);
        Assert.Contains("event: error\n", output);
        Assert.DoesNotContain("event: done", output);
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test backend/KnowledgeHub.Tests --filter ChatSseStreamerTests`
Expected: 編譯錯誤。

- [ ] **Step 3: 實作 ChatSseStreamer 與 Controller**

```csharp
// Api/Sse/ChatSseStreamer.cs
using System.Text;
using System.Text.Json;
using KnowledgeHub.Core;
using KnowledgeHub.Core.Interfaces;

namespace KnowledgeHub.Api.Sse;

public class ChatSseStreamer(IChatService chat, RetrievalContext context)
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task StreamAsync(Stream output, string message,
        IReadOnlyList<ChatTurn> history, CancellationToken ct)
    {
        try
        {
            await foreach (var token in chat.StreamAnswerAsync(message, history, ct))
                await WriteEventAsync(output, "token", JsonSerializer.Serialize(new { text = token }, JsonOpts), ct);

            if (context.Results.Count > 0)
            {
                var sources = context.Results.Select(r => new
                    { r.FileName, r.SequenceNumber, r.Content, r.Distance });
                await WriteEventAsync(output, "sources", JsonSerializer.Serialize(sources, JsonOpts), ct);
            }
            await WriteEventAsync(output, "done", "{}", ct);
        }
        catch (Exception ex)
        {
            await WriteEventAsync(output, "error",
                JsonSerializer.Serialize(new { message = ex.Message }, JsonOpts), ct);
        }
    }

    private static async Task WriteEventAsync(Stream output, string name, string data, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes($"event: {name}\ndata: {data}\n\n");
        await output.WriteAsync(bytes, ct);
        await output.FlushAsync(ct);
    }
}

// Api/Controllers/ChatController.cs
using KnowledgeHub.Api.Sse;
using KnowledgeHub.Core;
using KnowledgeHub.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeHub.Api.Controllers;

public record ChatRequest(string Message, List<ChatTurn> History);

[ApiController]
[Route("api/chat")]
[Authorize]
public class ChatController(IChatService chat, RetrievalContext context) : ControllerBase
{
    [HttpPost]
    public async Task Post(ChatRequest request, CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        await new ChatSseStreamer(chat, context)
            .StreamAsync(Response.Body, request.Message, request.History, ct);
    }
}
```

- [ ] **Step 4: 跑測試確認全綠**

Run: `dotnet test backend/KnowledgeHub.Tests --filter ChatSseStreamerTests`
Expected: PASS ×3。

- [ ] **Step 5: 實作 SemanticKernelChatService 與 Kernel DI**

```csharp
// Infrastructure/Ai/SemanticKernelChatService.cs
using System.Runtime.CompilerServices;
using KnowledgeHub.Core.Interfaces;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace KnowledgeHub.Infrastructure.Ai;

public class SemanticKernelChatService(Kernel kernel) : IChatService
{
    private const string SystemPrompt =
        "你是 QBurger 的企業知識庫助理。回答公司文件、SOP、規章問題前，必須先呼叫 search_knowledge_base 查詢；" +
        "根據查到的段落回答並保持忠實，查不到就直說知識庫沒有相關資料，不可自行編造。使用繁體中文回答。";

    public async IAsyncEnumerable<string> StreamAnswerAsync(
        string message, IReadOnlyList<ChatTurn> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var chatHistory = new ChatHistory(SystemPrompt);
        foreach (var turn in history)
        {
            if (turn.Role == "user") chatHistory.AddUserMessage(turn.Content);
            else chatHistory.AddAssistantMessage(turn.Content);
        }
        chatHistory.AddUserMessage(message);

        var settings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };
        var service = kernel.GetRequiredService<IChatCompletionService>();
        await foreach (var delta in service.GetStreamingChatMessageContentsAsync(
            chatHistory, settings, kernel, ct))
        {
            if (!string.IsNullOrEmpty(delta.Content))
                yield return delta.Content;
        }
    }
}
```

Program.cs 的 DI（全部 Scoped，因為 RetrievalContext 是 per-request）：

```csharp
builder.Services.AddScoped<RetrievalContext>();
builder.Services.AddScoped<IChunkRepository, ChunkRepository>();
builder.Services.AddScoped<IOutboxEmailRepository, OutboxEmailRepository>();
builder.Services.AddHttpClient<IEmbeddingService, GeminiEmbeddingService>(c =>
        c.BaseAddress = new Uri("https://generativelanguage.googleapis.com/"))
    .AddTypedClient<IEmbeddingService>((http, sp) =>
        new GeminiEmbeddingService(http, builder.Configuration["Gemini:ApiKey"]!));
builder.Services.AddScoped<RetrievalPlugin>();
builder.Services.AddScoped<EmailPlugin>();
builder.Services.AddScoped<IChatService, SemanticKernelChatService>();
builder.Services.AddScoped(sp =>
{
    var kb = Kernel.CreateBuilder();
    kb.AddOpenAIChatCompletion(
        modelId: builder.Configuration["Gemini:ChatModel"] ?? "gemini-2.5-flash",
        endpoint: new Uri("https://generativelanguage.googleapis.com/v1beta/openai/"),
        apiKey: builder.Configuration["Gemini:ApiKey"]!);
    var kernel = kb.Build();
    kernel.Plugins.AddFromObject(sp.GetRequiredService<RetrievalPlugin>(), "retrieval");
    kernel.Plugins.AddFromObject(sp.GetRequiredService<EmailPlugin>(), "email");
    return kernel;
});
```

注意：`AddOpenAIChatCompletion` 帶自訂 `endpoint` 的 overload 若當下 SK 版本簽名不同，改建 `OpenAIClient`（`OpenAIClientOptions.Endpoint` 指到相容端點）再用 `AddOpenAIChatCompletion(modelId, openAIClient)` 傳入——以編譯器與當版 SK 文件為準，不要硬湊。

```powershell
cd backend/KnowledgeHub.Api
dotnet user-secrets set "Gemini:ApiKey" "<AI Studio 免費層 key，只餵假資料>"
```

- [ ] **Step 6: curl 實測（spec 階段 2 驗收）**

```powershell
dotnet run --project backend/KnowledgeHub.Api
# 先 login 拿 token，再：
curl -k -N -X POST https://localhost:<port>/api/chat -H "Authorization: Bearer <token>" -H "Content-Type: application/json" -d '{"message":"幫我寄信給 test@example.com 主旨測試 內容哈囉","history":[]}'
```

Expected: 串流輸出 `token` 事件；問完後查 `OutboxEmails` 表有一筆紀錄。再問一個知識庫問題（此時庫是空的）→ 回答應表明查無資料且不發 `sources`。把兩次輸出貼進回報。

- [ ] **Step 7: Commit**

```powershell
git add -A; git commit -m "feat: SK Agent、SSE 聊天端點與 Gemini 接線"
```

---

### Task 10: 文件上傳 API 與清單/刪除

**Files:**
- Create: `backend/KnowledgeHub.Core/UploadOptions.cs`（`namespace KnowledgeHub.Core; public record UploadOptions(string Root);`）
- Create: `backend/KnowledgeHub.Api/Controllers/DocumentsController.cs`
- Create: `backend/KnowledgeHub.Infrastructure/Repositories/DocumentRepository.cs`
- Test: `backend/KnowledgeHub.Tests/DocumentsControllerTests.cs`

**Interfaces:**
- Consumes: `IDocumentRepository`、`IDocumentJobQueue`、`ICurrentUser`（Task 2/6）。
- Produces:
  - `POST /api/documents`（multipart，欄位名 `file`）→ 202 `{ "id": guid }`；非 .pdf/.md 或 >20MB → 400 `{ "error": "..." }`
  - `GET /api/documents` → 200 `[{ id, fileName, status, chunkCount, errorMessage, uploadedAtUtc }]`（只回自己部門）
  - `DELETE /api/documents/{id}` → 204（連帶刪 chunks 與上傳檔）；他部門的文件回 404
  - 上傳檔存 `<UploadRoot>/{docId}{ext}`，`UploadRoot` 讀設定 `Upload:Root`（預設 `uploads`）。Task 11 的 job 依賴此路徑規則。

- [ ] **Step 1: 寫失敗測試**

```csharp
using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using KnowledgeHub.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text;

public class DocumentsControllerTests
{
    private sealed class FakeDocs : IDocumentRepository
    {
        public readonly List<CompanyDocument> Added = [];
        public Task AddAsync(CompanyDocument doc, CancellationToken ct = default)
            { Added.Add(doc); return Task.CompletedTask; }
        public Task<CompanyDocument?> GetAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Added.FirstOrDefault(d => d.Id == id));
        public Task<IReadOnlyList<CompanyDocument>> ListByDepartmentAsync(string dept, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CompanyDocument>>(Added.Where(d => d.Department == dept).ToList());
        public Task DeleteAsync(Guid id, CancellationToken ct = default)
            { Added.RemoveAll(d => d.Id == id); return Task.CompletedTask; }
        public Task SaveChunksAndCompleteAsync(Guid docId, IReadOnlyList<DocumentChunk> chunks, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task UpdateStatusAsync(Guid docId, DocumentStatus status, string? errorMessage = null, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeQueue : IDocumentJobQueue
    {
        public readonly List<Guid> Enqueued = [];
        public void Enqueue(Guid documentId) => Enqueued.Add(documentId);
    }

    private sealed class FakeUser : ICurrentUser { public string Department => "IT"; }

    private static IFormFile File(string name, int sizeBytes)
    {
        var content = new byte[sizeBytes];
        return new FormFile(new MemoryStream(content), 0, sizeBytes, "file", name);
    }

    private static (DocumentsController Ctrl, FakeDocs Docs, FakeQueue Queue) NewController(string uploadRoot)
    {
        var docs = new FakeDocs();
        var queue = new FakeQueue();
        var ctrl = new DocumentsController(docs, queue, new FakeUser(), new UploadOptions(uploadRoot));
        return (ctrl, docs, queue);
    }

    [Theory]
    [InlineData("report.docx")]
    [InlineData("script.exe")]
    public async Task 非PDF或MD回400(string fileName)
    {
        var (ctrl, docs, queue) = NewController(Path.GetTempPath());
        var result = await ctrl.Upload(File(fileName, 100));
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(docs.Added);
        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task 超過20MB回400()
    {
        var (ctrl, _, _) = NewController(Path.GetTempPath());
        var result = await ctrl.Upload(File("big.pdf", 21 * 1024 * 1024));
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task 合法上傳_建Pending_存檔_排入job_回202()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var (ctrl, docs, queue) = NewController(root);

        var result = await ctrl.Upload(File("sop.md", 100));

        var accepted = Assert.IsType<AcceptedResult>(result);
        var doc = Assert.Single(docs.Added);
        Assert.Equal(DocumentStatus.Pending, doc.Status);
        Assert.Equal("IT", doc.Department);
        Assert.Equal("sop.md", doc.FileName);
        Assert.Equal([doc.Id], queue.Enqueued);
        Assert.True(System.IO.File.Exists(Path.Combine(root, $"{doc.Id}.md")));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task 刪除他部門文件回404()
    {
        var (ctrl, docs, _) = NewController(Path.GetTempPath());
        var other = new CompanyDocument { Id = Guid.NewGuid(), Department = "HR", FileName = "hr.pdf" };
        docs.Added.Add(other);

        var result = await ctrl.Delete(other.Id);

        Assert.IsType<NotFoundResult>(result);
        Assert.Contains(other, docs.Added); // 沒被刪
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test backend/KnowledgeHub.Tests --filter DocumentsControllerTests`
Expected: 編譯錯誤。

- [ ] **Step 3: 實作**

```csharp
// Api/Controllers/DocumentsController.cs
using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeHub.Api.Controllers;

[ApiController]
[Route("api/documents")]
[Authorize]
public class DocumentsController(
    IDocumentRepository docs, IDocumentJobQueue queue,
    ICurrentUser user, UploadOptions upload) : ControllerBase
{
    private static readonly string[] AllowedExtensions = [".pdf", ".md"];
    private const long MaxBytes = 20 * 1024 * 1024;

    [HttpPost]
    [RequestSizeLimit(MaxBytes + 1024)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return BadRequest(new { error = "只接受 .pdf 或 .md 檔案" });
        if (file.Length > MaxBytes)
            return BadRequest(new { error = "檔案不可超過 20MB" });

        var doc = new CompanyDocument
        {
            Id = Guid.NewGuid(), FileName = file.FileName,
            Department = user.Department, Status = DocumentStatus.Pending,
            UploadedAtUtc = DateTime.UtcNow
        };
        Directory.CreateDirectory(upload.Root);
        var path = Path.Combine(upload.Root, $"{doc.Id}{ext}");
        await using (var fs = System.IO.File.Create(path))
            await file.CopyToAsync(fs, ct);

        await docs.AddAsync(doc, ct);
        queue.Enqueue(doc.Id);
        return Accepted(new { id = doc.Id });
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct = default)
    {
        var list = await docs.ListByDepartmentAsync(user.Department, ct);
        return Ok(list.Select(d => new
        {
            d.Id, d.FileName, Status = d.Status.ToString(),
            d.ChunkCount, d.ErrorMessage, d.UploadedAtUtc
        }));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var doc = await docs.GetAsync(id, ct);
        if (doc is null || doc.Department != user.Department) return NotFound();

        await docs.DeleteAsync(id, ct);
        foreach (var ext in AllowedExtensions)
        {
            var path = Path.Combine(upload.Root, $"{id}{ext}");
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
        return NoContent();
    }
}
```

`UploadOptions` 定義在 Core（`namespace KnowledgeHub.Core; public record UploadOptions(string Root);`），Program.cs 註冊：

```csharp
builder.Services.AddSingleton(new UploadOptions(
    builder.Configuration["Upload:Root"] ?? "uploads"));
```

```csharp
// Infrastructure/Repositories/DocumentRepository.cs
using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Infrastructure.Repositories;

public class DocumentRepository(KnowledgeHubDbContext db) : IDocumentRepository
{
    public Task<CompanyDocument?> GetAsync(Guid id, CancellationToken ct = default)
        => db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<IReadOnlyList<CompanyDocument>> ListByDepartmentAsync(string department, CancellationToken ct = default)
        => await db.Documents.Where(d => d.Department == department)
            .OrderByDescending(d => d.UploadedAtUtc).ToListAsync(ct);

    public async Task AddAsync(CompanyDocument doc, CancellationToken ct = default)
    {
        db.Documents.Add(doc);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        => await db.Documents.Where(d => d.Id == id).ExecuteDeleteAsync(ct); // cascade 刪 chunks

    public async Task SaveChunksAndCompleteAsync(Guid docId, IReadOnlyList<DocumentChunk> chunks, CancellationToken ct = default)
    {
        db.DocumentChunks.AddRange(chunks);
        var doc = await db.Documents.FirstAsync(d => d.Id == docId, ct);
        doc.Status = DocumentStatus.Completed;
        doc.ChunkCount = chunks.Count;
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateStatusAsync(Guid docId, DocumentStatus status, string? errorMessage = null, CancellationToken ct = default)
    {
        var doc = await db.Documents.FirstAsync(d => d.Id == docId, ct);
        doc.Status = status;
        doc.ErrorMessage = errorMessage;
        await db.SaveChangesAsync(ct);
    }
}
```

Program.cs 註冊：`builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();`

- [ ] **Step 4: 跑測試確認全綠**

Run: `dotnet test backend/KnowledgeHub.Tests --filter DocumentsControllerTests`
Expected: PASS ×5。

- [ ] **Step 5: Commit**

```powershell
git add -A; git commit -m "feat: 文件上傳/清單/刪除 API（部門隔離）"
```

---

### Task 11: 文字抽取器、DocumentProcessingJob 與 Hangfire 接線

**Files:**
- Create: `backend/KnowledgeHub.Infrastructure/Extraction/PdfTextExtractor.cs`、`Extraction/MarkdownTextExtractor.cs`
- Create: `backend/KnowledgeHub.Infrastructure/Jobs/DocumentProcessingJob.cs`、`Jobs/HangfireDocumentJobQueue.cs`
- Modify: `backend/KnowledgeHub.Api/Program.cs`（Hangfire server）
- Test: `backend/KnowledgeHub.Tests/MarkdownTextExtractorTests.cs`、`DocumentProcessingJobTests.cs`

**Interfaces:**
- Consumes: `IDocumentTextExtractor`、`IDocumentRepository`、`IEmbeddingService`、`TextChunker`／`MarkdownChunker`（Task 2/3/7/10；`.md` 走 MarkdownChunker、其他走 TextChunker）。
- Produces: `DocumentProcessingJob.ProcessAsync(Guid documentId)`（Hangfire 進入點，`[AutomaticRetry(Attempts = 0)]`）；`HangfireDocumentJobQueue : IDocumentJobQueue`。

- [ ] **Step 1: 寫失敗測試**

```csharp
using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using KnowledgeHub.Infrastructure.Extraction;
using KnowledgeHub.Infrastructure.Jobs;

public class MarkdownTextExtractorTests
{
    private static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.md");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void 略過YAMLfrontmatter()
    {
        var path = WriteTemp("---\ntitle: SOP\nowner: IT\n---\n# 標題\n正文內容");
        var text = new MarkdownTextExtractor().ExtractText(path);
        Assert.DoesNotContain("owner: IT", text);
        Assert.Contains("正文內容", text);
        File.Delete(path);
    }

    [Fact]
    public void 無frontmatter原樣抽取()
    {
        var path = WriteTemp("# 標題\n正文");
        var text = new MarkdownTextExtractor().ExtractText(path);
        Assert.Contains("# 標題", text);
        File.Delete(path);
    }

    [Fact]
    public void 只處理md副檔名()
    {
        var extractor = new MarkdownTextExtractor();
        Assert.True(extractor.CanHandle(".md"));
        Assert.False(extractor.CanHandle(".pdf"));
    }
}

public class DocumentProcessingJobTests
{
    private sealed class FakeDocs : IDocumentRepository
    {
        public CompanyDocument? Doc;
        public readonly List<(DocumentStatus Status, string? Error)> StatusLog = [];
        public IReadOnlyList<DocumentChunk>? SavedChunks;

        public Task<CompanyDocument?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Doc);
        public Task UpdateStatusAsync(Guid docId, DocumentStatus status, string? errorMessage = null, CancellationToken ct = default)
            { StatusLog.Add((status, errorMessage)); return Task.CompletedTask; }
        public Task SaveChunksAndCompleteAsync(Guid docId, IReadOnlyList<DocumentChunk> chunks, CancellationToken ct = default)
            { SavedChunks = chunks; StatusLog.Add((DocumentStatus.Completed, null)); return Task.CompletedTask; }
        public Task AddAsync(CompanyDocument doc, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<CompanyDocument>> ListByDepartmentAsync(string d, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CompanyDocument>>([]);
    }

    private sealed class FakeExtractor(string result) : IDocumentTextExtractor
    {
        public bool CanHandle(string ext) => ext == ".md";
        public string ExtractText(string filePath) => result;
    }

    private sealed class FakeEmbedding : IEmbeddingService
    {
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new float[1536]).ToList());
    }

    private static (DocumentProcessingJob Job, FakeDocs Docs, string Root) Build(string extractedText)
    {
        var docs = new FakeDocs();
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        var doc = new CompanyDocument { Id = Guid.NewGuid(), FileName = "a.md", Department = "IT" };
        File.WriteAllText(Path.Combine(root, $"{doc.Id}.md"), "占位");
        docs.Doc = doc;
        var job = new DocumentProcessingJob(docs,
            [new FakeExtractor(extractedText)], new FakeEmbedding(),
            new UploadOptions(root));
        return (job, docs, root);
    }

    [Fact]
    public async Task 成功路徑_Processing後Completed_chunks序號連續()
    {
        var longText = new string('字', 1200); // 3 片
        var (job, docs, root) = Build(longText);

        await job.ProcessAsync(docs.Doc!.Id);

        Assert.Equal(DocumentStatus.Processing, docs.StatusLog[0].Status);
        Assert.Equal(DocumentStatus.Completed, docs.StatusLog[^1].Status);
        Assert.Equal(3, docs.SavedChunks!.Count);
        Assert.Equal([0, 1, 2], docs.SavedChunks.Select(c => c.SequenceNumber));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task Markdown帶標題_chunk內容帶標題路徑前綴()
    {
        var (job, docs, root) = Build("# 重開機流程\n步驟一");

        await job.ProcessAsync(docs.Doc!.Id);

        Assert.StartsWith("【重開機流程】\n", docs.SavedChunks!.Single().Content);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task 全文為空_標Failed_訊息注明無可抽取文字()
    {
        var (job, docs, root) = Build("   ");

        await job.ProcessAsync(docs.Doc!.Id);

        var last = docs.StatusLog[^1];
        Assert.Equal(DocumentStatus.Failed, last.Status);
        Assert.Contains("無可抽取文字", last.Error);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task 中途例外_標Failed_存錯誤訊息_不吞例外重丟()
    {
        var docs = new FakeDocs();
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        docs.Doc = new CompanyDocument { Id = Guid.NewGuid(), FileName = "a.md" };
        // 不寫入檔案 → 讀檔會丟 FileNotFoundException
        var job = new DocumentProcessingJob(docs,
            [new FakeExtractor("x")], new FakeEmbedding(),
            new UploadOptions(root));

        await Assert.ThrowsAnyAsync<Exception>(() => job.ProcessAsync(docs.Doc.Id));
        Assert.Equal(DocumentStatus.Failed, docs.StatusLog[^1].Status);
        Assert.NotNull(docs.StatusLog[^1].Error);
        Directory.Delete(root, recursive: true);
    }
}
```

（`UploadOptions` 已在 Task 10 定義於 Core，這裡直接 `using KnowledgeHub.Core;` 取用。）

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test backend/KnowledgeHub.Tests --filter "MarkdownTextExtractorTests|DocumentProcessingJobTests"`
Expected: 編譯錯誤。

- [ ] **Step 3: 實作**

```powershell
dotnet add backend/KnowledgeHub.Infrastructure package PdfPig
dotnet add backend/KnowledgeHub.Infrastructure package Hangfire.Core
dotnet add backend/KnowledgeHub.Infrastructure package Hangfire.SqlServer
dotnet add backend/KnowledgeHub.Api package Hangfire.AspNetCore
```

```csharp
// Infrastructure/Extraction/MarkdownTextExtractor.cs
using KnowledgeHub.Core.Interfaces;

namespace KnowledgeHub.Infrastructure.Extraction;

public class MarkdownTextExtractor : IDocumentTextExtractor
{
    public bool CanHandle(string fileExtension) => fileExtension == ".md";

    public string ExtractText(string filePath)
    {
        var text = File.ReadAllText(filePath);
        if (!text.StartsWith("---")) return text;

        var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0) return text;
        var bodyStart = text.IndexOf('\n', end + 1);
        return bodyStart < 0 ? "" : text[(bodyStart + 1)..];
    }
}

// Infrastructure/Extraction/PdfTextExtractor.cs
using System.Text;
using KnowledgeHub.Core.Interfaces;
using UglyToad.PdfPig;

namespace KnowledgeHub.Infrastructure.Extraction;

public class PdfTextExtractor : IDocumentTextExtractor
{
    public bool CanHandle(string fileExtension) => fileExtension == ".pdf";

    public string ExtractText(string filePath)
    {
        var sb = new StringBuilder();
        using var pdf = PdfDocument.Open(filePath);
        foreach (var page in pdf.GetPages())
            sb.AppendLine(page.Text);
        return sb.ToString();
    }
}

// Infrastructure/Jobs/DocumentProcessingJob.cs
using Hangfire;
using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using Microsoft.Data.SqlClient;

namespace KnowledgeHub.Infrastructure.Jobs;

public class DocumentProcessingJob(
    IDocumentRepository docs,
    IEnumerable<IDocumentTextExtractor> extractors,
    IEmbeddingService embedding,
    UploadOptions upload)
{
    [AutomaticRetry(Attempts = 0)] // 失敗不重試，狀態對使用者可見
    public async Task ProcessAsync(Guid documentId)
    {
        var doc = await docs.GetAsync(documentId)
            ?? throw new InvalidOperationException($"文件 {documentId} 不存在");
        await docs.UpdateStatusAsync(documentId, DocumentStatus.Processing);
        try
        {
            var ext = Path.GetExtension(doc.FileName).ToLowerInvariant();
            var extractor = extractors.FirstOrDefault(e => e.CanHandle(ext))
                ?? throw new InvalidOperationException($"不支援的副檔名 {ext}");
            var text = extractor.ExtractText(Path.Combine(upload.Root, $"{doc.Id}{ext}"));

            var pieces = ext == ".md" ? MarkdownChunker.Split(text) : TextChunker.Split(text);
            if (pieces.Count == 0)
            {
                await docs.UpdateStatusAsync(documentId, DocumentStatus.Failed, "無可抽取文字（可能是掃描檔）");
                return;
            }

            var vectors = await embedding.EmbedAsync(pieces);
            var chunks = pieces.Select((content, i) => new DocumentChunk
            {
                Id = Guid.NewGuid(), DocumentId = documentId,
                SequenceNumber = i, Content = content,
                Embedding = new SqlVector<float>(vectors[i])
            }).ToList();

            await docs.SaveChunksAndCompleteAsync(documentId, chunks);
        }
        catch (Exception ex)
        {
            await docs.UpdateStatusAsync(documentId, DocumentStatus.Failed, ex.Message);
            throw; // 不吞例外：Hangfire 面板要看得到
        }
    }
}

// Infrastructure/Jobs/HangfireDocumentJobQueue.cs
using Hangfire;
using KnowledgeHub.Core.Interfaces;

namespace KnowledgeHub.Infrastructure.Jobs;

public class HangfireDocumentJobQueue(IBackgroundJobClient client) : IDocumentJobQueue
{
    public void Enqueue(Guid documentId)
        => client.Enqueue<DocumentProcessingJob>(j => j.ProcessAsync(documentId));
}
```

Program.cs 加：

```csharp
builder.Services.AddHangfire(c => c.UseSqlServerStorage(
    builder.Configuration.GetConnectionString("Default")));
builder.Services.AddHangfireServer();
builder.Services.AddScoped<IDocumentJobQueue, HangfireDocumentJobQueue>();
builder.Services.AddScoped<DocumentProcessingJob>();
builder.Services.AddScoped<IDocumentTextExtractor, PdfTextExtractor>();
builder.Services.AddScoped<IDocumentTextExtractor, MarkdownTextExtractor>();
```

- [ ] **Step 4: 跑測試確認全綠**

Run: `dotnet test backend/KnowledgeHub.Tests --filter "MarkdownTextExtractorTests|DocumentProcessingJobTests"`
Expected: PASS ×7。

- [ ] **Step 5: 端到端實測（spec 階段 3 驗收）**

啟動 API，用 curl 上傳一份**假資料** Markdown（可拿同事 vault 的範本改寫成假內容）與一份真 PDF（非機密、自製）：

```powershell
curl -k -X POST https://localhost:<port>/api/documents -H "Authorization: Bearer <token>" -F "file=@fake-sop.md"
# 輪詢 GET /api/documents 直到 Completed，然後：
curl -k -N -X POST https://localhost:<port>/api/chat -H "Authorization: Bearer <token>" -H "Content-Type: application/json" -d '{"message":"<問一個 fake-sop.md 裡有答案的問題>","history":[]}'
```

Expected: 狀態 Pending→Processing→Completed、ChunkCount>0；問答回覆引用文件內容且發出 `sources` 事件。把輸出貼進回報。

- [ ] **Step 6: Commit**

```powershell
git add -A; git commit -m "feat: 背景解析管線（PDF/MD 抽取、切片、向量化、Hangfire）"
```

---

### Task 12: Vue 前端骨架與登入

**Files:**
- Create: `frontend/`（Vite + Vue 3 + TS + Tailwind）
- Create: `frontend/src/composables/useAuth.ts`、`src/views/LoginView.vue`、`src/App.vue`
- Modify: `.github/workflows/ci.yml`（加前端 job）

**Interfaces:**
- Consumes: `POST /api/auth/login`（Task 6）。
- Produces: `useAuth()` → `{ token: Ref<string | null>, department: Ref<string | null>, login(username, password): Promise<void>, logout(): void, authHeader(): Record<string, string> }`。token 只存 memory（重整要重登，YAGNI）。Task 13/14 依賴 `authHeader()`。

- [ ] **Step 1: 建專案**

```powershell
npm create vite@latest frontend -- --template vue-ts
cd frontend
npm install
npm install tailwindcss @tailwindcss/vite
```

`vite.config.ts`：加 Tailwind plugin 與 dev proxy（`/api` → `https://localhost:<API port>`，`secure: false`）。`src/style.css` 開頭 `@import "tailwindcss";`。刪範本元件。

- [ ] **Step 2: useAuth composable**

```typescript
// src/composables/useAuth.ts
import { ref } from 'vue'

const token = ref<string | null>(null)
const department = ref<string | null>(null)

export function useAuth() {
  async function login(username: string, password: string): Promise<void> {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    })
    if (!res.ok) throw new Error('帳號或密碼錯誤')
    token.value = (await res.json()).token
    // JWT payload 的 department claim（demo 等級解析，不驗簽）
    department.value = JSON.parse(atob(token.value!.split('.')[1])).department
  }
  function logout() { token.value = null; department.value = null }
  function authHeader(): Record<string, string> {
    return token.value ? { Authorization: `Bearer ${token.value}` } : {}
  }
  return { token, department, login, logout, authHeader }
}
```

- [ ] **Step 3: LoginView（極簡：下拉選 demo 使用者＋密碼欄＋錯誤訊息）與 App.vue（未登入顯示 LoginView，登入後顯示占位的雙欄骨架＋右上部門標籤與登出鈕）**

LoginView 綁 `useAuth().login`，失敗顯示紅字錯誤氣泡；不裝 router，用 `v-if="token"` 切換。

- [ ] **Step 4: CI 加前端 job（`.github/workflows/ci.yml`）**

```yaml
  frontend:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: '22'
      - run: npm ci
        working-directory: frontend
      - run: npm run build
        working-directory: frontend
```

- [ ] **Step 5: 驗證**

Run: `npm run build`（在 frontend/ 下）
Expected: vue-tsc 無型別錯誤、build 成功。再 `npm run dev` 開瀏覽器實測：登入成功進入骨架頁、錯密碼顯示錯誤。

- [ ] **Step 6: Commit**

```powershell
git add -A; git commit -m "feat: Vue 前端骨架與 JWT 登入"
```

---

### Task 13: 文件列表與上傳（含輪詢）

**Files:**
- Create: `frontend/src/composables/useDocuments.ts`、`src/components/DocumentPanel.vue`
- Modify: `frontend/src/App.vue`（左欄接入）

**Interfaces:**
- Consumes: Task 10 的三個端點、`useAuth().authHeader()`。
- Produces: `useDocuments()` → `{ documents: Ref<DocumentInfo[]>, load(): Promise<void>, upload(file: File): Promise<void>, remove(id: string): Promise<void> }`；`DocumentInfo = { id: string; fileName: string; status: 'Pending'|'Processing'|'Completed'|'Failed'; chunkCount: number; errorMessage: string | null; uploadedAtUtc: string }`。

- [ ] **Step 1: useDocuments composable（含輪詢規則：有 Pending/Processing 每 3 秒 `load()` 一次，全部完成即 `clearInterval`）**

```typescript
// src/composables/useDocuments.ts
import { ref, onUnmounted } from 'vue'
import { useAuth } from './useAuth'

export interface DocumentInfo {
  id: string; fileName: string
  status: 'Pending' | 'Processing' | 'Completed' | 'Failed'
  chunkCount: number; errorMessage: string | null; uploadedAtUtc: string
}

export function useDocuments() {
  const { authHeader } = useAuth()
  const documents = ref<DocumentInfo[]>([])
  let timer: number | undefined

  async function load(): Promise<void> {
    const res = await fetch('/api/documents', { headers: authHeader() })
    if (!res.ok) throw new Error('讀取文件清單失敗')
    documents.value = await res.json()
    syncPolling()
  }

  function syncPolling() {
    const busy = documents.value.some(d => d.status === 'Pending' || d.status === 'Processing')
    if (busy && timer === undefined) timer = window.setInterval(load, 3000)
    if (!busy && timer !== undefined) { clearInterval(timer); timer = undefined }
  }

  async function upload(file: File): Promise<void> {
    const form = new FormData()
    form.append('file', file)
    const res = await fetch('/api/documents', { method: 'POST', headers: authHeader(), body: form })
    if (!res.ok) throw new Error((await res.json()).error ?? '上傳失敗')
    await load()
  }

  async function remove(id: string): Promise<void> {
    const res = await fetch(`/api/documents/${id}`, { method: 'DELETE', headers: authHeader() })
    if (!res.ok) throw new Error('刪除失敗')
    await load()
  }

  onUnmounted(() => { if (timer !== undefined) clearInterval(timer) })
  return { documents, load, upload, remove }
}
```

- [ ] **Step 2: DocumentPanel.vue**

左欄：上傳區（`<input type="file" accept=".pdf,.md">` ＋拖放區，`dragover.prevent`/`drop.prevent`）、文件卡片列表（檔名、狀態 badge 四色：Pending 灰/Processing 藍/Completed 綠/Failed 紅、chunk 數、Failed 時顯示 errorMessage、刪除鈕带 confirm）。掛載時 `load()`。上傳與刪除的錯誤顯示在面板頂部訊息列。

- [ ] **Step 3: 瀏覽器實測**

上傳假資料 .md → badge 走 Pending→Processing→Completed、輪詢自動停止；上傳 .exe → 顯示後端的 400 錯誤訊息；刪除文件 → 清單即時更新。

- [ ] **Step 4: Commit**

```powershell
git add -A; git commit -m "feat: 文件面板（上傳、狀態輪詢、刪除）"
```

---

### Task 14: 聊天介面（SSE 解析與來源卡片）

**Files:**
- Create: `frontend/src/composables/useChat.ts`、`src/components/ChatPanel.vue`、`src/components/SourceCard.vue`
- Modify: `frontend/src/App.vue`（右欄接入）

**Interfaces:**
- Consumes: `POST /api/chat` 的 SSE 協定（Task 9）。
- Produces: `useChat()` → `{ messages: Ref<ChatMessage[]>, sending: Ref<boolean>, send(text: string): Promise<void> }`；`ChatMessage = { role: 'user'|'assistant'; content: string; sources: Source[]; error: string | null }`；`Source = { fileName: string; sequenceNumber: number; content: string; distance: number }`。

- [ ] **Step 1: useChat composable（fetch + ReadableStream 手動解析 SSE——POST 不能用 EventSource）**

```typescript
// src/composables/useChat.ts
import { ref } from 'vue'
import { useAuth } from './useAuth'

export interface Source { fileName: string; sequenceNumber: number; content: string; distance: number }
export interface ChatMessage { role: 'user' | 'assistant'; content: string; sources: Source[]; error: string | null }

export function useChat() {
  const { authHeader } = useAuth()
  const messages = ref<ChatMessage[]>([])
  const sending = ref(false)

  async function send(text: string): Promise<void> {
    sending.value = true
    const history = messages.value
      .filter(m => !m.error)
      .map(m => ({ role: m.role, content: m.content }))
    messages.value.push({ role: 'user', content: text, sources: [], error: null })
    const reply: ChatMessage = { role: 'assistant', content: '', sources: [], error: null }
    messages.value.push(reply)

    try {
      const res = await fetch('/api/chat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...authHeader() },
        body: JSON.stringify({ message: text, history }),
      })
      if (!res.ok || !res.body) throw new Error(`HTTP ${res.status}`)

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
      reply.error = e instanceof Error ? e.message : '連線失敗'
    } finally {
      sending.value = false
    }
  }

  function handleEvent(block: string, reply: ChatMessage) {
    const event = /^event: (.+)$/m.exec(block)?.[1]
    const data = /^data: (.+)$/m.exec(block)?.[1]
    if (!event || !data) return
    if (event === 'token') reply.content += JSON.parse(data).text
    else if (event === 'sources') reply.sources = JSON.parse(data)
    else if (event === 'error') reply.error = JSON.parse(data).message
  }

  return { messages, sending, send }
}
```

- [ ] **Step 2: ChatPanel.vue 與 SourceCard.vue**

ChatPanel：訊息串（user 靠右、assistant 靠左，token 逐字 append 自然形成打字機效果）、輸入框＋送出鈕（`sending` 時 disabled）、錯誤氣泡（紅底顯示 `error`）、自動捲到最新訊息（`watch` messages 深度變化後 `scrollTop = scrollHeight`）。assistant 訊息下方渲染 `SourceCard` 列表。
SourceCard：收合狀態顯示 `fileName 第N段`，點擊展開完整 `content`（展開的卡片加高亮邊框），再點收合。

- [ ] **Step 3: App.vue 組雙欄版面**

登入後：左欄 `DocumentPanel`（固定寬 320–380px）、右欄 `ChatPanel`（flex-1），高度撐滿視窗。

- [ ] **Step 4: 瀏覽器實測（spec 階段 4 驗收——完整走一遍）**

登入 → 上傳假資料 .md → 看進度到 Completed → 問文件裡有答案的問題 → 逐字串流顯示 → 來源卡片出現且可展開 → 換 hr-user 登入問同一問題 → 查無資料（部門隔離生效）。

- [ ] **Step 5: Commit**

```powershell
git add -A; git commit -m "feat: 聊天介面（SSE 串流、打字機、來源卡片）"
```

---

### Task 15: 端到端驗收與 README 完稿

**Files:**
- Modify: `README.md`

- [ ] **Step 1: 全量測試**

Run: `dotnet test backend/KnowledgeHub.sln`（本機含整合測試）與 `npm run build`
Expected: 全綠。輸出貼進回報。

- [ ] **Step 2: 對照 spec §11 驗收表逐條走一遍**

| 階段 | 驗收 | 證據 |
|---|---|---|
| 0 | CI 綠 | Actions 截圖或 URL |
| 1 | migration 實跑、向量查詢正確 TOP 5＋部門過濾 | 整合測試輸出 |
| 2 | chat 有 sources 事件；寄信 outbox 有紀錄 | curl 輸出＋SQL 查詢結果 |
| 3 | 上傳→狀態走完→chunks 入庫→立即可問答 | API 輸出序列 |
| 4 | 瀏覽器完整流程 | 逐步操作確認 |

任何一條沒過就修到過，逐條記錄證據，不可標「大致完成」。

- [ ] **Step 3: README 完稿**

補：架構圖（mermaid：三層＋Azure SQL＋Gemini＋Hangfire 流向）、本機啟動步驟（user-secrets 三個 key、migration、`dotnet run`、`npm run dev`）、demo 帳號表、「資料安全」一節（免費層只放假資料的原因、正式改 Vertex AI）、「擴充方向」一節（DiskANN 向量索引、EIP 待審助理 Phase B、frontmatter metadata 檢索）。

- [ ] **Step 4: 最終 commit**

```powershell
git add -A; git commit -m "docs: README 完稿與 Phase A 驗收記錄"
```

---

## 執行注意事項

- **需要使用者提供才能繼續的點**：Azure SQL 連線字串（Task 4）、Gemini API key（Task 9）、GitHub repo URL（Task 1）。到了就停下來要,不要用假值蒙混。
- **API 簽名以編譯器為準的點**：SK `AddOpenAIChatCompletion` 自訂 endpoint 的 overload（Task 9）、`SqlVector<float>` 建構子（Task 5/11）。簽名對不上就查當版官方文件，不要硬湊到編譯過為止。
- 每個 Task 結束都有獨立 commit；中斷後從最後一個綠色 commit 續作。



