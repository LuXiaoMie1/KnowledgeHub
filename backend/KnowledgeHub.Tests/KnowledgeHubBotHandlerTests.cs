using KnowledgeHub.Api.Bot;
using Microsoft.Bot.Builder.Adapters;

// 骨架驗收：Emulator／Teams 打進 /api/messages 後，bot 至少要能回覆固定格式的回音，
// 見 KnowledgeHubBotHandler 的類別註解（RAG 接線為後續工作）。
public class KnowledgeHubBotHandlerTests
{
    [Fact]
    public async Task 收到文字訊息_回覆固定格式回音()
    {
        var adapter = new TestAdapter(TestAdapter.CreateConversation(nameof(KnowledgeHubBotHandlerTests)));
        var bot = new KnowledgeHubBotHandler();

        await new TestFlow(adapter, bot.OnTurnAsync)
            .Send("哈囉")
            .AssertReply("收到：「哈囉」（RAG 接線為後續工作）")
            .StartTestAsync();
    }
}
