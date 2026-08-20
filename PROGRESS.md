# KnowledgeHub 進度（2026-08-18 存檔 — Teams 整合只剩「上傳 app」最後一步）

## 下次開場（接續點）

1. **重啟兩件**（session 結束背景服務已停）：
   - 後端：`dotnet run --project backend/KnowledgeHub.Api --launch-profile https`（等 Azure SQL 冷啟動 30–60s，看到 Now listening 才算好）
   - tunnel：`& "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\Microsoft.devtunnel_Microsoft.Winget.Source_8wekyb3d8bbwe\devtunnel.exe" host khub.jpe1`
   - （前端 vite 與 Teams bot 無關，測 bot 不用起）
2. **上傳 Teams app**：使用者 8/18 第一次上傳誤走「提交組織核准」流程（擱置中，可按垃圾桶刪）。正路＝Teams→應用程式→管理您的應用程式→上傳應用程式→選「**上傳自訂應用程式**」（不用核准）→ `teams-app\knowledgehub-teams-app.zip`。若沒有這選項＝sideload 政策沒套用，先登出重登 Teams，再不行查管理中心政策指派（KnowledgeHub-Dev-Sideload）
3. 上傳成功後對 bot 發問實測（單輪、鎖 ALL 文件）。若 bot 沒回，看後端 log：401「No Authorization header」以外的錯才是真問題
4. 通了之後收尾：teams-app/ commit 進 repo（appId 非機密可進公開 repo）、交接檔更新


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
- → 8b. **使用者上傳 zip**（Teams→應用程式→管理您的應用程式→上傳應用程式→選 `teams-app\knowledgehub-teams-app.zip`）→ 對 bot 發問實測
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
