namespace KnowledgeHub.Core.Interfaces;
using Entities;

public interface IDocumentRepository
{
    Task<CompanyDocument?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<CompanyDocument>> ListByDepartmentAsync(string department, CancellationToken ct = default);
    Task AddAsync(CompanyDocument doc, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task SaveChunksAndCompleteAsync(Guid docId, IReadOnlyList<DocumentChunk> chunks, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid docId, DocumentStatus status, string? errorMessage = null, CancellationToken ct = default);
}
