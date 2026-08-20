using System.Text;
using System.Text.Json;
using KnowledgeHub.Core;
using KnowledgeHub.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace KnowledgeHub.Api.Sse;

public class ChatSseStreamer(IChatService chat, RetrievalContext context, ILogger<ChatSseStreamer> logger)
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<(string Answer, string? SourcesJson)?> StreamAsync(Stream output, string message,
        IReadOnlyList<ChatTurn> history, CancellationToken ct)
    {
        try
        {
            var answer = new StringBuilder();
            await foreach (var token in chat.StreamAnswerAsync(message, history, ct))
            {
                answer.Append(token);
                await WriteEventAsync(output, "token", JsonSerializer.Serialize(new { text = token }, JsonOpts), ct);
            }

            string? sourcesJson = null;
            if (context.Results.Count > 0)
            {
                sourcesJson = JsonSerializer.Serialize(context.Results.Select(r => new
                    { r.FileName, r.SequenceNumber, r.Content, r.Distance }), JsonOpts);
                await WriteEventAsync(output, "sources", sourcesJson, ct);
            }
            await WriteEventAsync(output, "done", "{}", ct);
            return (answer.ToString(), sourcesJson);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // client 斷線或請求被取消：stream 可能已經死了，靜默結束，不再寫任何事件、不往外拋。
            return null;
        }
        catch (Exception ex)
        {
            // 上游例外訊息（可能含第三方 API 回應內容）不可原文送到瀏覽器，只記完整例外到 log。
            logger.LogError(ex, "Chat SSE 串流處理失敗");
            try
            {
                await WriteEventAsync(output, "error",
                    JsonSerializer.Serialize(new { message = "處理過程發生錯誤，請稍後再試" }, JsonOpts),
                    CancellationToken.None);
            }
            catch (Exception writeEx)
            {
                // 寫 error 事件本身失敗（例如 client 已斷線）：已記過原始例外，這裡只補記不再往外拋。
                logger.LogError(writeEx, "寫入 SSE error 事件失敗");
            }
            return null;
        }
    }

    public async Task WriteConversationEventAsync(Stream output, Guid id, string title, CancellationToken ct)
        => await WriteEventAsync(output, "conversation",
            JsonSerializer.Serialize(new { id, title }, JsonOpts), ct);

    private static async Task WriteEventAsync(Stream output, string name, string data, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes($"event: {name}\ndata: {data}\n\n");
        await output.WriteAsync(bytes, ct);
        await output.FlushAsync(ct);
    }
}
