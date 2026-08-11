namespace KnowledgeHub.Core.Interfaces;

public interface IChunkRepository
{
    /// <summary>
    /// 聯集：段落所屬文件部門 ∈ <paramref name="departments"/>，或文件為全公司共用（<see cref="Departments.All"/>）。
    /// </summary>
    Task<IReadOnlyList<ChunkSearchResult>> SearchSimilarChunksAsync(
        float[] queryVector, IReadOnlyList<string> departments, int topK = 5, CancellationToken ct = default);
}
