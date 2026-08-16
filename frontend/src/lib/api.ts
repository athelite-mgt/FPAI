import axios, { AxiosError, type AxiosRequestConfig } from 'axios'
import type { AuthResponse } from './types'

const ACCESS_KEY = 'fpai.access'
const REFRESH_KEY = 'fpai.refresh'

export const tokenStore = {
  get access() {
    return localStorage.getItem(ACCESS_KEY)
  },
  get refresh() {
    return localStorage.getItem(REFRESH_KEY)
  },
  set(auth: Pick<AuthResponse, 'accessToken' | 'refreshToken'>) {
    localStorage.setItem(ACCESS_KEY, auth.accessToken)
    localStorage.setItem(REFRESH_KEY, auth.refreshToken)
  },
  clear() {
    localStorage.removeItem(ACCESS_KEY)
    localStorage.removeItem(REFRESH_KEY)
  },
}

export const api = axios.create({
  baseURL: '/api',
  headers: { 'Content-Type': 'application/json' },
})

api.interceptors.request.use((config) => {
  const token = tokenStore.access
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

/** Fired when refreshing fails, so the app can drop the session and route to sign-in. */
type SessionExpiredHandler = () => void
let onSessionExpired: SessionExpiredHandler = () => {}
export function setSessionExpiredHandler(handler: SessionExpiredHandler) {
  onSessionExpired = handler
}

// A single in-flight refresh shared by every request that 401s concurrently,
// so a page with six queries does not fire six refreshes and rotate the token six times.
let refreshInFlight: Promise<string | null> | null = null

async function refreshAccessToken(): Promise<string | null> {
  const refreshToken = tokenStore.refresh
  if (!refreshToken) return null

  try {
    const { data } = await axios.post<AuthResponse>('/api/auth/refresh', { refreshToken })
    tokenStore.set(data)
    return data.accessToken
  } catch {
    tokenStore.clear()
    return null
  }
}

api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const original = error.config as (AxiosRequestConfig & { _retried?: boolean }) | undefined
    const status = error.response?.status

    const isAuthCall = original?.url?.includes('/auth/login') || original?.url?.includes('/auth/refresh')

    if (status === 401 && original && !original._retried && !isAuthCall) {
      original._retried = true
      refreshInFlight ??= refreshAccessToken().finally(() => {
        refreshInFlight = null
      })

      const token = await refreshInFlight
      if (token) {
        original.headers = { ...original.headers, Authorization: `Bearer ${token}` }
        return api.request(original)
      }
      onSessionExpired()
    }

    return Promise.reject(error)
  },
)

export interface ProblemDetails {
  title?: string
  detail?: string
  status?: number
  errors?: Record<string, string[]>
}

/** Turns an axios failure into a sentence suitable for a toast or inline error. */
export function describeError(error: unknown): string {
  if (axios.isAxiosError(error)) {
    const problem = error.response?.data as ProblemDetails | undefined

    if (problem?.errors) {
      const first = Object.values(problem.errors).flat().filter(Boolean)
      if (first.length) return first.join(' ')
    }
    if (problem?.detail) return problem.detail
    if (problem?.title) return problem.title
    if (error.code === 'ERR_NETWORK') return 'Cannot reach the server. Check your connection.'
    if (error.response?.status === 403) return 'You do not have permission to do that.'
    if (error.response?.status === 404) return 'That record could not be found.'
  }
  return error instanceof Error ? error.message : 'Something went wrong.'
}

/** Drops empty values so the query string stays clean and cache keys stay stable. */
export function toQuery(params: Record<string, unknown>): Record<string, string> {
  const out: Record<string, string> = {}
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') continue
    out[key] = String(value)
  }
  return out
}
