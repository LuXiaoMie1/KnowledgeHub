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
