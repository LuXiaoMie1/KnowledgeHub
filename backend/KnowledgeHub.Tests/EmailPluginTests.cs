using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using KnowledgeHub.Infrastructure.Ai;

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

    private sealed class FakeUser : ICurrentUser
    {
        public string Department => "IT";
        public IReadOnlyList<string> Departments => ["IT"];
        public string Username => "it-user";
    }

    [Fact]
    public async Task 寄信寫入outbox_欄位正確()
    {
        var outbox = new FakeOutbox();
        var plugin = new EmailPlugin(outbox, new FakeUser());

        var result = await plugin.SendEmailAsync("boss@qburger.com.tw", "週報", "本週進度…");

        var saved = Assert.Single(outbox.Saved);
        Assert.Equal("boss@qburger.com.tw", saved.To);
        Assert.Equal("週報", saved.Subject);
        Assert.Equal("本週進度…", saved.Body);
        Assert.Equal("IT", saved.Department);
        Assert.Equal("it-user", saved.RequestedBy);
        Assert.Contains("已寄出", result);
    }

    [Fact]
    public async Task 收件人格式無效_不寫入outbox_回傳錯誤訊息()
    {
        var outbox = new FakeOutbox();
        var plugin = new EmailPlugin(outbox, new FakeUser());

        var result = await plugin.SendEmailAsync("不是email", "週報", "本週進度…");

        Assert.Empty(outbox.Saved);
        Assert.Contains("格式無效", result);
    }
}
