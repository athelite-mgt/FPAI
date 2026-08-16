import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { KeyRound } from 'lucide-react'
import { api, describeError } from '../lib/api'
import { useAuth } from '../lib/auth'
import { humanise, initialsOf } from '../lib/format'
import { Badge, Button, Card, CardHeader, Field, Input, PageHeader, useToast } from '../components/ui'

export default function Profile() {
  const { user, signOut } = useAuth()
  const toast = useToast()

  const [form, setForm] = useState({ currentPassword: '', newPassword: '', confirm: '' })
  const [errors, setErrors] = useState<Record<string, string>>({})

  const changePassword = useMutation({
    mutationFn: async () =>
      api.post('/auth/change-password', {
        currentPassword: form.currentPassword,
        newPassword: form.newPassword,
      }),
    onSuccess: async () => {
      toast.success('Password changed. Please sign in again.')
      setForm({ currentPassword: '', newPassword: '', confirm: '' })
      // Every session was revoked server-side, so end this one cleanly too.
      await signOut()
      window.location.href = '/login'
    },
    onError: (e) => toast.error(describeError(e)),
  })

  function submit(e: React.FormEvent) {
    e.preventDefault()
    const next: Record<string, string> = {}
    if (!form.currentPassword) next.currentPassword = 'Enter your current password.'
    if (form.newPassword.length < 10) next.newPassword = 'At least 10 characters.'
    if (form.newPassword !== form.confirm) next.confirm = 'The passwords do not match.'
    if (form.currentPassword && form.currentPassword === form.newPassword) {
      next.newPassword = 'Choose a password you have not used here before.'
    }
    setErrors(next)
    if (Object.keys(next).length) return
    changePassword.mutate()
  }

  if (!user) return null

  return (
    <>
      <PageHeader title="Profile" subtitle="Your account details and password." />

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader title="Account" />
          <div className="flex items-center gap-4 border-b px-5 py-4">
            <span className="flex size-14 items-center justify-center rounded-full bg-[var(--accent-solid)] text-lg font-semibold text-white">
              {initialsOf(user.fullName)}
            </span>
            <div className="min-w-0">
              <p className="truncate text-base font-semibold">{user.fullName}</p>
              <p className="truncate text-sm text-[var(--text-muted)]">{user.email}</p>
            </div>
          </div>
          <dl className="grid grid-cols-2 gap-4 p-5">
            <Field label="Job title">{user.jobTitle ?? '—'}</Field>
            <Field label="Department">{user.departmentName ?? '—'}</Field>
            <Field label="Role">
              <div className="flex flex-wrap gap-1">
                {user.roles.map((r) => (
                  <Badge key={r} tone={r === 'SuperAdmin' ? 'danger' : 'info'}>{humanise(r)}</Badge>
                ))}
              </div>
            </Field>
            <Field label="Status"><Badge tone="success">{user.status}</Badge></Field>
          </dl>
          <p className="border-t px-5 py-3 text-xs text-[var(--text-subtle)]">
            Your name, role and department are managed by an administrator.
          </p>
        </Card>

        <Card>
          <CardHeader title="Change password"
            subtitle="Changing your password signs you out of every device." />
          <form onSubmit={submit} className="space-y-4 p-5" noValidate>
            <Input label="Current password" type="password" required autoComplete="current-password"
              value={form.currentPassword} error={errors.currentPassword}
              onChange={(e) => setForm({ ...form, currentPassword: e.target.value })} />
            <Input label="New password" type="password" required autoComplete="new-password"
              value={form.newPassword} error={errors.newPassword}
              hint="At least 10 characters, with upper case, lower case, a digit and a symbol."
              onChange={(e) => setForm({ ...form, newPassword: e.target.value })} />
            <Input label="Confirm new password" type="password" required autoComplete="new-password"
              value={form.confirm} error={errors.confirm}
              onChange={(e) => setForm({ ...form, confirm: e.target.value })} />
            <Button type="submit" variant="primary" loading={changePassword.isPending}
              icon={<KeyRound className="size-4" />}>
              Change password
            </Button>
          </form>
        </Card>
      </div>
    </>
  )
}
