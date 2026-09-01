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
