# KnowledgeHub 進度（2026-08-31 存檔 — 分支已 push；Azure SQL 免費額度月底用罄，驗收延至 9/1）

## 2026-08-31 本日進展（驗收被 DB 暫停擋下，一項都還沒跑）

- ✓ `feature/company-gpt-ui` 已 push 到 origin（19 commits；teams-app/ 仍 untracked 未 commit，照計畫等 Teams 驗完才 commit）
- ✗ **卡點：Azure SQL 免費層當月額度用完，DB 被暫停到月底**（錯誤碼 42119，Hangfire 連線即炸）。前端與 devtunnel 起得來，後端無 DB 不可用，三個服務已收掉
- → **9/1 00:00 UTC（台灣 9/1 早上 8 點）額度自動重置**，使用者選擇等待、不付費解鎖。明天開場：起三件組 → https 登入實測（8/24 補的 redirect URI 尚未驗過）→ 跑下方驗收清單 → Teams sideload（步驟見「下次開場」第 4 點與 Bot 計畫 8b）
- ✓ **Teams sideload 完成**（8b 辦結）：使用者已上傳 zip，KnowledgeHub bot 出現在 Teams 對話清單。尚未發話實測（後端沒起＋DB 暫停），明天服務起來後測：發問→多輪→「新對話」→web 側欄 OID 歸戶
- 備忘：Teams bot 架構已向使用者說明——邏輯全在本機（Teams/Azure Bot 只轉發、經 devtunnel 進本機 5106），本機服務停＝bot 停。同事存取：測試階段傳 zip 給同事各自 sideload（前提：sideload 政策對他們也開放）；正式發佈走組織應用程式目錄＝等後端離開開發機再說（tunnel 剩約 24 天）

## 2026-08-24 進展（驗收開場即卡登入，已解，尚未實際驗收）

- ✓ 起服務驗收：後端＋前端都起得來（Azure SQL 冷啟 Hangfire 逾時重試屬正常，等到 Now listening 即可）
- ✓ 卡點與解法：本機 `https://localhost:5173` 登入炸 **AADSTS50011**——Entra SPA 登記的 localhost 是 http 版，前端 8/12 已改 https。使用者已在 Portal 補加 `https://localhost:5173` 與 `…/redirect.html` 兩條（詳見 docs-private\KnowledgeHub-entra設定紀錄-2026-08-10.md 的 SPA 節）
- ⚠ 注意：瀏覽器手打 `localhost:5173` 會走 http → ERR_EMPTY_RESPONSE，要完整輸入 `https://localhost:5173/`（自簽憑證警告點進階→繼續）
- → **登入尚未實測**（補完 URI 就下班了）；驗收清單一項都還沒跑——下次開場：起服務 → https 登入 → 跑下方驗收清單

## 下次開場（接續點）

**目前在分支 `feature/company-gpt-ui`（19 commits，未合併 main、未 push）。改版已全部實作＋審查完畢，剩使用者驗收與三個決定。**

1. **起服務**（session 結束背景服務已停）：
   - 後端：`dotnet run --project backend/KnowledgeHub.Api --launch-profile https`（Azure SQL 冷啟 30–60s，Hangfire 第一次連線逾時會自己重試，看到 Now listening 才算好）
   - 前端：`npm run dev --prefix frontend` → 開 `https://localhost:5173/`
2. **使用者驗收清單**（新版 ChatGPT 式 UI）：
   - 新對話發問→網址變 /chat/{id}、側欄出現（帶相對時間）；切換／刪除／重新整理保留
   - **登出→另一帳號同一分頁登入→不得看到前人對話**（最終審查抓到的 C1 回歸，最重要）
   - Markdown 表格/清單渲染；貼 `<img src=x onerror=alert(1)>` 不得執行
   - 手機尺寸：側欄抽屜、輸入、/documents
   - Teams bot 多輪＋「新對話」指令＋Teams 對話出現在 web 側欄（OID 歸戶；要起 devtunnel：`& "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\Microsoft.devtunnel_Microsoft.Winget.Source_8wekyb3d8bbwe\devtunnel.exe" host khub.jpe1`）
3. **三個待決**：(a) 品牌色（暫定 #e4002b，`frontend/src/style.css` 一行替換）；(b) 已知未修項——既有對話發話後側欄排序/時間不即時更新（修法一行：useChat.ts 的 `sending` 提升為模組層，詳見 `.superpowers/sdd/2026-08-20-company-gpt-ui/progress.md` 的 parked ruling）；(c) 驗收過後合併 main
4. 沿前未完的 Teams app sideload（上傳 `teams-app\knowledgehub-teams-app.zip`，走「上傳自訂應用程式」不用核准）與 teams-app/ commit——與本次改版無關，bot 多輪在 Emulator/既有管道就能驗


> 歷史存檔（Phase A、Entra、內網直連）見 `docs-private\KnowledgeHub-entra設定紀錄-2026-08-10.md`。

## 前置狀態（今日開場確認）

- ✓ Entra 4 條 SPA redirect URI 使用者已加好並實測（同事內網直連登入 OK、群組→部門 claim 正確）
- ✓ Teams 自訂應用程式上傳政策生效（用戶端已出現「上傳應用程式」按鈕，截圖確認）
- main `7d6894d`，工作樹乾淨

## Teams/Azure Bot 整合計畫

- ✓ 1. Azure CLI 2.89.1 已裝（winget）
- ✓ 2. az login 完成（device code；訂閱 bcdb62f8-…、租戶 9713fce3-…）
- ✓ 3. tunnel 5106 埠已開（使用者執行；`https://25630g9l-5106.jpe1.devtunnels.ms`）
- ✓ 4a. App registration「KnowledgeHub-Bot」建好：appId `2629b8d6-a548-4c0c-9eb0-a1971cc16494`（single-tenant）
- ✓ 4b. client secret 已由使用者發（bot-dev，效期 1 年）
- ✓ 5. Azure Bot F0 `knowledgehub-dev-bot` 建好（rg-knowledgehub-dev），endpoint 指 tunnel 5106 `/api/messages`，Teams channel 已啟用
- ✓ 6. 四鍵已入 user-secrets（AppType=SingleTenant/AppId/Password/TenantId），list 確認存在
- ✓ 7. manifest（真實 appId 已填）＋icons＋zip：`teams-app/knowledgehub-teams-app.zip`
- ✓ 8a. 後端＋tunnel host 已起；驗證：本機 POST /api/messages 未簽章→401（Bot 驗證生效）；經 tunnel 繞 DNS 直打（4.190.51.35）→401（外部可達）
- ✓ 8b. 使用者上傳 zip 完成（2026-08-31）；→ 對 bot 發問實測（等 9/1 DB 恢復＋服務起來）
- □ 9. 收尾：交接檔更新、commit（teams-app/ 進 repo；secret 不進。注意 manifest 含 appId——公開 repo 可接受，appId 非機密）

## 環境事實

- tunnel `khub.jpe1`：現有 5173(https) 一埠，Anonymous connect，效期還有 ~24 天
- devtunnel.exe：`$env:LOCALAPPDATA\Microsoft\WinGet\Packages\Microsoft.devtunnel_Microsoft.Winget.Source_8wekyb3d8bbwe\devtunnel.exe`
- 後端埠：https 7152 / http 5106；bot 匿名模式現況＝Emulator 用，填 MicrosoftAppId 後改走 Bot Framework 簽章驗證
- 服務重啟三件組：`dotnet run --project backend/KnowledgeHub.Api --launch-profile https`、`npm run dev --prefix frontend`、`devtunnel host khub.jpe1`

## 2026-08-20 RAG 檢索品質（實測評估＋前兩項改善已做）

- ✓ 實測評估：16 組查詢打真實語料（87 份文件），報告在 `docs-private\KnowledgeHub-RAG檢索評估-2026-08-20.md`。結論：向量檢索本身夠好（12/12 命中 top-2）、rerank/query rewriting 暫不需要；優先做門檻與去重
- ✓ 相似度門檻：`Retrieval:MaxDistance = 0.38`（appsettings.json），RetrievalPlugin 過濾超標 chunk＋距離寫 log 供調參，web/bot 兩管道皆接上（commit `b20c8e6`）
- ✓ DB 去重：刪 12 份 IT 重複文件（逐 chunk 內容比對一致才刪，留最新）＋1 份 Failed 殘檔；清理後 74 份文件、1,024 chunks、重複歸零
- □ 後續（按評估報告優先序）：檢索端去重保險 → 索引端清樣板雜訊/過短 chunk → 混合檢索（救代碼精確查詢）→ 視情況 rerank

## 2026-08-20「公司專用 GPT」改版（分支 feature/company-gpt-ui，待合併）

Spec：`docs/superpowers/specs/2026-08-20-company-gpt-ui-design.md`；Plan：`docs/superpowers/plans/2026-08-20-company-gpt-ui.md`。
12 任務全數完成（subagent-driven，每任務獨立審查＋最終 opus 全分支審查＋fix wave）。

- ✓ 後端對話保存：Conversation/ConversationMessage 表＋migration（已套 DB）、ConversationRepository、`/api/conversations` 系列 API（SSE 發話、清單、讀取、刪除；舊 `/api/chat` 移除）、UserKey 歸戶（Entra oid／種子 sub）
- ✓ Teams bot 多輪：接續未結束對話、「新對話」/`/new` 指令、與 web 共用對話保存（AadObjectId 歸戶互通）——小債「bot 多輪對話歷史」了結
- ✓ 前端：vue-router（/chat/:id?、/documents）、ChatGPT 式版面（側欄＋置中對話流＋手機抽屜）、Markdown 渲染（markdown-it＋DOMPurify）、品牌點綴＋RWD
- → 待辦（合併前）：品牌色確認（暫定 #e4002b，`frontend/src/style.css` 一行替換）、使用者手動驗收（清單見交付訊息；重點：登出換帳號不得殘留前人對話、Teams 對話出現在 web 側欄驗 OID 歸戶）
- 已知留存（最終審查 triage 為可留，詳見審查報告）：bot LLM 失敗會留空對話、GET 清單無分頁、刪除無二次確認、部署時需設 SPA fallback（排除 /redirect.html）等——列於 `.superpowers` ledger 與交付訊息

## 小債（沿前）

- ~~bot 多輪對話歷史~~（2026-08-20 改版了結）、整合測試隔離資料（`ChunkRepositoryTests` 兩條筆數斷言 vs 共用 dev 庫真實資料，現況 2 敗）、feature/bot-rag＋三條已合併舊分支未清
- ~~QB-PD-A1-001 在 ALL 重複兩份~~（2026-08-20 去重掃描已無此重複，了結）
