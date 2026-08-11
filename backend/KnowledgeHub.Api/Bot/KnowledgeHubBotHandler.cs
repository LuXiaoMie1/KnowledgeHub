using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;

namespace KnowledgeHub.Api.Bot;

/// <summary>
/// KnowledgeHub bot 的對話邏輯進入點（掛在 /api/messages，見 Program.cs）。
/// 目前是零租戶依賴的骨架：收到訊息只回覆固定格式的回音，讓 Emulator／Teams
/// 能先打通一輪對話。之後要接 RAG，只需把 IChatService、RetrievalContext 之類
/// 的服務注入這個類別的建構子並在 OnMessageActivityAsync 呼叫，不影響
/// Program.cs 的 adapter／端點註冊。
/// </summary>
public class KnowledgeHubBotHandler : ActivityHandler
{
    protected override async Task OnMessageActivityAsync(
        ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
    {
        var reply = $"收到：「{turnContext.Activity.Text}」（RAG 接線為後續工作）";
        await turnContext.SendActivityAsync(MessageFactory.Text(reply), cancellationToken);
    }
}
