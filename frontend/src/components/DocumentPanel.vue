<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useDocuments } from '../composables/useDocuments'

const ALLOWED_EXTENSIONS = ['.pdf', '.md']
const MAX_BYTES = 20 * 1024 * 1024

const STATUS_LABEL: Record<string, string> = {
  Pending: '等待中',
  Processing: '處理中',
  Completed: '已完成',
  Failed: '失敗',
}
const STATUS_BADGE_CLASS: Record<string, string> = {
  Pending: 'bg-slate-200 text-slate-700',
  Processing: 'bg-blue-100 text-blue-700',
  Completed: 'bg-green-100 text-green-700',
  Failed: 'bg-red-100 text-red-700',
}

const { documents, pollError, load, upload, remove } = useDocuments()
const message = ref<string | null>(null)
const isDragging = ref(false)

onMounted(async () => {
  try {
    await load()
  } catch (e) {
    message.value = e instanceof Error ? e.message : '讀取文件清單失敗'
  }
})

function validate(file: File): string | null {
  const ext = file.name.slice(file.name.lastIndexOf('.')).toLowerCase()
  if (!ALLOWED_EXTENSIONS.includes(ext)) return '只接受 .pdf 或 .md 檔案'
  if (file.size > MAX_BYTES) return '檔案不可超過 20MB'
  return null
}

async function handleFile(file: File) {
  message.value = null
  const invalidReason = validate(file)
  if (invalidReason) {
    message.value = invalidReason
    return
  }
  try {
    await upload(file)
  } catch (e) {
    message.value = e instanceof Error ? e.message : '上傳失敗'
  }
}

function onFileChange(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  if (file) handleFile(file)
  input.value = ''
}

function onDrop(e: DragEvent) {
  isDragging.value = false
  const file = e.dataTransfer?.files?.[0]
  if (file) handleFile(file)
}

async function onDelete(id: string) {
  if (!confirm('確定要刪除此文件？')) return
  message.value = null
  try {
    await remove(id)
  } catch (e) {
    message.value = e instanceof Error ? e.message : '刪除失敗'
  }
}
</script>

<template>
  <div class="flex h-full flex-col gap-3">
    <p v-if="message" class="rounded bg-red-100 px-3 py-2 text-sm text-red-700">{{ message }}</p>
    <p v-if="pollError" class="rounded bg-red-100 px-3 py-2 text-sm text-red-700">{{ pollError }}</p>

    <div
      class="flex flex-col items-center justify-center rounded border-2 border-dashed p-4 text-center text-sm"
      :class="isDragging ? 'border-slate-500 bg-slate-100' : 'border-slate-300 text-slate-500'"
      @dragover.prevent="isDragging = true"
      @dragleave.prevent="isDragging = false"
      @drop.prevent="onDrop"
    >
      <p>拖放檔案到此處，或</p>
      <label class="mt-2 cursor-pointer rounded bg-slate-900 px-3 py-1 text-white">
        選擇檔案
        <input type="file" accept=".pdf,.md" class="hidden" @change="onFileChange" />
      </label>
      <p class="mt-1 text-xs text-slate-400">僅接受 .pdf / .md，20MB 以內</p>
    </div>

    <ul class="flex-1 space-y-2 overflow-y-auto">
      <li v-if="documents.length === 0" class="text-sm text-slate-400">尚無文件</li>
      <li v-for="doc in documents" :key="doc.id" class="rounded border border-slate-200 p-3 text-sm">
        <div class="flex items-center justify-between gap-2">
          <span class="truncate font-medium text-slate-900" :title="doc.fileName">{{ doc.fileName }}</span>
          <button class="shrink-0 text-xs text-red-600 hover:underline" @click="onDelete(doc.id)">刪除</button>
        </div>
        <div class="mt-1 flex items-center gap-2">
          <span
            class="rounded-full px-2 py-0.5 text-xs font-medium"
            :class="STATUS_BADGE_CLASS[doc.status]"
          >
            {{ STATUS_LABEL[doc.status] }}
          </span>
          <span class="text-xs text-slate-500">{{ doc.chunkCount }} 段落</span>
        </div>
        <p v-if="doc.status === 'Failed' && doc.errorMessage" class="mt-1 text-xs text-red-600">
          {{ doc.errorMessage }}
        </p>
      </li>
    </ul>
  </div>
</template>
