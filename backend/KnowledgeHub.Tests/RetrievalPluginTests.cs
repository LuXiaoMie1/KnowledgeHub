using KnowledgeHub.Api.Bot;
using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using KnowledgeHub.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;

public class RetrievalPluginTests
{
    private sealed class FakeEmbedding : IEmbeddingService
    {
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<float[]>>([new float[1536]]);
    }

    private sealed class FakeChunks(IReadOnlyList<ChunkSearchResult> results) : IChunkRepository
    {
        public IReadOnlyList<string>? QueriedDepartments;
        public Task<IReadOnlyList<ChunkSearchResult>> SearchSimilarChunksAsync(
            float[] queryVector, IReadOnlyList<string> departments, int topK = 5, CancellationToken ct = default)
        {
            QueriedDepartments = departments;
            return Task.FromResult(results);
        }
    }

    private sealed class FakeDepartmentScope(params string[] departments) : IDepartmentScope
    {
        public IReadOnlyList<string> Departments => departments;
    }

    private static readonly ChunkSearchResult Hit =
        new(Guid.NewGuid(), Guid.NewGuid(), "sop.md", 3, "重開 POS 主機的步驟…", 0.12);

    // 門檻值依 2026-08-20 真實語料實測：可回答問題 top-1 ≤ 0.31、無答案問題 ≥ 0.39
    private static readonly RetrievalOptions Options = new(MaxDistance: 0.38);

    private static RetrievalPlugin CreatePlugin(
        IChunkRepository chunks, RetrievalContext context, IDepartmentScope scope)
        => new(new FakeEmbedding(), chunks, context, scope, Options,
            NullLogger<RetrievalPlugin>.Instance);

    [Fact]
    public async Task 命中時_回傳文字含來源與內容_且寫入context()
    {
        var context = new RetrievalContext();
        var chunks = new FakeChunks([Hit]);
        var plugin = CreatePlugin(chunks, context, new FakeDepartmentScope("IT"));

        var answer = await plugin.SearchKnowledgeBaseAsync("POS 怎麼重開");

        Assert.Contains("sop.md", answer);
        Assert.Contains("重開 POS 主機的步驟", answer);
        Assert.Equal(["IT"], chunks.QueriedDepartments);   // 部門取自 claim，不是參數
        Assert.Single(context.Results);
        Assert.Equal(Hit, context.Results[0]);
    }

    [Fact]
    public async Task 無命中_回傳查無訊息_context保持空()
    {
        var context = new RetrievalContext();
        var plugin = CreatePlugin(new FakeChunks([]), context, new FakeDepartmentScope("IT"));

        var answer = await plugin.SearchKnowledgeBaseAsync("完全無關的問題");

        Assert.Contains("找不到相關資料", answer);
        Assert.Empty(context.Results);
    }

    [Fact]
    public async Task 多部門使用者_檢索傳入所屬全部部門()
    {
        var context = new RetrievalContext();
        var chunks = new FakeChunks([Hit]);
        var plugin = CreatePlugin(chunks, context, new FakeDepartmentScope("IT", "HR"));

        await plugin.SearchKnowledgeBaseAsync("POS 怎麼重開");

        Assert.Equal(["IT", "HR"], chunks.QueriedDepartments);
    }

    [Fact]
    public async Task Bot用AllDepartmentsScope_檢索傳入ALL而非任何部門()
    {
        var context = new RetrievalContext();
        var chunks = new FakeChunks([Hit]);
        var plugin = CreatePlugin(chunks, context, new AllDepartmentsScope());

        await plugin.SearchKnowledgeBaseAsync("POS 怎麼重開");

        Assert.Equal([Departments.All], chunks.QueriedDepartments);
    }

    [Fact]
    public async Task 距離超過門檻的chunk被過濾_不進回答與context()
    {
        var farChunk = new ChunkSearchResult(
            Guid.NewGuid(), Guid.NewGuid(), "unrelated.pdf", 0, "公務車輛使用範圍…", 0.45);
        var context = new RetrievalContext();
        var plugin = CreatePlugin(new FakeChunks([Hit, farChunk]), context, new FakeDepartmentScope("IT"));

        var answer = await plugin.SearchKnowledgeBaseAsync("POS 怎麼重開");

        Assert.Contains("重開 POS 主機的步驟", answer);
        Assert.DoesNotContain("公務車輛", answer);
        Assert.Equal([Hit], context.Results);
    }

    [Fact]
    public async Task 全部chunk超過門檻_視同無命中()
    {
        var farChunk = new ChunkSearchResult(
            Guid.NewGuid(), Guid.NewGuid(), "unrelated.pdf", 0, "公務車輛使用範圍…", 0.45);
        var context = new RetrievalContext();
        var plugin = CreatePlugin(new FakeChunks([farChunk]), context, new FakeDepartmentScope("IT"));

        var answer = await plugin.SearchKnowledgeBaseAsync("今年年終獎金發多少個月？");

        Assert.Contains("找不到相關資料", answer);
        Assert.Empty(context.Results);
    }

    [Fact]
    public async Task 距離剛好等於門檻_保留()
    {
        var edgeChunk = new ChunkSearchResult(
            Guid.NewGuid(), Guid.NewGuid(), "edge.md", 1, "邊界內容…", 0.38);
        var context = new RetrievalContext();
        var plugin = CreatePlugin(new FakeChunks([edgeChunk]), context, new FakeDepartmentScope("IT"));

        var answer = await plugin.SearchKnowledgeBaseAsync("邊界問題");

        Assert.Contains("邊界內容", answer);
        Assert.Equal([edgeChunk], context.Results);
    }
}
