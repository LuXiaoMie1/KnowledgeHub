import { ref, onUnmounted } from 'vue'
import { useAuth } from './useAuth'

export interface DocumentInfo {
  id: string
  fileName: string
  status: 'Pending' | 'Processing' | 'Completed' | 'Failed'
  chunkCount: number
  errorMessage: string | null
  uploadedAtUtc: string
}

const MAX_CONSECUTIVE_POLL_FAILURES = 3

/** 非 2xx 回應嘗試解析後端 { error } JSON；沒有 JSON body（例如 500）就退回固定訊息，不讓 SyntaxError 冒出去。 */
async function safeError(res: Response, fallback: string): Promise<string> {
  try {
    const body = await res.json()
    if (body && typeof body.error === 'string') return body.error
  } catch {
    // body 不是合法 JSON，走 fallback
  }
  return `${fallback}（HTTP ${res.status}）`
}

export function useDocuments() {
  const { authHeader, checkNoDepartment } = useAuth()
  const documents = ref<DocumentInfo[]>([])
  const pollError = ref<string | null>(null)
  let timer: number | undefined
  let consecutiveFailures = 0

  async function load(): Promise<void> {
    const res = await fetch('/api/documents', { headers: authHeader() })
    if (!res.ok) {
      if (await checkNoDepartment(res)) return
      throw new Error(await safeError(res, '讀取文件清單失敗'))
    }
    documents.value = await res.json()
    consecutiveFailures = 0
    pollError.value = null
    syncPolling()
  }

  function stopPolling() {
    if (timer !== undefined) {
      clearInterval(timer)
      timer = undefined
    }
  }

  /** setInterval 掛的包裝函式：吞掉 load() 的例外，連續失敗達上限才停止輪詢並留訊息，避免無限重試＋未處理 rejection。 */
  async function poll(): Promise<void> {
    try {
      await load()
    } catch (e) {
      consecutiveFailures++
      if (consecutiveFailures >= MAX_CONSECUTIVE_POLL_FAILURES) {
        stopPolling()
        pollError.value = e instanceof Error ? e.message : '輪詢文件狀態失敗'
      }
    }
  }

  function syncPolling() {
    const busy = documents.value.some((d) => d.status === 'Pending' || d.status === 'Processing')
    if (busy && timer === undefined) timer = window.setInterval(poll, 3000)
    if (!busy) stopPolling()
  }

  async function upload(file: File): Promise<void> {
    const form = new FormData()
    form.append('file', file)
    const res = await fetch('/api/documents', { method: 'POST', headers: authHeader(), body: form })
    if (!res.ok) throw new Error(await safeError(res, '上傳失敗'))
    await load()
  }

  async function remove(id: string): Promise<void> {
    const res = await fetch(`/api/documents/${id}`, { method: 'DELETE', headers: authHeader() })
    if (!res.ok) throw new Error(await safeError(res, '刪除失敗'))
    await load()
  }

  onUnmounted(() => {
    stopPolling()
  })

  return { documents, pollError, load, upload, remove }
}
