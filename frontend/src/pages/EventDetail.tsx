import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, UserPlus, X } from 'lucide-react'
import { api, describeError } from '../lib/api'
import { useAuth } from '../lib/auth'
import { usePlayerLookup } from '../lib/hooks'
import { formatCompactCurrency, formatCurrency, formatDate, humanise } from '../lib/format'
import type { EventDetail } from '../lib/types'
import {
  Badge, Button, Card, CardHeader, Checkbox, ErrorState, Field, Modal, PageHeader, Select,
  Spinner, statusTone, useToast, WorkflowRail,
} from '../components/ui'
import DocumentPanel from '../components/DocumentPanel'

const FLOW = ['Planned', 'Dispatched', 'Ongoing', 'Completed']

export default function EventDetailPage() {
  const { id = '' } = useParams()
  const toast = useToast()
  const queryClient = useQueryClient()
  const { canWriteDepartment } = useAuth()
  const players = usePlayerLookup()

  const [addOpen, setAddOpen] = useState(false)
  const [selected, setSelected] = useState<string[]>([])
  const [search, setSearch] = useState('')

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['event', id],
    queryFn: async () => (await api.get<EventDetail>(`/events/${id}`)).data,
    enabled: Boolean(id),
  })

  function invalidate() {
    void queryClient.invalidateQueries({ queryKey: ['event', id] })
    void queryClient.invalidateQueries({ queryKey: ['events'] })
    void queryClient.invalidateQueries({ queryKey: ['event-stats'] })
  }

  const changeStatus = useMutation({
    mutationFn: async (status: string) => api.post(`/events/${id}/status`, { status }),
    onSuccess: (_, status) => { toast.success(`Event marked ${humanise(status)}.`); invalidate() },
    onError: (e) => toast.error(describeError(e)),
  })

  const addParticipants = useMutation({
    mutationFn: async () => api.post(`/events/${id}/participants`, { playerIds: selected }),
    onSuccess: () => {
      toast.success(`${selected.length} member(s) added.`)
      setSelected([])
      setAddOpen(false)
      invalidate()
    },
    onError: (e) => toast.error(describeError(e)),
  })

  const removeParticipant = useMutation({
    mutationFn: async (participantId: string) =>
      api.delete(`/events/${id}/participants/${participantId}`),
    onSuccess: () => { toast.success('Participant removed.'); invalidate() },
    onError: (e) => toast.error(describeError(e)),
  })

  const setAttendance = useMutation({
    mutationFn: async ({ participantId, status }: { participantId: string; status: string }) =>
      api.post(`/events/${id}/participants/${participantId}/attendance`, { status }),
    onSuccess: () => invalidate(),
    onError: (e) => toast.error(describeError(e)),
  })

  if (isLoading) return <Spinner label="Loading event" />
  if (isError || !data) {
    return <ErrorState message="This event could not be loaded." onRetry={() => void refetch()} />
  }

  const canWrite = canWriteDepartment(data.departmentId)
  const existingIds = new Set(data.participants.map((p) => p.playerId))
  const available = (players.data ?? []).filter(
    (p) => !existingIds.has(p.id) && p.label.toLowerCase().includes(search.toLowerCase()),
  )
  const overBudget = data.actualCost > data.budgetAmount && data.budgetAmount > 0

  return (
    <>
      <Link to="/events"
        className="mb-3 inline-flex items-center gap-1.5 text-sm text-[var(--text-muted)] hover:text-[var(--text)]">
        <ArrowLeft className="size-4" /> Back to events
      </Link>

      <PageHeader title={data.name} subtitle={`${data.referenceNumber} · ${formatDate(data.startDate)}`}
        actions={canWrite && data.allowedTransitions.map((next) => (
          <Button key={next} variant={next === 'Completed' ? 'primary' : 'secondary'}
            loading={changeStatus.isPending} onClick={() => changeStatus.mutate(next)}>
            Mark {humanise(next)}
          </Button>
        ))}
      />

      <Card className="mb-4 p-4">
        <WorkflowRail steps={FLOW} current={data.status === 'Cancelled' ? 'Planned' : data.status} />
        {overBudget && (
          <p className="mt-3 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-900 dark:border-amber-500/30 dark:bg-amber-950/40 dark:text-amber-200">
            Actual cost exceeds the approved budget by{' '}
            {formatCurrency(data.actualCost - data.budgetAmount)}.
          </p>
        )}
      </Card>

      <div className="grid gap-4 lg:grid-cols-3">
        <div className="space-y-4 lg:col-span-2">
          <Card>
            <CardHeader title="Event details" />
            <dl className="grid grid-cols-2 gap-4 p-5 sm:grid-cols-3">
              <Field label="Status"><Badge tone={statusTone(data.status)}>{humanise(data.status)}</Badge></Field>
              <Field label="Type"><Badge tone="info">{data.type}</Badge></Field>
              <Field label="Owner">{data.ownerName ?? '—'}</Field>
              <Field label="Venue">{data.venue ?? '—'}</Field>
              <Field label="City">{data.city ?? '—'}</Field>
              <Field label="Dates">
                {formatDate(data.startDate)}
                {data.endDate && ` – ${formatDate(data.endDate)}`}
              </Field>
              <Field label="Budget">{formatCompactCurrency(data.budgetAmount)}</Field>
              <Field label="Actual cost">
                <span className={overBudget ? 'text-red-600' : undefined}>
                  {formatCompactCurrency(data.actualCost)}
                </span>
              </Field>
              <Field label="Attendance">
                {data.actualAttendees} of {data.expectedAttendees} expected
              </Field>
            </dl>
            {data.description && (
              <div className="border-t px-5 py-4">
                <p className="mb-1 text-xs font-medium text-[var(--text-muted)]">Description</p>
                <p className="text-sm leading-relaxed whitespace-pre-wrap">{data.description}</p>
              </div>
            )}
          </Card>

          <Card>
            <CardHeader title="Participants" subtitle={`${data.participants.length} members`}
              action={canWrite && (
                <Button size="sm" icon={<UserPlus className="size-3.5" />} onClick={() => setAddOpen(true)}>
                  Add members
                </Button>
              )} />
            {data.participants.length === 0 ? (
              <p className="px-5 py-8 text-center text-sm text-[var(--text-muted)]">
                No members have been added yet.
              </p>
            ) : (
              <ul className="divide-y">
                {data.participants.map((p) => (
                  <li key={p.id} className="flex items-center gap-3 px-5 py-2.5">
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-sm">{p.playerName}</p>
                      <p className="text-xs text-[var(--text-subtle)]">{p.clubName ?? '—'}</p>
                    </div>
                    {canWrite ? (
                      <Select className="h-8 w-auto text-xs" value={p.status}
                        options={['Invited', 'Accepted', 'Declined', 'Attended', 'Absent']
                          .map((s) => ({ value: s, label: s }))}
                        onChange={(e) =>
                          setAttendance.mutate({ participantId: p.id, status: e.target.value })} />
                    ) : (
                      <Badge tone={statusTone(p.status)}>{p.status}</Badge>
                    )}
                    {canWrite && (
                      <button aria-label={`Remove ${p.playerName}`}
                        onClick={() => removeParticipant.mutate(p.id)}
                        className="rounded p-1.5 text-[var(--text-subtle)] hover:bg-red-50 hover:text-red-600 dark:hover:bg-red-500/10">
                        <X className="size-4" />
                      </button>
                    )}
                  </li>
                ))}
              </ul>
            )}
          </Card>
        </div>

        <div className="space-y-4">
          <DocumentPanel documents={data.documents} departmentId={data.departmentId}
            linkField="eventId" linkId={data.id} canUpload={canWrite} onChanged={invalidate} />
        </div>
      </div>

      <Modal open={addOpen} onClose={() => setAddOpen(false)} title="Add members to this event"
        description={`${selected.length} selected`}
        footer={
          <>
            <Button variant="ghost" onClick={() => setAddOpen(false)}>Cancel</Button>
            <Button variant="primary" loading={addParticipants.isPending} disabled={selected.length === 0}
              onClick={() => addParticipants.mutate()}>Add {selected.length || ''} member(s)</Button>
          </>
        }>
        <input type="search" value={search} onChange={(e) => setSearch(e.target.value)}
          placeholder="Filter members…" aria-label="Filter members"
          className="mb-3 h-9 w-full rounded-lg border bg-[var(--surface-raised)] px-3 text-sm" />
        <div className="max-h-72 space-y-1.5 overflow-y-auto">
          {available.length === 0 ? (
            <p className="py-6 text-center text-sm text-[var(--text-muted)]">
              No further members to add.
            </p>
          ) : (
            available.map((p) => (
              <Checkbox key={p.id} label={`${p.label} · ${p.sub}`} checked={selected.includes(p.id)}
                onChange={(e) =>
                  setSelected((c) => e.target.checked ? [...c, p.id] : c.filter((x) => x !== p.id))} />
            ))
          )}
        </div>
      </Modal>
    </>
  )
}
