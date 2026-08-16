import { Check, Monitor, Moon, Sun } from 'lucide-react'
import { usePreferences } from '../../lib/preferences'
import { FONTS, SCHEMES, type ThemeMode } from '../../lib/theme'
import { Badge, Card, CardHeader } from '../../components/ui'

const MODES: { key: ThemeMode; label: string; icon: typeof Sun; hint: string }[] = [
  { key: 'System', label: 'Match system', icon: Monitor, hint: 'Follows your device setting' },
  { key: 'Light', label: 'Light', icon: Sun, hint: 'Always light' },
  { key: 'Dark', label: 'Dark', icon: Moon, hint: 'Always dark' },
]

export default function Appearance() {
  const { themeMode, colorScheme, fontChoice, resolvedTheme, update, saving } = usePreferences()

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader
          title="Light and dark"
          subtitle="Saved to your account, so it follows you to any device."
          action={saving ? <Badge>Saving…</Badge> : undefined}
        />
        <div className="grid gap-3 p-5 sm:grid-cols-3">
          {MODES.map(({ key, label, icon: Icon, hint }) => {
            const active = themeMode === key
            return (
              <button
                key={key}
                onClick={() => void update({ themeMode: key })}
                aria-pressed={active}
                className={`flex items-center gap-3 rounded-xl border p-4 text-left transition-colors ${
                  active
                    ? 'border-[var(--accent-solid)] bg-[var(--accent-soft-bg)]'
                    : 'hover:bg-[var(--surface-sunken)]'
                }`}
              >
                <Icon className={`size-5 shrink-0 ${active ? 'text-[var(--accent-text)]' : 'text-[var(--text-subtle)]'}`} />
                <span className="min-w-0">
                  <span className="block text-sm font-medium">{label}</span>
                  <span className="block text-xs text-[var(--text-muted)]">{hint}</span>
                </span>
                {active && <Check className="ml-auto size-4 shrink-0 text-[var(--accent-text)]" />}
              </button>
            )
          })}
        </div>
      </Card>

      <Card>
        <CardHeader
          title="Colour scheme"
          subtitle="Every scheme is checked for contrast in both light and dark."
        />
        <div className="grid gap-3 p-5 sm:grid-cols-2 lg:grid-cols-3">
          {SCHEMES.map((scheme) => {
            const active = colorScheme === scheme.key
            const preview = scheme.swatch[resolvedTheme]
            return (
              <button
                key={scheme.key}
                onClick={() => void update({ colorScheme: scheme.key })}
                aria-pressed={active}
                className={`rounded-xl border p-4 text-left transition-colors ${
                  active
                    ? 'border-[var(--accent-solid)] bg-[var(--accent-soft-bg)]'
                    : 'hover:bg-[var(--surface-sunken)]'
                }`}
              >
                <span className="flex items-center gap-2">
                  <span
                    className="size-6 shrink-0 rounded-lg border"
                    style={{ background: preview }}
                    aria-hidden
                  />
                  <span className="text-sm font-medium">{scheme.name}</span>
                  {active && <Check className="ml-auto size-4 text-[var(--accent-text)]" />}
                </span>
                <span className="mt-2 block text-xs leading-relaxed text-[var(--text-muted)]">
                  {scheme.description}
                </span>
                {/* Honest preview: the same three tones the interface actually uses. */}
                <span className="mt-3 flex gap-1" aria-hidden>
                  <span className="h-1.5 flex-1 rounded-full" style={{ background: scheme.swatch.light }} />
                  <span className="h-1.5 flex-1 rounded-full" style={{ background: scheme.swatch.dark }} />
                  <span className="h-1.5 flex-1 rounded-full bg-[var(--border-strong)]" />
                </span>
              </button>
            )
          })}
        </div>
      </Card>

      <Card>
        <CardHeader
          title="Typeface"
          subtitle="All six use fonts already installed on your device, so switching is instant and works offline."
        />
        <div className="grid gap-3 p-5 sm:grid-cols-2 lg:grid-cols-3">
          {FONTS.map((font) => {
            const active = fontChoice === font.key
            return (
              <button
                key={font.key}
                onClick={() => void update({ fontChoice: font.key })}
                aria-pressed={active}
                className={`rounded-xl border p-4 text-left transition-colors ${
                  active
                    ? 'border-[var(--accent-solid)] bg-[var(--accent-soft-bg)]'
                    : 'hover:bg-[var(--surface-sunken)]'
                }`}
              >
                <span className="flex items-center justify-between gap-2">
                  <span className="text-sm font-medium">{font.name}</span>
                  {active && <Check className="size-4 text-[var(--accent-text)]" />}
                </span>
                {/* Rendered in the actual stack, so the sample is not a lie. */}
                <span className="mt-2 block text-lg" style={{ fontFamily: font.stack }}>
                  FIFA DRC 2025/042
                </span>
                <span
                  className="block text-xs text-[var(--text-muted)]"
                  style={{ fontFamily: font.stack }}
                >
                  Welfare case ₹14.8L · 15 Oct
                </span>
                <span className="mt-2 block text-xs leading-relaxed text-[var(--text-subtle)]">
                  {font.description}
                </span>
              </button>
            )
          })}
        </div>
      </Card>
    </div>
  )
}
