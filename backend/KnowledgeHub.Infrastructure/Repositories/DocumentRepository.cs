using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Infrastructure.Repositories;

public class DocumentRepository(KnowledgeHubDbContext db) : IDocumentRepository
{
    public Task<CompanyDocument?> GetAsync(Guid id, CancellationToken ct = default)
        => db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<IReadOnlyList<CompanyDocument>> ListByDepartmentsAsync(IReadOnlyList<string> departments, CancellationToken ct = default)
        => await db.Documents.Where(d => departments.Contains(d.Department) || d.Department == Departments.All)
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

    // 用 ExecuteUpdateAsync（不經 change tracker）：呼叫端（DocumentProcessingJob 失敗處理）
    // 常與同一個 DbContext 上殘留的失敗 SaveChanges（例如 chunks 寫入失敗留下 Added 實體）共用，
    // 若改走 change tracker 的 SaveChangesAsync，殘留的髒實體會讓這次狀態更新一併失敗，文件永卡 Processing。
    public async Task UpdateStatusAsync(Guid docId, DocumentStatus status, string? errorMessage = null, CancellationToken ct = default)
        => await db.Documents.Where(d => d.Id == docId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, status)
                .SetProperty(d => d.ErrorMessage, errorMessage), ct);
}
