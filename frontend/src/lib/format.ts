/** Presentation helpers. The API speaks UTC ISO strings and plain numbers. */

const inr = new Intl.NumberFormat('en-IN', {
  style: 'currency',
  currency: 'INR',
  maximumFractionDigits: 0,
})

export function formatCurrency(value: number | null | undefined): string {
  if (value === null || value === undefined) return '—'
  return inr.format(value)
}

/** Drops trailing zeros so 2.50 reads as 2.5 and 2.00 as 2. */
function trimZeros(value: string): string {
  return value.includes('.') ? value.replace(/0+$/, '').replace(/\.$/, '') : value
}

/** Indian short scale: 14,80,000 reads as ₹14.8L, 2,50,00,000 as ₹2.5Cr. */
export function formatCompactCurrency(value: number | null | undefined): string {
  if (value === null || value === undefined) return '—'
  const abs = Math.abs(value)
  if (abs >= 1_00_00_000) return `₹${trimZeros((value / 1_00_00_000).toFixed(2))}Cr`
  if (abs >= 1_00_000) return `₹${trimZeros((value / 1_00_000).toFixed(1))}L`
  if (abs >= 1_000) return `₹${trimZeros((value / 1_000).toFixed(1))}K`
  return inr.format(value)
}

export function formatNumber(value: number | null | undefined): string {
  if (value === null || value === undefined) return '—'
  return new Intl.NumberFormat('en-IN').format(value)
}

export function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return '—'
  return date.toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' })
}

export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return '—'
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return '—'
  return date.toLocaleString('en-IN', {
    day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit',
  })
}

/** For datetime-local inputs, which want local time without a zone suffix. */
export function toLocalInputValue(iso: string | null | undefined): string {
  if (!iso) return ''
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}

export function toDateInputValue(iso: string | null | undefined): string {
  if (!iso) return ''
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

export function formatRelative(iso: string | null | undefined): string {
  if (!iso) return '—'
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return '—'

  const seconds = Math.round((date.getTime() - Date.now()) / 1000)
  const rtf = new Intl.RelativeTimeFormat('en', { numeric: 'auto' })
  const units: [Intl.RelativeTimeFormatUnit, number][] = [
    ['year', 31536000], ['month', 2592000], ['week', 604800],
    ['day', 86400], ['hour', 3600], ['minute', 60],
  ]
  for (const [unit, secondsPerUnit] of units) {
    if (Math.abs(seconds) >= secondsPerUnit) {
      return rtf.format(Math.round(seconds / secondsPerUnit), unit)
    }
  }
  return 'just now'
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

/** FifaDrc -> FIFA DRC, MentalHealth -> Mental Health, UnderReview -> Under Review. */
export function humanise(value: string | null | undefined): string {
  if (!value) return '—'
  const special: Record<string, string> = {
    FifaDrc: 'FIFA DRC', Cas: 'CAS', Psc: 'PSC',
    SuperAdmin: 'Super Admin', DepartmentHead: 'Department Head',
    ExternalAccountant: 'External Accountant', GeneralBody: 'General Body',
  }
  if (special[value]) return special[value]
  return value.replace(/([a-z])([A-Z])/g, '$1 $2')
}

export function initialsOf(name: string): string {
  return name
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? '')
    .join('')
}
