<script setup lang="ts">
import { useAuth } from '../composables/useAuth'
import { useConversations } from '../composables/useConversations'

defineProps<{ activeId: string | null }>()
const emit = defineEmits<{ select: [id: string]; new: []; deleted: [id: string] }>()

const { department, departments, logout } = useAuth()
const { list, remove } = useConversations()

const rtf = new Intl.RelativeTimeFormat('zh-TW', { numeric: 'auto' })

/** 側欄相對時間（spec §6）：7 天內用「幾分鐘/小時/天前」，超過 7 天顯示日期。 */
function formatRelative(updatedAtUtc: string): string {
  const date = new Date(updatedAtUtc)
  const diffMs = date.getTime() - Date.now()
  const diffDay = diffMs / 86_400_000
  if (Math.abs(diffDay) >= 7) return date.toLocaleDateString('zh-TW')
  if (Math.abs(diffDay) >= 1) return rtf.format(Math.round(diffDay), 'day')
  const diffHour = diffMs / 3_600_000
  if (Math.abs(diffHour) >= 1) return rtf.format(Math.round(diffHour), 'hour')
  const diffMin = diffMs / 60_000
  return rtf.format(Math.round(diffMin), 'minute')
}

async function onDelete(id: string) {
  if (await remove(id)) emit('deleted', id)
}
</script>

<template>
  <div class="flex h-full flex-col bg-slate-50">
    <div class="space-y-3 p-3">
      <p class="flex items-center gap-1.5 px-1 text-sm font-semibold text-slate-900">
        <span class="text-brand">●</span> KnowledgeHub
      </p>
      <button
        class="w-full rounded-lg bg-brand px-3 py-2 text-left text-sm font-medium text-white hover:bg-brand-hover"
        @click="emit('new')"
      >
        ＋ 新對話
      </button>
    </div>
    <nav class="flex-1 space-y-0.5 overflow-y-auto px-2">
      <div
        v-for="c in list"
        :key="c.id"
        class="group flex items-center gap-1 rounded-lg px-2 py-2 text-sm hover:bg-slate-200"
        :class="c.id === activeId ? 'bg-slate-200 font-medium' : 'text-slate-700'"
      >
        <button class="min-w-0 flex-1 text-left" @click="emit('select', c.id)">
          <span class="block truncate">{{ c.title }}</span>
          <span class="block truncate text-xs text-slate-400">{{ formatRelative(c.updatedAtUtc) }}</span>
        </button>
        <span
          v-if="c.channel === 'teams'"
          class="shrink-0 rounded bg-indigo-100 px-1 text-[10px] text-indigo-700"
          >Teams</span
        >
        <button
          class="block shrink-0 text-slate-400 hover:text-red-600 md:hidden md:group-hover:block"
          title="刪除對話"
          @click="onDelete(c.id)"
        >
          ✕
        </button>
      </div>
    </nav>
    <div class="border-t border-slate-200 p-3 text-sm">
      <p class="truncate font-medium text-slate-900">
        {{ departments.length > 0 ? departments.join('、') : department }}
      </p>
      <div class="mt-2 flex items-center justify-between">
        <router-link to="/documents" class="text-slate-600 hover:text-slate-900">文件管理</router-link>
        <button class="text-slate-600 hover:text-slate-900" @click="logout">登出</button>
      </div>
    </div>
  </div>
</template>
