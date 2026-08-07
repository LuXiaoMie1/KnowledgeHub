using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Infrastructure.Repositories;

public class DocumentRepository(KnowledgeHubDbContext db) : IDocumentRepository
{
    public Task<CompanyDocument?> GetAsync(Guid id, CancellationToken ct = default)
        => db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<IReadOnlyList<CompanyDocument>> ListByDepartmentAsync(string department, CancellationToken ct = default)
        => await db.Documents.Where(d => d.Department == department)
            .OrderByDescending(d => d.UploadedAtUtc).ToListAsync(ct);

    public async Task AddAsync(CompanyDocument doc, CancellationToken ct = default)
    {
        db.Documents.Add(doc);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        => await db.Documents.Where(d => d.Id == id).ExecuteDeleteAsync(ct); // cascade 刪 chunks

    public async Task SaveChunksAndCompleteAsync(Guid docId, IReadOnlyList<DocumentChunk> chunks, CancellationToken ct = default)
    {
        db.DocumentChunks.AddRange(chunks);
        var doc = await db.Documents.FirstAsync(d => d.Id == docId, ct);
        doc.Status = DocumentStatus.Completed;
        doc.ChunkCount = chunks.Count;
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateStatusAsync(Guid docId, DocumentStatus status, string? errorMessage = null, CancellationToken ct = default)
    {
        var doc = await db.Documents.FirstAsync(d => d.Id == docId, ct);
        doc.Status = status;
        doc.ErrorMessage = errorMessage;
        await db.SaveChangesAsync(ct);
    }
}
