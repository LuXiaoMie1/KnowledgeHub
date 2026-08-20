<script setup lang="ts">
import { ref } from 'vue'
import type { Source } from '../composables/useChat'

defineProps<{ source: Source }>()
const expanded = ref(false)
</script>

<template>
  <div
    class="cursor-pointer rounded border p-2 text-xs transition-colors"
    :class="expanded ? 'border-slate-500 bg-slate-50' : 'border-slate-200 bg-white hover:bg-slate-50'"
    @click="expanded = !expanded"
  >
    <div class="flex items-center justify-between gap-2 text-slate-600">
      <span class="min-w-0 flex-1 truncate font-medium" :title="source.fileName">
        {{ source.fileName }} 第{{ source.sequenceNumber }}段
      </span>
      <span class="shrink-0 text-slate-400">{{ expanded ? '收合' : '展開' }}</span>
    </div>
    <p v-if="expanded" class="mt-2 whitespace-pre-wrap text-slate-700">{{ source.content }}</p>
  </div>
</template>
