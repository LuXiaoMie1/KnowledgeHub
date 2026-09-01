# KnowledgeHub 進度（2026-09-01 — 真實文件大量匯入完成、Teams bot 實測通過、驗收大半辦結）

## 2026-09-01 本日進展

- ✓ **DB 復活**（免費額度 9/1 重置），三件組全數起妥
- ✓ **Teams bot 端到端通了**：卡點是 AADSTS7000229（app reg 缺 service principal），`az ad sp create --id 2629b8d6-…` 補上即通。發問／多輪／「新對話」皆實測正常。teams-app/ 已 commit（`209e596`），Bot 計畫第 9 步辦結
- ✓ **真實文件大量匯入**：一階 62 份、二階 105 份（補傳 3 份 AD-B 後齊）、四階表單改用「表單總目錄」方案。知識庫從 74 份 → 210+ 份，全 Completed
  - 刪 1 份內容重複（PD-A1-001 帶「(1)」後綴的重複下載；比對時注意**文管系統每頁蓋下載日期浮水印**，同文件不同天下載內容比對會不一致，只有浮水印日期差異即可視為重複）
  - □ 三階流程圖缺 7 份未補傳：PD-C-001/002、QA-C-001/002/003、TD-C-024/025
  - □ 表單總目錄（`docs-private\QBurger表單總目錄.md`，137/138 筆——121-135 頁最後一筆截圖被截掉待補）與營運循環總覽（`docs-private\QBurger營運循環總覽.md`，「九大」數字待使用者確認）等使用者上傳
- ✓ **新功能：.docx 上傳支援**（`f11a000`）：DocxTextExtractor（OpenXml）＋前後端白名單，測試 20/20 綠
- ✓ **兩個 bug 修掉**：側欄發話後不刷新（`2ca7e9a`，sending 提升模組層）；側欄時間差 8 小時（`18933eb`，UTC 字串無 Z 被當本地時間）
- ✓ **RAG 品質診斷方法建立**：後端 log 有每次檢索的距離分數（grep「知識庫檢索」）。實例：「9大循環」全部 chunk 距離 0.39-0.41 被 0.38 門檻擋光→回「找不到」；彙總型問題靠「總覽文件」解，不動門檻
- ✓ 同事內網測試完成（`https://qbn034.qburger.ent.com.tw:5173`）

## 驗收清單剩餘項（下次開場先做）

- □ **C1 最重要**：登出→另一帳號同一分頁登入→不得看到前人對話
- □ Teams 對話出現在 web 側欄（OID 歸戶）——bot 已通，回 web 看側欄即可驗
- □ Markdown XSS：貼 `<img src=x onerror=alert(1)>` 不得執行
- □ 手機尺寸：側欄抽屜、輸入、/documents
- ✓ https 登入（今日實測 OK）、新對話→/chat/{id}→側欄（同事測試已覆蓋）

## 三個待決（合併前）

1. 品牌色確認（暫定 #e4002b，`frontend/src/style.css` 一行替換）
2. ~~側欄排序/時間不即時更新~~（9/1 已修，`2ca7e9a`）
3. 驗收過後合併 main（分支 `feature/company-gpt-ui`，現 22 commits 已 push 部分——9/1 新增 4 commits 未 push）

## 待辦（非阻塞）

- 「回答附表單下載連結」功能——使用者問過，評估過先用表單總目錄指路頂替，真要做需下載端點＋權限
- RAG 後續（8/20 評估報告優先序）：檢索端去重保險（來源洗版）→ 索引端清樣板雜訊（**含文管浮水印行**）→ 混合檢索 → rerank
- Excel 表單（.xls/.xlsx）與 .doc 舊格式不支援，維持轉 PDF 或跳過
- 小債：`ChunkRepositoryTests` 兩條筆數斷言 vs 共用 dev 庫真實資料（現況 2 敗，測試隔離問題非功能問題）。~~已合併舊分支未清~~（9/1 四條全刪辦結）
- tunnel `khub.jpe1` 效期剩約 23 天；正式部署（離開開發機）議題未動

## 環境事實

- 三件組：`dotnet run --project backend/KnowledgeHub.Api --launch-profile https`（https 7152／http 5106）、`npm run dev --prefix frontend`（https 5173）、`devtunnel host khub.jpe1`（5106+5173）
- devtunnel.exe：`$env:LOCALAPPDATA\Microsoft\WinGet\Packages\Microsoft.devtunnel_Microsoft.Winget.Source_8wekyb3d8bbwe\devtunnel.exe`
- Azure SQL 免費層額度按月，8 月曾用罄整庫暫停到月底——本月留意用量
- 同事測試網址：`https://qbn034.qburger.ent.com.tw:5173/`（完整含 https、自簽憑證按進階繼續）
- DB 唯讀查詢：連線字串在 user-secrets（`dotnet user-secrets list --project backend/KnowledgeHub.Api`），表名 `Documents`／`DocumentChunks`／`Conversations`

> 歷史存檔：Entra／內網直連見 `docs-private\KnowledgeHub-entra設定紀錄-2026-08-10.md`；RAG 評估見 `docs-private\KnowledgeHub-RAG檢索評估-2026-08-20.md`；改版裁決見 `.superpowers/sdd/2026-08-20-company-gpt-ui/progress.md`
