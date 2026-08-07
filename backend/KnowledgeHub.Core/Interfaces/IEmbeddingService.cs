namespace KnowledgeHub.Core.Interfaces;

public interface IEmbeddingService
{
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
