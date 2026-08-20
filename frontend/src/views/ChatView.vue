<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ConversationSidebar from '../components/ConversationSidebar.vue'
import ChatPanel from '../components/ChatPanel.vue'
import { useChat } from '../composables/useChat'
import { useConversations } from '../composables/useConversations'

const route = useRoute()
const router = useRouter()
const { conversationId, open, reset } = useChat()
const { load } = useConversations()
const drawerOpen = ref(false)

onMounted(load)

// 網址 → 對話：進 /chat/:id 載入該對話；進 /chat 清空
// open() 回傳 false 代表這條 id 本身失敗（404／被刪／非本人，useChat 內部已 reset），
// 網址不能停在壞 id，改回 /chat；回傳 true 則涵蓋成功套用與「已被更新的切換取代」兩種情況，不必處理。
watch(
  () => route.params.id,
  async (id) => {
    if (typeof id === 'string' && id) {
      if (id !== conversationId.value) {
        const ok = await open(id)
        if (!ok) router.replace('/chat')
      }
    } else {
      reset()
    }
  },
  { immediate: true },
)

// 對話 → 網址：新對話拿到 id 後補網址並刷新側欄
watch(conversationId, (id) => {
  if (id && route.params.id !== id) {
    router.replace(`/chat/${id}`)
    load()
  }
})

function onNew() {
  drawerOpen.value = false
  router.push('/chat')
}

function onSelect(id: string) {
  drawerOpen.value = false
  router.push(`/chat/${id}`)
}

function onDeleted(id: string) {
  if (conversationId.value === id) router.push('/chat')
}
</script>

<template>
  <div class="flex h-screen overflow-hidden">
    <!-- 桌機常駐側欄 -->
    <aside class="hidden w-64 shrink-0 border-r border-slate-200 md:block">
      <ConversationSidebar :active-id="conversationId" @new="onNew" @select="onSelect" @deleted="onDeleted" />
    </aside>

    <!-- 手機抽屜 -->
    <div v-if="drawerOpen" class="fixed inset-0 z-20 md:hidden">
      <div class="absolute inset-0 bg-black/30" @click="drawerOpen = false"></div>
      <aside class="absolute inset-y-0 left-0 w-72 bg-slate-50 shadow-xl">
        <ConversationSidebar :active-id="conversationId" @new="onNew" @select="onSelect" @deleted="onDeleted" />
      </aside>
    </div>

    <main class="flex min-w-0 flex-1 flex-col">
      <header class="flex items-center gap-3 border-b border-slate-200 px-4 py-2 md:hidden">
        <button class="text-slate-600" aria-label="開啟選單" @click="drawerOpen = true">☰</button>
        <h1 class="text-base font-semibold text-slate-900">KnowledgeHub</h1>
      </header>
      <ChatPanel class="min-h-0 flex-1" />
    </main>
  </div>
</template>
