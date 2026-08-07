namespace KnowledgeHub.Core.Interfaces;

public interface IDocumentJobQueue
{
    void Enqueue(Guid documentId);
}
