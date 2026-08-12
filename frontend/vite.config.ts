import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import fs from 'node:fs'

// 內網跨機測試：.certs/ 下有自簽憑證（簽給本機的公司網域名稱）就開 https 並監聽內網，
// 讓同事直連 https://<主機名>.qburger.ent.com.tw:5173。沒有憑證時維持原本 localhost http。
const useHttps = fs.existsSync('.certs/key.pem') && fs.existsSync('.certs/cert.pem')

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue(), tailwindcss()],
  build: {
    rollupOptions: {
      // redirect.html 是 MSAL popup 的回跳頁（第二個進入點），要跑 redirect-bridge
      // 的 module script，所以放專案根目錄讓 Vite 轉譯，不能放 public/ 當靜態檔。
      input: {
        main: 'index.html',
        redirect: 'redirect.html',
      },
    },
  },
  server: {
    // Dev Tunnel 與內網直連（同事跨機測試用）：外部轉發／直連進來的請求 Host 是
    // *.devtunnels.ms 或 *.qburger.ent.com.tw，要列入允許清單，否則 Vite dev server
    // 會擋（403）。API 不用另外開——同事的 /api 請求由這台機器的 Vite 代理到本機
    // 後端（下方 proxy 設定）。
    host: useHttps ? true : undefined,
    https: useHttps
      ? { key: fs.readFileSync('.certs/key.pem'), cert: fs.readFileSync('.certs/cert.pem') }
      : undefined,
    allowedHosts: ['.devtunnels.ms', '.qburger.ent.com.tw'],
    proxy: {
      '/api': {
        target: 'https://localhost:7152',
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
