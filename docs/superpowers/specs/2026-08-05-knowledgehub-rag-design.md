# KnowledgeHub — 企業 AI 助理 設計文件

- 日期：2026-08-05；2026-08-06 修訂（定位升級為企業 AI 助理、OpenAI 換 Gemini、新增 Markdown 匯入與 Phase B roadmap）
- 狀態：設計方向已核准，本文件待使用者確認
- 定位：可擴充工具的 AI Agent 平台——第一個能力是企業知識庫問答（Phase A，本文件主體），第二個能力是 EIP 待審助理（Phase B，見 §12）。目標是公司內部實際使用，同時放個人 GitHub 作為作品集。**repo 會公開，任何金鑰、連線字串、公司內部網址與頁面結構永不進版控。**

## 1. 目標

一個可實際跑起來的企業知識庫問答系統：使用者上傳 PDF 或 Markdown 文件 → 背景解析切片並向量化 → 依部門權限檢索 → AI Agent 以串流回答並附參考來源，且能自行決定呼叫「查知識庫」或「寄信通知」工具。

Markdown 匯入的直接動機：團隊已有一套 Obsidian 維運知識庫（POS/APP SOP、事件案例，分類與 frontmatter 完整），是本系統最好的第一批真實語料。

面試要能講的故事：.NET 10 分層架構、SQL Server 原生向量搜尋、Semantic Kernel function calling（Gemini，provider 可切換）、Hangfire 非同步管線、SSE 串流、Vue 3 前端，且是解決自己公司真實痛點的系統。

### 非目標（YAGNI）

- 不做多租戶、不做使用者管理後台（使用者為種子資料）
- 不做文件版本控制；格式只支援 PDF 與 Markdown，其他格式留擴充介面即可
- 不做向量索引調校（資料量小，exact KNN 足夠；README 註明 DiskANN 為擴充方向)
- 不做真實寄信（EmailPlugin 寫 outbox 表）

## 2. 技術棧（定案）

| 層 | 選型 |
|---|---|
| 後端 | .NET 10（LTS）、ASP.NET Core Web API |
| ORM/DB | EF Core 10 + Azure SQL Database 免費層（原生 `VECTOR(1536)`） |
| AI | Gemini：chat 模型走設定 `Gemini:ChatModel`，預設 `gemini-flash-latest`（原定 `gemini-2.5-flash` 對 2026-08 後新發的 API key 已停供，實測 404）；`gemini-embedding-001`（指定 1536 維）。provider 以設定切換：開發用 AI Studio 免費層 key（**只餵假資料**，免費層資料會被 Google 用於訓練）；接真實公司文件時切 Vertex AI（Cloud 資料治理，不用於訓練，可吃 GCP 試用抵免額） |
| Agent | Semantic Kernel（auto function calling） |
| 背景工作 | Hangfire + Hangfire.SqlServer（同一顆 Azure SQL） |
| PDF 解析 | PdfPig |
| 前端 | Vue 3（`<script setup>` + TS）+ Vite + Tailwind CSS |
| 認證 | 簡易 JWT（種子使用者 3 名，部門放 claim） |
| 機密 | 開發用 `dotnet user-secrets`；CI 不需要金鑰 |
| 測試/CI | xUnit；GitHub Actions（build + test） |

Gemini 接法開工時定案二選一：Semantic Kernel 的 Google 連接器（長期掛 preview，開工前查現況），或 Gemini 的 OpenAI 相容端點＋OpenAI 連接器。兩者皆支援 function calling；程式碼已用 Core 的 `IChatService`／`IEmbeddingService` 介面隔離，切換不影響其他層。

## 3. 方案結構

```
KnowledgeHub/
├── backend/
│   ├── KnowledgeHub.Api/            # Controllers、SSE、DI 組裝、JWT 設定、Hangfire server
│   ├── KnowledgeHub.Core/           # 實體、介面、純邏輯（切片器）— 不依賴 EF 與任何 AI SDK
│   ├── KnowledgeHub.Infrastructure/ # EF Core DbContext、Repository、Gemini/SK 實作、Hangfire job
│   └── KnowledgeHub.Tests/          # 單元測試
├── frontend/                        # Vue 3 + Vite + Tailwind
├── docs/
└── .github/workflows/ci.yml
```

依賴方向：`Api → Infrastructure → Core`；`Core` 零外部依賴。介面（`IChunkRepository`、`IEmbeddingService`、`IChatService`）定義在 Core，實作在 Infrastructure。

## 4. 資料模型（階段一）

```csharp
class CompanyDocument {
    Guid Id;
    string FileName;            // 原始檔名
    string Department;          // 部門（權限邊界），如 "HR"、"IT"、"Finance"
    DocumentStatus Status;      // Pending → Processing → Completed / Failed
    string? ErrorMessage;       // Failed 時的原因，絕不吞例外
    int ChunkCount;             // 完成後回填
    DateTime UploadedAtUtc;
    List<DocumentChunk> Chunks;
}

class DocumentChunk {
    Guid Id;
    Guid DocumentId;            // FK → CompanyDocument（cascade delete）
    int SequenceNumber;         // 段落序號（從 0）
    string Content;             // nvarchar(max)
    SqlVector<float> Embedding; // VECTOR(1536)
}

class OutboxEmail {             // EmailPlugin 的落地
    Guid Id;
    string To; string Subject; string Body;
    DateTime CreatedAtUtc;
}
```

- Embedding 欄位用 EF Core 10 對 SQL `VECTOR(1536)` 的原生映射（`SqlVector<float>`）。
- 相似度查詢：`EF.Functions.VectorDistance("cosine", c.Embedding, @queryVector)` 排序取 TOP 5，前置 `WHERE d.Department == dept && d.Status == Completed`。
- Repository 介面（Core）：

```csharp
interface IChunkRepository {
    Task<IReadOnlyList<ChunkSearchResult>> SearchSimilarChunksAsync(
        float[] queryVector, string department, int topK = 5, CancellationToken ct = default);
}
// ChunkSearchResult: ChunkId, DocumentId, FileName, SequenceNumber, Content, Distance
```

## 5. 認證與權限

- `POST /api/auth/login`（帳號＋密碼）→ JWT。種子使用者寫在設定（非 DB）：`hr-user/HR`、`it-user/IT`、`fin-user/Finance`，密碼為固定 demo 值直接寫在 `appsettings.json`（demo 帳號非真實機密，README 註明；JWT signing key 才是機密、走 user-secrets）。
- JWT claim：`department`。所有文件與檢索 API 皆 `[Authorize]`，後端一律從 claim 取部門——**前端傳什麼都不信**。
- 上傳的文件歸屬 = 上傳者部門；檢索只搜自己部門的 Completed 文件。

## 6. AI 服務與 Agent（階段二）

### SK 組裝

- `Kernel` 註冊 Gemini chat + 兩個 plugin，`FunctionChoiceBehavior.Auto()` 讓模型自行決定要不要調工具。
- `RetrievalPlugin.SearchKnowledgeBase(query)`：把 query 轉 embedding → `SearchSimilarChunksAsync`（部門取自當前請求的 claim）→ 回傳段落文字給模型，**同時**把命中結果掛到 per-request 的 `RetrievalContext`（scoped service），供 API 層取用。
- `EmailPlugin.SendEmail(to, subject, body)`：寫入 `OutboxEmail` 表，回傳「已寄出」訊息。

### 串流協定（SSE）

`POST /api/chat`（body: `{ message, history }`）→ `text/event-stream`：

```
event: token    data: {"text":"..."}        # 逐段文字（IAsyncEnumerable 逐項 flush）
event: sources  data: [{fileName, sequenceNumber, content, distance}, ...]
                                            # RetrievalContext 有內容才發（模型沒查庫就沒有）
event: done     data: {}
```

- 對話 history 由前端持有、每次帶上（無伺服器端 session——YAGNI）。
- 錯誤處理：串流中途例外 → 發 `event: error` 帶訊息後結束，前端顯示錯誤氣泡。

## 7. 文件上傳與背景解析（階段三）

```
POST /api/documents (multipart) ──► 驗證(PDF/MD、≤20MB) ──► 存檔 uploads/{docId}.{副檔名}
        ──► 建 CompanyDocument(Pending) ──► Hangfire Enqueue(docId) ──► 202 + docId
```

背景 job `DocumentProcessingJob.ProcessAsync(Guid docId)`：

1. 標記 Processing
2. 依副檔名抽全文：PDF 用 PdfPig 逐頁抽文字；Markdown 直接讀入，略過開頭的 YAML frontmatter（frontmatter 之後可擴充為檢索 metadata，本階段不做）
3. 切片（**Core 內純函式**，字元數計算、中文適用）：Markdown 走 `MarkdownChunker.Split` — 依標題分段、每片前綴標題路徑（如「POS 維運 > 重開機流程」），超長段落內部再以 500 字元／10% 重疊細切，無標題時退回固定切片；PDF 走 `TextChunker.Split(text, chunkSize: 500, overlapRatio: 0.1)` 固定切片
4. 批次呼叫 embedding API（每批 ≤ 64 段），組 `DocumentChunk` 整批寫入
5. 標記 Completed + 回填 ChunkCount；任何一步失敗 → 標記 Failed + 存 ErrorMessage，不重試（Hangfire 自動重試設為關閉，失敗狀態對使用者可見）
6. 全文為空（掃描檔）→ Failed，訊息注明「無可抽取文字」

查詢端點：`GET /api/documents`（自己部門的清單＋狀態）、`DELETE /api/documents/{id}`（連帶刪 chunks 與檔案）。

## 8. 前端（階段四）

單頁（無 router）：

- **左欄**：文件列表（檔名、狀態標籤 Pending/Processing/Completed/Failed、chunk 數）＋上傳區（拖放或選檔）。有 Pending/Processing 文件時每 3 秒輪詢一次，全部完成即停。
- **右欄**：聊天。`fetch` + `ReadableStream` 手動解析 SSE（POST 無法用 EventSource），`token` 事件逐字 append 實現打字機；`sources` 事件在該則回答下渲染參考來源卡片，點擊展開段落內容並高亮。
- 登入頁（極簡）：選使用者登入 → JWT 存 memory（重整要重登，YAGNI）。
- 狀態管理用 composables（`useAuth`、`useDocuments`、`useChat`），不上 Pinia。

## 9. 測試

| 對象 | 測法 |
|---|---|
| `TextChunker` | 純函式單元測試：長度、重疊、中文、邊界（空字串、短於 500） |
| `MarkdownChunker` | 純函式單元測試：依標題分段、標題路徑前綴、超長段落細切、無標題退回固定切片 |
| `SearchSimilarChunksAsync` | 部門過濾與 TOP K 的查詢邏輯（sqlite 不支援 VECTOR，故此層用整合測試標記，CI 跳過、本機對 Azure SQL 跑；README 註明） |
| Chat 服務 | mock `IChatCompletionService`，驗證 sources 事件的觸發邏輯 |
| 上傳 API | 驗證拒絕非 PDF/MD、超額檔案回 400 |

CI（GitHub Actions）：`dotnet build` + `dotnet test`（排除整合測試）+ `npm run build`。不需任何金鑰。

## 10. 環境與機密

- 連線字串、Gemini API key（或 Vertex AI 憑證）、JWT signing key：開發放 `dotnet user-secrets`；`appsettings.json` 只留空位與註解。
- `.gitignore`：`node_modules/`、`bin/`、`obj/`、`uploads/`、`.env`。
- Azure SQL 免費層注意事項（寫進 README）：serverless 自動暫停，閒置後首個請求冷啟動 30–60 秒，demo 前先打一次 `GET /api/health`；每月 100k vCore-秒對開發用量綽綽有餘。

## 11. 實作階段（每階段獨立 commit、可驗證）

| # | 內容 | 驗收 |
|---|---|---|
| 0 | 方案骨架、gitignore、CI、README 雛形 | CI 綠 |
| 1 | 實體、DbContext、migration、Repository（向量搜尋）、JWT auth | 對 Azure SQL 實跑 migration；手插測試向量查得回正確 TOP 5 且部門過濾生效 |
| 2 | SK Agent、兩個 plugin、SSE chat 端點 | curl 實測：問知識庫問題有 sources 事件；請它寄信 outbox 表有紀錄 |
| 3 | 上傳 API + Hangfire job + 切片器 | 上傳真實 PDF 與 Markdown（拿團隊 Obsidian vault 的假資料版測）→ 狀態走完 → chunks 入庫 → 立即可問答 |
| 4 | Vue 前端整合 | 瀏覽器完整走一遍：登入→上傳→看進度→問答→看來源卡片 |

前置作業（使用者自行操作，開工前完成）：建 Azure SQL Database 免費層、取得連線字串；到 Google AI Studio 申請 Gemini API key（免費層，開發用）。

## 12. Roadmap — Phase B：EIP 待審助理

Phase A（上表階段 0–4）完成後，以新 plugin 形式加入 EIP 待審助理：自動讀取 EIP 待審文件 → AI 摘要整理 → 使用者確認後代為簽核。實作前另寫獨立設計文件；以下三條原則現在先定死，屆時不重議：

1. **人工確認閘門**：AI 只做摘要與建議；每一件簽核都需使用者逐件明確確認才執行。自動化過程遇到任何非預期畫面立即中止並回報，絕不猜測操作。
2. **公私分離**：公司 EIP 只有網頁介面，需走瀏覽器自動化（Playwright）。公開 repo 只放 plugin 介面與假資料 demo 實作；真正的 EIP connector（內部網址、頁面選擇器、登入流程）放私有 repo，永不進公開版控。
3. **資料安全**：EIP 公文可能含人事／財務資料，只允許送 Vertex AI（付費層、不用於訓練），禁用 AI Studio 免費層。

## 13. 檢索品質 Backlog（2026-08-07 討論定案）

定位確認：本專案走「.NET 生態的 AI 工程師」路線，不為對齊主流關鍵字改寫 Python；主流 Python 棧（LlamaIndex／LangChain + pgvector）之後以小型對照實驗 repo 練習（同一批語料重做檢索管線、跑評估集比較兩版），不重寫本系統。

Phase A 已內建：Markdown 標題感知切片（§7 步驟 3——第一批語料是 Obsidian vault，此改動效益最大）。以下為 Phase A 之後的演進方向（寫進 README 的擴充方向，Phase A 不做）：

1. **Hybrid search**：SQL Server 全文檢索（BM25）＋向量兩路召回、RRF 合併——維運 SOP 充滿錯誤代碼、品名等精確詞，純向量檢索對這類查詢會漏
2. **Reranker**：召回 top 20–50 後以 cross-encoder 重排取 top 5
3. **評估集**：golden questions＋自動化指標（如 Ragas 類），讓 chunk 大小、top-K 等調整有依據，不憑感覺
