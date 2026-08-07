using Hangfire;
using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using Microsoft.Data.SqlTypes;
using Microsoft.Extensions.Logging;

namespace KnowledgeHub.Infrastructure.Jobs;

public class DocumentProcessingJob(
    IDocumentRepository docs,
    IEnumerable<IDocumentTextExtractor> extractors,
    IEmbeddingService embedding,
    UploadOptions upload,
    ILogger<DocumentProcessingJob> logger)
{
    [AutomaticRetry(Attempts = 0)] // 失敗不重試，狀態對使用者可見
    public async Task ProcessAsync(Guid documentId)
    {
        var doc = await docs.GetAsync(documentId)
            ?? throw new InvalidOperationException($"文件 {documentId} 不存在");
        await docs.UpdateStatusAsync(documentId, DocumentStatus.Processing);
        try
        {
            var ext = Path.GetExtension(doc.FileName).ToLowerInvariant();
            var extractor = extractors.FirstOrDefault(e => e.CanHandle(ext))
                ?? throw new InvalidOperationException($"不支援的副檔名 {ext}");
            var path = Path.Combine(upload.Root, $"{doc.Id}{ext}");
            if (!File.Exists(path))
                throw new FileNotFoundException($"找不到檔案 {path}", path);
            var text = extractor.ExtractText(path);

            var pieces = ext == ".md" ? MarkdownChunker.Split(text) : TextChunker.Split(text);
            if (pieces.Count == 0)
            {
                await docs.UpdateStatusAsync(documentId, DocumentStatus.Failed, "無可抽取文字（可能是掃描檔）");
                return;
            }

            var vectors = await embedding.EmbedAsync(pieces);
            var chunks = pieces.Select((content, i) => new DocumentChunk
            {
                Id = Guid.NewGuid(), DocumentId = documentId,
                SequenceNumber = i, Content = content,
                Embedding = new SqlVector<float>(vectors[i])
            }).ToList();

            await docs.SaveChunksAndCompleteAsync(documentId, chunks);
        }
        catch (Exception ex)
        {
            // 例外原文（可能含第三方 API 回應內容，例如 Gemini embedding 的上游 body）不可存進
            // ErrorMessage 原文送到前端，只記完整例外到 log，資料庫存分類後的短訊息。
            logger.LogError(ex, "文件 {DocumentId} 處理失敗", documentId);
            var message = ex is HttpRequestException ? "文字向量化失敗，請稍後重試" : "處理失敗";
            await docs.UpdateStatusAsync(documentId, DocumentStatus.Failed, message);
            throw; // 不吞例外：Hangfire 面板要看得到
        }
    }
}
