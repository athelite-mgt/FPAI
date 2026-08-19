import clsx from 'clsx'
import {
  createContext, useCallback, useContext, useEffect, useId, useMemo, useRef, useState,
  type ButtonHTMLAttributes, type InputHTMLAttributes, type ReactNode,
  type SelectHTMLAttributes, type TextareaHTMLAttributes,
} from 'react'
import { AlertCircle, CheckCircle2, ChevronLeft, ChevronRight, Info, Loader2, X } from 'lucide-react'

/* ------------------------------------------------------------------ surfaces */

export function Card({ className, children, ...rest }: { className?: string; children: ReactNode } & React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      {...rest}
      className={clsx(
        'rounded-xl border bg-[var(--surface-raised)] shadow-[var(--shadow-card)]',
        className,
      )}
    >
      {children}
    </div>
  )
}

export function CardHeader({ title, subtitle, action }: { title: ReactNode; subtitle?: ReactNode; action?: ReactNode }) {
  return (
    <div className="flex items-start justify-between gap-4 border-b px-5 py-4">
      <div className="min-w-0">
        <h3 className="truncate text-sm font-semibold text-[var(--text)]">{title}</h3>
        {subtitle && <p className="mt-0.5 text-xs text-[var(--text-muted)]">{subtitle}</p>}
      </div>
      {action && <div className="shrink-0">{action}</div>}
    </div>
  )
}

export function PageHeader({ title, subtitle, actions }: { title: string; subtitle?: string; actions?: ReactNode }) {
  return (
    <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight text-[var(--text)]">{title}</h1>
        {subtitle && <p className="mt-1 text-sm text-[var(--text-muted)]">{subtitle}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </div>
  )
}

/* ------------------------------------------------------------------ button */

type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger' | 'subtle'
type ButtonSize = 'sm' | 'md'

const buttonVariants: Record<ButtonVariant, string> = {
  primary:
    'bg-[var(--accent-solid)] text-white hover:bg-[var(--accent-solid-hover)] active:bg-[var(--accent-solid-active)] disabled:opacity-60',
  secondary:
    'border bg-[var(--surface-raised)] text-[var(--text)] hover:bg-[var(--surface-sunken)]',
  ghost: 'text-[var(--text-muted)] hover:bg-[var(--surface-sunken)] hover:text-[var(--text)]',
  danger: 'bg-red-600 text-white hover:bg-red-700 active:bg-red-800 disabled:bg-red-600/50',
  subtle: 'bg-[var(--surface-sunken)] text-[var(--text)] hover:bg-[var(--border)]',
}

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
  size?: ButtonSize
  loading?: boolean
  icon?: ReactNode
}

export function Button({
  variant = 'secondary', size = 'md', loading, icon, className, children, disabled, ...rest
}: ButtonProps) {
  return (
    <button
      {...rest}
      disabled={disabled || loading}
      className={clsx(
        'inline-flex items-center justify-center gap-2 rounded-lg font-medium transition-colors',
        'disabled:cursor-not-allowed disabled:opacity-60',
        size === 'sm' ? 'h-8 px-3 text-xs' : 'h-9.5 px-4 text-sm',
        buttonVariants[variant],
        className,
      )}
    >
      {loading ? <Loader2 className="size-4 animate-spin" aria-hidden /> : icon}
      {children}
    </button>
  )
}

/* ------------------------------------------------------------------ fields */

function FieldShell({
  label, hint, error, required, htmlFor, children,
}: { label?: string; hint?: string; error?: string; required?: boolean; htmlFor?: string; children: ReactNode }) {
  return (
    <div className="space-y-1.5">
      {label && (
        <label htmlFor={htmlFor} className="block text-xs font-medium text-[var(--text-muted)]">
          {label}
          {required && <span className="ml-0.5 text-red-500">*</span>}
        </label>
      )}
      {children}
      {error ? (
        <p className="flex items-center gap-1 text-xs text-red-600 dark:text-red-400">
          <AlertCircle className="size-3.5 shrink-0" aria-hidden />
          {error}
        </p>
      ) : hint ? (
        <p className="text-xs text-[var(--text-subtle)]">{hint}</p>
      ) : null}
    </div>
  )
}

const controlClass =
  'w-full rounded-lg border bg-[var(--surface-raised)] px-3 text-sm text-[var(--text)] ' +
  'placeholder:text-[var(--text-subtle)] transition-colors ' +
  'focus:border-[var(--accent-solid)] disabled:cursor-not-allowed disabled:opacity-60'

export interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: string; hint?: string; error?: string
}

export function Input({ label, hint, error, className, id, ...rest }: InputProps) {
  const generatedId = useId()
  const fieldId = id ?? generatedId
  return (
    <FieldShell label={label} hint={hint} error={error} required={rest.required} htmlFor={fieldId}>
      <input
        {...rest}
        id={fieldId}
        aria-invalid={error ? true : undefined}
        className={clsx(controlClass, 'h-9.5', error && 'border-red-500', className)}
      />
    </FieldShell>
  )
}

export interface TextareaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  label?: string; hint?: string; error?: string
}

export function Textarea({ label, hint, error, className, id, ...rest }: TextareaProps) {
  const generatedId = useId()
  const fieldId = id ?? generatedId
  return (
    <FieldShell label={label} hint={hint} error={error} required={rest.required} htmlFor={fieldId}>
      <textarea
        {...rest}
        id={fieldId}
        aria-invalid={error ? true : undefined}
        className={clsx(controlClass, 'py-2 leading-relaxed', error && 'border-red-500', className)}
      />
    </FieldShell>
  )
}

export interface SelectProps extends SelectHTMLAttributes<HTMLSelectElement> {
  label?: string; hint?: string; error?: string
  options: { value: string; label: string }[]
  placeholder?: string
}

export function Select({
  label, hint, error, options, placeholder, className, id, ...rest
}: SelectProps) {
  const generatedId = useId()
  const fieldId = id ?? generatedId
  return (
    <FieldShell label={label} hint={hint} error={error} required={rest.required} htmlFor={fieldId}>
      <select
        {...rest}
        id={fieldId}
        aria-invalid={error ? true : undefined}
        className={clsx(controlClass, 'h-9.5', error && 'border-red-500', className)}
      >
        {placeholder && <option value="">{placeholder}</option>}
        {options.map((option) => (
          <option key={option.value} value={option.value}>{option.label}</option>
        ))}
      </select>
    </FieldShell>
  )
}

export function Checkbox({
  label, className, id, ...rest
}: InputHTMLAttributes<HTMLInputElement> & { label: string }) {
  const generatedId = useId()
  const fieldId = id ?? generatedId
  return (
    <label htmlFor={fieldId} className="inline-flex cursor-pointer items-center gap-2 text-sm text-[var(--text)]">
      <input
        {...rest}
        id={fieldId}
        type="checkbox"
        className={clsx('size-4 rounded border accent-[var(--accent-solid)]', className)}
      />
      {label}
    </label>
  )
}

/* ------------------------------------------------------------------ badges */

export type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger' | 'accent'

const toneClasses: Record<Tone, string> = {
  neutral: 'bg-[var(--surface-sunken)] text-[var(--text-muted)] border-[var(--border)]',
  info: 'bg-sky-50 text-sky-700 border-sky-200 dark:bg-sky-500/10 dark:text-sky-300 dark:border-sky-500/25',
  success: 'bg-[var(--accent-soft-bg)] text-[var(--accent-soft-text)] border-[var(--accent-soft-border)] ',
  warning: 'bg-amber-50 text-amber-800 border-amber-200 dark:bg-amber-500/10 dark:text-amber-300 dark:border-amber-500/25',
  danger: 'bg-red-50 text-red-700 border-red-200 dark:bg-red-500/10 dark:text-red-300 dark:border-red-500/25',
  accent: 'bg-violet-50 text-violet-700 border-violet-200 dark:bg-violet-500/10 dark:text-violet-300 dark:border-violet-500/25',
}

export function Badge({ tone = 'neutral', children, className }: { tone?: Tone; children: ReactNode; className?: string }) {
  return (
    <span
      className={clsx(
        'inline-flex items-center gap-1 rounded-md border px-2 py-0.5 text-xs font-medium whitespace-nowrap',
        toneClasses[tone],
        className,
      )}
    >
      {children}
    </span>
  )
}

/** Maps every workflow status in the system to a colour, so status reads consistently. */
export function statusTone(status: string): Tone {
  switch (status) {
    case 'New': case 'Registered': case 'Draft': case 'Created': case 'Todo': case 'Planned':
    case 'Invited': case 'Received':
      return 'neutral'
    case 'UnderReview': case 'DocumentsPending': case 'Pending': case 'PendingApproval':
    case 'InvoiceAttached': case 'Scheduled': case 'Open': case 'Dispatched': case 'Verified':
      return 'info'
    case 'Assigned': case 'InProgress': case 'Filed': case 'HearingScheduled':
    case 'AccountantReview': case 'Ongoing': case 'Accepted':
      return 'warning'
    case 'Resolved': case 'Approved': case 'Reconciled': case 'Passed': case 'Done':
    case 'Completed': case 'Attended': case 'Active': case 'Won': case 'Paid':
      return 'success'
    case 'Rejected': case 'Failed': case 'Cancelled': case 'Blocked': case 'Declined':
    case 'Absent': case 'Suspended': case 'Lost': case 'Disputed': case 'Critical':
      return 'danger'
    case 'Closed': case 'DecisionReceived': case 'Withdrawn': case 'Settled':
      return 'accent'
    default:
      return 'neutral'
  }
}

export function priorityTone(priority: string): Tone {
  switch (priority) {
    case 'Critical': return 'danger'
    case 'High': return 'warning'
    case 'Medium': return 'info'
    default: return 'neutral'
  }
}

/* ------------------------------------------------------------------ table */

export function Table({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div className="overflow-x-auto">
      <table className={clsx('w-full min-w-[40rem] border-collapse text-sm', className)}>{children}</table>
    </div>
  )
}

export function Th({
  children, className, sortable, active, descending, onSort,
}: {
  children: ReactNode; className?: string; sortable?: boolean
  active?: boolean; descending?: boolean; onSort?: () => void
}) {
  const content = (
    <span className={clsx('inline-flex items-center gap-1', active && 'text-[var(--text)]')}>
      {children}
      {sortable && (
        <span aria-hidden className="text-[10px] leading-none">
          {active ? (descending ? '▼' : '▲') : '↕'}
        </span>
      )}
    </span>
  )
  return (
    <th
      scope="col"
      aria-sort={active ? (descending ? 'descending' : 'ascending') : undefined}
      className={clsx(
        'border-b bg-[var(--surface-sunken)] px-4 py-2.5 text-left text-xs font-semibold',
        'text-[var(--text-muted)] whitespace-nowrap',
        className,
      )}
    >
      {sortable ? (
        <button type="button" onClick={onSort} className="hover:text-[var(--text)]">{content}</button>
      ) : content}
    </th>
  )
}

export function Td({ children, className }: { children: ReactNode; className?: string }) {
  return <td className={clsx('border-b px-4 py-2.5 align-middle', className)}>{children}</td>}

export function Tr({ children, onClick, className }: { children: ReactNode; onClick?: () => void; className?: string }) {
  return (
    <tr
      onClick={onClick}
      tabIndex={onClick ? 0 : undefined}
      role={onClick ? 'button' : undefined}
      onKeyDown={onClick ? (e) => { if (e.key === 'Enter') onClick() } : undefined}
      className={clsx(
        'transition-colors',
        onClick && 'cursor-pointer hover:bg-[var(--surface-sunken)]',
        className,
      )}
    >
      {children}
    </tr>
  )
}

/* ------------------------------------------------------------------ states */

export function Spinner({ label = 'Loading' }: { label?: string }) {
  return (
    <div className="flex items-center justify-center gap-2 py-12 text-sm text-[var(--text-muted)]">
      <Loader2 className="size-4 animate-spin" aria-hidden />
      {label}…
    </div>
  )
}

export function SkeletonRows({ rows = 6, cols = 5 }: { rows?: number; cols?: number }) {
  return (
    <tbody>
      {Array.from({ length: rows }).map((_, r) => (
        <tr key={r}>
          {Array.from({ length: cols }).map((__, c) => (
            <td key={c} className="border-b px-4 py-3">
              <div className="shimmer h-3.5 rounded bg-[var(--surface-sunken)]" />
            </td>
          ))}
        </tr>
      ))}
    </tbody>
  )
}

export function EmptyState({
  title, description, action, icon,
}: { title: string; description?: string; action?: ReactNode; icon?: ReactNode }) {
  return (
    <div className="flex flex-col items-center justify-center px-6 py-16 text-center">
      <div className="mb-3 flex size-11 items-center justify-center rounded-full bg-[var(--surface-sunken)] text-[var(--text-subtle)]">
        {icon ?? <Info className="size-5" aria-hidden />}
      </div>
      <p className="text-sm font-medium text-[var(--text)]">{title}</p>
      {description && <p className="mt-1 max-w-sm text-sm text-[var(--text-muted)]">{description}</p>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  )
}

export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div className="flex flex-col items-center justify-center px-6 py-16 text-center">
      <div className="mb-3 flex size-11 items-center justify-center rounded-full bg-red-50 text-red-600 dark:bg-red-500/10 dark:text-red-400">
        <AlertCircle className="size-5" aria-hidden />
      </div>
      <p className="text-sm font-medium text-[var(--text)]">Something went wrong</p>
      <p className="mt-1 max-w-md text-sm text-[var(--text-muted)]">{message}</p>
      {onRetry && <Button className="mt-4" onClick={onRetry}>Try again</Button>}
    </div>
  )
}

/* ------------------------------------------------------------------ pagination */

export function Pagination({
  page, pageSize, totalCount, totalPages, onPage,
}: { page: number; pageSize: number; totalCount: number; totalPages: number; onPage: (page: number) => void }) {
  if (totalCount === 0) return null
  const first = (page - 1) * pageSize + 1
  const last = Math.min(page * pageSize, totalCount)

  return (
    <div className="flex flex-wrap items-center justify-between gap-3 px-4 py-3 text-sm">
      <p className="text-[var(--text-muted)]">
        <span className="tabular">{first}–{last}</span> of <span className="tabular">{totalCount}</span>
      </p>
      <div className="flex items-center gap-1">
        <Button size="sm" variant="ghost" disabled={page <= 1} onClick={() => onPage(page - 1)}
          aria-label="Previous page" icon={<ChevronLeft className="size-4" />}>
          Prev
        </Button>
        <span className="px-2 text-xs text-[var(--text-muted)] tabular">
          {page} / {Math.max(totalPages, 1)}
        </span>
        <Button size="sm" variant="ghost" disabled={page >= totalPages} onClick={() => onPage(page + 1)}
          aria-label="Next page">
          Next <ChevronRight className="size-4" />
        </Button>
      </div>
    </div>
  )
}

/* ------------------------------------------------------------------ modal */

export function Modal({
  open, onClose, title, description, children, footer, wide,
}: {
  open: boolean; onClose: () => void; title: string; description?: string
  children: ReactNode; footer?: ReactNode; wide?: boolean
}) {
  const ref = useRef<HTMLDivElement>(null)

  // Scroll-lock and initial focus only ever need to happen once, when the dialog opens —
  // not on every render. Keying this on `open` alone (rather than also `onClose`, which is
  // a fresh function identity on every parent render) stops it from re-running and stealing
  // focus back from a field inside the dialog on every keystroke.
  useEffect(() => {
    if (!open) return
    const previous = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    ref.current?.focus()
    return () => { document.body.style.overflow = previous }
  }, [open])

  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [open, onClose])

  if (!open) return null

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-black/40 p-4 sm:p-8"
      onMouseDown={(e) => { if (e.target === e.currentTarget) onClose() }}>
      <div
        ref={ref}
        tabIndex={-1}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className={clsx(
          'animate-in my-auto w-full rounded-xl border bg-[var(--surface-raised)] shadow-xl outline-none',
          wide ? 'max-w-3xl' : 'max-w-lg',
        )}
      >
        <div className="flex items-start justify-between gap-4 border-b px-5 py-4">
          <div>
            <h2 className="text-base font-semibold text-[var(--text)]">{title}</h2>
            {description && <p className="mt-0.5 text-xs text-[var(--text-muted)]">{description}</p>}
          </div>
          <button onClick={onClose} aria-label="Close dialog"
            className="rounded-md p-1 text-[var(--text-subtle)] hover:bg-[var(--surface-sunken)] hover:text-[var(--text)]">
            <X className="size-4" />
          </button>
        </div>
        <div className="max-h-[70vh] overflow-y-auto px-5 py-4">{children}</div>
        {footer && <div className="flex justify-end gap-2 border-t px-5 py-3">{footer}</div>}
      </div>
    </div>
  )
}

export function ConfirmDialog({
  open, onClose, onConfirm, title, message, confirmLabel = 'Confirm', danger, loading,
}: {
  open: boolean; onClose: () => void; onConfirm: () => void
  title: string; message: string; confirmLabel?: string; danger?: boolean; loading?: boolean
}) {
  return (
    <Modal open={open} onClose={onClose} title={title}
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button variant={danger ? 'danger' : 'primary'} loading={loading} onClick={onConfirm}>
            {confirmLabel}
          </Button>
        </>
      }>
      <p className="text-sm text-[var(--text-muted)]">{message}</p>
    </Modal>
  )
}

/* ------------------------------------------------------------------ toasts */

interface Toast { id: number; message: string; tone: 'success' | 'error' | 'info' }
interface ToastContextValue {
  success: (message: string) => void
  error: (message: string) => void
  info: (message: string) => void
}

const ToastContext = createContext<ToastContextValue | null>(null)

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])
  const counter = useRef(0)

  const push = useCallback((message: string, tone: Toast['tone']) => {
    const id = ++counter.current
    setToasts((current) => [...current, { id, message, tone }])
    setTimeout(() => setToasts((current) => current.filter((t) => t.id !== id)), 5000)
  }, [])

  const value = useMemo<ToastContextValue>(() => ({
    success: (m) => push(m, 'success'),
    error: (m) => push(m, 'error'),
    info: (m) => push(m, 'info'),
  }), [push])

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className="pointer-events-none fixed right-4 bottom-4 z-100 flex w-80 flex-col gap-2"
        role="status" aria-live="polite">
        {toasts.map((toast) => (
          <div key={toast.id}
            className={clsx(
              'animate-in pointer-events-auto flex items-start gap-2 rounded-lg border px-3 py-2.5 text-sm shadow-lg',
              toast.tone === 'success' && 'border-[var(--accent-soft-border)] bg-[var(--accent-soft-bg)] text-[var(--accent-soft-text)] ',
              toast.tone === 'error' && 'border-red-300 bg-red-50 text-red-900 dark:border-red-500/30 dark:bg-red-950/70 dark:text-red-100',
              toast.tone === 'info' && 'border-[var(--border)] bg-[var(--surface-raised)] text-[var(--text)]',
            )}>
            {toast.tone === 'success' ? <CheckCircle2 className="mt-0.5 size-4 shrink-0" aria-hidden />
              : toast.tone === 'error' ? <AlertCircle className="mt-0.5 size-4 shrink-0" aria-hidden />
                : <Info className="mt-0.5 size-4 shrink-0" aria-hidden />}
            <span className="flex-1">{toast.message}</span>
            <button aria-label="Dismiss" onClick={() => setToasts((c) => c.filter((t) => t.id !== toast.id))}>
              <X className="size-3.5 opacity-60 hover:opacity-100" />
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  )
}

export function useToast(): ToastContextValue {
  const context = useContext(ToastContext)
  if (!context) throw new Error('useToast must be used inside a ToastProvider')
  return context
}

/* ------------------------------------------------------------------ misc */

export function StatTile({
  label, value, sub, tone = 'neutral', icon,
}: { label: string; value: ReactNode; sub?: string; tone?: Tone; icon?: ReactNode }) {
  return (
    <Card className="p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="truncate text-xs font-medium text-[var(--text-muted)]">{label}</p>
          <p className="tabular mt-1.5 text-2xl font-semibold tracking-tight text-[var(--text)]">{value}</p>
          {sub && <p className="mt-1 truncate text-xs text-[var(--text-subtle)]">{sub}</p>}
        </div>
        {icon && (
          <div className={clsx('flex size-9 shrink-0 items-center justify-center rounded-lg border', toneClasses[tone])}>
            {icon}
          </div>
        )}
      </div>
    </Card>
  )
}

export function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <dt className="text-xs font-medium text-[var(--text-muted)]">{label}</dt>
      <dd className="mt-0.5 text-sm text-[var(--text)]">{children}</dd>
    </div>
  )
}

export function SearchInput({
  value, onChange, placeholder = 'Search…',
}: { value: string; onChange: (value: string) => void; placeholder?: string }) {
  return (
    <input
      type="search"
      value={value}
      onChange={(e) => onChange(e.target.value)}
      placeholder={placeholder}
      aria-label={placeholder}
      className={clsx(controlClass, 'h-9 sm:w-64')}
    />
  )
}

/** Renders the workflow as a rail so a user can see where a record sits at a glance. */
export function WorkflowRail({ steps, current }: { steps: string[]; current: string }) {
  const index = steps.indexOf(current)
  return (
    <ol className="flex flex-wrap items-center gap-x-1 gap-y-2">
      {steps.map((step, i) => {
        const done = index >= 0 && i < index
        const active = step === current
        return (
          <li key={step} className="flex items-center gap-1">
            <span
              className={clsx(
                'rounded-md px-2 py-1 text-xs font-medium whitespace-nowrap',
                active && 'bg-[var(--accent-solid)] text-white',
                done && 'bg-[var(--accent-soft-bg)] text-[var(--accent-soft-text)] ',
                !active && !done && 'bg-[var(--surface-sunken)] text-[var(--text-subtle)]',
              )}
            >
              {step.replace(/([a-z])([A-Z])/g, '$1 $2')}
            </span>
            {i < steps.length - 1 && <span aria-hidden className="text-[var(--text-subtle)]">›</span>}
          </li>
        )
      })}
    </ol>
  )
}
