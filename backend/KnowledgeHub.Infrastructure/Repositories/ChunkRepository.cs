using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Infrastructure.Repositories;

public class ChunkRepository(KnowledgeHubDbContext db) : IChunkRepository
{
    public async Task<IReadOnlyList<ChunkSearchResult>> SearchSimilarChunksAsync(
        float[] queryVector, IReadOnlyList<string> departments, int topK = 5, CancellationToken ct = default)
    {
        var qv = new SqlVector<float>(queryVector);
        return await db.DocumentChunks
            .Where(c => (departments.Contains(c.Document.Department) || c.Document.Department == Departments.All)
                     && c.Document.Status == DocumentStatus.Completed)
            .OrderBy(c => EF.Functions.VectorDistance("cosine", c.Embedding, qv))
            .Take(topK)
            .Select(c => new ChunkSearchResult(
                c.Id, c.DocumentId, c.Document.FileName, c.SequenceNumber, c.Content,
                EF.Functions.VectorDistance("cosine", c.Embedding, qv)))
            .ToListAsync(ct);
    }
}
