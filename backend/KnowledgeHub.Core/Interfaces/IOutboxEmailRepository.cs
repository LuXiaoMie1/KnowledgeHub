namespace KnowledgeHub.Core.Interfaces;
using Entities;

public interface IOutboxEmailRepository
{
    Task AddAsync(OutboxEmail email, CancellationToken ct = default);
}
