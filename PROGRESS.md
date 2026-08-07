# KnowledgeHub 進度（2026-08-07 執行中）

## 狀態總覽

Phase A 以 superpowers:subagent-driven-development 執行中，**7/15 任務完成**。
詳細記錄（含每任務 commit 範圍、fix rounds、deferred minors）：`.superpowers/sdd/2026-08-06-knowledgehub-phase-a/progress.md`（SDD ledger，git-ignored）。

## ✓ 已完成（皆通過 spec＋品質審查）

1. 方案骨架、gitignore、CI（GitHub Actions 綠）
2. Core 實體與介面（fix：改官方 Microsoft.Data.SqlTypes.SqlVector&lt;float&gt;）
3. TextChunker＋MarkdownChunker，14 測試（fix：CRLF 正規化）
4. DbContext＋migration 已對 Azure SQL 實跑，vector(1536) 確認
5. ChunkRepository 向量搜尋（整合測試 2/2 實跑）
6. JWT 認證（4/4＋curl 實測 200/401）
7. GeminiEmbeddingService（fix：跨批次順序測試、非 200 診斷；24/24）

## → 進行中

Task 8：RetrievalContext、RetrievalPlugin、EmailPlugin（含補 IChunkRepository DI 註冊）

## 環境事實（重啟 session 必讀）

- 分支 `feature/phase-a`，remote：https://github.com/LuXiaoMie1/KnowledgeHub（public）
- Azure SQL：`testqb.database.windows.net` / DB `free-sql-db-9033387`（免費層 serverless，冷啟動 30–60s）
- 機密全在 user-secrets（Api 專案，ID 3fc8ee2a-…）：`ConnectionStrings:Default`、`Gemini:ApiKey`、`Jwt:SigningKey`——皆已實測有效
- Gemini：1536 維 embedding 實測不自動正規化（norm 0.694），手動 L2 必要
- 模型調度：轉錄型任務 haiku、整合型 sonnet、審查 sonnet；haiku 錯一次即升級（Task 2 案例）

## □ 待辦（Task 8 之後）

Task 9 SSE chat → 10 上傳 API → 11 Hangfire 管線 → 12–14 Vue 前端 → 15 端到端驗收＋README
全部完成後：最終全分支審查（opus）→ finishing-a-development-branch
