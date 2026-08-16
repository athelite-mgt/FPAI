import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CheckCircle2, Inbox, UserCheck } from 'lucide-react'
import { api, describeError } from '../../lib/api'
import { useDepartments, enumOptions } from '../../lib/hooks'
import { formatRelative, humanise } from '../../lib/format'
import type { PendingUser } from '../../lib/types'
import {
  Badge, Button, Card, CardHeader, EmptyState, ErrorState, Modal, Select, Spinner,
  Textarea, useToast,
} from '../../components/ui'

const ROLES = ['SuperAdmin', 'DepartmentHead', 'Staff', 'ExternalAccountant'] as const

export default function Approvals() {
  const toast = useToast()
  const queryClient = useQueryClient()
  const departments = useDepartments()

  const [approving, setApproving] = useState<PendingUser | null>(null)
  const [rejecting, setRejecting] = useState<PendingUser | null>(null)
  const [form, setForm] = useState({ role: 'Staff', departmentId: '', note: '' })
  const [reason, setReason] = useState('')
  const [errors, setErrors] = useState<Record<string, string>>({})

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['pending-users'],
    queryFn: async () => (await api.get<PendingUser[]>('/users/pending')).data,
    refetchInterval: 60_000,
  })

  function invalidate() {
    void queryClient.invalidateQueries({ queryKey: ['pending-users'] })
    void queryClient.invalidateQueries({ queryKey: ['users'] })
    void queryClient.invalidateQueries({ queryKey: ['roles'] })
  }

  const approve = useMutation({
    mutationFn: async () =>
      api.post(`/users/${approving!.id}/approve`, {
        role: form.role,
        departmentId: form.departmentId || null,
        note: form.note || null,
      }),
    onSuccess: () => {
      toast.success(`${approving!.fullName} can now sign in.`)
      setApproving(null)
      invalidate()
    },
    onError: (e) => toast.error(describeError(e)),
  })

  const reject = useMutation({
    mutationFn: async () => api.post(`/users/${rejecting!.id}/reject`, { reason }),
    onSuccess: () => {
      toast.success('Request declined.')
      setRejecting(null)
      setReason('')
      invalidate()
    },
    onError: (e) => toast.error(describeError(e)),
  })

  function submitApproval() {
    const next: Record<string, string> = {}
    if (form.role !== 'SuperAdmin' && !form.departmentId) {
      next.departmentId = `A department is required for the ${humanise(form.role)} role.`
    }
    setErrors(next)
    if (Object.keys(next).length) return
    approve.mutate()
  }

  if (isLoading) return <Spinner label="Loading requests" />
  if (isError) {
    return <ErrorState message="Pending requests could not be loaded." onRetry={() => void refetch()} />
  }

  return (
    <>
      <Card>
        <CardHeader
          title="Access requests"
          subtitle="People who signed up or used Google sign-in. Until you approve one, the account has no role and receives no access token at all."
          action={<Badge tone={data?.length ? 'warning' : 'neutral'}>{data?.length ?? 0} waiting</Badge>}
        />

        {!data?.length ? (
          <EmptyState
            icon={<Inbox className="size-5" />}
            title="No one is waiting"
            description="New sign-ups will appear here for you to approve or decline."
          />
        ) : (
          <ul className="divide-y">
            {data.map((person) => (
              <li key={person.id} className="flex flex-wrap items-start gap-4 px-5 py-4">
                <div className="min-w-0 flex-1">
                  <p className="flex flex-wrap items-center gap-2 text-sm font-medium">
                    {person.fullName}
                    {person.signedUpWithGoogle && <Badge tone="info">Google</Badge>}
                    <Badge tone="warning">Pending</Badge>
                  </p>
                  <p className="text-sm text-[var(--text-muted)]">{person.email}</p>
                  {person.jobTitle && (
                    <p className="text-xs text-[var(--text-subtle)]">{person.jobTitle}</p>
                  )}
                  {person.registrationNote && (
                    <p className="mt-2 rounded-lg bg-[var(--surface-sunken)] px-3 py-2 text-sm">
                      “{person.registrationNote}”
                    </p>
                  )}
                  <p className="mt-1 text-xs text-[var(--text-subtle)]">
                    Requested {formatRelative(person.createdAt)}
                  </p>
                </div>

                <div className="flex gap-2">
                  <Button
                    variant="primary"
                    icon={<UserCheck className="size-4" />}
                    onClick={() => {
                      setApproving(person)
                      setForm({ role: 'Staff', departmentId: '', note: '' })
                      setErrors({})
                    }}
                  >
                    Approve
                  </Button>
                  <Button
                    variant="danger"
                    onClick={() => {
                      setRejecting(person)
                      setReason('')
                    }}
                  >
                    Decline
                  </Button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </Card>

      <Modal
        open={Boolean(approving)}
        onClose={() => setApproving(null)}
        title={`Approve ${approving?.fullName ?? ''}`}
        description="Choose the role and department this person will work under. Both can be changed later."
        footer={
          <>
            <Button variant="ghost" onClick={() => setApproving(null)}>Cancel</Button>
            <Button
              variant="primary"
              loading={approve.isPending}
              icon={<CheckCircle2 className="size-4" />}
              onClick={submitApproval}
            >
              Approve and grant access
            </Button>
          </>
        }
      >
        <div className="space-y-4">
          <p className="rounded-lg bg-[var(--surface-sunken)] px-3 py-2 text-sm text-[var(--text-muted)]">
            {approving?.email}
          </p>

          <Select
            label="Role"
            required
            value={form.role}
            options={enumOptions(ROLES)}
            onChange={(e) => setForm({ ...form, role: e.target.value })}
          />

          <Select
            label="Department"
            required={form.role !== 'SuperAdmin'}
            value={form.departmentId}
            error={errors.departmentId}
            placeholder={form.role === 'SuperAdmin' ? 'Optional for Super Admin' : 'Select…'}
            options={(departments.data ?? []).map((d) => ({ value: d.id, label: d.name }))}
            onChange={(e) => setForm({ ...form, departmentId: e.target.value })}
          />

          <Textarea
            label="Note (optional)"
            rows={2}
            value={form.note}
            placeholder="Recorded against the approval for the audit trail."
            onChange={(e) => setForm({ ...form, note: e.target.value })}
          />
        </div>
      </Modal>

      <Modal
        open={Boolean(rejecting)}
        onClose={() => setRejecting(null)}
        title={`Decline ${rejecting?.fullName ?? ''}`}
        description="The reason is shown to the applicant the next time they try to sign in."
        footer={
          <>
            <Button variant="ghost" onClick={() => setRejecting(null)}>Cancel</Button>
            <Button
              variant="danger"
              loading={reject.isPending}
              disabled={!reason.trim()}
              onClick={() => reject.mutate()}
            >
              Decline request
            </Button>
          </>
        }
      >
        <Textarea
          label="Reason"
          required
          rows={3}
          autoFocus
          value={reason}
          placeholder="e.g. This address is not associated with an FPAI member or member of staff."
          onChange={(e) => setReason(e.target.value)}
        />
      </Modal>
    </>
  )
}
