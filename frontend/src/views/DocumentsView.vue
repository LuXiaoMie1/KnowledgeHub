<script setup lang="ts">
import DocumentPanel from '../components/DocumentPanel.vue'
</script>

<template>
  <div class="min-h-screen bg-slate-50">
    <header class="flex items-center justify-between border-b border-slate-200 bg-white px-6 py-3">
      <router-link to="/chat" class="text-sm text-slate-600 hover:text-slate-900">← 回對話</router-link>
      <h1 class="text-lg font-semibold text-slate-900">文件管理</h1>
      <span class="w-16"></span>
    </header>
    <main class="mx-auto max-w-3xl p-6">
      <div class="rounded-lg border border-slate-200 bg-white p-4">
        <DocumentPanel />
      </div>
    </main>
  </div>
</template>
