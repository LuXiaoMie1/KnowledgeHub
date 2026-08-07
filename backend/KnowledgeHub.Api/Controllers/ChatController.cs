using KnowledgeHub.Api.Sse;
using KnowledgeHub.Core;
using KnowledgeHub.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeHub.Api.Controllers;

public record ChatRequest(string Message, List<ChatTurn> History);

[ApiController]
[Route("api/chat")]
[Authorize]
public class ChatController(IChatService chat, RetrievalContext context, ILogger<ChatSseStreamer> logger)
    : ControllerBase
{
    // 伺服器端上限：避免免費層 LLM 配額被異常長的輸入或愈滾愈長的對話紀錄燒掉。
    private const int MaxHistoryTurns = 10;
    private const int MaxContentLength = 4000;

    [HttpPost]
    public async Task Post(ChatRequest request, CancellationToken ct)
    {
        if (request.Message.Length > MaxContentLength)
        {
            await WriteBadRequestAsync($"訊息長度不可超過 {MaxContentLength} 字元", ct);
            return;
        }

        var history = request.History.Count > MaxHistoryTurns
            ? request.History.TakeLast(MaxHistoryTurns).ToList()
            : request.History;
        if (history.Any(t => t.Content.Length > MaxContentLength))
        {
            await WriteBadRequestAsync($"對話紀錄單則內容不可超過 {MaxContentLength} 字元", ct);
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        await new ChatSseStreamer(chat, context, logger)
            .StreamAsync(Response.Body, request.Message, history, ct);
    }

    private async Task WriteBadRequestAsync(string error, CancellationToken ct)
    {
        Response.StatusCode = StatusCodes.Status400BadRequest;
        await Response.WriteAsJsonAsync(new { error }, ct);
    }
}
