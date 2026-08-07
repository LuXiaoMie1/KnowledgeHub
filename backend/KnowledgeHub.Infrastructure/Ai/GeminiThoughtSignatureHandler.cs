using System.Text;
using System.Text.Json.Nodes;

namespace KnowledgeHub.Infrastructure.Ai;

/// <summary>
/// Gemini 的 OpenAI 相容端點要求每個 assistant tool_call 都帶回 extra_content.google.thought_signature
/// （多輪 function calling 的簽章回填），否則第二輪呼叫回 400 INVALID_ARGUMENT。
/// Semantic Kernel 1.79 的 OpenAI 連接器不認得這個 Gemini 專屬擴充欄位，序列化下一輪請求時會遺失它。
/// Google 官方文件（https://ai.google.dev/gemini-api/docs/thought-signatures）提供的相容路徑是：
/// 帶入固定字串 "skip_thought_signature_validator" 可跳過該輪驗證。這個 handler 在請求送出前
/// 幫遺漏的 tool_call 補上這個佔位值，屬於已知 Gemini↔OpenAI-相容層落差的暫時解法。
/// </summary>
public class GeminiThoughtSignatureHandler : DelegatingHandler
{
    private const string PlaceholderSignature = "skip_thought_signature_validator";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Content is not null &&
            request.RequestUri?.AbsolutePath.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) == true)
        {
            var body = await request.Content.ReadAsStringAsync(ct);
            var patched = PatchMissingThoughtSignatures(body);
            if (patched is not null)
            {
                request.Content = new StringContent(patched, Encoding.UTF8, "application/json");
            }
        }
        return await base.SendAsync(request, ct);
    }

    private static string? PatchMissingThoughtSignatures(string body)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(body); }
        catch { return null; }

        if (root is not JsonObject rootObj || rootObj["messages"] is not JsonArray messages)
            return null;

        var changed = false;
        foreach (var message in messages)
        {
            if (message is not JsonObject msgObj) continue;
            if (msgObj["tool_calls"] is not JsonArray toolCalls) continue;

            foreach (var toolCall in toolCalls)
            {
                if (toolCall is not JsonObject toolCallObj) continue;
                if (toolCallObj.ContainsKey("extra_content")) continue;

                toolCallObj["extra_content"] = new JsonObject
                {
                    ["google"] = new JsonObject { ["thought_signature"] = PlaceholderSignature }
                };
                changed = true;
            }
        }

        return changed ? root.ToJsonString() : null;
    }
}
