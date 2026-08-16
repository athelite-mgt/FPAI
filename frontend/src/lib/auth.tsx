import {
  createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode,
} from 'react'
import axios from 'axios'
import { api, setSessionExpiredHandler, tokenStore } from './api'
import type { AuthResponse, CurrentUser, RegistrationResult, Role } from './types'

/**
 * Thrown when the credential was correct but the account cannot sign in yet — a
 * self-registered account awaiting approval, or one that was declined or suspended.
 * No token is issued in any of those cases.
 */
export class AccountNotApprovedError extends Error {
  readonly result: RegistrationResult

  constructor(result: RegistrationResult) {
    super(result.message)
    this.name = 'AccountNotApprovedError'
    this.result = result
  }
}

/** Recognises the shape the server returns for a pending/rejected/suspended account. */
function asRegistrationResult(error: unknown): RegistrationResult | null {
  if (!axios.isAxiosError(error) || error.response?.status !== 403) return null
  const data = error.response.data as Partial<RegistrationResult> | undefined
  if (data && typeof data.status === 'string' && typeof data.message === 'string') {
    return data as RegistrationResult
  }
  return null
}

interface AuthContextValue {
  user: CurrentUser | null
  loading: boolean
  signIn: (email: string, password: string) => Promise<void>
  signInWithGoogle: (credential: string) => Promise<void>
  signInWithMicrosoft: (idToken: string) => Promise<void>
  register: (input: {
    fullName: string; email: string; password: string; jobTitle?: string; note?: string
  }) => Promise<RegistrationResult>
  signOut: () => Promise<void>
  refreshUser: () => Promise<void>
  hasRole: (...roles: Role[]) => boolean
  isSuperAdmin: boolean
  /** True when the user may write to the given department (mirrors the server rule). */
  canWriteDepartment: (departmentId?: string | null) => boolean
  /** True when the user may approve within the given department (mirrors the server rule). */
  canApproveDepartment: (departmentId?: string | null) => boolean
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<CurrentUser | null>(null)
  const [loading, setLoading] = useState(true)

  const clearSession = useCallback(() => {
    tokenStore.clear()
    setUser(null)
  }, [])

  useEffect(() => {
    setSessionExpiredHandler(clearSession)
  }, [clearSession])

  // Restore the session on first load: the access token may be stale, but the
  // interceptor will refresh it transparently before this call resolves.
  useEffect(() => {
    let cancelled = false

    async function restore() {
      if (!tokenStore.access && !tokenStore.refresh) {
        setLoading(false)
        return
      }
      try {
        const { data } = await api.get<CurrentUser>('/auth/me')
        if (!cancelled) setUser(data)
      } catch {
        if (!cancelled) clearSession()
      } finally {
        if (!cancelled) setLoading(false)
      }
    }

    void restore()
    return () => {
      cancelled = true
    }
  }, [clearSession])

  const signIn = useCallback(async (email: string, password: string) => {
    try {
      const { data } = await api.post<AuthResponse>('/auth/login', { email, password })
      tokenStore.set(data)
      setUser(data.user)
    } catch (error) {
      const pending = asRegistrationResult(error)
      if (pending) throw new AccountNotApprovedError(pending)
      throw error
    }
  }, [])

  const signInWithGoogle = useCallback(async (credential: string) => {
    try {
      const { data } = await api.post<AuthResponse>('/auth/google', { credential })
      tokenStore.set(data)
      setUser(data.user)
    } catch (error) {
      // A Google account we have not seen registers itself and lands here as pending.
      const pending = asRegistrationResult(error)
      if (pending) throw new AccountNotApprovedError(pending)
      throw error
    }
  }, [])

  const signInWithMicrosoft = useCallback(async (idToken: string) => {
    try {
      const { data } = await api.post<AuthResponse>('/auth/microsoft', { idToken })
      tokenStore.set(data)
      setUser(data.user)
    } catch (error) {
      // A Microsoft account we have not seen registers itself and lands here as pending.
      const pending = asRegistrationResult(error)
      if (pending) throw new AccountNotApprovedError(pending)
      throw error
    }
  }, [])

  const register = useCallback(async (input: {
    fullName: string; email: string; password: string; jobTitle?: string; note?: string
  }) => {
    const { data } = await api.post<RegistrationResult>('/auth/register', input)
    return data
  }, [])

  const signOut = useCallback(async () => {
    const refreshToken = tokenStore.refresh
    // Best-effort server-side revocation; the local session goes regardless.
    if (refreshToken) {
      try {
        await api.post('/auth/logout', { refreshToken })
      } catch {
        /* already invalid server-side */
      }
    }
    clearSession()
  }, [clearSession])

  const refreshUser = useCallback(async () => {
    const { data } = await api.get<CurrentUser>('/auth/me')
    setUser(data)
  }, [])

  const value = useMemo<AuthContextValue>(() => {
    const roles = user?.roles ?? []
    const hasRole = (...wanted: Role[]) => wanted.some((r) => roles.includes(r))
    const isSuperAdmin = roles.includes('SuperAdmin')

    return {
      user,
      loading,
      signIn,
      signInWithGoogle,
      signInWithMicrosoft,
      register,
      signOut,
      refreshUser,
      hasRole,
      isSuperAdmin,
      canWriteDepartment: (departmentId) => {
        if (!user) return false
        if (isSuperAdmin) return true
        if (roles.includes('ExternalAccountant')) return false
        if (!departmentId) return false
        return user.departmentId === departmentId
      },
      canApproveDepartment: (departmentId) => {
        if (!user) return false
        if (isSuperAdmin) return true
        if (!roles.includes('DepartmentHead')) return false
        if (!departmentId) return false
        return user.departmentId === departmentId
      },
    }
  }, [user, loading, signIn, signInWithGoogle, signInWithMicrosoft, register, signOut, refreshUser])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used inside an AuthProvider')
  return context
}
