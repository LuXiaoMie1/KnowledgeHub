using Hangfire;
using KnowledgeHub.Core.Interfaces;

namespace KnowledgeHub.Infrastructure.Jobs;

public class HangfireDocumentJobQueue(IBackgroundJobClient client) : IDocumentJobQueue
{
    public void Enqueue(Guid documentId)
        => client.Enqueue<DocumentProcessingJob>(j => j.ProcessAsync(documentId));
}
