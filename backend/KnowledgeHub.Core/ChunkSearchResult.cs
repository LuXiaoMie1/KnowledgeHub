namespace KnowledgeHub.Core;

public record ChunkSearchResult(
    Guid ChunkId, Guid DocumentId, string FileName,
    int SequenceNumber, string Content, double Distance);
