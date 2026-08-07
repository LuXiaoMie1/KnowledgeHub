# KnowledgeHub

企業內部知識庫問答系統：上傳 PDF／Markdown 文件，AI Agent 依部門權限檢索後以串流回答並附參考來源。

## 技術棧

| 層 | 選型 |
|---|---|
| 後端 | .NET 10、ASP.NET Core Web API |
| ORM/DB | EF Core 10 + Azure SQL Database 免費層（原生 `VECTOR(1536)`） |
| AI | Gemini（`gemini-2.5-flash` 對話、`gemini-embedding-001` 1536 維向量） |
| Agent | Semantic Kernel（auto function calling） |
| 背景工作 | Hangfire + Hangfire.SqlServer |
| PDF 解析 | PdfPig |
| 前端 | Vue 3（`<script setup>` + TS）+ Vite + Tailwind CSS |
| 認證 | JWT |
| 測試/CI | xUnit；GitHub Actions（build + test） |

## 設計文件

完整架構、資料模型與決策紀錄見 [`docs/superpowers/specs/2026-08-05-knowledgehub-rag-design.md`](docs/superpowers/specs/2026-08-05-knowledgehub-rag-design.md)。

## 開發環境設定

### 機密（user-secrets）

機密永不進版控。在 `backend/KnowledgeHub.Api` 目錄下執行：

```powershell
cd backend/KnowledgeHub.Api
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Default" "<Azure SQL 連線字串>"
dotnet user-secrets set "Gemini:ApiKey" "<Gemini API key>"
dotnet user-secrets set "Jwt:SigningKey" "<JWT 簽章金鑰>"
```

### Azure SQL 免費層注意事項

Azure SQL Database 免費層採 serverless，閒置一段時間後會自動暫停，**首個請求會有 30–60 秒的冷啟動延遲**。Demo 前請先呼叫 `GET /api/health` 喚醒資料庫，避免現場等待逾時。

## 建置與測試

```powershell
dotnet build backend/KnowledgeHub.sln
dotnet test backend/KnowledgeHub.sln --filter "Category!=Integration"
```

整合測試標記 `[Trait("Category", "Integration")]`，CI 與一般開發跑測試時預設排除（需要外部資源如資料庫）。

## 擴充方向

Phase A 之後、尚未實作的檢索品質演進方向：

1. **Hybrid search**：SQL Server 全文檢索（BM25）＋向量兩路召回，以 RRF（Reciprocal Rank Fusion）合併——維運 SOP 常含錯誤代碼、品名等精確詞，純向量檢索容易漏掉這類查詢。
2. **Reranker**：召回 top 20–50 後以 cross-encoder 重排，取 top 5 送進 LLM。
3. **評估集**：golden questions 搭配自動化指標（如 Ragas 類工具），讓 chunk 大小、top-K 等參數調整有依據，不憑感覺。
4. **向量索引 DiskANN**：目前資料量小，exact KNN 已足夠；資料量成長後可評估 SQL Server 的 DiskANN 近似索引以維持查詢效能。
