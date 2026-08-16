/**
 * The catalogue of colour schemes and font stacks offered in Settings → Appearance.
 * The visual definitions live in index.css keyed on `data-scheme` / `data-font`;
 * this file is the list the interface renders and the keys stored against each user.
 */

export type ThemeMode = 'System' | 'Light' | 'Dark'
export type ResolvedTheme = 'light' | 'dark'

export interface SchemeOption {
  key: string
  name: string
  description: string
  /** Swatches for the picker, in light and dark, so the preview is honest. */
  swatch: { light: string; dark: string }
}

export interface FontOption {
  key: string
  name: string
  description: string
  /** The same stack as index.css, so the picker previews the real thing. */
  stack: string
}

export const SCHEMES: SchemeOption[] = [
  {
    key: 'pitch',
    name: 'Pitch Green',
    description: 'The FPAI default. Calm and legible for long casework sessions.',
    swatch: { light: '#128551', dark: '#1da565' },
  },
  {
    key: 'saffron',
    name: 'Indian Saffron',
    description: 'Warm and high-energy, drawn from the national colours.',
    swatch: { light: '#c2410c', dark: '#f97316' },
  },
  {
    key: 'royal',
    name: 'Royal Blue',
    description: 'Institutional and neutral; the safest choice for printed reports.',
    swatch: { light: '#2563eb', dark: '#3b82f6' },
  },
  {
    key: 'violet',
    name: 'Deep Violet',
    description: 'Distinctive without being loud. Good contrast in dark mode.',
    swatch: { light: '#7c3aed', dark: '#8b5cf6' },
  },
  {
    key: 'slate',
    name: 'Slate Monochrome',
    description: 'No colour except status badges, so data stands out on its own.',
    swatch: { light: '#334155', dark: '#64748b' },
  },
  {
    key: 'crimson',
    name: 'Crimson',
    description: 'Bold and urgent. Suits users who live in the approvals queue.',
    swatch: { light: '#be123c', dark: '#e11d48' },
  },
]

export const FONTS: FontOption[] = [
  {
    key: 'sans',
    name: 'System Sans',
    description: 'Your operating system’s interface font. Fastest and most familiar.',
    stack: 'system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif',
  },
  {
    key: 'grotesk',
    name: 'Grotesk',
    description: 'Neutral and tight. Fits more columns on screen.',
    stack: '"Helvetica Neue", Helvetica, Arial, "Liberation Sans", sans-serif',
  },
  {
    key: 'serif',
    name: 'Classic Serif',
    description: 'Formal, and easier for reading long minutes and case descriptions.',
    stack: 'Georgia, Cambria, "Times New Roman", "Liberation Serif", serif',
  },
  {
    key: 'rounded',
    name: 'Rounded',
    description: 'Softer letterforms. A friendlier, less clinical feel.',
    stack: 'ui-rounded, "SF Pro Rounded", "Segoe UI Variable", Nunito, system-ui, sans-serif',
  },
  {
    key: 'mono',
    name: 'Monospace',
    description: 'Every character the same width; reference numbers line up exactly.',
    stack: 'ui-monospace, "SF Mono", "Cascadia Mono", "DejaVu Sans Mono", Menlo, monospace',
  },
  {
    key: 'legible',
    name: 'High Legibility',
    description: 'Wide, open letterforms chosen for small sizes and low vision.',
    stack: 'Verdana, Tahoma, "DejaVu Sans", "Segoe UI", sans-serif',
  },
]

export const DEFAULT_PREFERENCES = {
  themeMode: 'System' as ThemeMode,
  colorScheme: 'pitch',
  fontChoice: 'sans',
}

export function isKnownScheme(key: string): boolean {
  return SCHEMES.some((s) => s.key === key)
}

export function isKnownFont(key: string): boolean {
  return FONTS.some((f) => f.key === key)
}

export function resolveTheme(mode: ThemeMode): ResolvedTheme {
  if (mode === 'Light') return 'light'
  if (mode === 'Dark') return 'dark'
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

/**
 * Writes the three axes onto <html>. Unknown keys fall back to the defaults rather than
 * leaving the page unstyled — a preference saved by a newer build must never break an
 * older one.
 */
export function applyTheme(mode: ThemeMode, scheme: string, font: string) {
  const root = document.documentElement
  root.dataset.theme = resolveTheme(mode)
  root.dataset.scheme = isKnownScheme(scheme) ? scheme : DEFAULT_PREFERENCES.colorScheme
  root.dataset.font = isKnownFont(font) ? font : DEFAULT_PREFERENCES.fontChoice
}

/** Reads a resolved CSS custom property, for the chart library which needs real colours. */
export function cssVar(name: string, fallback = '#128551'): string {
  if (typeof window === 'undefined') return fallback
  const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim()
  return value || fallback
}
