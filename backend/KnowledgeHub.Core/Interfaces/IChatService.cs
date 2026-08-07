namespace KnowledgeHub.Core.Interfaces;

public record ChatTurn(string Role, string Content);   // Role: "user" | "assistant"

public interface IChatService
{
    IAsyncEnumerable<string> StreamAnswerAsync(
        string message, IReadOnlyList<ChatTurn> history, CancellationToken ct = default);
}
