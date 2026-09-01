<script setup lang="ts">
import { watch } from 'vue'
import { useAuth } from './composables/useAuth'
import { useChat } from './composables/useChat'
import { useConversations } from './composables/useConversations'
import LoginView from './views/LoginView.vue'

const { token, noDepartmentMessage, logout } = useAuth()
const { reset: resetChat } = useChat()
const { clear: clearConversations } = useConversations()

// 登出（token 變 falsy）時清空模組層的對話狀態與側欄清單，避免下一位在同分頁登入的
// 使用者看到前一位使用者殘留的對話內容（機密性回歸：useChat/useConversations 是模組層
// 單例，useAuth 的登出原本只清 token，不會自動清掉這兩份狀態）。
watch(token, (t) => {
  if (!t) {
    resetChat()
    clearConversations()
  }
})
</script>

<template>
  <LoginView v-if="!token" />
  <div
    v-else-if="noDepartmentMessage"
    class="flex h-screen flex-col items-center justify-center gap-4 bg-slate-100 px-6 text-center"
  >
    <p class="max-w-md text-slate-700">{{ noDepartmentMessage }}</p>
    <button class="rounded bg-slate-900 px-4 py-2 text-white" @click="logout">返回登入頁</button>
  </div>
  <router-view v-else />
</template>
