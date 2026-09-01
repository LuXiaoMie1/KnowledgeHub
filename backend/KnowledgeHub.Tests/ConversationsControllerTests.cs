using System.Runtime.CompilerServices;
using System.Text;
using KnowledgeHub.Api.Controllers;
using KnowledgeHub.Api.Sse;
using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

public class ConversationsControllerTests
{
    private sealed class FakeUser : ICurrentUser
    {
        public string Department => "IT";
        public IReadOnlyList<string> Departments => ["IT"];
        public string Username => "it-user";
        public string UserKey => "test-user";
    }

    private sealed class FakeChat(string answer, Exception? throwEx = null) : IChatService
    {
        public List<IReadOnlyList<ChatTurn>> CapturedHistories { get; } = [];

        public async IAsyncEnumerable<string> StreamAnswerAsync(
            string message, IReadOnlyList<ChatTurn> history,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            CapturedHistories.Add(history);
            if (throwEx is not null) throw throwEx;
            yield return answer;
            await Task.CompletedTask;
        }
    }

    private sealed class FakeConversations : IConversationRepository
    {
        public Conversation? OwnedConversation;
        public List<ConversationMessage> Messages = [];
        public bool DeleteResult = true;
        public List<(string UserKey, string Channel, string Title)> CreateCalls { get; } = [];
        public List<(Guid ConversationId, string Role, string Content, string? SourcesJson)> AppendCalls { get; } = [];

        public Task<Conversation> CreateAsync(string userKey, string channel, string title,
            string? teamsConversationId = null, CancellationToken ct = default)
        {
            CreateCalls.Add((userKey, channel, title));
            var now = DateTime.UtcNow;
            return Task.FromResult(new Conversation
            {
                Id = Guid.NewGuid(), UserKey = userKey, Channel = channel, Title = title,
                CreatedAtUtc = now, UpdatedAtUtc = now
            });
        }

        public Task<Conversation?> FindOwnedAsync(Guid id, string userKey, CancellationToken ct = default)
            => Task.FromResult(OwnedConversation);

        public Task<IReadOnlyList<ConversationSummary>> ListAsync(string userKey, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ConversationSummary>>([]);

        public Task<IReadOnlyList<ConversationMessage>> GetMessagesAsync(
            Guid conversationId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ConversationMessage>>(Messages);

        public Task AppendMessageAsync(Guid conversationId, string role, string content,
            string? sourcesJson = null, CancellationToken ct = default)
        {
            AppendCalls.Add((conversationId, role, content, sourcesJson));
            return Task.CompletedTask;
        }

        public Task<bool> DeleteOwnedAsync(Guid id, string userKey, CancellationToken ct = default)
            => Task.FromResult(DeleteResult);

        public Task<Conversation?> FindActiveTeamsAsync(string teamsConversationId, CancellationToken ct = default)
            => Task.FromResult<Conversation?>(null);

        public Task EndAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static (ConversationsController Ctrl, MemoryStream Body) NewController(
        FakeConversations conversations, IChatService chat)
    {
        var ctrl = new ConversationsController(conversations, chat, new FakeUser(),
            new RetrievalContext(), NullLogger<ChatSseStreamer>.Instance);
        var body = new MemoryStream();
        var httpContext = new DefaultHttpContext { Response = { Body = body } };
        ctrl.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return (ctrl, body);
    }

    private static string ReadResponseBody(MemoryStream body)
    {
        body.Seek(0, SeekOrigin.Begin);
        return Encoding.UTF8.GetString(body.ToArray());
    }

    [Fact]
    public async Task 帶他人conversationId_回404不串流()
    {
        var repo = new FakeConversations();
        var chat = new FakeChat("答");
        var (ctrl, _) = NewController(repo, chat);

        await ctrl.SendMessage(new SendMessageRequest(Guid.NewGuid(), "hi"), CancellationToken.None);

        Assert.Equal(404, ctrl.Response.StatusCode);
        Assert.Empty(chat.CapturedHistories);
    }

    [Fact]
    public async Task 新對話_先發conversation事件_落庫user與assistant訊息()
    {
        var repo = new FakeConversations();
        var chat = new FakeChat("答");
        var (ctrl, body) = NewController(repo, chat);

        await ctrl.SendMessage(new SendMessageRequest(null, "第一句問題"), CancellationToken.None);

        var text = ReadResponseBody(body);
        Assert.Contains("event: conversation", text);
        Assert.True(text.IndexOf("event: conversation", StringComparison.Ordinal)
            < text.IndexOf("event: token", StringComparison.Ordinal));

        var create = Assert.Single(repo.CreateCalls);
        Assert.Equal(("test-user", ConversationChannels.Web, "第一句問題"), create);

        Assert.Equal(2, repo.AppendCalls.Count);
        Assert.Equal("user", repo.AppendCalls[0].Role);
        Assert.Equal("第一句問題", repo.AppendCalls[0].Content);
        Assert.Null(repo.AppendCalls[0].SourcesJson);
        Assert.Equal("assistant", repo.AppendCalls[1].Role);
        Assert.Equal("答", repo.AppendCalls[1].Content);
    }

    [Fact]
    public async Task 歷史裁切_只送近10則給chat()
    {
        var existingId = Guid.NewGuid();
        var repo = new FakeConversations
        {
            OwnedConversation = new Conversation
            {
                Id = existingId, UserKey = "test-user", Channel = ConversationChannels.Web, Title = "舊對話",
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            },
            Messages = Enumerable.Range(1, 15).Select(i => new ConversationMessage
            {
                Id = Guid.NewGuid(), ConversationId = existingId,
                Role = i % 2 == 1 ? "user" : "assistant", Content = $"第{i}則",
                CreatedAtUtc = DateTime.UtcNow
            }).ToList()
        };
        var chat = new FakeChat("答");
        var (ctrl, _) = NewController(repo, chat);

        await ctrl.SendMessage(new SendMessageRequest(existingId, "新訊息"), CancellationToken.None);

        var history = Assert.Single(chat.CapturedHistories);
        Assert.Equal(10, history.Count);
        Assert.Equal("第6則", history[0].Content);
        Assert.Equal("第15則", history[^1].Content);
    }

    [Fact]
    public async Task 訊息超過4000字_回400()
    {
        var repo = new FakeConversations();
        var chat = new FakeChat("答");
        var (ctrl, _) = NewController(repo, chat);

        await ctrl.SendMessage(new SendMessageRequest(null, new string('字', 4001)), CancellationToken.None);

        Assert.Equal(400, ctrl.Response.StatusCode);
        Assert.Empty(repo.CreateCalls);
        Assert.Empty(chat.CapturedHistories);
    }

    [Fact]
    public async Task 串流失敗_不落庫assistant訊息()
    {
        var repo = new FakeConversations();
        var chat = new FakeChat("", throwEx: new InvalidOperationException("boom"));
        var (ctrl, _) = NewController(repo, chat);

        await ctrl.SendMessage(new SendMessageRequest(null, "問題"), CancellationToken.None);

        var append = Assert.Single(repo.AppendCalls);
        Assert.Equal("user", append.Role);
        Assert.Equal("問題", append.Content);
    }

    [Fact]
    public async Task Get帶他人id或不存在_回404()
    {
        var repo = new FakeConversations();
        var (ctrl, _) = NewController(repo, new FakeChat("答"));

        var result = await ctrl.Get(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Get成功_回傳訊息內容()
    {
        var id = Guid.NewGuid();
        var repo = new FakeConversations
        {
            OwnedConversation = new Conversation
            {
                Id = id, UserKey = "test-user", Channel = ConversationChannels.Web, Title = "t",
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            },
            Messages =
            [
                new ConversationMessage
                {
                    Id = Guid.NewGuid(), ConversationId = id, Role = "user", Content = "哈囉",
                    SourcesJson = null, CreatedAtUtc = DateTime.UtcNow
                }
            ]
        };
        var (ctrl, _) = NewController(repo, new FakeChat("答"));

        var result = await ctrl.Get(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var first = Assert.Single((IEnumerable<object>)ok.Value!);
        var type = first.GetType();
        Assert.Equal("user", type.GetProperty("Role")!.GetValue(first));
        Assert.Equal("哈囉", type.GetProperty("Content")!.GetValue(first));
    }

    [Fact]
    public async Task Delete不存在或非本人_回404()
    {
        var repo = new FakeConversations { DeleteResult = false };
        var (ctrl, _) = NewController(repo, new FakeChat("答"));

        var result = await ctrl.Delete(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Delete成功_回204()
    {
        var repo = new FakeConversations { DeleteResult = true };
        var (ctrl, _) = NewController(repo, new FakeChat("答"));

        var result = await ctrl.Delete(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }
}
