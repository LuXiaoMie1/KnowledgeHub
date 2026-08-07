namespace KnowledgeHub.Core.Interfaces;

public interface IChunkRepository
{
    Task<IReadOnlyList<ChunkSearchResult>> SearchSimilarChunksAsync(
        float[] queryVector, string department, int topK = 5, CancellationToken ct = default);
}
