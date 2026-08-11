namespace KnowledgeHub.Core.Interfaces;
using Entities;

public interface IDocumentRepository
{
    Task<CompanyDocument?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// 聯集：文件部門 ∈ <paramref name="departments"/>，或文件為全公司共用（<see cref="Departments.All"/>）。
    /// </summary>
    Task<IReadOnlyList<CompanyDocument>> ListByDepartmentsAsync(IReadOnlyList<string> departments, CancellationToken ct = default);

    Task AddAsync(CompanyDocument doc, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task SaveChunksAndCompleteAsync(Guid docId, IReadOnlyList<DocumentChunk> chunks, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid docId, DocumentStatus status, string? errorMessage = null, CancellationToken ct = default);
}
