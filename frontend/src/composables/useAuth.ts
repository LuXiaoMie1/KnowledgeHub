import { ref } from 'vue'

const token = ref<string | null>(null)
const department = ref<string | null>(null)

export function useAuth() {
  async function login(username: string, password: string): Promise<void> {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    })
    if (!res.ok) throw new Error('帳號或密碼錯誤')
    token.value = (await res.json()).token
    // JWT payload 的 department claim（demo 等級解析，不驗簽）
    // JWT 是 base64url，department 若含特殊字元（+/ 對應 -_）需先轉回標準 base64 再 atob
    const base64 = token.value!.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')
    department.value = JSON.parse(atob(base64)).department
  }
  function logout() {
    token.value = null
    department.value = null
  }
  function authHeader(): Record<string, string> {
    return token.value ? { Authorization: `Bearer ${token.value}` } : {}
  }
  return { token, department, login, logout, authHeader }
}
