using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using KnowledgeHub.Infrastructure.Ai;

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

    private sealed class FakeUser(params string[] departments) : ICurrentUser
    {
        public string Department => departments is [var only] ? only
            : throw new InvalidOperationException("使用者屬於多個部門，無法使用單一部門語意");
        public IReadOnlyList<string> Departments => departments;
        public string Username => "it-user";
    }

    private static readonly ChunkSearchResult Hit =
        new(Guid.NewGuid(), Guid.NewGuid(), "sop.md", 3, "重開 POS 主機的步驟…", 0.12);

    [Fact]
    public async Task 命中時_回傳文字含來源與內容_且寫入context()
    {
        var context = new RetrievalContext();
        var chunks = new FakeChunks([Hit]);
        var plugin = new RetrievalPlugin(new FakeEmbedding(), chunks, context, new FakeUser("IT"));

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
        var plugin = new RetrievalPlugin(new FakeEmbedding(), new FakeChunks([]), context, new FakeUser("IT"));

        var answer = await plugin.SearchKnowledgeBaseAsync("完全無關的問題");

        Assert.Contains("找不到相關資料", answer);
        Assert.Empty(context.Results);
    }

    [Fact]
    public async Task 多部門使用者_檢索傳入所屬全部部門()
    {
        var context = new RetrievalContext();
        var chunks = new FakeChunks([Hit]);
        var plugin = new RetrievalPlugin(new FakeEmbedding(), chunks, context, new FakeUser("IT", "HR"));

        await plugin.SearchKnowledgeBaseAsync("POS 怎麼重開");

        Assert.Equal(["IT", "HR"], chunks.QueriedDepartments);
    }
}
