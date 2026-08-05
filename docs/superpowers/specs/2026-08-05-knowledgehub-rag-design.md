# KnowledgeHub — 企業知識庫 RAG 問答系統 設計文件

- 日期：2026-08-05
- 狀態：已由使用者核准設計方向，待核准本文件
- 定位：個人 side project，放個人 GitHub（面試作品）。**repo 未來可能公開，任何金鑰、連線字串永不進版控。**

## 1. 目標

一個可實際跑起來的企業知識庫問答系統：使用者上傳 PDF 文件 → 背景解析切片並向量化 → 依部門權限檢索 → AI Agent 以串流回答並附參考來源，且能自行決定呼叫「查知識庫」或「寄信通知」工具。

面試要能講的故事：.NET 10 分層架構、SQL Server 原生向量搜尋、Semantic Kernel function calling、Hangfire 非同步管線、SSE 串流、Vue 3 前端。

### 非目標（YAGNI）

- 不做多租戶、不做使用者管理後台（使用者為種子資料）
- 不做文件版本控制、不做 PDF 以外的格式（介面留擴充點即可）
- 不做向量索引調校（資料量小，exact KNN 足夠；README 註明 DiskANN 為擴充方向)
- 不做真實寄信（EmailPlugin 寫 outbox 表）

## 2. 技術棧（定案）

| 層 | 選型 |
|---|---|
| 後端 | .NET 10（LTS）、ASP.NET Core Web API |
| ORM/DB | EF Core 10 + Azure SQL Database 免費層（原生 `VECTOR(1536)`） |
| AI | OpenAI：`gpt-4o-mini`（chat）、`text-embedding-3-small`（1536 維） |
| Agent | Semantic Kernel（auto function calling） |
| 背景工作 | Hangfire + Hangfire.SqlServer（同一顆 Azure SQL） |
| PDF 解析 | PdfPig |
| 前端 | Vue 3（`<script setup>` + TS）+ Vite + Tailwind CSS |
| 認證 | 簡易 JWT（種子使用者 3 名，部門放 claim） |
| 機密 | 開發用 `dotnet user-secrets`；CI 不需要金鑰 |
| 測試/CI | xUnit；GitHub Actions（build + test） |

## 3. 方案結構

```
KnowledgeHub/
├── backend/
│   ├── KnowledgeHub.Api/            # Controllers、SSE、DI 組裝、JWT 設定、Hangfire server
│   ├── KnowledgeHub.Core/           # 實體、介面、純邏輯（切片器）— 不依賴 EF/OpenAI
│   ├── KnowledgeHub.Infrastructure/ # EF Core DbContext、Repository、OpenAI/SK 實作、Hangfire job
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

- `Kernel` 註冊 OpenAI chat + 兩個 plugin，`FunctionChoiceBehavior.Auto()` 讓模型自行決定要不要調工具。
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
POST /api/documents (multipart) ──► 驗證(PDF、≤20MB) ──► 存檔 uploads/{docId}.pdf
        ──► 建 CompanyDocument(Pending) ──► Hangfire Enqueue(docId) ──► 202 + docId
```

背景 job `DocumentProcessingJob.ProcessAsync(Guid docId)`：

1. 標記 Processing
2. PdfPig 逐頁抽文字，串成全文
3. `TextChunker.Split(text, chunkSize: 500, overlapRatio: 0.1)` — **Core 內純函式**，字元數計算（中文適用），10% 重疊
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
| `SearchSimilarChunksAsync` | 部門過濾與 TOP K 的查詢邏輯（sqlite 不支援 VECTOR，故此層用整合測試標記，CI 跳過、本機對 Azure SQL 跑；README 註明） |
| Chat 服務 | mock `IChatCompletionService`，驗證 sources 事件的觸發邏輯 |
| 上傳 API | 驗證拒絕非 PDF、超額檔案回 400 |

CI（GitHub Actions）：`dotnet build` + `dotnet test`（排除整合測試）+ `npm run build`。不需任何金鑰。

## 10. 環境與機密

- 連線字串、OpenAI key、JWT signing key：開發放 `dotnet user-secrets`；`appsettings.json` 只留空位與註解。
- `.gitignore`：`node_modules/`、`bin/`、`obj/`、`uploads/`、`.env`。
- Azure SQL 免費層注意事項（寫進 README）：serverless 自動暫停，閒置後首個請求冷啟動 30–60 秒，demo 前先打一次 `GET /api/health`；每月 100k vCore-秒對開發用量綽綽有餘。

## 11. 實作階段（每階段獨立 commit、可驗證）

| # | 內容 | 驗收 |
|---|---|---|
| 0 | 方案骨架、gitignore、CI、README 雛形 | CI 綠 |
| 1 | 實體、DbContext、migration、Repository（向量搜尋）、JWT auth | 對 Azure SQL 實跑 migration；手插測試向量查得回正確 TOP 5 且部門過濾生效 |
| 2 | SK Agent、兩個 plugin、SSE chat 端點 | curl 實測：問知識庫問題有 sources 事件；請它寄信 outbox 表有紀錄 |
| 3 | 上傳 API + Hangfire job + 切片器 | 上傳真實 PDF → 狀態走完 → chunks 入庫 → 立即可問答 |
| 4 | Vue 前端整合 | 瀏覽器完整走一遍：登入→上傳→看進度→問答→看來源卡片 |

前置作業（使用者自行操作，開工前完成）：建 Azure SQL Database 免費層、取得連線字串；準備 OpenAI API key。
