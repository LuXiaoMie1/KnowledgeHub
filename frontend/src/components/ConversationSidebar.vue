<script setup lang="ts">
import { useAuth } from '../composables/useAuth'
import { useConversations } from '../composables/useConversations'

defineProps<{ activeId: string | null }>()
const emit = defineEmits<{ select: [id: string]; new: []; deleted: [id: string] }>()

const { department, departments, logout } = useAuth()
const { list, remove } = useConversations()

async function onDelete(id: string) {
  if (await remove(id)) emit('deleted', id)
}
</script>

<template>
  <div class="flex h-full flex-col bg-slate-50">
    <div class="p-3">
      <button
        class="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-left text-sm font-medium text-slate-900 hover:bg-slate-100"
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
        <button class="min-w-0 flex-1 truncate text-left" @click="emit('select', c.id)">
          {{ c.title }}
        </button>
        <span
          v-if="c.channel === 'teams'"
          class="shrink-0 rounded bg-indigo-100 px-1 text-[10px] text-indigo-700"
          >Teams</span
        >
        <button
          class="hidden shrink-0 text-slate-400 hover:text-red-600 group-hover:block"
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
