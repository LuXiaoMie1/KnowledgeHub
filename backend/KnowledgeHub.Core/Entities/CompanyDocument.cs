namespace KnowledgeHub.Core.Entities;

public class CompanyDocument
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = "";
    public string Department { get; set; } = "";
    public DocumentStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public int ChunkCount { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public List<DocumentChunk> Chunks { get; set; } = [];
}
