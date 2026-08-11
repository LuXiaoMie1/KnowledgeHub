using System.ComponentModel;
using System.Text;
using KnowledgeHub.Core;
using KnowledgeHub.Core.Interfaces;
using Microsoft.SemanticKernel;

namespace KnowledgeHub.Infrastructure.Ai;

public class RetrievalPlugin(
    IEmbeddingService embedding, IChunkRepository chunks,
    RetrievalContext context, ICurrentUser user)
{
    [KernelFunction("search_knowledge_base")]
    [Description("搜尋公司知識庫，回傳與問題最相關的文件段落。回答任何公司規章、SOP、文件相關問題前必須先呼叫。")]
    public async Task<string> SearchKnowledgeBaseAsync(
        [Description("要查詢的問題")] string query)
    {
        var vector = (await embedding.EmbedAsync([query]))[0];
        var results = await chunks.SearchSimilarChunksAsync(vector, user.Departments, topK: 5);
        if (results.Count == 0) return "知識庫中找不到相關資料。";

        context.Results.AddRange(results);
        var sb = new StringBuilder();
        for (var i = 0; i < results.Count; i++)
            sb.AppendLine($"[來源{i + 1}] {results[i].FileName} 第{results[i].SequenceNumber}段：{results[i].Content}");
        return sb.ToString();
    }
}
