using System.Net;
using System.Text.Json;
using KnowledgeHub.Infrastructure.Ai;

public class GeminiEmbeddingServiceTests
{
    // 回錄請求並回傳指定向量的假 handler
    private sealed class FakeHandler(Func<int, float[]> vectorFactory) : HttpMessageHandler
    {
        public List<JsonDocument> CapturedBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            CapturedBodies.Add(body);
            var count = body.RootElement.GetProperty("requests").GetArrayLength();
            var embeddings = Enumerable.Range(0, count)
                .Select(i => new { values = vectorFactory(i) });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { embeddings }))
            };
        }
    }

    private static GeminiEmbeddingService NewService(FakeHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/") },
            apiKey: "test-key");

    [Fact]
    public async Task 請求含outputDimensionality1536與正確模型()
    {
        var handler = new FakeHandler(_ => [3f, 4f]);
        await NewService(handler).EmbedAsync(["哈囉"]);

        var req = handler.CapturedBodies.Single().RootElement
            .GetProperty("requests")[0];
        Assert.Equal(1536, req.GetProperty("outputDimensionality").GetInt32());
        Assert.Equal("models/gemini-embedding-001", req.GetProperty("model").GetString());
        Assert.Equal("哈囉", req.GetProperty("content").GetProperty("parts")[0]
            .GetProperty("text").GetString());
    }

    [Fact]
    public async Task 回傳向量有做L2正規化()
    {
        var handler = new FakeHandler(_ => [3f, 4f]); // 長度 5
        var result = await NewService(handler).EmbedAsync(["x"]);

        Assert.Equal(0.6f, result[0][0], precision: 5); // 3/5
        Assert.Equal(0.8f, result[0][1], precision: 5); // 4/5
    }

    [Fact]
    public async Task 超過64段自動分批()
    {
        var handler = new FakeHandler(_ => [1f]);
        var texts = Enumerable.Range(0, 130).Select(i => $"段{i}").ToList();

        var result = await NewService(handler).EmbedAsync(texts);

        Assert.Equal(130, result.Count);
        Assert.Equal(3, handler.CapturedBodies.Count); // 64+64+2
        Assert.Equal(64, handler.CapturedBodies[0].RootElement.GetProperty("requests").GetArrayLength());
        Assert.Equal(2,  handler.CapturedBodies[2].RootElement.GetProperty("requests").GetArrayLength());
    }

    [Fact]
    public async Task 空清單不打API()
    {
        var handler = new FakeHandler(_ => [1f]);
        var result = await NewService(handler).EmbedAsync([]);
        Assert.Empty(result);
        Assert.Empty(handler.CapturedBodies);
    }
}
