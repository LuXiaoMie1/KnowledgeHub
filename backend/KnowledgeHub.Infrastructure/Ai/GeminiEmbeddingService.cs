using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeHub.Core.Interfaces;

namespace KnowledgeHub.Infrastructure.Ai;

public class GeminiEmbeddingService(HttpClient http, string projectId, string location) : IEmbeddingService
{
    // 實測（2026-08-07，Vertex predict 端點）：單請求 64 instances 回 200 且順序正確，
    // 沿用與舊 batchEmbedContents 相同的批次大小。
    private const int BatchSize = 64;
    private const int Dimensions = 1536;
    private const string Model = "gemini-embedding-001";

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var all = new List<float[]>(texts.Count);
        foreach (var batch in texts.Chunk(BatchSize))
        {
            var payload = new
            {
                instances = batch.Select(t => new { content = t }),
                parameters = new { outputDimensionality = Dimensions }
            };
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"v1/projects/{projectId}/locations/{location}/publishers/google/models/{Model}:predict");
            request.Content = JsonContent.Create(payload);

            var response = await http.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                var summary = responseBody.Length > 500 ? responseBody[..500] : responseBody;
                throw new HttpRequestException(
                    $"Vertex embedding API 回傳非成功狀態 {(int)response.StatusCode} {response.StatusCode}：{summary}");
            }

            using var json = JsonDocument.Parse(responseBody);
            foreach (var p in json.RootElement.GetProperty("predictions").EnumerateArray())
            {
                var vector = p.GetProperty("embeddings").GetProperty("values").EnumerateArray()
                    .Select(v => v.GetSingle()).ToArray();
                all.Add(Normalize(vector));
            }
        }
        return all;
    }

    // gemini-embedding-001 非 3072 維的輸出未正規化，官方要求自行做 L2 正規化
    private static float[] Normalize(float[] v)
    {
        var norm = MathF.Sqrt(v.Sum(x => x * x));
        return norm == 0 ? v : v.Select(x => x / norm).ToArray();
    }
}
