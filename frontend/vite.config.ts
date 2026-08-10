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
    proxy: {
      '/api': {
        target: 'https://localhost:7152',
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
