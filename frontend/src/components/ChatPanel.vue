<script setup lang="ts">
import { nextTick, onUnmounted, ref, watch } from 'vue'
import { useChat, type ChatMessage } from '../composables/useChat'
import SourceCard from './SourceCard.vue'

const { messages, sending, send, cancel } = useChat()
const input = ref('')
const listEl = ref<HTMLElement | null>(null)

onUnmounted(cancel)

watch(
  messages,
  async () => {
    await nextTick()
    if (listEl.value) listEl.value.scrollTop = listEl.value.scrollHeight
  },
  { deep: true },
)

function isStreaming(m: ChatMessage, i: number): boolean {
  return m.role === 'assistant' && sending.value && i === messages.value.length - 1 && !m.error
}

async function onSubmit() {
  const text = input.value.trim()
  if (!text || sending.value) return
  input.value = ''
  await send(text)
}
</script>

<template>
  <div class="flex h-full flex-col">
    <div ref="listEl" class="flex-1 space-y-3 overflow-y-auto p-4">
      <p v-if="messages.length === 0" class="text-sm text-slate-400">開始提問吧</p>
      <div
        v-for="(m, i) in messages"
        :key="i"
        class="flex flex-col gap-2"
        :class="m.role === 'user' ? 'items-end' : 'items-start'"
      >
        <div
          v-if="m.content"
          class="max-w-[75%] whitespace-pre-wrap rounded-lg px-3 py-2 text-sm"
          :class="m.role === 'user' ? 'bg-slate-900 text-white' : 'bg-slate-100 text-slate-900'"
        >
          {{ m.content }}<span v-if="isStreaming(m, i)" class="animate-pulse">▍</span>
        </div>
        <div
          v-else-if="isStreaming(m, i)"
          class="max-w-[75%] rounded-lg bg-slate-100 px-3 py-2 text-sm text-slate-400"
        >
          思考中…
        </div>
        <div v-if="m.error" class="max-w-[75%] rounded-lg bg-red-100 px-3 py-2 text-sm text-red-700">
          {{ m.error }}
        </div>
        <div v-if="m.sources.length > 0" class="w-full max-w-[75%] space-y-1">
          <SourceCard v-for="(s, si) in m.sources" :key="si" :source="s" />
        </div>
      </div>
    </div>

    <form class="flex gap-2 border-t border-slate-200 p-3" @submit.prevent="onSubmit">
      <input
        v-model="input"
        type="text"
        placeholder="輸入問題…"
        class="flex-1 rounded border border-slate-300 px-3 py-2 text-sm"
        :disabled="sending"
      />
      <button
        type="submit"
        :disabled="sending || !input.trim()"
        class="rounded bg-slate-900 px-4 py-2 text-sm text-white disabled:opacity-50"
      >
        送出
      </button>
    </form>
  </div>
</template>
