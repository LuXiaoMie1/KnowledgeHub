namespace KnowledgeHub.Core.Entities;
using Microsoft.Data.SqlClient;

public class DocumentChunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public CompanyDocument Document { get; set; } = null!;
    public int SequenceNumber { get; set; }
    public string Content { get; set; } = "";
    public SqlVector<float> Embedding { get; set; } = null!;
}
