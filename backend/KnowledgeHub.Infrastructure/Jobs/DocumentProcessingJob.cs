using Hangfire;
using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using Microsoft.Data.SqlTypes;

namespace KnowledgeHub.Infrastructure.Jobs;

public class DocumentProcessingJob(
    IDocumentRepository docs,
    IEnumerable<IDocumentTextExtractor> extractors,
    IEmbeddingService embedding,
    UploadOptions upload)
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
            await docs.UpdateStatusAsync(documentId, DocumentStatus.Failed, ex.Message);
            throw; // 不吞例外：Hangfire 面板要看得到
        }
    }
}
