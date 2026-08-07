namespace KnowledgeHub.Core.Entities;

public class OutboxEmail
{
    public Guid Id { get; set; }
    public string To { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
