<script setup lang="ts">
import { ref } from 'vue'
import { useAuth } from '../composables/useAuth'

const { login, loginWithEntra, entraEnabled } = useAuth()
const username = ref('')
const password = ref('')
const error = ref<string | null>(null)
const loading = ref(false)
const entraLoading = ref(false)

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

async function onEntraLogin() {
  error.value = null
  entraLoading.value = true
  try {
    await loginWithEntra()
  } catch (e) {
    error.value = e instanceof Error ? e.message : '公司帳號登入失敗'
  } finally {
    entraLoading.value = false
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
        <input
          id="username"
          v-model="username"
          type="text"
          autocomplete="username"
          class="mt-1 w-full rounded border border-slate-300 px-3 py-2"
        />
      </div>

      <div>
        <label class="block text-sm font-medium text-slate-700" for="password">密碼</label>
        <input
          id="password"
          v-model="password"
          type="password"
          autocomplete="current-password"
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

      <template v-if="entraEnabled">
        <div class="flex items-center gap-2 text-xs text-slate-400">
          <span class="h-px flex-1 bg-slate-200" />
          或
          <span class="h-px flex-1 bg-slate-200" />
        </div>

        <button
          type="button"
          :disabled="entraLoading"
          class="w-full rounded border border-slate-300 px-3 py-2 text-slate-700 hover:bg-slate-100 disabled:opacity-50"
          @click="onEntraLogin"
        >
          {{ entraLoading ? '登入中…' : '使用公司帳號登入' }}
        </button>
      </template>
    </form>
  </div>
</template>
