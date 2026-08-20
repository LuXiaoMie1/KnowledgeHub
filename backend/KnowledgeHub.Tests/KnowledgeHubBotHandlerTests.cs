using System.Runtime.CompilerServices;
using KnowledgeHub.Api.Bot;
using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
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
        public List<IReadOnlyList<ChatTurn>> CapturedHistories { get; } = [];
        public int CallCount { get; private set; }

        public async IAsyncEnumerable<string> StreamAnswerAsync(
            string message, IReadOnlyList<ChatTurn> history,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            CallCount++;
            CapturedHistories.Add(history);
            foreach (var t in tokens) { yield return t; await Task.Yield(); }
        }
    }

    private sealed class ThrowingChat : IChatService
    {
        public IAsyncEnumerable<string> StreamAnswerAsync(
            string message, IReadOnlyList<ChatTurn> history, CancellationToken ct = default)
            => throw new InvalidOperationException("LLM 呼叫失敗（模擬）");
    }

    private sealed class FakeConversations : IConversationRepository
    {
        public Conversation? ActiveConversation;
        public List<ConversationMessage> Messages = [];
        public List<(string UserKey, string Channel, string Title, string? TeamsConversationId)> CreateCalls { get; } = [];
        public List<(Guid ConversationId, string Role, string Content)> AppendCalls { get; } = [];
        public List<Guid> EndCalls { get; } = [];
        public Exception? AppendException;

        public Task<Conversation> CreateAsync(string userKey, string channel, string title,
            string? teamsConversationId = null, CancellationToken ct = default)
        {
            CreateCalls.Add((userKey, channel, title, teamsConversationId));
            var now = DateTime.UtcNow;
            var conversation = new Conversation
            {
                Id = Guid.NewGuid(), UserKey = userKey, Channel = channel, Title = title,
                TeamsConversationId = teamsConversationId, CreatedAtUtc = now, UpdatedAtUtc = now
            };
            ActiveConversation = conversation;
            return Task.FromResult(conversation);
        }

        public Task<Conversation?> FindOwnedAsync(Guid id, string userKey, CancellationToken ct = default)
            => throw new NotSupportedException("bot 不使用");

        public Task<IReadOnlyList<ConversationSummary>> ListAsync(string userKey, CancellationToken ct = default)
            => throw new NotSupportedException("bot 不使用");

        public Task<IReadOnlyList<ConversationMessage>> GetMessagesAsync(
            Guid conversationId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ConversationMessage>>(Messages);

        public Task AppendMessageAsync(Guid conversationId, string role, string content,
            string? sourcesJson = null, CancellationToken ct = default)
        {
            AppendCalls.Add((conversationId, role, content));
            if (AppendException is not null) throw AppendException;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteOwnedAsync(Guid id, string userKey, CancellationToken ct = default)
            => throw new NotSupportedException("bot 不使用");

        public Task<Conversation?> FindActiveTeamsAsync(string teamsConversationId, CancellationToken ct = default)
            => Task.FromResult(ActiveConversation);

        public Task EndAsync(Guid id, CancellationToken ct = default)
        {
            EndCalls.Add(id);
            return Task.CompletedTask;
        }
    }

    private static TestAdapter NewAdapter() =>
        new(TestAdapter.CreateConversation(nameof(KnowledgeHubBotHandlerTests)));

    /// <summary>建 Teams 訊息活動，帶 AadObjectId（Teams 端使用者身分），供多輪對話測試用。</summary>
    private static Activity NewTeamsActivity(TestAdapter adapter, string text, string aadObjectId = "aad-oid-123")
    {
        var activity = adapter.MakeActivity(text);
        activity.From.AadObjectId = aadObjectId;
        return activity;
    }

    [Fact]
    public async Task 收到訊息_先送typing_再回覆RAG答案含來源清單()
    {
        var context = new RetrievalContext();
        context.Results.Add(new ChunkSearchResult(Guid.NewGuid(), Guid.NewGuid(), "sop.md", 1, "重開步驟…", 0.1));
        var bot = new KnowledgeHubBotHandler(new FakeChat(["重開", "POS 的步驟如上"]), context,
            NullLogger<KnowledgeHubBotHandler>.Instance, new FakeConversations());

        await new TestFlow(NewAdapter(), bot.OnTurnAsync)
            .Send("POS 怎麼重開")
            .AssertReply(a => Assert.Equal(ActivityTypes.Typing, a.Type))
            .AssertReply("重開POS 的步驟如上\n\n來源：sop.md")
            .StartTestAsync();
    }

    [Fact]
    public async Task 沒有檢索結果_回覆內容不附來源行()
    {
        var bot = new KnowledgeHubBotHandler(new FakeChat(["你好"]), new RetrievalContext(),
            NullLogger<KnowledgeHubBotHandler>.Instance, new FakeConversations());

        await new TestFlow(NewAdapter(), bot.OnTurnAsync)
            .Send("哈囉")
            .AssertReply(a => Assert.Equal(ActivityTypes.Typing, a.Type))
            .AssertReply("你好")
            .StartTestAsync();
    }

    [Fact]
    public async Task LLM呼叫失敗_回覆友善錯誤訊息_不拋出例外()
    {
        var bot = new KnowledgeHubBotHandler(new ThrowingChat(), new RetrievalContext(),
            NullLogger<KnowledgeHubBotHandler>.Instance, new FakeConversations());

        var ex = await Record.ExceptionAsync(() =>
            new TestFlow(NewAdapter(), bot.OnTurnAsync)
                .Send("問題")
                .AssertReply(a => Assert.Equal(ActivityTypes.Typing, a.Type))
                .AssertReply("抱歉，處理您的問題時發生錯誤，請稍後再試。")
                .StartTestAsync());

        Assert.Null(ex);
    }

    [Fact]
    public async Task 一般訊息_接續未結束對話_歷史送入chat()
    {
        var adapter = NewAdapter();
        var existingId = Guid.NewGuid();
        var teamsConversationId = adapter.MakeActivity().Conversation.Id;
        var repo = new FakeConversations
        {
            ActiveConversation = new Conversation
            {
                Id = existingId, UserKey = "aad-oid-123", Channel = ConversationChannels.Teams,
                Title = "舊對話", TeamsConversationId = teamsConversationId,
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            },
            Messages =
            [
                new ConversationMessage { Id = Guid.NewGuid(), ConversationId = existingId, Role = "user", Content = "第1則", CreatedAtUtc = DateTime.UtcNow },
                new ConversationMessage { Id = Guid.NewGuid(), ConversationId = existingId, Role = "assistant", Content = "第2則", CreatedAtUtc = DateTime.UtcNow }
            ]
        };
        var chat = new FakeChat(["答"]);
        var bot = new KnowledgeHubBotHandler(chat, new RetrievalContext(), NullLogger<KnowledgeHubBotHandler>.Instance, repo);

        await new TestFlow(adapter, bot.OnTurnAsync)
            .Send(NewTeamsActivity(adapter, "接下來呢"))
            .AssertReply(a => Assert.Equal(ActivityTypes.Typing, a.Type))
            .AssertReply("答")
            .StartTestAsync();

        var history = Assert.Single(chat.CapturedHistories);
        Assert.Equal(2, history.Count);
        Assert.Equal(2, repo.AppendCalls.Count);
        Assert.Equal((existingId, "user", "接下來呢"), repo.AppendCalls[0]);
        Assert.Equal((existingId, "assistant", "答"), repo.AppendCalls[1]);
    }

    [Fact]
    public async Task 沒有活躍對話_自動建新的()
    {
        var adapter = NewAdapter();
        var repo = new FakeConversations();
        var chat = new FakeChat(["答"]);
        var bot = new KnowledgeHubBotHandler(chat, new RetrievalContext(), NullLogger<KnowledgeHubBotHandler>.Instance, repo);
        var activity = NewTeamsActivity(adapter, "第一句問題");

        await new TestFlow(adapter, bot.OnTurnAsync)
            .Send(activity)
            .AssertReply(a => Assert.Equal(ActivityTypes.Typing, a.Type))
            .AssertReply("答")
            .StartTestAsync();

        var create = Assert.Single(repo.CreateCalls);
        Assert.Equal("aad-oid-123", create.UserKey);
        Assert.Equal(ConversationChannels.Teams, create.Channel);
        Assert.Equal(activity.Conversation.Id, create.TeamsConversationId);
    }

    [Fact]
    public async Task 新對話指令_蓋章結束並回確認_不進RAG()
    {
        var adapter = NewAdapter();
        var existingId = Guid.NewGuid();
        var repo = new FakeConversations
        {
            ActiveConversation = new Conversation
            {
                Id = existingId, UserKey = "aad-oid-123", Channel = ConversationChannels.Teams,
                Title = "舊對話", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            }
        };
        var chat = new FakeChat(["答"]);
        var bot = new KnowledgeHubBotHandler(chat, new RetrievalContext(), NullLogger<KnowledgeHubBotHandler>.Instance, repo);

        await new TestFlow(adapter, bot.OnTurnAsync)
            .Send(NewTeamsActivity(adapter, "新對話"))
            .AssertReply("好的，已為您開啟新對話，請直接提問。")
            .StartTestAsync();

        Assert.Equal([existingId], repo.EndCalls);
        Assert.Equal(0, chat.CallCount);
    }

    [Fact]
    public async Task slash_new_等同新對話指令()
    {
        var adapter = NewAdapter();
        var existingId = Guid.NewGuid();
        var repo = new FakeConversations
        {
            ActiveConversation = new Conversation
            {
                Id = existingId, UserKey = "aad-oid-123", Channel = ConversationChannels.Teams,
                Title = "舊對話", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            }
        };
        var chat = new FakeChat(["答"]);
        var bot = new KnowledgeHubBotHandler(chat, new RetrievalContext(), NullLogger<KnowledgeHubBotHandler>.Instance, repo);

        await new TestFlow(adapter, bot.OnTurnAsync)
            .Send(NewTeamsActivity(adapter, "/new"))
            .AssertReply("好的，已為您開啟新對話，請直接提問。")
            .StartTestAsync();

        Assert.Equal([existingId], repo.EndCalls);
        Assert.Equal(0, chat.CallCount);
    }

    [Fact]
    public async Task 落庫失敗_不覆蓋已算出的回答()
    {
        var adapter = NewAdapter();
        var repo = new FakeConversations { AppendException = new InvalidOperationException("db 落庫失敗（模擬）") };
        var chat = new FakeChat(["這是正確答案"]);
        var bot = new KnowledgeHubBotHandler(chat, new RetrievalContext(), NullLogger<KnowledgeHubBotHandler>.Instance, repo);

        var ex = await Record.ExceptionAsync(() =>
            new TestFlow(adapter, bot.OnTurnAsync)
                .Send(NewTeamsActivity(adapter, "問題"))
                .AssertReply(a => Assert.Equal(ActivityTypes.Typing, a.Type))
                .AssertReply("這是正確答案")
                .StartTestAsync());

        Assert.Null(ex);
    }
}
