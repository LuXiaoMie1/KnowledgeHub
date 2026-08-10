import { ref } from 'vue'
import {
  createStandardPublicClientApplication,
  type AccountInfo,
  type IPublicClientApplication,
} from '@azure/msal-browser'

const token = ref<string | null>(null)
const department = ref<string | null>(null)

const entraTenantId = import.meta.env.VITE_ENTRA_TENANT_ID
const entraSpaClientId = import.meta.env.VITE_ENTRA_SPA_CLIENT_ID
const entraApiScope = import.meta.env.VITE_ENTRA_API_SCOPE

// 三個 env 變數缺任何一個就視為未設定 Entra（公開 demo 模式），登入頁不顯示公司帳號按鈕。
export const entraEnabled = Boolean(entraTenantId && entraSpaClientId && entraApiScope)

let msalInstancePromise: Promise<IPublicClientApplication> | null = null
let refreshTimer: number | undefined

function getMsalInstance(): Promise<IPublicClientApplication> {
  if (!msalInstancePromise) {
    msalInstancePromise = createStandardPublicClientApplication({
      auth: {
        clientId: entraSpaClientId,
        authority: `https://login.microsoftonline.com/${entraTenantId}`,
        // 彈窗與 silent iframe 都跳回專用空白頁（public/redirect.html），不能指向 SPA 本體：
        // app 啟動會搶在 MSAL 讀取授權碼之前動網址，彈窗會留在畫面上不關（block_nested_popups）。
        // 這個 URI 也要登記在 Entra 的 SPA redirect URI 清單。
        redirectUri: `${window.location.origin}/redirect.html`,
      },
    })
  }
  return msalInstancePromise
}

function stopRefresh() {
  if (refreshTimer !== undefined) {
    clearTimeout(refreshTimer)
    refreshTimer = undefined
  }
}

function clearSession() {
  stopRefresh()
  token.value = null
  department.value = null
}

/**
 * 在 access token 到期前用 acquireTokenSilent（MSAL 快取）換新，讓下游 fetch 呼叫的
 * authHeader() 永遠拿到未過期的 token，不用另外改下游呼叫層的同步取值方式。
 * 換新失敗（例如 refresh token 也過期）就清空登入狀態退回登入頁，使用者重新走互動式登入。
 */
function scheduleRefresh(msal: IPublicClientApplication, account: AccountInfo, expiresOn: Date | null) {
  stopRefresh()
  const bufferMs = 5 * 60 * 1000
  const delay = Math.max((expiresOn?.getTime() ?? Date.now()) - Date.now() - bufferMs, 0)
  refreshTimer = window.setTimeout(async () => {
    try {
      const result = await msal.acquireTokenSilent({ scopes: [entraApiScope], account })
      token.value = result.accessToken
      scheduleRefresh(msal, account, result.expiresOn)
    } catch {
      clearSession()
    }
  }, delay)
}

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
  /**
   * 公司帳號登入：loginPopup 直接要求 API scope 的 token（免另外再呼叫 acquireTokenSilent）。
   * department 這裡沒有真正的部門（Entra token 的 department claim 是後端驗證後才動態加上，
   * 前端拿到的原始 access token 沒有這個 claim），沿用現有 UI 顯示欄位改放 MSAL account 的
   * name/username，讓使用者看到自己是誰登入的；真正的部門過濾仍由後端 groups claim 映射決定。
   */
  async function loginWithEntra(): Promise<void> {
    const msal = await getMsalInstance()
    const result = await msal.loginPopup({ scopes: [entraApiScope] })
    msal.setActiveAccount(result.account)
    token.value = result.accessToken
    department.value = result.account.name || result.account.username
    scheduleRefresh(msal, result.account, result.expiresOn)
  }
  function logout() {
    clearSession()
  }
  function authHeader(): Record<string, string> {
    return token.value ? { Authorization: `Bearer ${token.value}` } : {}
  }
  return { token, department, login, loginWithEntra, logout, authHeader, entraEnabled }
}
