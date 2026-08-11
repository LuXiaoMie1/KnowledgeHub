using System.Runtime.CompilerServices;
using KnowledgeHub.Api.Bot;
using KnowledgeHub.Core;
using KnowledgeHub.Core.Interfaces;
using Microsoft.Bot.Builder.Adapters;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging.Abstractions;

// bot 的 RAG 問答驗收：收到訊息先送 typing indicator，再回覆聚合後的完整答案＋來源清單；
// LLM 失敗時回友善訊息、不炸例外（見 KnowledgeHubBotHandler 類別註解）。
// 「檢索範圍固定為 ALL」與「bot kernel 不掛 EmailPlugin」分別由
// RetrievalPluginTests.Bot用AllDepartmentsScope_檢索傳入ALL而非任何部門、
// KernelFactoryTests.不給EmailPlugin_kernel不含email外掛_只含retrieval 驗證。
public class KnowledgeHubBotHandlerTests
{
    private sealed class FakeChat(IEnumerable<string> tokens) : IChatService
    {
        public async IAsyncEnumerable<string> StreamAnswerAsync(
            string message, IReadOnlyList<ChatTurn> history,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var t in tokens) { yield return t; await Task.Yield(); }
        }
    }

    private sealed class ThrowingChat : IChatService
    {
        public IAsyncEnumerable<string> StreamAnswerAsync(
            string message, IReadOnlyList<ChatTurn> history, CancellationToken ct = default)
            => throw new InvalidOperationException("LLM 呼叫失敗（模擬）");
    }

    private static TestAdapter NewAdapter() =>
        new(TestAdapter.CreateConversation(nameof(KnowledgeHubBotHandlerTests)));

    [Fact]
    public async Task 收到訊息_先送typing_再回覆RAG答案含來源清單()
    {
        var context = new RetrievalContext();
        context.Results.Add(new ChunkSearchResult(Guid.NewGuid(), Guid.NewGuid(), "sop.md", 1, "重開步驟…", 0.1));
        var bot = new KnowledgeHubBotHandler(new FakeChat(["重開", "POS 的步驟如上"]), context, NullLogger<KnowledgeHubBotHandler>.Instance);

        await new TestFlow(NewAdapter(), bot.OnTurnAsync)
            .Send("POS 怎麼重開")
            .AssertReply(a => Assert.Equal(ActivityTypes.Typing, a.Type))
            .AssertReply("重開POS 的步驟如上\n\n來源：sop.md")
            .StartTestAsync();
    }

    [Fact]
    public async Task 沒有檢索結果_回覆內容不附來源行()
    {
        var bot = new KnowledgeHubBotHandler(new FakeChat(["你好"]), new RetrievalContext(), NullLogger<KnowledgeHubBotHandler>.Instance);

        await new TestFlow(NewAdapter(), bot.OnTurnAsync)
            .Send("哈囉")
            .AssertReply(a => Assert.Equal(ActivityTypes.Typing, a.Type))
            .AssertReply("你好")
            .StartTestAsync();
    }

    [Fact]
    public async Task LLM呼叫失敗_回覆友善錯誤訊息_不拋出例外()
    {
        var bot = new KnowledgeHubBotHandler(new ThrowingChat(), new RetrievalContext(), NullLogger<KnowledgeHubBotHandler>.Instance);

        var ex = await Record.ExceptionAsync(() =>
            new TestFlow(NewAdapter(), bot.OnTurnAsync)
                .Send("問題")
                .AssertReply(a => Assert.Equal(ActivityTypes.Typing, a.Type))
                .AssertReply("抱歉，處理您的問題時發生錯誤，請稍後再試。")
                .StartTestAsync());

        Assert.Null(ex);
    }
}
