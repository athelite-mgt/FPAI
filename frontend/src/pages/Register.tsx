import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { AlertCircle, ArrowLeft, Clock, ShieldCheck } from 'lucide-react'
import { useAuth } from '../lib/auth'
import { describeError } from '../lib/api'
import type { RegistrationResult } from '../lib/types'
import { Button, Input, Textarea } from '../components/ui'

/** Shown after a successful request, and reused by Login when a pending account signs in. */
export function AwaitingApproval({
  result, onBack,
}: { result: RegistrationResult; onBack: () => void }) {
  const declined = result.status === 'Rejected'

  return (
    <div className="w-full max-w-sm text-center">
      <div
        className={`mx-auto mb-4 flex size-12 items-center justify-center rounded-full ${
          declined
            ? 'bg-red-50 text-red-600 dark:bg-red-500/10 dark:text-red-400'
            : 'bg-[var(--accent-soft-bg)] text-[var(--accent-text)]'
        }`}
      >
        {declined ? <AlertCircle className="size-6" /> : <Clock className="size-6" />}
      </div>

      <h1 className="text-xl font-semibold tracking-tight">
        {declined ? 'Request declined' : 'Waiting for approval'}
      </h1>
      <p className="mt-2 text-sm leading-relaxed text-[var(--text-muted)]">{result.message}</p>

      {result.email && (
        <p className="mt-3 rounded-lg bg-[var(--surface-sunken)] px-3 py-2 text-sm font-medium">
          {result.email}
        </p>
      )}

      {!declined && (
        <p className="mt-4 text-xs leading-relaxed text-[var(--text-subtle)]">
          An FPAI administrator reviews each request and assigns your role and department.
          Try signing in again once they have.
        </p>
      )}

      <Button className="mt-6 w-full" onClick={onBack}>Back to sign in</Button>
    </div>
  )
}

export default function Register() {
  const { register } = useAuth()
  const navigate = useNavigate()

  const [form, setForm] = useState({
    fullName: '', email: '', password: '', confirm: '', jobTitle: '', note: '',
  })
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [submitting, setSubmitting] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)
  const [result, setResult] = useState<RegistrationResult | null>(null)

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    const next: Record<string, string> = {}
    if (!form.fullName.trim()) next.fullName = 'Enter your full name.'
    if (!/^\S+@\S+\.\S+$/.test(form.email)) next.email = 'Enter a valid email address.'
    if (form.password.length < 10) next.password = 'At least 10 characters.'
    if (form.password !== form.confirm) next.confirm = 'The passwords do not match.'
    setErrors(next)
    if (Object.keys(next).length) return

    setFailure(null)
    setSubmitting(true)
    try {
      const response = await register({
        fullName: form.fullName.trim(),
        email: form.email.trim(),
        password: form.password,
        jobTitle: form.jobTitle || undefined,
        note: form.note || undefined,
      })
      setResult(response)
    } catch (error) {
      setFailure(describeError(error))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="flex min-h-full">
      <div className="relative hidden w-1/2 flex-col justify-between bg-[var(--chrome)] p-12 lg:flex">
        <div className="flex items-center gap-3">
          <div className="flex size-10 items-center justify-center rounded-xl bg-[var(--accent-solid)] text-lg font-bold text-[var(--accent-on-solid)]">
            F
          </div>
          <div>
            <p className="font-semibold text-[var(--chrome-text)]">FPAI Connect</p>
            <p className="text-xs text-[var(--chrome-muted)]">Football Players Association of India</p>
          </div>
        </div>

        <div className="max-w-md">
          <h2 className="text-3xl leading-tight font-semibold text-[var(--chrome-text)]">
            Request access to FPAI Connect.
          </h2>
          <p className="mt-4 text-sm leading-relaxed text-[var(--chrome-muted)]">
            Anyone may ask for an account. An administrator reviews every request and decides
            the role and department before the account can be used.
          </p>
          <ul className="mt-8 space-y-3">
            {[
              'Your request reaches every administrator immediately',
              'No data is visible until your account is approved',
              'Your role decides exactly what you can see and change',
            ].map((line) => (
              <li key={line} className="flex items-start gap-2.5 text-sm text-[var(--chrome-muted)]">
                <ShieldCheck className="mt-0.5 size-4 shrink-0 text-[var(--chrome-accent)]" aria-hidden />
                {line}
              </li>
            ))}
          </ul>
        </div>

        <p className="text-xs text-[var(--chrome-muted)]">
          © {new Date().getFullYear()} FPAI. All rights reserved.
        </p>
      </div>

      <div className="flex w-full items-center justify-center p-6 lg:w-1/2">
        {result ? (
          <AwaitingApproval result={result} onBack={() => navigate('/login')} />
        ) : (
          <div className="w-full max-w-sm">
            <Link
              to="/login"
              className="mb-5 inline-flex items-center gap-1.5 text-sm text-[var(--text-muted)] hover:text-[var(--text)]"
            >
              <ArrowLeft className="size-4" /> Back to sign in
            </Link>

            <h1 className="text-2xl font-semibold tracking-tight">Request an account</h1>
            <p className="mt-1 mb-6 text-sm text-[var(--text-muted)]">
              An administrator will review your request.
            </p>

            {failure && (
              <div
                role="alert"
                className="mb-4 flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2.5 text-sm text-red-800 dark:border-red-500/30 dark:bg-red-950/40 dark:text-red-200"
              >
                <AlertCircle className="mt-0.5 size-4 shrink-0" aria-hidden />
                <span>{failure}</span>
              </div>
            )}

            <form onSubmit={handleSubmit} className="space-y-4" noValidate>
              <Input
                label="Full name"
                required
                autoComplete="name"
                value={form.fullName}
                error={errors.fullName}
                onChange={(e) => setForm({ ...form, fullName: e.target.value })}
              />
              <Input
                label="Email address"
                type="email"
                required
                autoComplete="username"
                value={form.email}
                error={errors.email}
                placeholder="you@fpai.in"
                onChange={(e) => setForm({ ...form, email: e.target.value })}
              />
              <Input
                label="Job title"
                value={form.jobTitle}
                placeholder="Optional"
                onChange={(e) => setForm({ ...form, jobTitle: e.target.value })}
              />
              <Input
                label="Password"
                type="password"
                required
                autoComplete="new-password"
                value={form.password}
                error={errors.password}
                hint="At least 10 characters, with upper case, lower case, a digit and a symbol."
                onChange={(e) => setForm({ ...form, password: e.target.value })}
              />
              <Input
                label="Confirm password"
                type="password"
                required
                autoComplete="new-password"
                value={form.confirm}
                error={errors.confirm}
                onChange={(e) => setForm({ ...form, confirm: e.target.value })}
              />
              <Textarea
                label="Why do you need access?"
                rows={2}
                value={form.note}
                placeholder="Optional, but it helps the administrator decide."
                onChange={(e) => setForm({ ...form, note: e.target.value })}
              />

              <Button type="submit" variant="primary" loading={submitting} className="w-full">
                Request access
              </Button>
            </form>
          </div>
        )}
      </div>
    </div>
  )
}
