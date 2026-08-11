using System.Text;
using KnowledgeHub.Core;
using KnowledgeHub.Core.Interfaces;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnowledgeHub.Api.Bot;

/// <summary>
/// KnowledgeHub bot 的對話邏輯進入點（掛在 /api/messages，見 Program.cs）。
/// 單輪問答：不維護對話歷史，每則訊息都是獨立的一次 RAG 查詢（Teams SSO／多輪對話
/// 是後續工作）。
///
/// 安全前提（不可妥協）：bot 走 Bot Framework 匿名/自家驗證，沒有使用者身分與部門
/// claim。因此這裡注入的 <see cref="IChatService"/> 一律用 "bot" 這個 keyed 服務——
/// 其底層 Kernel（見 Program.cs 的 "bot" keyed 服務註冊）只掛 retrieval plugin、不掛
/// EmailPlugin（匿名管道不可觸發寄信），且 retrieval plugin 的部門範圍固定是
/// <see cref="AllDepartmentsScope"/>（只查全公司共用文件），不會、也不能查到部門限定
/// 文件。不要把這裡改成注入不帶 key 的 IChatService／Kernel——那一份是 web 端
/// （/api/chat）專用，掛了 EmailPlugin 且部門範圍取自 ICurrentUser，兩者不可互換。
/// </summary>
public class KnowledgeHubBotHandler(
    [FromKeyedServices("bot")] IChatService chat,
    RetrievalContext retrievalContext,
    ILogger<KnowledgeHubBotHandler> logger) : ActivityHandler
{
    protected override async Task OnMessageActivityAsync(
        ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
    {
        await turnContext.SendActivityAsync(new Activity { Type = ActivityTypes.Typing }, cancellationToken);

        string reply;
        try
        {
            var sb = new StringBuilder();
            await foreach (var token in chat.StreamAnswerAsync(
                turnContext.Activity.Text, history: [], cancellationToken))
                sb.Append(token);
            reply = sb.ToString();

            if (retrievalContext.Results.Count > 0)
            {
                var sources = retrievalContext.Results.Select(r => r.FileName).Distinct();
                reply += "\n\n來源：" + string.Join("、", sources);
            }
        }
        catch (Exception ex)
        {
            // LLM 呼叫失敗或逾時：回友善訊息，不可讓例外炸到 adapter 變 500
            // （同 ChatSseStreamer 的錯誤處理原則，見該類別註解）。
            logger.LogError(ex, "Bot RAG 問答處理失敗");
            reply = "抱歉，處理您的問題時發生錯誤，請稍後再試。";
        }

        await turnContext.SendActivityAsync(MessageFactory.Text(reply), cancellationToken);
    }
}
