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
