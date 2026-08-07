<script setup lang="ts">
import { useAuth } from './composables/useAuth'
import LoginView from './views/LoginView.vue'

const { token, department, logout } = useAuth()
</script>

<template>
  <LoginView v-if="!token" />
  <div v-else class="flex min-h-screen flex-col">
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
    <main class="flex flex-1">
      <aside class="w-64 border-r border-slate-200 p-4 text-sm text-slate-500">
        文件清單（待 Task 13）
      </aside>
      <section class="flex-1 p-4 text-sm text-slate-500">聊天視窗（待 Task 14）</section>
    </main>
  </div>
</template>
