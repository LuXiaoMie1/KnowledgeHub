import { ref } from 'vue'
import { useAuth } from './useAuth'

export interface Source { fileName: string; sequenceNumber: number; content: string; distance: number }
export interface ChatMessage { role: 'user' | 'assistant'; content: string; sources: Source[]; error: string | null }

// 模組層保存目前這次串流的 AbortController：元件卸載（例如登出）時 cancel() 要能中止還在跑的 fetch，
// 避免免費層 LLM 配額被已經沒人看的畫面繼續燒掉。
let controller: AbortController | null = null

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
    messages.value.push({ role: 'assistant', content: '', sources: [], error: null })
    // 從 messages.value 讀回剛推入的物件，取得的是 Vue 包過的 reactive proxy；
    // 若改用推入前的原始物件字面量直接 mutate（reply.content += ...），mutation 繞過 proxy 的 set trap，
    // 不會觸發畫面重繪，只有等其他真正被追蹤的 ref（例如 sending）變動時才會「一次補畫全部內容」，
    // 打字機效果會失效（最終內容仍正確，但逐字顯示的動畫不會發生）。
    const reply = messages.value[messages.value.length - 1]

    controller = new AbortController()

    try {
      const res = await fetch('/api/chat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...authHeader() },
        body: JSON.stringify({ message: text, history }),
        signal: controller.signal,
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
      // 使用者主動取消（cancel()／元件卸載）造成的 AbortError 是預期行為，不是連線失敗，靜默處理即可。
      if (!(e instanceof DOMException && e.name === 'AbortError')) {
        reply.error = e instanceof Error ? e.message : '連線失敗'
      }
    } finally {
      sending.value = false
      controller = null
    }
  }

  function cancel(): void {
    controller?.abort()
  }

  function handleEvent(block: string, reply: ChatMessage) {
    const event = /^event: (.+)$/m.exec(block)?.[1]
    const data = /^data: (.+)$/m.exec(block)?.[1]
    if (!event || !data) return
    if (event === 'token') reply.content += JSON.parse(data).text
    else if (event === 'sources') reply.sources = JSON.parse(data)
    else if (event === 'error') reply.error = JSON.parse(data).message
  }

  return { messages, sending, send, cancel }
}
