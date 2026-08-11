<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useDocuments } from '../composables/useDocuments'
import { useAuth } from '../composables/useAuth'

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
const { departments } = useAuth()
const message = ref<string | null>(null)
const isDragging = ref(false)

// 上傳範圍：全公司共用（scope=company）或所屬部門（scope=department，多部門使用者需另選部門）。
const isCompanyWide = ref(false)
const selectedDepartment = ref<string | null>(null)
watch(
  departments,
  (list) => {
    if (list.length > 0 && (!selectedDepartment.value || !list.includes(selectedDepartment.value))) {
      selectedDepartment.value = list[0]
    }
  },
  { immediate: true },
)

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

const uploading = ref(false)

async function handleFiles(files: File[]) {
  if (files.length === 0 || uploading.value) return
  message.value = null
  uploading.value = true
  const failed: string[] = []
  const scope = isCompanyWide.value ? 'company' : 'department'
  const department =
    scope === 'department' && departments.value.length > 1 ? (selectedDepartment.value ?? undefined) : undefined
  try {
    for (const file of files) {
      const invalidReason = validate(file)
      if (invalidReason) {
        failed.push(`${file.name}：${invalidReason}`)
        continue
      }
      try {
        await upload(file, { scope, department })
      } catch (e) {
        failed.push(`${file.name}：${e instanceof Error ? e.message : '上傳失敗'}`)
      }
    }
  } finally {
    uploading.value = false
  }
  if (failed.length > 0) message.value = `${failed.length} 個檔案失敗——${failed.join('；')}`
}

function onFileChange(e: Event) {
  const input = e.target as HTMLInputElement
  // 資料夾模式會夾帶非 pdf/md 的檔案，先靜默過濾再上傳
  const files = Array.from(input.files ?? []).filter((f) => hasAllowedExtension(f.name))
  handleFiles(files)
  input.value = ''
}

function hasAllowedExtension(name: string): boolean {
  const ext = name.slice(name.lastIndexOf('.')).toLowerCase()
  return ALLOWED_EXTENSIONS.includes(ext)
}

// 遞迴展開拖放進來的資料夾（webkitGetAsEntry API）
async function collectEntry(entry: FileSystemEntry): Promise<File[]> {
  if (entry.isFile) {
    const file = await new Promise<File>((resolve, reject) =>
      (entry as FileSystemFileEntry).file(resolve, reject),
    )
    return hasAllowedExtension(file.name) ? [file] : []
  }
  if (entry.isDirectory) {
    const reader = (entry as FileSystemDirectoryEntry).createReader()
    const out: File[] = []
    // readEntries 一次最多回 100 筆，要迴圈讀到空
    for (;;) {
      const batch = await new Promise<FileSystemEntry[]>((resolve, reject) =>
        reader.readEntries(resolve, reject),
      )
      if (batch.length === 0) break
      for (const child of batch) out.push(...(await collectEntry(child)))
    }
    return out
  }
  return []
}

async function onDrop(e: DragEvent) {
  isDragging.value = false
  const items = Array.from(e.dataTransfer?.items ?? [])
  const entries = items.map((i) => i.webkitGetAsEntry?.()).filter((x): x is FileSystemEntry => !!x)
  if (entries.length > 0) {
    const files = (await Promise.all(entries.map(collectEntry))).flat()
    if (files.length === 0) {
      message.value = '拖進來的內容沒有 .pdf / .md 檔案'
      return
    }
    handleFiles(files)
    return
  }
  handleFiles(Array.from(e.dataTransfer?.files ?? []).filter((f) => hasAllowedExtension(f.name)))
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

    <div class="flex flex-col gap-2 rounded border border-slate-200 p-3 text-sm">
      <label class="flex items-center gap-2">
        <input v-model="isCompanyWide" type="checkbox" />
        全公司共用
      </label>
      <label v-if="!isCompanyWide && departments.length > 1" class="flex items-center gap-2">
        部門
        <select v-model="selectedDepartment" class="rounded border border-slate-300 px-2 py-1">
          <option v-for="d in departments" :key="d" :value="d">{{ d }}</option>
        </select>
      </label>
    </div>

    <div
      class="flex flex-col items-center justify-center rounded border-2 border-dashed p-4 text-center text-sm"
      :class="isDragging ? 'border-slate-500 bg-slate-100' : 'border-slate-300 text-slate-500'"
      @dragover.prevent="isDragging = true"
      @dragleave.prevent="isDragging = false"
      @drop.prevent="onDrop"
    >
      <p>拖放檔案或整個資料夾到此處，或</p>
      <div class="mt-2 flex gap-2">
        <label class="cursor-pointer rounded bg-slate-900 px-3 py-1 text-white" :class="{ 'opacity-50': uploading }">
          {{ uploading ? '上傳中…' : '選擇檔案' }}
          <input type="file" accept=".pdf,.md" multiple class="hidden" :disabled="uploading" @change="onFileChange" />
        </label>
        <label class="cursor-pointer rounded border border-slate-400 px-3 py-1 text-slate-700" :class="{ 'opacity-50': uploading }">
          選擇資料夾
          <input type="file" webkitdirectory class="hidden" :disabled="uploading" @change="onFileChange" />
        </label>
      </div>
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
          <span v-if="doc.isCompanyWide" class="rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-700">
            全公司
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
