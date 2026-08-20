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
