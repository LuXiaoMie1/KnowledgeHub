import { ref } from 'vue'
import { useAuth } from './useAuth'

export interface Source { fileName: string; sequenceNumber: number; content: string; distance: number }
export interface ChatMessage { role: 'user' | 'assistant'; content: string; sources: Source[]; error: string | null }

// 模組層保存目前這次串流的 AbortController：元件卸載（例如登出）時 cancel() 要能中止還在跑的 fetch，
// 避免免費層 LLM 配額被已經沒人看的畫面繼續燒掉。
let controller: AbortController | null = null

// 模組層單例：目前開啟的對話（null＝新對話尚未建立）
const messages = ref<ChatMessage[]>([])
const conversationId = ref<string | null>(null)

// 每次 open()/reset() 遞增的世代號：連續切換對話時，晚啟動、晚回來的 fetch 若已不是最新一次
// 呼叫（generation 對不上），代表使用者已經切到別的對話，結果直接丟棄，不寫入 state。
let generation = 0

export function useChat() {
  const { authHeader, checkNoDepartment } = useAuth()
  const sending = ref(false)

  /**
   * 載入既有對話（側欄點選／網址帶 id 進入）。
   * 回傳 false＝這次呼叫本身失敗（404，被刪或非本人，已 reset 回空白新對話）；
   * 回傳 true＝成功套用，或本次呼叫被更新的 open()/reset() 取代（呼叫端不必視為失敗處理）。
   */
  async function open(id: string): Promise<boolean> {
    cancel()
    const gen = ++generation
    try {
      const res = await fetch(`/api/conversations/${id}`, { headers: authHeader() })
      if (gen !== generation) return true // 已被更新的呼叫取代，結果作廢
      if (!res.ok) { reset(); return false }
      const rows: { role: 'user' | 'assistant'; content: string; sourcesJson: string | null }[] =
        await res.json()
      if (gen !== generation) return true // 已被更新的呼叫取代，結果作廢
      conversationId.value = id
      messages.value = rows.map((r) => ({
        role: r.role, content: r.content, error: null,
        sources: r.sourcesJson ? JSON.parse(r.sourcesJson) : [],
      }))
      return true
    } catch {
      // fetch 被拒絕（斷線）或 JSON 解析失敗：視同這次呼叫失敗，reset 回空白新對話，
      // 讓呼叫端（ChatView 的網址 watcher）走既有的「回退到 /chat」路徑。
      reset()
      return false
    }
  }

  function reset(): void {
    cancel()
    generation++
    conversationId.value = null
    messages.value = []
  }

  async function send(text: string): Promise<void> {
    sending.value = true
    messages.value.push({ role: 'user', content: text, sources: [], error: null })
    messages.value.push({ role: 'assistant', content: '', sources: [], error: null })
    // 從 messages.value 讀回剛推入的物件，取得的是 Vue 包過的 reactive proxy；
    // 若改用推入前的原始物件字面量直接 mutate（reply.content += ...），mutation 繞過 proxy 的 set trap，
    // 不會觸發畫面重繪，只有等其他真正被追蹤的 ref（例如 sending）變動時才會「一次補畫全部內容」，
    // 打字機效果會失效（最終內容仍正確，但逐字顯示的動畫不會發生）。
    const reply = messages.value[messages.value.length - 1]

    controller = new AbortController()

    try {
      const res = await fetch('/api/conversations/messages', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...authHeader() },
        body: JSON.stringify({ conversationId: conversationId.value, message: text }),
        signal: controller.signal,
      })
      if (!res.ok) {
        if (await checkNoDepartment(res)) return
        throw new Error(`HTTP ${res.status}`)
      }
      if (!res.body) throw new Error(`HTTP ${res.status}`)

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
    if (event === 'conversation') conversationId.value = JSON.parse(data).id
    else if (event === 'token') reply.content += JSON.parse(data).text
    else if (event === 'sources') reply.sources = JSON.parse(data)
    else if (event === 'error') reply.error = JSON.parse(data).message
  }

  return { messages, sending, conversationId, send, open, reset, cancel }
}
