namespace KnowledgeHub.Core.Entities;
using Microsoft.Data.SqlTypes;

public class DocumentChunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public CompanyDocument Document { get; set; } = null!;
    public int SequenceNumber { get; set; }
    public string Content { get; set; } = "";
    public SqlVector<float> Embedding { get; set; }
}
