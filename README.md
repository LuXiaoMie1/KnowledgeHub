# KnowledgeHub

企業內部知識庫問答系統：上傳 PDF／Markdown 文件，AI Agent 依部門權限檢索後以串流回答並附參考來源。

## 技術棧

| 層 | 選型 |
|---|---|
| 後端 | .NET 10、ASP.NET Core Web API |
| ORM/DB | EF Core 10 + Azure SQL Database 免費層（原生 `VECTOR(1536)`） |
| AI | Vertex AI Gemini（`google/gemini-2.5-flash` 對話、`gemini-embedding-001` 1536 維向量；服務帳戶 OAuth） |
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
    Gemini{{Vertex AI Gemini<br/>chat + embedding}}

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
dotnet user-secrets set "Jwt:SigningKey" "<JWT 簽章金鑰，任意長隨機字串>"
dotnet user-secrets set "Vertex:SaKeyPath" "<服務帳戶金鑰 JSON 的本機路徑，例如 C:/secrets/knowledgehub-sa.json>"
```

AI provider 是 Vertex AI，走服務帳戶 OAuth（不是 API key）。`Vertex:SaKeyPath` 只存**路徑**，金鑰 JSON 本身**放 repo 外任意位置、絕不進版控**。取得服務帳戶金鑰：在 GCP Console 建立服務帳戶並授予 Vertex AI User 角色，下載其 JSON 金鑰檔即可。`Vertex:ProjectId`／`Vertex:Location` 非機密，已直接寫在 `appsettings.json`。

### Entra ID（公司帳號登入）

認證是雙 scheme 並存：本機開發沿用種子帳號＋自簽 JWT，公司帳號改走 Entra ID（M365 租戶），依 token 的 issuer 自動分流（見 `Auth/EntraSchemeSelector.cs`），兩種 token 對 `[Authorize]` 端點都有效。

`Entra:TenantId`／`Entra:ClientId`／`Entra:GroupDepartmentMap` 含真實租戶與公司安全性群組 Object ID，此 repo 是公開作品集，一律不進版控。二選一：

- `dotnet user-secrets set "Entra:TenantId" "<租戶 ID>"`（`ClientId` 同理；`GroupDepartmentMap` 是巢狀物件，user-secrets 支援用冒號打巢狀 key，例如 `dotnet user-secrets set "Entra:GroupDepartmentMap:<群組 Object ID>" "IT"`）
- 或在 `backend/KnowledgeHub.Api/appsettings.Local.json`（已被 `.gitignore` 排除，`dotnet run` 會自動載入、覆蓋 `appsettings.json` 的同名設定）填：
  ```json
  {
    "Entra": {
      "TenantId": "<租戶 ID>",
      "ClientId": "<應用程式註冊的 Client ID>",
      "GroupDepartmentMap": { "<群組 Object ID>": "IT" }
    }
  }
  ```

Entra 登入者的 access token audience 為 `api://<ClientId>`，需在應用程式註冊的 Expose an API 頁面把 Application ID URI 設成這個值。`groups` claim（安全性群組 Object ID，多值）依 `GroupDepartmentMap` 的宣告順序比對，第一個命中的群組決定部門（與既有種子帳號相同格式的 `department` claim）；沒有任何命中就不給部門 claim，下游 `ICurrentUser.Department` 會因此丟例外（維持既有「查無部門即拒絕」行為）；命中多個已映射群組時取第一個並記 log warning（聯集檢索是後續工作）。詳見 `Auth/EntraGroupDepartmentMapper.cs`。

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

### Bot Framework（本機用 Emulator 測試）

`/api/messages`（見 `Program.cs`、`Bot/KnowledgeHubBotHandler.cs`）是路線 D（Entra 驗證＋Teams bot）的骨架端點，目前只回覆固定格式的回音，RAG 接線為後續工作。本機零租戶依賴：`Bot:MicrosoftAppId` 留空時走匿名認證，不需要任何 Azure Bot 註冊或憑證。

1. 下載並安裝 [Bot Framework Emulator](https://github.com/microsoft/BotFramework-Emulator/releases)（Windows/macOS/Linux 皆有）
2. 啟動後端（`dotnet run --launch-profile https`，見上方「啟動」）
3. 開啟 Emulator → `File > Open Bot`，Bot URL 填 `http://localhost:5106/api/messages`（或 `https://localhost:7152/api/messages`），App ID／App Password 留空 → `Connect`
4. 在 Emulator 對話框輸入任意文字，應收到「收到：「訊息內容」（RAG 接線為後續工作）」的回覆

之後串 Teams／Azure Bot Service，把 `Bot:MicrosoftAppType`／`MicrosoftAppId`／`MicrosoftAppPassword`／`MicrosoftAppTenantId` 從 user-secrets 帶入正式值即可，不需改程式碼。

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

本 repo 為公開作品集。開發初期曾用 Google AI Studio 的 Gemini **免費層** API key（免費層資料會被 Google 用於模型訓練），已於 2026-08 切換至 **Vertex AI**（Cloud 資料治理層級，不用於訓練，服務帳戶 OAuth 認證）。程式碼已透過 Core 層的 `IChatService`／`IEmbeddingService` 介面隔離 provider 實作，切換不影響其餘各層。

即使已切換至 Vertex AI，**本 repo 與所有 demo 仍只餵入自製假資料**（虛構的 SOP、代碼、公司名稱），從未上傳任何真實公司文件或個資。

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
