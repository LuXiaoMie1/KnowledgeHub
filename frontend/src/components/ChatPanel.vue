<script setup lang="ts">
import { nextTick, onUnmounted, ref, watch } from 'vue'
import { useChat, type ChatMessage } from '../composables/useChat'
import MarkdownContent from './MarkdownContent.vue'
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

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault()
    onSubmit()
  }
}

function autoGrow(e: Event) {
  const el = e.target as HTMLTextAreaElement
  el.style.height = 'auto'
  el.style.height = `${Math.min(el.scrollHeight, 200)}px`
}
</script>

<template>
  <div class="flex h-full flex-col">
    <div ref="listEl" class="flex-1 overflow-y-auto">
      <div class="mx-auto max-w-3xl space-y-6 px-4 py-6">
        <p v-if="messages.length === 0" class="pt-16 text-center text-slate-400">
          有什麼想問公司知識庫的？
        </p>
        <div v-for="(m, i) in messages" :key="i">
          <!-- 使用者：右側泡泡 -->
          <div v-if="m.role === 'user'" class="flex justify-end">
            <div class="max-w-[85%] whitespace-pre-wrap rounded-2xl bg-slate-900 px-4 py-2 text-sm text-white">
              {{ m.content }}
            </div>
          </div>
          <!-- assistant：無泡泡直排版 -->
          <div v-else class="space-y-2">
            <div v-if="m.content" class="overflow-x-auto text-sm text-slate-900">
              <MarkdownContent :content="m.content" />
              <span v-if="isStreaming(m, i)" class="animate-pulse">▍</span>
            </div>
            <p v-else-if="isStreaming(m, i)" class="text-sm text-slate-400">思考中…</p>
            <div v-if="m.error" class="rounded-lg bg-red-100 px-3 py-2 text-sm text-red-700">
              {{ m.error }}
            </div>
            <div v-if="m.sources.length > 0" class="space-y-1">
              <SourceCard v-for="(s, si) in m.sources" :key="si" :source="s" />
            </div>
          </div>
        </div>
      </div>
    </div>

    <form class="border-t border-slate-200 bg-white" @submit.prevent="onSubmit">
      <div class="mx-auto flex max-w-3xl items-end gap-2 px-4 py-3">
        <textarea
          v-model="input"
          rows="1"
          placeholder="輸入問題…（Enter 送出，Shift+Enter 換行）"
          class="max-h-[200px] flex-1 resize-none rounded-xl border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand"
          :disabled="sending"
          @keydown="onKeydown"
          @input="autoGrow"
        ></textarea>
        <button
          type="submit"
          :disabled="sending || !input.trim()"
          class="rounded-xl bg-brand px-4 py-2 text-sm text-white hover:bg-brand-hover disabled:opacity-50"
        >
          送出
        </button>
      </div>
    </form>
  </div>
</template>
