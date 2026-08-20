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
