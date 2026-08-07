using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeHub.Core.Interfaces;

namespace KnowledgeHub.Infrastructure.Ai;

public class GeminiEmbeddingService(HttpClient http, string apiKey) : IEmbeddingService
{
    private const int BatchSize = 64;
    private const int Dimensions = 1536;
    private const string Model = "models/gemini-embedding-001";

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var all = new List<float[]>(texts.Count);
        foreach (var batch in texts.Chunk(BatchSize))
        {
            var payload = new
            {
                requests = batch.Select(t => new
                {
                    model = Model,
                    content = new { parts = new[] { new { text = t } } },
                    outputDimensionality = Dimensions
                })
            };
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"v1beta/{Model}:batchEmbedContents");
            request.Headers.Add("x-goog-api-key", apiKey);
            request.Content = JsonContent.Create(payload);

            var response = await http.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                var summary = responseBody.Length > 500 ? responseBody[..500] : responseBody;
                throw new HttpRequestException(
                    $"Gemini embedding API 回傳非成功狀態 {(int)response.StatusCode} {response.StatusCode}：{summary}");
            }

            using var json = JsonDocument.Parse(responseBody);
            foreach (var e in json.RootElement.GetProperty("embeddings").EnumerateArray())
            {
                var vector = e.GetProperty("values").EnumerateArray()
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
