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
