using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;

namespace KnowledgeHub.Infrastructure.Repositories;

public class OutboxEmailRepository(KnowledgeHubDbContext db) : IOutboxEmailRepository
{
    public async Task AddAsync(OutboxEmail email, CancellationToken ct = default)
    {
        db.OutboxEmails.Add(email);
        await db.SaveChangesAsync(ct);
    }
}
