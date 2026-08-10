<script setup lang="ts">
import { useAuth } from './composables/useAuth'
import LoginView from './views/LoginView.vue'
import DocumentPanel from './components/DocumentPanel.vue'
import ChatPanel from './components/ChatPanel.vue'

const { token, department, noDepartmentMessage, logout } = useAuth()
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
  <div v-else class="flex h-screen flex-col">
    <header class="flex items-center justify-between border-b border-slate-200 px-6 py-3">
      <h1 class="text-lg font-semibold text-slate-900">KnowledgeHub</h1>
      <div class="flex items-center gap-3">
        <span class="rounded-full bg-slate-900 px-3 py-1 text-xs font-medium text-white">
          {{ department }}
        </span>
        <button
          class="rounded border border-slate-300 px-3 py-1 text-sm text-slate-700 hover:bg-slate-100"
          @click="logout"
        >
          登出
        </button>
      </div>
    </header>
    <main class="flex flex-1 overflow-hidden">
      <aside class="w-80 shrink-0 border-r border-slate-200 p-4">
        <DocumentPanel />
      </aside>
      <section class="flex-1 overflow-hidden">
        <ChatPanel />
      </section>
    </main>
  </div>
</template>
