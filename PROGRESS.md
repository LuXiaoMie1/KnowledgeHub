# KnowledgeHub 進度（2026-08-06 下班存檔）

## ✓ 已完成

- 設計文件（已核准）：`docs/superpowers/specs/2026-08-05-knowledgehub-rag-design.md`
  - 定位：企業 AI 助理平台（Phase A 知識庫問答＋Phase B EIP 待審助理 roadmap）
  - AI 換成 Gemini：chat 走 OpenAI 相容端點＋SK OpenAI 連接器（SK Google 連接器仍 alpha，不用）；embedding 走原生 REST `gemini-embedding-001`＋`outputDimensionality:1536`＋手動 L2 正規化（皆已查證，2026-08-06）
  - 支援 PDF＋Markdown（同事的 Obsidian vault 是第一批語料，開發期只用假資料版）
- Phase A 實作計畫（已核准、read-back 驗證過）：`docs/superpowers/plans/2026-08-06-knowledgehub-phase-a.md`（15 個任務，含完整測試碼/實作碼）
- RAG 說明文件（給同事）：`docs/RAG完整說明.md`
- 最新 commit：`24910f2`

## → 下一步（明天從這裡開始）

1. 用 superpowers:subagent-driven-development 執行實作計畫（使用者已選定此執行方式），從 Task 1 開始
2. 開工時先跑 skill 的 `scripts/sdd-workspace <計畫檔路徑>` 建 ledger

## □ 待使用者提供（到對應任務會用到）

- GitHub repo URL（Task 1 推 CI）
- Azure SQL Database 免費層連線字串（Task 4）
- Google AI Studio 的 Gemini API key（Task 9；免費層即可，開發只餵假資料）

## 未驗證的假設

- SK `AddOpenAIChatCompletion` 自訂 endpoint 的 overload 簽名（Task 9 有備案註記）
- Vertex AI 可折抵 GCP $300 試用額度（接真資料前再驗，試用期至 2026-11-05）
