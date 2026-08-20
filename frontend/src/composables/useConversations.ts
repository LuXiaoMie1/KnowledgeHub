import { ref } from 'vue'
import { useAuth } from './useAuth'

export interface ConversationSummary {
  id: string; title: string; channel: string; updatedAtUtc: string
}

// 模組層單例：側欄與 ChatView 共享同一份清單（照 useAuth/useChat 既有慣例）
const list = ref<ConversationSummary[]>([])

export function useConversations() {
  const { authHeader } = useAuth()

  async function load(): Promise<void> {
    const res = await fetch('/api/conversations', { headers: authHeader() })
    if (res.ok) list.value = await res.json()
  }

  async function remove(id: string): Promise<boolean> {
    const res = await fetch(`/api/conversations/${id}`, { method: 'DELETE', headers: authHeader() })
    if (res.ok) list.value = list.value.filter((c) => c.id !== id)
    return res.ok
  }

  /** 登出時清空側欄清單，避免下一位登入者在清單重新載入前看到前一位使用者的對話標題。 */
  function clear(): void {
    list.value = []
  }

  return { list, load, remove, clear }
}
