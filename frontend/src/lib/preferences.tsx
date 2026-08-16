import {
  createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode,
} from 'react'
import { api } from './api'
import { useAuth } from './auth'
import {
  applyTheme, cssVar, DEFAULT_PREFERENCES, resolveTheme,
  type ResolvedTheme, type ThemeMode,
} from './theme'

export interface Preferences {
  themeMode: ThemeMode
  colorScheme: string
  fontChoice: string
}

interface PreferencesContextValue extends Preferences {
  resolvedTheme: ResolvedTheme
  /** Applies immediately, then persists. Reverts if the server rejects it. */
  update: (patch: Partial<Preferences>) => Promise<void>
  toggleMode: () => void
  saving: boolean
}

const STORAGE_KEY = 'fpai.preferences'

/** Read the cached copy so the first paint is already correct, before /auth/me returns. */
function readCache(): Preferences {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return { ...DEFAULT_PREFERENCES }
    return { ...DEFAULT_PREFERENCES, ...(JSON.parse(raw) as Partial<Preferences>) }
  } catch {
    return { ...DEFAULT_PREFERENCES }
  }
}

const PreferencesContext = createContext<PreferencesContextValue | null>(null)

export function PreferencesProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth()
  const [prefs, setPrefs] = useState<Preferences>(readCache)
  const [resolved, setResolved] = useState<ResolvedTheme>(() => resolveTheme(readCache().themeMode))
  const [saving, setSaving] = useState(false)

  // Adopt the signed-in user's stored preferences; they are the source of truth and
  // follow the person between devices.
  useEffect(() => {
    if (!user?.preferences) return
    const fromServer: Preferences = {
      themeMode: (user.preferences.themeMode as ThemeMode) ?? DEFAULT_PREFERENCES.themeMode,
      colorScheme: user.preferences.colorScheme || DEFAULT_PREFERENCES.colorScheme,
      fontChoice: user.preferences.fontChoice || DEFAULT_PREFERENCES.fontChoice,
    }
    setPrefs(fromServer)
    localStorage.setItem(STORAGE_KEY, JSON.stringify(fromServer))
  }, [user?.preferences])

  useEffect(() => {
    applyTheme(prefs.themeMode, prefs.colorScheme, prefs.fontChoice)
    setResolved(resolveTheme(prefs.themeMode))
    localStorage.setItem(STORAGE_KEY, JSON.stringify(prefs))
  }, [prefs])

  // Follow the operating system while the mode is System.
  useEffect(() => {
    if (prefs.themeMode !== 'System') return
    const media = window.matchMedia('(prefers-color-scheme: dark)')
    const onChange = () => {
      applyTheme(prefs.themeMode, prefs.colorScheme, prefs.fontChoice)
      setResolved(resolveTheme(prefs.themeMode))
    }
    media.addEventListener('change', onChange)
    return () => media.removeEventListener('change', onChange)
  }, [prefs])

  const update = useCallback(
    async (patch: Partial<Preferences>) => {
      const previous = prefs
      const next = { ...prefs, ...patch }
      setPrefs(next) // optimistic: the change is felt instantly

      // Anonymous visitors (the sign-in page) get local-only preferences.
      if (!user) return

      setSaving(true)
      try {
        await api.put('/auth/me/preferences', {
          themeMode: next.themeMode,
          colorScheme: next.colorScheme,
          fontChoice: next.fontChoice,
        })
      } catch {
        setPrefs(previous) // the server refused it, so do not pretend it was saved
      } finally {
        setSaving(false)
      }
    },
    [prefs, user],
  )

  const toggleMode = useCallback(() => {
    // Toggling from System commits to the opposite of what is currently showing.
    const next: ThemeMode = resolveTheme(prefs.themeMode) === 'dark' ? 'Light' : 'Dark'
    void update({ themeMode: next })
  }, [prefs.themeMode, update])

  const value = useMemo<PreferencesContextValue>(
    () => ({ ...prefs, resolvedTheme: resolved, update, toggleMode, saving }),
    [prefs, resolved, update, toggleMode, saving],
  )

  return <PreferencesContext.Provider value={value}>{children}</PreferencesContext.Provider>
}

export function usePreferences(): PreferencesContextValue {
  const context = useContext(PreferencesContext)
  if (!context) throw new Error('usePreferences must be used inside a PreferencesProvider')
  return context
}

/**
 * Chart colours, re-read whenever the scheme or mode changes. Recharts needs real colour
 * values rather than CSS variables, so they are resolved from the live computed style.
 */
export function useChartColors() {
  const { colorScheme, resolvedTheme } = usePreferences()

  return useMemo(() => {
    void colorScheme
    void resolvedTheme
    const accent = cssVar('--accent-solid')
    return {
      accent,
      // A fixed companion hue for the second series, kept distinguishable from every scheme.
      contrast: resolvedTheme === 'dark' ? '#f7a53b' : '#e8860d',
      grid: cssVar('--border', '#dde3ea'),
      axis: cssVar('--text-subtle', '#8494a8'),
      surface: cssVar('--surface-raised', '#ffffff'),
      categorical: [
        accent,
        resolvedTheme === 'dark' ? '#f7a53b' : '#e8860d',
        resolvedTheme === 'dark' ? '#7cb0fb' : '#2563eb',
        resolvedTheme === 'dark' ? '#b198fb' : '#7c3aed',
        resolvedTheme === 'dark' ? '#fb8098' : '#be123c',
        resolvedTheme === 'dark' ? '#a9b7c8' : '#64748b',
      ],
    }
  }, [colorScheme, resolvedTheme])
}
