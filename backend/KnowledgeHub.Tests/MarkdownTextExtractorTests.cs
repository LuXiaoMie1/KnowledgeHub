using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using KnowledgeHub.Infrastructure.Extraction;
using KnowledgeHub.Infrastructure.Jobs;
using Microsoft.Extensions.Logging.Abstractions;

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
        // 模擬審查發現的 bug：chunks 寫入失敗（例如 SaveChanges 拋例外）。
        public Exception? SaveChunksException;

        public Task<CompanyDocument?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Doc);
        public Task UpdateStatusAsync(Guid docId, DocumentStatus status, string? errorMessage = null, CancellationToken ct = default)
            { StatusLog.Add((status, errorMessage)); return Task.CompletedTask; }
        public Task SaveChunksAndCompleteAsync(Guid docId, IReadOnlyList<DocumentChunk> chunks, CancellationToken ct = default)
        {
            if (SaveChunksException is not null) throw SaveChunksException;
            SavedChunks = chunks;
            StatusLog.Add((DocumentStatus.Completed, null));
            return Task.CompletedTask;
        }
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

    // 模擬 GeminiEmbeddingService 對上游非成功回應拋出的例外，訊息內含上游 body。
    private sealed class ThrowingEmbedding(Exception ex) : IEmbeddingService
    {
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
            => throw ex;
    }

    private static (DocumentProcessingJob Job, FakeDocs Docs, string Root) Build(
        string extractedText, IEmbeddingService? embedding = null)
    {
        var docs = new FakeDocs();
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        var doc = new CompanyDocument { Id = Guid.NewGuid(), FileName = "a.md", Department = "IT" };
        File.WriteAllText(Path.Combine(root, $"{doc.Id}.md"), "占位");
        docs.Doc = doc;
        var job = new DocumentProcessingJob(docs,
            [new FakeExtractor(extractedText)], embedding ?? new FakeEmbedding(),
            new UploadOptions(root), NullLogger<DocumentProcessingJob>.Instance);
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
            new UploadOptions(root), NullLogger<DocumentProcessingJob>.Instance);

        await Assert.ThrowsAnyAsync<Exception>(() => job.ProcessAsync(docs.Doc.Id));
        Assert.Equal(DocumentStatus.Failed, docs.StatusLog[^1].Status);
        Assert.NotNull(docs.StatusLog[^1].Error);
        Directory.Delete(root, recursive: true);
    }

    // 審查發現的 bug：SaveChunksAndCompleteAsync 拋例外後，若 UpdateStatusAsync 與其共用
    // 同一個 DbContext 的 change tracker，殘留的 Added chunks 會讓狀態更新也失敗，文件永卡 Processing。
    // 用 fake repository 驗證 job 層的行為：無論如何，最終狀態必須是 Failed 且 ErrorMessage 非空。
    [Fact]
    public async Task chunk寫入失敗_最終狀態為Failed且ErrorMessage非空()
    {
        var (job, docs, root) = Build("一些內容");
        docs.SaveChunksException = new InvalidOperationException("SaveChanges 失敗（模擬 change tracker 殘留）");

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ProcessAsync(docs.Doc!.Id));

        Assert.Equal(DocumentStatus.Failed, docs.StatusLog[^1].Status);
        Assert.False(string.IsNullOrEmpty(docs.StatusLog[^1].Error));
        Directory.Delete(root, recursive: true);
    }

    // 審查發現的 bug：Gemini embedding 例外訊息含上游 500 body，原文存進 ErrorMessage 會經
    // List API 洩到瀏覽器。驗證持久化的 ErrorMessage 是分類後的短訊息，不含上游 body 內容。
    [Fact]
    public async Task embedding失敗_ErrorMessage不含上游body內容()
    {
        const string upstreamBody = "quota exceeded：機密內部錯誤細節 xyz-123";
        var embeddingError = new HttpRequestException($"Gemini embedding API 回傳非成功狀態 500：{upstreamBody}");
        var (job, docs, root) = Build("一些內容", new ThrowingEmbedding(embeddingError));

        await Assert.ThrowsAsync<HttpRequestException>(() => job.ProcessAsync(docs.Doc!.Id));

        var last = docs.StatusLog[^1];
        Assert.Equal(DocumentStatus.Failed, last.Status);
        Assert.False(string.IsNullOrEmpty(last.Error));
        Assert.DoesNotContain(upstreamBody, last.Error);
        Directory.Delete(root, recursive: true);
    }
}
