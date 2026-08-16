import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { AlertCircle, ShieldCheck } from 'lucide-react'
import { AccountNotApprovedError, useAuth } from '../lib/auth'
import { describeError } from '../lib/api'
import type { RegistrationResult } from '../lib/types'
import { Button, Input } from '../components/ui'
import { AwaitingApproval } from './Register'

const GOOGLE_CLIENT_ID = import.meta.env.VITE_GOOGLE_CLIENT_ID as string | undefined

declare global {
  interface Window {
    google?: {
      accounts: {
        id: {
          initialize: (config: { client_id: string; callback: (r: { credential: string }) => void }) => void
          renderButton: (parent: HTMLElement, options: Record<string, unknown>) => void
        }
      }
    }
  }
}

/** Loads Google Identity Services only when a client id is configured. */
function useGoogleButton(onCredential: (credential: string) => void) {
  const container = useRef<HTMLDivElement>(null)
  const [ready, setReady] = useState(false)
  const callback = useRef(onCredential)
  callback.current = onCredential

  useEffect(() => {
    if (!GOOGLE_CLIENT_ID) return

    function render() {
      if (!window.google || !container.current) return
      window.google.accounts.id.initialize({
        client_id: GOOGLE_CLIENT_ID!,
        callback: (response) => callback.current(response.credential),
      })
      window.google.accounts.id.renderButton(container.current, {
        theme: 'outline', size: 'large', width: 320, text: 'signin_with',
      })
      setReady(true)
    }

    if (window.google) { render(); return }

    const script = document.createElement('script')
    script.src = 'https://accounts.google.com/gsi/client'
    script.async = true
    script.defer = true
    script.onload = render
    document.head.appendChild(script)
    return () => { script.remove() }
  }, [])

  return { container, ready, configured: Boolean(GOOGLE_CLIENT_ID) }
}

export default function Login() {
  const { signIn, signInWithGoogle, user } = useAuth()
  const navigate = useNavigate()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  // Set when the credential was right but the account is not approved yet.
  const [pending, setPending] = useState<RegistrationResult | null>(null)

  useEffect(() => {
    if (user) navigate('/', { replace: true })
  }, [user, navigate])

  async function handleGoogle(credential: string) {
    setError(null)
    setSubmitting(true)
    try {
      await signInWithGoogle(credential)
      navigate('/', { replace: true })
    } catch (err) {
      // An unrecognised Google account registers itself and lands here awaiting approval.
      if (err instanceof AccountNotApprovedError) setPending(err.result)
      else setError(describeError(err))
    } finally {
      setSubmitting(false)
    }
  }

  const google = useGoogleButton(handleGoogle)

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setError(null)
    setSubmitting(true)
    try {
      await signIn(email.trim(), password)
      navigate('/', { replace: true })
    } catch (err) {
      if (err instanceof AccountNotApprovedError) setPending(err.result)
      else setError(describeError(err))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="flex min-h-full">
      {/* Brand panel */}
      <div className="relative hidden w-1/2 flex-col justify-between bg-[var(--chrome)] p-12 lg:flex">
        <div className="flex items-center gap-3">
          <div className="flex size-10 items-center justify-center rounded-xl bg-[var(--accent-solid)] text-lg font-bold text-white">
            F
          </div>
          <div>
            <p className="font-semibold text-white">FPAI Connect</p>
            <p className="text-xs text-[var(--chrome-muted)]">Football Players Association of India</p>
          </div>
        </div>

        <div className="max-w-md">
          <h2 className="text-3xl leading-tight font-semibold text-white">
            One system for welfare, legal, finance and governance.
          </h2>
          <p className="mt-4 text-sm leading-relaxed text-[var(--chrome-muted)]">
            Casework, disputes, vouchers, board motions and member records — managed in one
            place, with every action recorded against the person who took it.
          </p>
          <ul className="mt-8 space-y-3">
            {[
              'Player welfare casework from intake to resolution',
              'FIFA DRC, CAS, PSC and arbitration matters',
              'Voucher and expense approval with accountant review',
              'Board meetings, motions and recorded voting',
            ].map((line) => (
              <li key={line} className="flex items-start gap-2.5 text-sm text-[var(--chrome-muted)]">
                <ShieldCheck className="mt-0.5 size-4 shrink-0 text-[var(--chrome-accent)]" aria-hidden />
                {line}
              </li>
            ))}
          </ul>
        </div>

        <p className="text-xs text-[var(--chrome-muted)]">© {new Date().getFullYear()} FPAI. All rights reserved.</p>
      </div>

      {/* Form panel */}
      <div className="flex w-full items-center justify-center p-6 lg:w-1/2">
        {pending ? (
          <AwaitingApproval result={pending} onBack={() => { setPending(null); setPassword('') }} />
        ) : (
        <div className="w-full max-w-sm">
          <div className="mb-8 lg:hidden">
            <div className="mb-3 flex size-10 items-center justify-center rounded-xl bg-[var(--accent-solid)] text-lg font-bold text-white">
              F
            </div>
            <p className="text-lg font-semibold">FPAI Connect</p>
          </div>

          <h1 className="text-2xl font-semibold tracking-tight">Sign in</h1>
          <p className="mt-1 mb-6 text-sm text-[var(--text-muted)]">
            Use your FPAI account to continue.
          </p>

          {error && (
            <div role="alert"
              className="mb-4 flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2.5 text-sm text-red-800 dark:border-red-500/30 dark:bg-red-950/40 dark:text-red-200">
              <AlertCircle className="mt-0.5 size-4 shrink-0" aria-hidden />
              <span>{error}</span>
            </div>
          )}

          <form onSubmit={handleSubmit} className="space-y-4" noValidate>
            <Input
              label="Email address"
              type="email"
              autoComplete="username"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="you@fpai.in"
            />
            <Input
              label="Password"
              type="password"
              autoComplete="current-password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="••••••••••"
            />
            <Button type="submit" variant="primary" loading={submitting} className="w-full">
              Sign in
            </Button>
          </form>

          {google.configured && (
            <>
              <div className="my-6 flex items-center gap-3">
                <span className="h-px flex-1 bg-[var(--border)]" />
                <span className="text-xs text-[var(--text-subtle)]">or</span>
                <span className="h-px flex-1 bg-[var(--border)]" />
              </div>
              <div ref={google.container} className="flex justify-center" />
              {!google.ready && (
                <p className="mt-2 text-center text-xs text-[var(--text-subtle)]">
                  Loading Google sign-in…
                </p>
              )}
            </>
          )}

          <p className="mt-8 text-center text-sm text-[var(--text-muted)]">
            Need an account?{' '}
            <Link to="/register" className="font-medium text-[var(--accent-text)] hover:underline">
              Request access
            </Link>
          </p>
          <p className="mt-2 text-center text-xs text-[var(--text-subtle)]">
            New accounts are reviewed by an FPAI administrator before they can be used.
          </p>
        </div>
        )}
      </div>
    </div>
  )
}
