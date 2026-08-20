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
/// 多輪問答：對話歷史與 web 端共用同一套 <see cref="IConversationRepository"/> 落庫——
/// 同一個 Teams 對話串（<c>Activity.Conversation.Id</c>）會接續尚未結束的對話；使用者輸入
/// 「新對話」或 <c>/new</c> 會把目前對話蓋上結束章，下一句話開新對話。
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
    ILogger<KnowledgeHubBotHandler> logger,
    IConversationRepository conversations) : ActivityHandler
{
    private const int MaxHistoryTurns = 10;

    protected override async Task OnMessageActivityAsync(
        ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
    {
        var text = turnContext.Activity.Text?.Trim() ?? "";
        var teamsConversationId = turnContext.Activity.Conversation.Id;
        // Teams 的 AadObjectId 即 Entra oid，與 web 端 UserKey 同源（歸戶互通）；非 Teams 管道（Emulator）退用 From.Id
        var userKey = turnContext.Activity.From?.AadObjectId ?? turnContext.Activity.From?.Id ?? "unknown";

        if (text is "新對話" or "/new")
        {
            var active = await conversations.FindActiveTeamsAsync(teamsConversationId, cancellationToken);
            if (active is not null) await conversations.EndAsync(active.Id, cancellationToken);
            await turnContext.SendActivityAsync(
                MessageFactory.Text("好的，已為您開啟新對話，請直接提問。"), cancellationToken);
            return;
        }

        await turnContext.SendActivityAsync(new Activity { Type = ActivityTypes.Typing }, cancellationToken);

        string reply;
        try
        {
            var conversation = await conversations.FindActiveTeamsAsync(teamsConversationId, cancellationToken)
                ?? await conversations.CreateAsync(userKey, ConversationChannels.Teams,
                    ConversationTitle.From(text), teamsConversationId, cancellationToken);
            var history = (await conversations.GetMessagesAsync(conversation.Id, cancellationToken))
                .TakeLast(MaxHistoryTurns)
                .Select(m => new ChatTurn(m.Role, m.Content))
                .ToList();

            var sb = new StringBuilder();
            await foreach (var token in chat.StreamAnswerAsync(text, history, cancellationToken))
                sb.Append(token);
            reply = sb.ToString();

            if (retrievalContext.Results.Count > 0)
            {
                var sources = retrievalContext.Results.Select(r => r.FileName).Distinct();
                reply += "\n\n來源：" + string.Join("、", sources);
            }

            await conversations.AppendMessageAsync(conversation.Id, "user", text, ct: cancellationToken);
            await conversations.AppendMessageAsync(conversation.Id, "assistant", sb.ToString(), ct: cancellationToken);
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
