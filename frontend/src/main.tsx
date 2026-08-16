import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { applyTheme, DEFAULT_PREFERENCES } from './lib/theme'

// Apply the cached preferences before the first paint, so there is no flash of the wrong
// theme, scheme or font while /auth/me is still in flight.
try {
  const cached = localStorage.getItem('fpai.preferences')
  const prefs = cached ? { ...DEFAULT_PREFERENCES, ...JSON.parse(cached) } : DEFAULT_PREFERENCES
  applyTheme(prefs.themeMode, prefs.colorScheme, prefs.fontChoice)
} catch {
  applyTheme(DEFAULT_PREFERENCES.themeMode, DEFAULT_PREFERENCES.colorScheme, DEFAULT_PREFERENCES.fontChoice)
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
