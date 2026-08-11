import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'

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
    // Dev Tunnel（同事跨機測試用）：tunnel 轉發進來的請求 Host 是 *.devtunnels.ms，
    // 要列入允許清單，否則 Vite dev server 會擋（403）。API 不用另外開——同事的
    // /api 請求由這台機器的 Vite 代理到本機後端（下方 proxy 設定）。
    allowedHosts: ['.devtunnels.ms'],
    proxy: {
      '/api': {
        target: 'https://localhost:7152',
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
