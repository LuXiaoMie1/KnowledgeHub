# KnowledgeHub

企業內部知識庫問答系統：上傳 PDF／Markdown 文件，AI Agent 依部門權限檢索後以串流回答並附參考來源。

## 技術棧

| 層 | 選型 |
|---|---|
| 後端 | .NET 10、ASP.NET Core Web API |
| ORM/DB | EF Core 10 + Azure SQL Database 免費層（原生 `VECTOR(1536)`） |
| AI | Gemini（`gemini-flash-latest` 對話、`gemini-embedding-001` 1536 維向量） |
| Agent | Semantic Kernel（auto function calling） |
| 背景工作 | Hangfire + Hangfire.SqlServer |
| PDF 解析 | PdfPig |
| 前端 | Vue 3（`<script setup>` + TS）+ Vite + Tailwind CSS |
| 認證 | JWT |
| 測試/CI | xUnit；GitHub Actions（build + test） |

## 架構

```mermaid
flowchart TB
    subgraph Client["前端（Vue 3 + Vite）"]
        UI[登入 / 文件面板 / 聊天面板]
    end

    subgraph Api["KnowledgeHub.Api（ASP.NET Core）"]
        Auth[AuthController<br/>JWT 簽發]
        Docs[DocumentsController<br/>上傳／清單／刪除]
        Chat[ChatController<br/>SSE 串流端點]
        HF[Hangfire Server<br/>背景 worker]
    end

    subgraph Infra["KnowledgeHub.Infrastructure"]
        Repo[ChunkRepository<br/>向量相似度檢索]
        Job[DocumentProcessingJob<br/>抽取／切片／向量化]
        SK[SemanticKernelChatService<br/>RetrievalPlugin + EmailPlugin]
        Emb[GeminiEmbeddingService]
    end

    subgraph Core["KnowledgeHub.Core"]
        Entities[實體 / 介面 / TextChunker]
    end

    DB[(Azure SQL Database<br/>VECTOR(1536) + Documents/Chunks/Outbox)]
    Gemini{{Gemini API<br/>chat + embedding}}

    UI -- "JWT Bearer" --> Auth
    UI -- "POST /api/documents" --> Docs
    UI -- "POST /api/chat（SSE）" --> Chat

    Docs -- "存檔＋排入佇列" --> HF
    HF --> Job
    Job -- "抽取文字" --> Job
    Job -- "切片" --> Entities
    Job -- "向量化" --> Emb
    Emb -- "embedding 請求" --> Gemini
    Job -- "寫入 chunks" --> DB

    Chat --> SK
    SK -- "auto function calling：查知識庫" --> Repo
    Repo -- "向量相似度＋部門過濾" --> DB
    SK -- "auto function calling：寄信" --> DB
    SK -- "chat 請求（含檢索結果）" --> Gemini
    SK -- "token 串流 + sources" --> Chat

    Infra --> Core
    Api --> Infra
```

依賴方向：`Api → Infrastructure → Core`；`Core` 零外部依賴。完整架構、資料模型與決策紀錄見 [`docs/superpowers/specs/2026-08-05-knowledgehub-rag-design.md`](docs/superpowers/specs/2026-08-05-knowledgehub-rag-design.md)。

## 開發環境設定

### 機密（user-secrets）

機密永不進版控。在 `backend/KnowledgeHub.Api` 目錄下執行：

```powershell
cd backend/KnowledgeHub.Api
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Default" "<Azure SQL 連線字串>"
dotnet user-secrets set "Gemini:ApiKey" "<Gemini API key>"
dotnet user-secrets set "Jwt:SigningKey" "<JWT 簽章金鑰，任意長隨機字串>"
```

### 資料庫 migration

首次啟動前，對目標 Azure SQL 套用 migration：

```powershell
cd backend
dotnet ef database update --project KnowledgeHub.Infrastructure --startup-project KnowledgeHub.Api
```

### 啟動

```powershell
# 後端（於 backend/KnowledgeHub.Api）
dotnet run --launch-profile https   # https://localhost:7152，http://localhost:5106

# 前端（於 frontend）
npm install
npm run dev                          # http://localhost:5173，/api 已 proxy 到後端 7152
```

瀏覽器開 `http://localhost:5173` 即可登入使用。

### Azure SQL 免費層注意事項

Azure SQL Database 免費層採 serverless，閒置一段時間後會自動暫停，**首個請求會有 30–60 秒的冷啟動延遲**。Demo 前請先呼叫 `GET /api/health` 喚醒資料庫，避免現場等待逾時。

### Demo 帳號

種子使用者（見 `appsettings.json` 的 `SeedUsers`），密碼為明文僅供本機 demo：

| 帳號 | 密碼 | 部門 |
|---|---|---|
| `hr-user` | `demo-hr-2026` | HR |
| `it-user` | `demo-it-2026` | IT |
| `fin-user` | `demo-fin-2026` | Finance |

文件檢索與問答依登入者部門過濾，不同部門看不到彼此上傳的文件。

## 建置與測試

```powershell
dotnet build backend/KnowledgeHub.sln
dotnet test backend/KnowledgeHub.sln --filter "Category!=Integration"
```

整合測試標記 `[Trait("Category", "Integration")]`，CI 與一般開發跑測試時預設排除（需要外部資源如資料庫）。

## 資料安全

本 repo 為公開作品集，開發／demo 階段使用 Google AI Studio 的 Gemini **免費層** API key。免費層資料會被 Google 用於模型訓練，因此**本 repo 與所有 demo 只餵入自製假資料**（虛構的 SOP、代碼、公司名稱），從未上傳任何真實公司文件或個資。

正式導入公司內部使用時，需將 AI provider 切換為 **Vertex AI**（Cloud 資料治理層級，不用於訓練），才可餵入真實文件。程式碼已透過 Core 層的 `IChatService`／`IEmbeddingService` 介面隔離 provider 實作，切換不影響其餘各層。

## 擴充方向

Phase A 之後、尚未實作的方向：

### 檢索品質

1. **Hybrid search**：SQL Server 全文檢索（BM25）＋向量兩路召回，以 RRF（Reciprocal Rank Fusion）合併——維運 SOP 常含錯誤代碼、品名等精確詞，純向量檢索容易漏掉這類查詢。
2. **Reranker**：召回 top 20–50 後以 cross-encoder 重排，取 top 5 送進 LLM。
3. **評估集**：golden questions 搭配自動化指標（如 Ragas 類工具），讓 chunk 大小、top-K 等參數調整有依據，不憑感覺。
4. **向量索引 DiskANN**：目前資料量小，exact KNN（全表掃描比對）已足夠；資料量成長到數十萬筆以上後，可評估改用 SQL Server 的 DiskANN 近似最近鄰（ANN）索引，以犧牲極小精度換取查詢延遲不隨資料量線性增長。
5. **Frontmatter metadata 檢索**：第一批語料（Obsidian 維運知識庫）的 Markdown 檔案帶有完整 YAML frontmatter（分類、標籤等），目前解析時直接略過。之後可將 frontmatter 解析為結構化欄位，讓檢索除了向量相似度外，也能用分類／標籤做前置過濾或加權，提升精確詞查詢的準確率。

### 平台

6. **EIP 待審助理（Phase B）**：以新 plugin 形式加入，自動讀取 EIP 待審文件、AI 摘要整理、使用者確認後代為簽核。三條現在先定死的原則：（a）人工確認閘門——AI 只做摘要與建議，每一件簽核都需使用者逐件明確確認才執行；（b）公私分離——公司 EIP 只有網頁介面需走瀏覽器自動化，公開 repo 只放 plugin 介面與假資料 demo，真正的 EIP connector（內部網址、頁面選擇器、登入流程）放私有 repo 永不進公開版控；（c）資料安全——EIP 公文可能含人事／財務資料，只允許送 Vertex AI，禁用 AI Studio 免費層。詳見設計文件 §12。
