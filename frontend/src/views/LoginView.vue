<script setup lang="ts">
import { ref } from 'vue'
import { useAuth } from '../composables/useAuth'

const DEMO_USERS = [
  { username: 'hr-user', label: 'HR（hr-user）' },
  { username: 'it-user', label: 'IT（it-user）' },
  { username: 'fin-user', label: 'Finance（fin-user）' },
]

const { login } = useAuth()
const username = ref(DEMO_USERS[0].username)
const password = ref('')
const error = ref<string | null>(null)
const loading = ref(false)

async function onSubmit() {
  error.value = null
  loading.value = true
  try {
    await login(username.value, password.value)
  } catch (e) {
    error.value = e instanceof Error ? e.message : '登入失敗'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="flex min-h-screen items-center justify-center bg-slate-100">
    <form
      class="w-full max-w-sm space-y-4 rounded-lg bg-white p-8 shadow"
      @submit.prevent="onSubmit"
    >
      <h1 class="text-xl font-semibold text-slate-900">KnowledgeHub 登入</h1>

      <div>
        <label class="block text-sm font-medium text-slate-700" for="username">使用者</label>
        <select
          id="username"
          v-model="username"
          class="mt-1 w-full rounded border border-slate-300 px-3 py-2"
        >
          <option v-for="u in DEMO_USERS" :key="u.username" :value="u.username">
            {{ u.label }}
          </option>
        </select>
      </div>

      <div>
        <label class="block text-sm font-medium text-slate-700" for="password">密碼</label>
        <input
          id="password"
          v-model="password"
          type="password"
          class="mt-1 w-full rounded border border-slate-300 px-3 py-2"
        />
      </div>

      <p v-if="error" class="rounded bg-red-100 px-3 py-2 text-sm text-red-700">
        {{ error }}
      </p>

      <button
        type="submit"
        :disabled="loading"
        class="w-full rounded bg-slate-900 px-3 py-2 text-white disabled:opacity-50"
      >
        {{ loading ? '登入中…' : '登入' }}
      </button>
    </form>
  </div>
</template>
