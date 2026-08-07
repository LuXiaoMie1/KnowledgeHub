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
    department.value = JSON.parse(atob(token.value!.split('.')[1])).department
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
