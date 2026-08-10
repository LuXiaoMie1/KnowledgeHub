import { ref } from 'vue'
import {
  createStandardPublicClientApplication,
  type AccountInfo,
  type IPublicClientApplication,
} from '@azure/msal-browser'

const token = ref<string | null>(null)
const department = ref<string | null>(null)
// 已登入但沒有部門（不在任何已映射安全性群組）時，後端回 403 no_department，放這裡讓
// App.vue 切到專屬畫面；非 null 代表要顯示該畫面。
const noDepartmentMessage = ref<string | null>(null)

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
  noDepartmentMessage.value = null
}

/**
 * 後端 403 帶 { error: "no_department" } 時，記錄訊息讓 App.vue 顯示專屬畫面。
 * 用 res.clone() 讀 body：回傳 false 時原始 response 的 body 還沒被消耗，
 * 呼叫端（例如 safeError）可以照常再讀一次自己判斷要顯示的訊息。
 */
async function checkNoDepartment(res: Response): Promise<boolean> {
  if (res.status !== 403) return false
  try {
    const body = await res.clone().json()
    if (body?.error === 'no_department') {
      noDepartmentMessage.value = body.message ?? '帳號尚未授權使用 KnowledgeHub，請聯絡資訊部'
      return true
    }
  } catch {
    // body 不是合法 JSON，不是我們要處理的情況
  }
  return false
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
    noDepartmentMessage.value = null
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
    noDepartmentMessage.value = null
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
  return {
    token,
    department,
    noDepartmentMessage,
    login,
    loginWithEntra,
    logout,
    authHeader,
    checkNoDepartment,
    entraEnabled,
  }
}
