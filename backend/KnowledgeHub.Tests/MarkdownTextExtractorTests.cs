using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using KnowledgeHub.Infrastructure.Extraction;
using KnowledgeHub.Infrastructure.Jobs;

public class MarkdownTextExtractorTests
{
    private static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.md");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void 略過YAMLfrontmatter()
    {
        var path = WriteTemp("---\ntitle: SOP\nowner: IT\n---\n# 標題\n正文內容");
        var text = new MarkdownTextExtractor().ExtractText(path);
        Assert.DoesNotContain("owner: IT", text);
        Assert.Contains("正文內容", text);
        File.Delete(path);
    }

    [Fact]
    public void 無frontmatter原樣抽取()
    {
        var path = WriteTemp("# 標題\n正文");
        var text = new MarkdownTextExtractor().ExtractText(path);
        Assert.Contains("# 標題", text);
        File.Delete(path);
    }

    [Fact]
    public void 只處理md副檔名()
    {
        var extractor = new MarkdownTextExtractor();
        Assert.True(extractor.CanHandle(".md"));
        Assert.False(extractor.CanHandle(".pdf"));
    }
}

public class DocumentProcessingJobTests
{
    private sealed class FakeDocs : IDocumentRepository
    {
        public CompanyDocument? Doc;
        public readonly List<(DocumentStatus Status, string? Error)> StatusLog = [];
        public IReadOnlyList<DocumentChunk>? SavedChunks;

        public Task<CompanyDocument?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Doc);
        public Task UpdateStatusAsync(Guid docId, DocumentStatus status, string? errorMessage = null, CancellationToken ct = default)
            { StatusLog.Add((status, errorMessage)); return Task.CompletedTask; }
        public Task SaveChunksAndCompleteAsync(Guid docId, IReadOnlyList<DocumentChunk> chunks, CancellationToken ct = default)
            { SavedChunks = chunks; StatusLog.Add((DocumentStatus.Completed, null)); return Task.CompletedTask; }
        public Task AddAsync(CompanyDocument doc, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<CompanyDocument>> ListByDepartmentAsync(string d, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CompanyDocument>>([]);
    }

    private sealed class FakeExtractor(string result) : IDocumentTextExtractor
    {
        public bool CanHandle(string ext) => ext == ".md";
        public string ExtractText(string filePath) => result;
    }

    private sealed class FakeEmbedding : IEmbeddingService
    {
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new float[1536]).ToList());
    }

    private static (DocumentProcessingJob Job, FakeDocs Docs, string Root) Build(string extractedText)
    {
        var docs = new FakeDocs();
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        var doc = new CompanyDocument { Id = Guid.NewGuid(), FileName = "a.md", Department = "IT" };
        File.WriteAllText(Path.Combine(root, $"{doc.Id}.md"), "占位");
        docs.Doc = doc;
        var job = new DocumentProcessingJob(docs,
            [new FakeExtractor(extractedText)], new FakeEmbedding(),
            new UploadOptions(root));
        return (job, docs, root);
    }

    [Fact]
    public async Task 成功路徑_Processing後Completed_chunks序號連續()
    {
        var longText = new string('字', 1200); // 3 片
        var (job, docs, root) = Build(longText);

        await job.ProcessAsync(docs.Doc!.Id);

        Assert.Equal(DocumentStatus.Processing, docs.StatusLog[0].Status);
        Assert.Equal(DocumentStatus.Completed, docs.StatusLog[^1].Status);
        Assert.Equal(3, docs.SavedChunks!.Count);
        Assert.Equal([0, 1, 2], docs.SavedChunks.Select(c => c.SequenceNumber));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task Markdown帶標題_chunk內容帶標題路徑前綴()
    {
        var (job, docs, root) = Build("# 重開機流程\n步驟一");

        await job.ProcessAsync(docs.Doc!.Id);

        Assert.StartsWith("【重開機流程】\n", docs.SavedChunks!.Single().Content);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task 全文為空_標Failed_訊息注明無可抽取文字()
    {
        var (job, docs, root) = Build("   ");

        await job.ProcessAsync(docs.Doc!.Id);

        var last = docs.StatusLog[^1];
        Assert.Equal(DocumentStatus.Failed, last.Status);
        Assert.Contains("無可抽取文字", last.Error);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task 中途例外_標Failed_存錯誤訊息_不吞例外重丟()
    {
        var docs = new FakeDocs();
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        docs.Doc = new CompanyDocument { Id = Guid.NewGuid(), FileName = "a.md" };
        // 不寫入檔案 → 讀檔會丟 FileNotFoundException
        var job = new DocumentProcessingJob(docs,
            [new FakeExtractor("x")], new FakeEmbedding(),
            new UploadOptions(root));

        await Assert.ThrowsAnyAsync<Exception>(() => job.ProcessAsync(docs.Doc.Id));
        Assert.Equal(DocumentStatus.Failed, docs.StatusLog[^1].Status);
        Assert.NotNull(docs.StatusLog[^1].Error);
        Directory.Delete(root, recursive: true);
    }
}
