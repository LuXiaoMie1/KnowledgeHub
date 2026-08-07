using System.Runtime.CompilerServices;
using KnowledgeHub.Core.Interfaces;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace KnowledgeHub.Infrastructure.Ai;

public class SemanticKernelChatService(Kernel kernel) : IChatService
{
    private const string SystemPrompt =
        "你是 QBurger 的企業知識庫助理。回答公司文件、SOP、規章問題前，必須先呼叫 search_knowledge_base 查詢；" +
        "根據查到的段落回答並保持忠實，查不到就直說知識庫沒有相關資料，不可自行編造。使用繁體中文回答。";

    public async IAsyncEnumerable<string> StreamAnswerAsync(
        string message, IReadOnlyList<ChatTurn> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var chatHistory = new ChatHistory(SystemPrompt);
        foreach (var turn in history)
        {
            if (turn.Role == "user") chatHistory.AddUserMessage(turn.Content);
            else chatHistory.AddAssistantMessage(turn.Content);
        }
        chatHistory.AddUserMessage(message);

        var settings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };
        var service = kernel.GetRequiredService<IChatCompletionService>();
        await foreach (var delta in service.GetStreamingChatMessageContentsAsync(
            chatHistory, settings, kernel, ct))
        {
            if (!string.IsNullOrEmpty(delta.Content))
                yield return delta.Content;
        }
    }
}
