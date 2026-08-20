<script setup lang="ts">
import { useAuth } from './composables/useAuth'
import LoginView from './views/LoginView.vue'

const { token, noDepartmentMessage, logout } = useAuth()
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
