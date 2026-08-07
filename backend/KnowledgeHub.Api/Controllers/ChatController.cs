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
public class ChatController(IChatService chat, RetrievalContext context) : ControllerBase
{
    [HttpPost]
    public async Task Post(ChatRequest request, CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        await new ChatSseStreamer(chat, context)
            .StreamAsync(Response.Body, request.Message, request.History, ct);
    }
}
