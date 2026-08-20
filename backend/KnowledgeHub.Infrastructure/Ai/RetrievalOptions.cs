namespace KnowledgeHub.Infrastructure.Ai;

// 檢索調參（appsettings.json "Retrieval" 節）。
// MaxDistance：cosine 距離超過此值的 chunk 不進回答——依 2026-08-20 真實語料實測，
// 可回答問題的 top-1 距離 ≤ 0.31、語料中無答案的問題 ≥ 0.39，0.38 可乾淨分開兩群
// （見 docs-private/KnowledgeHub-RAG檢索評估-2026-08-20.md）。
public record RetrievalOptions(double MaxDistance);
