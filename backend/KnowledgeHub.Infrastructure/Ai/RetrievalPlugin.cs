using System.ComponentModel;
using System.Text;
using KnowledgeHub.Core;
using KnowledgeHub.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace KnowledgeHub.Infrastructure.Ai;

public class RetrievalPlugin(
    IEmbeddingService embedding, IChunkRepository chunks,
    RetrievalContext context, IDepartmentScope departmentScope,
    RetrievalOptions options, ILogger<RetrievalPlugin> logger)
{
    [KernelFunction("search_knowledge_base")]
    [Description("搜尋公司知識庫，回傳與問題最相關的文件段落。回答任何公司規章、SOP、文件相關問題前必須先呼叫。")]
    public async Task<string> SearchKnowledgeBaseAsync(
        [Description("要查詢的問題")] string query)
    {
        var vector = (await embedding.EmbedAsync([query]))[0];
        var searched = await chunks.SearchSimilarChunksAsync(vector, departmentScope.Departments, topK: 5);
        // 距離超過門檻的視同無關：語料中沒有答案時，不讓勉強湊數的段落進 prompt 變成幻覺原料
        var results = searched.Where(r => r.Distance <= options.MaxDistance).ToList();
        logger.LogInformation(
            "知識庫檢索「{Query}」：距離 [{Distances}]，門檻 {MaxDistance} 過濾後剩 {Kept}/{Total} 段",
            query, string.Join(", ", searched.Select(r => r.Distance.ToString("F4"))),
            options.MaxDistance, results.Count, searched.Count);
        if (results.Count == 0) return "知識庫中找不到相關資料。";

        context.Results.AddRange(results);
        var sb = new StringBuilder();
        for (var i = 0; i < results.Count; i++)
            sb.AppendLine($"[來源{i + 1}] {results[i].FileName} 第{results[i].SequenceNumber}段：{results[i].Content}");
        return sb.ToString();
    }
}
