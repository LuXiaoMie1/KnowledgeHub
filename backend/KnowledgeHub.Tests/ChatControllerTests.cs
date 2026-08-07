using System.Runtime.CompilerServices;
using System.Text.Json;
using KnowledgeHub.Api.Controllers;
using KnowledgeHub.Api.Sse;
using KnowledgeHub.Core;
using KnowledgeHub.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

public class ChatControllerTests
{
    private sealed class FakeChat(List<IReadOnlyList<ChatTurn>> capturedHistories) : IChatService
    {
        public async IAsyncEnumerable<string> StreamAnswerAsync(
            string message, IReadOnlyList<ChatTurn> history,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            capturedHistories.Add(history);
            yield return "answer";
            await Task.CompletedTask;
        }
    }

    private static (ChatController Ctrl, MemoryStream Body) NewController(IChatService chat)
    {
        var ctrl = new ChatController(chat, new RetrievalContext(), NullLogger<ChatSseStreamer>.Instance);
        var body = new MemoryStream();
        var httpContext = new DefaultHttpContext { Response = { Body = body } };
        ctrl.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return (ctrl, body);
    }

    private static (int Status, string? Error) ReadError(MemoryStream body, HttpContext ctx)
    {
        body.Seek(0, SeekOrigin.Begin);
        using var doc = JsonDocument.Parse(body.ToArray());
        return (ctx.Response.StatusCode, doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task 訊息過長回400()
    {
        var (ctrl, body) = NewController(new FakeChat([]));
        var longMessage = new string('x', 4001);

        await ctrl.Post(new ChatRequest(longMessage, []), CancellationToken.None);

        var (status, error) = ReadError(body, ctrl.HttpContext);
        Assert.Equal(400, status);
        Assert.Contains("4000", error);
    }

    [Fact]
    public async Task history單則過長回400()
    {
        var (ctrl, body) = NewController(new FakeChat([]));
        var history = new List<ChatTurn> { new("user", new string('x', 4001)) };

        await ctrl.Post(new ChatRequest("正常訊息", history), CancellationToken.None);

        var (status, _) = ReadError(body, ctrl.HttpContext);
        Assert.Equal(400, status);
    }

    [Fact]
    public async Task history超過10輪只取最後10輪()
    {
        var captured = new List<IReadOnlyList<ChatTurn>>();
        var (ctrl, _) = NewController(new FakeChat(captured));
        var history = Enumerable.Range(0, 15)
            .Select(i => new ChatTurn(i % 2 == 0 ? "user" : "assistant", $"第{i}輪"))
            .ToList();

        await ctrl.Post(new ChatRequest("問題", history), CancellationToken.None);

        var sent = Assert.Single(captured);
        Assert.Equal(10, sent.Count);
        Assert.Equal("第5輪", sent[0].Content);
        Assert.Equal("第14輪", sent[^1].Content);
    }
}
