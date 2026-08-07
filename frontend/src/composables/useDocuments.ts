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

export function useDocuments() {
  const { authHeader } = useAuth()
  const documents = ref<DocumentInfo[]>([])
  let timer: number | undefined

  async function load(): Promise<void> {
    const res = await fetch('/api/documents', { headers: authHeader() })
    if (!res.ok) throw new Error('讀取文件清單失敗')
    documents.value = await res.json()
    syncPolling()
  }

  function syncPolling() {
    const busy = documents.value.some((d) => d.status === 'Pending' || d.status === 'Processing')
    if (busy && timer === undefined) timer = window.setInterval(load, 3000)
    if (!busy && timer !== undefined) {
      clearInterval(timer)
      timer = undefined
    }
  }

  async function upload(file: File): Promise<void> {
    const form = new FormData()
    form.append('file', file)
    const res = await fetch('/api/documents', { method: 'POST', headers: authHeader(), body: form })
    if (!res.ok) throw new Error((await res.json()).error ?? '上傳失敗')
    await load()
  }

  async function remove(id: string): Promise<void> {
    const res = await fetch(`/api/documents/${id}`, { method: 'DELETE', headers: authHeader() })
    if (!res.ok) throw new Error('刪除失敗')
    await load()
  }

  onUnmounted(() => {
    if (timer !== undefined) clearInterval(timer)
  })

  return { documents, load, upload, remove }
}
