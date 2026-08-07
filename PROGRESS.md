# KnowledgeHub 進度（2026-08-07 Phase A 完成）

## 狀態總覽

Phase A 以 superpowers:subagent-driven-development 執行，**15/15 任務全部完成**。後端全棒打通（JWT、向量檢索、SK function calling、SSE 串流、上傳 API、Hangfire 背景解析管線）＋前端骨架、文件面板、聊天介面皆過審查與實測；Task 15 完成端到端驗收與 README 完稿。重要環境事實新增：gemini-2.5-flash 對新 API key 停供→chat 模型走設定 Gemini:ChatModel 預設 gemini-flash-latest；SK 1.79.0 丟 thought_signature→DelegatingHandler 權宜解；Gemini 免費層 chat 模型配額對 **function calling 路徑**（單次提問內部至少 2 次 API 呼叫）特別容易撞牆，不觸發工具呼叫的純聊天當日仍可成功——這是比「每日 20 次」更精確的卡點定位（Task 15 實測發現），非短暫限流，重試無效。
詳細記錄（含每任務 commit 範圍、fix rounds、deferred minors）：`.superpowers/sdd/2026-08-06-knowledgehub-phase-a/progress.md`（SDD ledger，git-ignored）。

## ✓ 已完成（皆通過 spec＋品質審查）

1. 方案骨架、gitignore、CI（GitHub Actions 綠）
2. Core 實體與介面（fix：改官方 Microsoft.Data.SqlTypes.SqlVector&lt;float&gt;）
3. TextChunker＋MarkdownChunker，14 測試（fix：CRLF 正規化）
4. DbContext＋migration 已對 Azure SQL 實跑，vector(1536) 確認
5. ChunkRepository 向量搜尋（整合測試 2/2 實跑）
6. JWT 認證（4/4＋curl 實測 200/401）
7. GeminiEmbeddingService（fix：跨批次順序測試、非 200 診斷；24/24）
8. RetrievalContext、RetrievalPlugin、EmailPlugin
9. SK Agent、SSE 聊天端點與 Gemini 接線
10. 文件上傳/清單/刪除 API（部門隔離）
11. 文字抽取器（PDF/MD）、DocumentProcessingJob、HangfireDocumentJobQueue（47/47；curl 實測上傳 md+pdf→Processing→Completed、chunk 內容經真 Gemini embedding 向量檢索命中正確段落；chat 問答因 Gemini 免費層每日配額用盡未能實跑，改以真實 embedding+向量搜尋驗證檢索層）
12. Vue 3 前端骨架＋JWT 登入
13. 文件面板（上傳、狀態輪詢、刪除）
14. 聊天介面（SSE 串流、打字機、來源卡片；fix：串流於元件卸載時中止避免燒配額）
15. 端到端驗收與 README 完稿：全量測試 47/47（含整合測試）、`npm run build` 綠、CI 綠；README 補齊架構圖／啟動步驟／demo 帳號／資料安全／擴充方向；小收尾（刪 weatherforecast 死引用、frontend title/README、useAuth atob base64url 修正）。瀏覽器 E2E 走到「登入→上傳→完成→送出問題→SSE error 正確渲染」，LLM 生成含 sources 事件的完整回答因 Gemini 免費層 chat 模型 function-calling 路徑當日配額耗盡未能現場重現（純聊天當日仍可成功，卡點精準定位在 function calling 的第二次 API 呼叫），已用 Task 9 歷史真實 sources 事件證據＋今日 outbox/embedding 交叉證據補足，回報 DONE_WITH_CONCERNS

## 環境事實（重啟 session 必讀）

- 分支 `feature/phase-a`，remote：https://github.com/LuXiaoMie1/KnowledgeHub（public），CI 綠（commit 312515e）
- Azure SQL：`testqb.database.windows.net` / DB `free-sql-db-9033387`（免費層 serverless，冷啟動 30–60s）
- 機密全在 user-secrets（Api 專案，ID 3fc8ee2a-…）：`ConnectionStrings:Default`、`Gemini:ApiKey`、`Jwt:SigningKey`——皆已實測有效
- Gemini：1536 維 embedding 實測不自動正規化（norm 0.694），手動 L2 必要；chat 模型 function-calling 路徑（單次提問內部 ≥2 次 API 呼叫）比純聊天更容易撞配額牆，兩者要分開判斷
- 模型調度：轉錄型任務 haiku、整合型 sonnet、審查 sonnet；haiku 錯一次即升級（Task 2 案例）

## 待辦

Phase A 15/15 全部完成。下一步：最終全分支審查（opus）→ finishing-a-development-branch（尚未執行，需使用者確認是否進行）。
