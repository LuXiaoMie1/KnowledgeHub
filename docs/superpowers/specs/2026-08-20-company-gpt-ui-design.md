# KnowledgeHub「公司專用 GPT」改版設計

- 日期：2026-08-20
- 狀態：已與使用者逐節確認（對話紀錄），待實作計畫
- 前情：RAG 檢索品質評估與門檻/去重改善見 `docs-private/KnowledgeHub-RAG檢索評估-2026-08-20.md`

## 1. 目標與背景

把 KnowledgeHub 從「文件面板＋聊天並排的工具頁」改成「公司內部專用的 ChatGPT」：
對話是畫面主角，對話可保存、可切換、跨裝置與跨管道（web／Teams bot）一致；
文件管理退到獨立頁。

## 2. 範圍

**做：**
1. ChatGPT 式版面（sidebar 對話清單＋置中對話流）
2. 對話保存與切換——後端為唯一事實來源，web 與 Teams bot 共用同一套資料
3. Teams bot 多輪對話（一併了結既有小債）
4. assistant 回覆 Markdown 渲染
5. 乾淨專業視覺＋品牌點綴、手機 RWD
6. 文件管理移至獨立頁 `/documents`（權限維持現狀：依部門人人可上傳/刪除）

**不做（明確排除）：**
- 管理者角色/權限分級（文件管理人人可用，維持現狀）
- 深色模式
- 前端測試框架（Vitest 等）——以手動驗收清單代替
- RAG 檢索邏輯變更（本次純 UI/對話管理，檢索管線不動）

## 3. 資料模型（EF Core migration，Azure SQL）

### Conversation
| 欄位 | 型別 | 說明 |
|---|---|---|
| Id | Guid PK | |
| UserKey | string，索引 | 歸戶鍵，見下 |
| Channel | string | `web`／`teams` |
| Title | string | 首則使用者訊息前 ~30 字自動截取 |
| TeamsConversationId | string? | 僅 teams：Bot Framework 的 conversation id，用於找「目前接續中」的對話 |
| EndedAtUtc | DateTime? | 僅 teams：使用者下「新對話」指令時蓋章結束；bot 接續查詢只找未結束的 |
| CreatedAtUtc / UpdatedAtUtc | DateTime | UpdatedAtUtc 供側欄排序 |

### ConversationMessage
| 欄位 | 型別 | 說明 |
|---|---|---|
| Id | Guid PK | |
| ConversationId | FK → Conversation，cascade delete | |
| Role | string | `user`／`assistant` |
| Content | string | |
| SourcesJson | string? | assistant 訊息存當次檢索來源（檔名、段號、內容），翻舊對話時來源卡片重現 |
| CreatedAtUtc | DateTime | 排序依據 |

### UserKey 取法（關鍵設計）
- Entra 使用者：OID（object id claim）
- 本機種子帳號：username
- Teams 使用者：activity 的 `AadObjectId`——與 Entra OID 是同一個值，因此**同一人在 web 與 Teams 的對話自然歸戶互通**，web 側欄看得到自己在 Teams 問過的對話（標註管道），無需帳號綁定。
- `ICurrentUser` 增加穩定識別屬性 `UserKey`（Entra→OID、種子帳號→username）。

## 4. API

### `POST /api/conversations/messages`（唯一發話端點，SSE）
- body：`{ conversationId?: Guid, message: string }`
- `conversationId` 為空 → 後端建新對話（Channel=web、Title 自動取），SSE 先送 `conversation` 事件 `{id, title}`，再照既有 `token`／`sources`／`error` 事件流
- 後端自行載入該對話歷史（沿用既有上限：近 10 輪、單則 4000 字），串流回覆，結束後把 user＋assistant 訊息（含 SourcesJson）落庫並更新 UpdatedAtUtc
- 不建空對話：前端按「新對話」只清畫面，第一句送出才建立

### 其餘端點
- `GET /api/conversations`：目前使用者的清單（id、title、channel、updatedAtUtc），依 UpdatedAtUtc 倒序
- `GET /api/conversations/{id}`：訊息列表＋來源
- `DELETE /api/conversations/{id}`
- 所有端點驗 UserKey 歸屬；不是本人的對話一律回 404（不回 403，避免洩漏存在性）

### 移除
- 舊 `POST /api/chat` 刪除，前端同步改接新端點。`IChatService` 介面不動。

## 5. Teams bot 行為

- 收訊息：以 `TeamsConversationId` 找最新一條**未結束**（EndedAtUtc 為空）的對話接續；找不到就建新的（Channel=teams、UserKey=AadObjectId，AadObjectId 缺值時退用 `From.Id`）
- 使用者輸入「新對話」或 `/new`（trim 後全等比對）→ 將目前對話的 EndedAtUtc 蓋章、回覆確認訊息；下一句話因查不到未結束對話而自然開新的
- 載入近 10 輪歷史進 `StreamAnswerAsync`；回覆與來源列表格式照舊
- **安全前提不變（不可妥協）**：bot 管道仍用 `"bot"` keyed 服務——`AllDepartmentsScope` 只查全公司文件、不掛 EmailPlugin。本次只加「歷史從 DB 載入」，不動該隔離設計（見 `KnowledgeHubBotHandler` 類別註解）。

## 6. 前端結構

- 引入 **vue-router**：`/chat`（預設）、`/chat/:id`、`/documents`。未登入／無部門攔截由 App.vue 的 v-if 狀態機改為 router guard，行為不變
- **版面**：
  - 左側 sidebar（桌機常駐）：「＋ 新對話」→ 對話清單（標題＋相對時間，teams 對話標小徽章，hover 出刪除鈕）→ 底部使用者區（名稱＋部門、「文件管理」入口、登出）
  - 主區：對話流置中 max-w-3xl；使用者訊息維持右側泡泡，assistant 改無泡泡直排版；來源卡片沿用 `SourceCard` 置於回覆下方
  - 輸入框：textarea 自動長高，Enter 送出、Shift+Enter 換行
- **狀態管理**：沿用既有 composables 模組層 ref 慣例，新增 `useConversations`；不引入 Pinia
- `useChat` 改接新端點，處理 `conversation` SSE 事件（取得新對話 id 後更新網址與側欄）
- `DocumentPanel` 功能原樣搬至 `/documents` 置中卡片版面，上傳/輪詢/刪除邏輯不動

## 7. Markdown 渲染

- assistant 訊息：`markdown-it` 渲染 → `DOMPurify` 消毒 → `v-html`。LLM 輸出視為不可信內容，**消毒為必要防線**；串流中逐 token 重渲染
- 使用者訊息維持純文字
- 排版用 `@tailwindcss/typography` 的 prose class

## 8. 視覺與 RWD

- 白底＋slate 灰階主體、淺灰側欄；品牌點綴色僅用於「新對話」鈕、送出鈕、focus ring、logo
- QBurger 品牌色票實作時自官網取得，套用前先給使用者確認
- RWD：中斷點以下 sidebar 收成抽屜（漢堡鈕）、輸入框 sticky bottom；驗收基準＝iPhone 尺寸瀏覽器可完整對話與翻歷史
- 深色模式不做

## 9. 測試與驗收

**後端單元測試（xunit，沿既有慣例，斷言業務值）：**
- 首訊建對話＋自動標題截取
- 歷史載入裁切為近 10 輪
- 歸戶隔離：拿他人對話 id 回 404
- bot「新對話」／`/new` 關鍵字重開；一般訊息接續同一條
- SourcesJson 序列化往返（存→讀→來源卡片資料完整）

**前端手動驗收清單：**
新對話／切換／刪除／重新整理後對話保留／Teams 對話出現在 web 側欄（含徽章）／手機抽屜操作／Markdown 表格與清單渲染／舊對話來源卡片重現

**實機 E2E：**
- 兩個帳號互看不到對方對話
- Teams bot 多輪接續＋「新對話」重開實測

## 10. 實作順序（分期，每期可獨立驗收）

1. 後端：資料表＋conversation 服務＋API＋單元測試
2. bot 多輪（接同一套服務）
3. 前端：vue-router＋版面重構＋接新 API
4. Markdown 渲染
5. 視覺打磨＋RWD

## 11. 風險與未決

- 品牌色票未定：實作時自官網取，套用前經使用者確認（§8）
- Entra OID 與 Teams AadObjectId 的一致性：設計依據為兩者同為 Entra object id；實作第 2 期時以真帳號實測驗證歸戶互通，若有出入（如 guest 帳號），退化行為是兩邊各自成列（不互通），不影響其他功能
- 既有整合測試隔離資料的小債不在本次範圍，但新增測試不得加重它（新測試自建自清資料）
