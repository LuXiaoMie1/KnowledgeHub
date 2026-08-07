# KnowledgeHub 進度（2026-08-07 執行中）

## 狀態總覽

Phase A 以 superpowers:subagent-driven-development 執行中，**11/15 任務完成**（後端全棒打通：JWT、向量檢索、SK function calling、SSE 串流、上傳 API、Hangfire 背景解析管線皆過審查與實測）。重要環境事實新增：gemini-2.5-flash 對新 API key 停供→chat 模型走設定 Gemini:ChatModel 預設 gemini-flash-latest；SK 1.79.0 丟 thought_signature→DelegatingHandler 權宜解；Gemini 免費層 chat 模型有**每日** 20 次配額（非每分鐘），額度用盡需等隔日重置，勿誤判為短暫限流重試可解。
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

## → 進行中

Task 12：Vue 前端骨架

## 環境事實（重啟 session 必讀）

- 分支 `feature/phase-a`，remote：https://github.com/LuXiaoMie1/KnowledgeHub（public）
- Azure SQL：`testqb.database.windows.net` / DB `free-sql-db-9033387`（免費層 serverless，冷啟動 30–60s）
- 機密全在 user-secrets（Api 專案，ID 3fc8ee2a-…）：`ConnectionStrings:Default`、`Gemini:ApiKey`、`Jwt:SigningKey`——皆已實測有效
- Gemini：1536 維 embedding 實測不自動正規化（norm 0.694），手動 L2 必要
- 模型調度：轉錄型任務 haiku、整合型 sonnet、審查 sonnet；haiku 錯一次即升級（Task 2 案例）

## □ 待辦（Task 11 之後）

Task 12–14 Vue 前端 → 15 端到端驗收＋README
全部完成後：最終全分支審查（opus）→ finishing-a-development-branch
