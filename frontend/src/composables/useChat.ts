import { ref } from 'vue'
import { useAuth } from './useAuth'

export interface Source { fileName: string; sequenceNumber: number; content: string; distance: number }
export interface ChatMessage { role: 'user' | 'assistant'; content: string; sources: Source[]; error: string | null }

export function useChat() {
  const { authHeader } = useAuth()
  const messages = ref<ChatMessage[]>([])
  const sending = ref(false)

  async function send(text: string): Promise<void> {
    sending.value = true
    const history = messages.value
      .filter((m) => !m.error)
      .map((m) => ({ role: m.role, content: m.content }))
    messages.value.push({ role: 'user', content: text, sources: [], error: null })
    const reply: ChatMessage = { role: 'assistant', content: '', sources: [], error: null }
    messages.value.push(reply)

    try {
      const res = await fetch('/api/chat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...authHeader() },
        body: JSON.stringify({ message: text, history }),
      })
      if (!res.ok || !res.body) throw new Error(`HTTP ${res.status}`)

      const reader = res.body.pipeThrough(new TextDecoderStream()).getReader()
      let buffer = ''
      while (true) {
        const { value, done } = await reader.read()
        if (done) break
        buffer += value
        let sep: number
        while ((sep = buffer.indexOf('\n\n')) >= 0) {
          handleEvent(buffer.slice(0, sep), reply)
          buffer = buffer.slice(sep + 2)
        }
      }
    } catch (e) {
      reply.error = e instanceof Error ? e.message : '連線失敗'
    } finally {
      sending.value = false
    }
  }

  function handleEvent(block: string, reply: ChatMessage) {
    const event = /^event: (.+)$/m.exec(block)?.[1]
    const data = /^data: (.+)$/m.exec(block)?.[1]
    if (!event || !data) return
    if (event === 'token') reply.content += JSON.parse(data).text
    else if (event === 'sources') reply.sources = JSON.parse(data)
    else if (event === 'error') reply.error = JSON.parse(data).message
  }

  return { messages, sending, send }
}
