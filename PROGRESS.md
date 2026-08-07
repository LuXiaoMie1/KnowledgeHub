# KnowledgeHub 進度（2026-08-07 Phase A 開發完成，待驗收與合併）

## 狀態總覽

**15/15 任務完成＋最終全分支審查（opus）通過**：0 Critical；6 Important＋2 Minor 已由單一修復波修畢並經 scoped re-review 確認 8/8 ADDRESSED（commits `c1aa104`＋`c2e5602` migration＋`1b71834` Newtonsoft pin）。最終狀態：單元 52/52＋整合 2/2、npm build 乾淨、build 0 警告、CI 綠。分支 `feature/phase-a` HEAD `1b71834` 已推 GitHub。
詳細記錄：`.superpowers/sdd/2026-08-06-knowledgehub-phase-a/progress.md`（SDD ledger，git-ignored）。

最終審查修復要點：文件處理狀態機防卡死（ExecuteUpdateAsync 繞開 change tracker）、上游 API 錯誤不再經 ErrorMessage 洩到前端、IEmbeddingService DI 除雙重註冊、chat history 伺服器端上限（10 turns/4000 字元）、設定 fail-fast 一致化、OutboxEmail 加 Department/RequestedBy 稽核欄位（migration 20260807055128 已上線）＋to 格式驗證。

## → 進行中／待辦（依序）

1. **使用者瀏覽器驗收**：服務已起（前端 http://localhost:5173、後端 https://localhost:7152，帳號 hr-user/it-user/fin-user，密碼在 appsettings SeedUsers）
2. **Gemini 付費層儲值**：key 已切 Tier 1 Prepay 但**餘額 NT$0**（使用者在 Buy credits 畫面按了取消）→ 目前所有 Gemini 呼叫 429。待使用者完成 NT$1,000 儲值（Auto-reload 關閉）→ 實測 chat round-trip（含 Task 11/15 遞延的 sources 事件端到端）
3. **真實公司文件實驗**（使用者主動提出）：內控循環辦法 PDF 批次上傳。**紅線：必須付費層生效後才可上傳**（免費層資料會被 Google 訓練，已與使用者確認）。待使用者：下載 PDF 到資料夾＋告知路徑＋決定部門歸屬（建議單一帳號上傳）；我寫批次上傳腳本
4. **合併**：驗收 OK 後 superpowers:finishing-a-development-branch（merge 到 main、刪 SDD workspace）

## 關鍵環境事實（重啟 session 必讀）

- Gemini 計費查證（2026-08-07，報告：~/.claude/jobs/3b553121/tmp/gemini-billing-research.md）：GCP $300 試用額度**不能**付 AI Studio Gemini API（官方明文排除）；AI Studio 付費=Prepay 儲值（不可退、一年效期）；付費層資料不用於訓練（2026-03-23 條款）、免費層會＋人工審閱；Vertex AI 大機率吃試用額度＋產品層級不訓練承諾——正式上線路線
- 分支 `feature/phase-a`，remote：https://github.com/LuXiaoMie1/KnowledgeHub（public）
- Azure SQL：`testqb.database.windows.net` / DB `free-sql-db-9033387`（免費層 serverless，冷啟動 30–60s）
- 機密全在 user-secrets（Api 專案，ID 3fc8ee2a-…）：`ConnectionStrings:Default`、`Gemini:ApiKey`、`Jwt:SigningKey`
- Gemini：1536 維 embedding 不自動正規化（手動 L2 必要）；gemini-2.5-flash 對新 key 停供（ChatModel 設定預設 gemini-flash-latest）；SK 1.79.0 丟 thought_signature（DelegatingHandler 權宜解）；function-calling 一次提問內部 ≥2 次 API 呼叫，配額消耗要分開判斷
- 模型調度經驗：轉錄型 haiku、整合型 sonnet、審查 sonnet（複雜棒次 opus）；haiku 錯一次即升級

## ✓ Phase A 完成清單（15 任務皆過任務審查；細節見 git log 與 ledger）

骨架/CI、Core 實體介面、雙切片器（Markdown 標題感知＋CRLF 修正）、EF+VECTOR(1536) migration、向量檢索（部門過濾）、JWT、Gemini embedding（L2 正規化＋跨批次順序保證）、SK plugins（檢索/寄信）、SSE chat（斷線/例外硬化）、上傳 API（部門隔離）、Hangfire 管線、Vue 登入/文件面板（輪詢三振）/聊天（打字機＋來源卡片＋卸載中止）、README 完稿
