using System.Net;
using System.Text.Json;
using KnowledgeHub.Infrastructure.Ai;

public class GeminiEmbeddingServiceTests
{
    // 回錄請求並回傳指定向量的假 handler。vectorFactory 收到的是「跨批次的全域 index」，
    // 不是單批內的 index，才能驗證分批後順序沒有錯位。
    private sealed class FakeHandler(Func<int, float[]> vectorFactory) : HttpMessageHandler
    {
        public List<JsonDocument> CapturedBodies { get; } = [];
        private int _globalIndex;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            CapturedBodies.Add(body);
            var count = body.RootElement.GetProperty("requests").GetArrayLength();
            var embeddings = Enumerable.Range(0, count)
                .Select(_ => new { values = vectorFactory(_globalIndex++) })
                .ToList();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { embeddings }))
            };
        }
    }

    // 固定回傳指定狀態碼與 body 的假 handler，用來測非 200 錯誤路徑。
    private sealed class ErrorHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    private static GeminiEmbeddingService NewService(HttpMessageHandler handler) =>
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
    public async Task 跨批次順序不錯位()
    {
        // 用 [1, globalIndex] 編碼全域位置：正規化後方向不變，
        // result[i][1] / result[i][0] 還原得回 i，可驗證每個輸入對應到正確輸出，
        // 而不只是batch 數量與長度對得上。
        var handler = new FakeHandler(i => [1f, i]);
        var texts = Enumerable.Range(0, 130).Select(i => $"段{i}").ToList();

        var result = await NewService(handler).EmbedAsync(texts);

        Assert.Equal(3, handler.CapturedBodies.Count); // 64+64+2，確認真的跨了批次
        foreach (var i in new[] { 0, 63, 64, 127, 128, 129 }) // 首批第一個／批次邊界／最後一個
        {
            var norm = MathF.Sqrt(1f + (float)i * i);
            Assert.Equal(1f / norm, result[i][0], precision: 5);
            Assert.Equal(i / norm, result[i][1], precision: 5);
        }
    }

    [Fact]
    public async Task 空清單不打API()
    {
        var handler = new FakeHandler(_ => [1f]);
        var result = await NewService(handler).EmbedAsync([]);
        Assert.Empty(result);
        Assert.Empty(handler.CapturedBodies);
    }

    [Fact]
    public async Task 非200回應拋出含狀態碼與body摘要的例外()
    {
        var handler = new ErrorHandler(HttpStatusCode.BadRequest,
            """{"error":{"code":400,"message":"invalid argument: outputDimensionality"}}""");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => NewService(handler).EmbedAsync(["x"]));

        Assert.Contains("400", ex.Message);
        Assert.Contains("invalid argument: outputDimensionality", ex.Message);
    }
}
