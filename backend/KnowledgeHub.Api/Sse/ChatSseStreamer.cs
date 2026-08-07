using System.Text;
using System.Text.Json;
using KnowledgeHub.Core;
using KnowledgeHub.Core.Interfaces;

namespace KnowledgeHub.Api.Sse;

public class ChatSseStreamer(IChatService chat, RetrievalContext context)
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task StreamAsync(Stream output, string message,
        IReadOnlyList<ChatTurn> history, CancellationToken ct)
    {
        try
        {
            await foreach (var token in chat.StreamAnswerAsync(message, history, ct))
                await WriteEventAsync(output, "token", JsonSerializer.Serialize(new { text = token }, JsonOpts), ct);

            if (context.Results.Count > 0)
            {
                var sources = context.Results.Select(r => new
                    { r.FileName, r.SequenceNumber, r.Content, r.Distance });
                await WriteEventAsync(output, "sources", JsonSerializer.Serialize(sources, JsonOpts), ct);
            }
            await WriteEventAsync(output, "done", "{}", ct);
        }
        catch (Exception ex)
        {
            await WriteEventAsync(output, "error",
                JsonSerializer.Serialize(new { message = ex.Message }, JsonOpts), ct);
        }
    }

    private static async Task WriteEventAsync(Stream output, string name, string data, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes($"event: {name}\ndata: {data}\n\n");
        await output.WriteAsync(bytes, ct);
        await output.FlushAsync(ct);
    }
}
