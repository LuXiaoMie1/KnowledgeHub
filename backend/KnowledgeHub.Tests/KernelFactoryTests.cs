using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using KnowledgeHub.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;

// KernelFactory：bot 管道用來組 kernel 的同一份邏輯，重點驗證「不給 EmailPlugin 就真的
// 不掛上」——bot 是匿名管道，絕不可讓 kernel 帶有 send_email 這個 function（見
// KnowledgeHubBotHandler、Program.cs 的 "bot" keyed 服務註冊處註解）。
public class KernelFactoryTests
{
    private sealed class FakeEmbedding : IEmbeddingService
    {
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<float[]>>([new float[1536]]);
    }

    private sealed class FakeChunks : IChunkRepository
    {
        public Task<IReadOnlyList<ChunkSearchResult>> SearchSimilarChunksAsync(
            float[] queryVector, IReadOnlyList<string> departments, int topK = 5, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChunkSearchResult>>([]);
    }

    private sealed class FakeDepartmentScope : IDepartmentScope
    {
        public IReadOnlyList<string> Departments => ["ALL"];
    }

    private sealed class FakeOutbox : IOutboxEmailRepository
    {
        public Task AddAsync(OutboxEmail email, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeUser : ICurrentUser
    {
        public string Department => "IT";
        public IReadOnlyList<string> Departments => ["IT"];
        public string Username => "it-user";
    }

    private static RetrievalPlugin NewRetrievalPlugin() =>
        new(new FakeEmbedding(), new FakeChunks(), new RetrievalContext(), new FakeDepartmentScope(),
            new RetrievalOptions(MaxDistance: 0.38), NullLogger<RetrievalPlugin>.Instance);

    [Fact]
    public void 不給EmailPlugin_kernel不含email外掛_只含retrieval()
    {
        var kernel = KernelFactory.Build(
            "model", new Uri("https://example.invalid/"), new HttpClient(),
            NewRetrievalPlugin(), email: null);

        Assert.True(kernel.Plugins.Contains("retrieval"));
        Assert.False(kernel.Plugins.Contains("email"));
    }

    [Fact]
    public void 給EmailPlugin_kernel含email外掛()
    {
        var kernel = KernelFactory.Build(
            "model", new Uri("https://example.invalid/"), new HttpClient(),
            NewRetrievalPlugin(), new EmailPlugin(new FakeOutbox(), new FakeUser()));

        Assert.True(kernel.Plugins.Contains("retrieval"));
        Assert.True(kernel.Plugins.Contains("email"));
    }
}
