using KnowledgeHub.Api.Sse;
using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeHub.Api.Controllers;

public record SendMessageRequest(Guid? ConversationId, string Message);

[ApiController]
[Route("api/conversations")]
[Authorize]
public class ConversationsController(
    IConversationRepository conversations, IChatService chat, ICurrentUser user,
    RetrievalContext context, ILogger<ChatSseStreamer> logger) : ControllerBase
{
    // 沿用舊 ChatController 的上限（免費層 LLM 配額保護）
    private const int MaxHistoryTurns = 10;
    private const int MaxContentLength = 4000;

    [HttpGet]
    public async Task<IReadOnlyList<ConversationSummary>> List(CancellationToken ct)
        => await conversations.ListAsync(user.UserKey, ct);

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (await conversations.FindOwnedAsync(id, user.UserKey, ct) is null) return NotFound();
        var messages = await conversations.GetMessagesAsync(id, ct);
        return Ok(messages.Select(m => new { m.Role, m.Content, m.SourcesJson, m.CreatedAtUtc }));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => await conversations.DeleteOwnedAsync(id, user.UserKey, ct) ? NoContent() : NotFound();

    [HttpPost("messages")]
    public async Task SendMessage(SendMessageRequest request, CancellationToken ct)
    {
        if (request.Message.Length > MaxContentLength)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { error = $"訊息長度不可超過 {MaxContentLength} 字元" }, ct);
            return;
        }

        Conversation conversation;
        var isNew = request.ConversationId is null;
        if (request.ConversationId is Guid id)
        {
            var found = await conversations.FindOwnedAsync(id, user.UserKey, ct);
            if (found is null) { Response.StatusCode = StatusCodes.Status404NotFound; return; }
            conversation = found;
        }
        else
        {
            conversation = await conversations.CreateAsync(user.UserKey,
                ConversationChannels.Web, ConversationTitle.From(request.Message), ct: ct);
        }

        // 先載歷史再落 user 訊息，歷史才不會把當前訊息算進去
        var history = (await conversations.GetMessagesAsync(conversation.Id, ct))
            .TakeLast(MaxHistoryTurns)
            .Select(m => new ChatTurn(m.Role, m.Content))
            .ToList();
        await conversations.AppendMessageAsync(conversation.Id, "user", request.Message, ct: ct);

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        var streamer = new ChatSseStreamer(chat, context, logger);
        if (isNew)
            await streamer.WriteConversationEventAsync(Response.Body, conversation.Id, conversation.Title, ct);
        var result = await streamer.StreamAsync(Response.Body, request.Message, history, ct);
        // 失敗（null）不落 assistant 訊息：使用者重試時對話裡只有問句，與 ChatGPT 行為一致
        if (result is not null)
            await conversations.AppendMessageAsync(conversation.Id, "assistant",
                result.Value.Answer, result.Value.SourcesJson, ct);
    }
}
