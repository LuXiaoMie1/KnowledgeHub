using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Infrastructure;
using KnowledgeHub.Infrastructure.Repositories;
using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KnowledgeHub.Tests.Integration;

[Trait("Category", "Integration")]
public class ChunkRepositoryTests : IAsyncLifetime
{
    private KnowledgeHubDbContext _db = null!;
    private readonly List<Guid> _createdDocIds = [];

    public async Task InitializeAsync()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets("3fc8ee2a-3351-4410-a176-d589385e97f1").Build();
        var options = new DbContextOptionsBuilder<KnowledgeHubDbContext>()
            .UseSqlServer(config.GetConnectionString("Default")).Options;
        _db = new KnowledgeHubDbContext(options);
        await SeedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.Documents.Where(d => _createdDocIds.Contains(d.Id)).ExecuteDeleteAsync();
        await _db.DisposeAsync();
    }

    // 1536 維單位向量：只有 index 位置是 1，其餘 0 → 彼此 cosine 距離 = 1，自身 = 0
    private static SqlVector<float> BasisVector(int index)
    {
        var v = new float[1536];
        v[index] = 1f;
        return new SqlVector<float>(v);
    }

    private async Task SeedAsync()
    {
        var itDoc = NewDoc("it.md", "IT", DocumentStatus.Completed,
            (0, "IT 段落 0", BasisVector(0)), (1, "IT 段落 1", BasisVector(1)));
        var hrDoc = NewDoc("hr.md", "HR", DocumentStatus.Completed,
            (0, "HR 段落 0", BasisVector(0)));
        var pendingIt = NewDoc("pending.md", "IT", DocumentStatus.Pending,
            (0, "未完成文件的段落", BasisVector(0)));
        _db.Documents.AddRange(itDoc, hrDoc, pendingIt);
        await _db.SaveChangesAsync();
    }

    private CompanyDocument NewDoc(string name, string dept, DocumentStatus status,
        params (int Seq, string Content, SqlVector<float> Emb)[] chunks)
    {
        var doc = new CompanyDocument
        {
            Id = Guid.NewGuid(), FileName = name, Department = dept, Status = status,
            UploadedAtUtc = DateTime.UtcNow, ChunkCount = chunks.Length,
            Chunks = chunks.Select(c => new DocumentChunk
            {
                Id = Guid.NewGuid(), SequenceNumber = c.Seq,
                Content = c.Content, Embedding = c.Emb
            }).ToList()
        };
        _createdDocIds.Add(doc.Id);
        return doc;
    }

    [Fact]
    public async Task 依cosine距離排序_部門與狀態過濾生效()
    {
        var repo = new ChunkRepository(_db);
        var query = new float[1536];
        query[0] = 1f; // 與 BasisVector(0) 完全同向

        var results = await repo.SearchSimilarChunksAsync(query, "IT", topK: 5);

        // 只回 IT 且 Completed：命中 2 段（排除 HR 的與 Pending 的）
        Assert.Equal(2, results.Count);
        Assert.Equal("IT 段落 0", results[0].Content);   // 距離 0，排最前
        Assert.Equal("IT 段落 1", results[1].Content);   // 距離 1
        Assert.True(results[0].Distance < 0.0001);
        Assert.Equal("it.md", results[0].FileName);
        Assert.DoesNotContain(results, r => r.Content.Contains("HR"));
        Assert.DoesNotContain(results, r => r.Content.Contains("未完成"));
    }

    [Fact]
    public async Task topK限制回傳筆數()
    {
        var repo = new ChunkRepository(_db);
        var query = new float[1536];
        query[0] = 1f;

        var results = await repo.SearchSimilarChunksAsync(query, "IT", topK: 1);
        Assert.Single(results);
    }
}
