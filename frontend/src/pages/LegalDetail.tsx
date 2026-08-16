import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, CalendarPlus, Trash2 } from 'lucide-react'
import { api, describeError } from '../lib/api'
import { useAuth } from '../lib/auth'
import { formatCurrency, formatDate, formatDateTime, formatRelative, humanise } from '../lib/format'
import type { LegalCaseDetail } from '../lib/types'
import {
  Badge, Button, Card, CardHeader, ConfirmDialog, ErrorState, Field, Input, Modal, PageHeader,
  priorityTone, Spinner, statusTone, Textarea, useToast, WorkflowRail,
} from '../components/ui'
import DocumentPanel from '../components/DocumentPanel'

const FLOW = ['Registered', 'DocumentsPending', 'Filed', 'HearingScheduled', 'DecisionReceived', 'Closed']

export default function LegalDetailPage() {
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const toast = useToast()
  const queryClient = useQueryClient()
  const { canWriteDepartment, canApproveDepartment } = useAuth()

  const [eventOpen, setEventOpen] = useState(false)
  const [eventForm, setEventForm] = useState({ title: '', detail: '' })
  const [transition, setTransition] = useState<string | null>(null)
  const [comment, setComment] = useState('')
  const [confirmDelete, setConfirmDelete] = useState(false)

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['legal', id],
    queryFn: async () => (await api.get<LegalCaseDetail>(`/legal/cases/${id}`)).data,
    enabled: Boolean(id),
  })

  function invalidate() {
    void queryClient.invalidateQueries({ queryKey: ['legal'] })
    void queryClient.invalidateQueries({ queryKey: ['legal-stats'] })
  }

  const addEvent = useMutation({
    mutationFn: async () => api.post(`/legal/cases/${id}/events`, eventForm),
    onSuccess: () => {
      toast.success('Event recorded.')
      setEventForm({ title: '', detail: '' })
      setEventOpen(false)
      invalidate()
    },
    onError: (e) => toast.error(describeError(e)),
  })

  const changeStatus = useMutation({
    mutationFn: async (status: string) =>
      api.post(`/legal/cases/${id}/status`, { status, comment: comment || undefined }),
    onSuccess: (_, status) => {
      toast.success(`Matter moved to ${humanise(status)}.`)
      setTransition(null)
      setComment('')
      invalidate()
    },
    onError: (e) => toast.error(describeError(e)),
  })

  const remove = useMutation({
    mutationFn: async () => api.delete(`/legal/cases/${id}`),
    onSuccess: () => {
      toast.success('Matter deleted.')
      invalidate()
      navigate('/legal')
    },
    onError: (e) => toast.error(describeError(e)),
  })

  if (isLoading) return <Spinner label="Loading matter" />
  if (isError || !data) {
    return <ErrorState message="This matter could not be loaded." onRetry={() => void refetch()} />
  }

  const canWrite = canWriteDepartment(data.departmentId)
  const canApprove = canApproveDepartment(data.departmentId)

  return (
    <>
      <Link to="/legal"
        className="mb-3 inline-flex items-center gap-1.5 text-sm text-[var(--text-muted)] hover:text-[var(--text)]">
        <ArrowLeft className="size-4" /> Back to legal matters
      </Link>

      <PageHeader title={data.caseNumber} subtitle={data.title}
        actions={
          <>
            {canWrite && (
              <Button icon={<CalendarPlus className="size-4" />} onClick={() => setEventOpen(true)}>
                Record event
              </Button>
            )}
            {canApprove && (
              <Button variant="danger" icon={<Trash2 className="size-4" />} onClick={() => setConfirmDelete(true)}>
                Delete
              </Button>
            )}
          </>
        }
      />

      <Card className="mb-4 p-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <WorkflowRail steps={FLOW} current={data.status} />
          {canWrite && data.allowedTransitions.length > 0 && (
            <div className="flex flex-wrap gap-2">
              {data.allowedTransitions.map((next) => (
                <Button key={next} size="sm" variant={next === 'Closed' ? 'primary' : 'secondary'}
                  onClick={() => setTransition(next)}>
                  Move to {humanise(next)}
                </Button>
              ))}
            </div>
          )}
        </div>
      </Card>

      <div className="grid gap-4 lg:grid-cols-3">
        <div className="space-y-4 lg:col-span-2">
          <Card>
            <CardHeader title="Matter details" />
            <dl className="grid grid-cols-2 gap-4 p-5 sm:grid-cols-3">
              <Field label="Forum"><Badge tone="info">{humanise(data.type)}</Badge></Field>
              <Field label="Status"><Badge tone={statusTone(data.status)}>{humanise(data.status)}</Badge></Field>
              <Field label="Outcome"><Badge tone={statusTone(data.outcome)}>{data.outcome}</Badge></Field>
              <Field label="Priority"><Badge tone={priorityTone(data.priority)}>{data.priority}</Badge></Field>
              <Field label="Member">{data.playerName}</Field>
              <Field label="Opposing club">{data.opposingClubName ?? '—'}</Field>
              <Field label="Lawyer">{data.lawyerName ?? '—'}</Field>
              <Field label="Firm">{data.lawyerFirm ?? '—'}</Field>
              <Field label="Internal counsel">{data.assignedCounselName ?? 'Unassigned'}</Field>
              <Field label="Claim amount">{formatCurrency(data.claimAmount)}</Field>
              <Field label="Awarded">{formatCurrency(data.awardedAmount)}</Field>
              <Field label="Filed">{formatDate(data.filedAt)}</Field>
              <Field label="Hearing">{formatDate(data.hearingDate)}</Field>
              <Field label="Decision">{formatDate(data.decisionDate)}</Field>
              <Field label="Closed">{formatDate(data.closedAt)}</Field>
            </dl>
            {data.description && (
              <div className="border-t px-5 py-4">
                <p className="mb-1 text-xs font-medium text-[var(--text-muted)]">Description</p>
                <p className="text-sm leading-relaxed whitespace-pre-wrap">{data.description}</p>
              </div>
            )}
          </Card>

          <Card>
            <CardHeader title="Case timeline" subtitle={`${data.events.length} events`} />
            {data.events.length === 0 ? (
              <p className="px-5 py-8 text-center text-sm text-[var(--text-muted)]">No events recorded.</p>
            ) : (
              <ol className="p-5">
                {data.events.map((entry, i) => (
                  <li key={entry.id} className="relative flex gap-4 pb-5 last:pb-0">
                    {i < data.events.length - 1 && (
                      <span className="absolute top-5 left-[7px] h-full w-px bg-[var(--border)]" aria-hidden />
                    )}
                    <span className="relative mt-1.5 size-3.5 shrink-0 rounded-full border-2 border-[var(--surface-raised)] bg-violet-500" />
                    <div className="min-w-0 flex-1">
                      <p className="text-sm font-medium text-[var(--text)]">{entry.title}</p>
                      {entry.detail && (
                        <p className="mt-0.5 text-sm leading-relaxed text-[var(--text-muted)]">{entry.detail}</p>
                      )}
                      <p className="mt-0.5 text-xs text-[var(--text-subtle)]">
                        {entry.authorName ?? 'System'} · {formatDateTime(entry.occurredAt)} ·{' '}
                        {formatRelative(entry.occurredAt)}
                      </p>
                    </div>
                  </li>
                ))}
              </ol>
            )}
          </Card>
        </div>

        <div className="space-y-4">
          <DocumentPanel documents={data.documents} departmentId={data.departmentId}
            linkField="legalCaseId" linkId={data.id} canUpload={canWrite}
            onChanged={() => void queryClient.invalidateQueries({ queryKey: ['legal', id] })} />

          <Card>
            <CardHeader title="Record" />
            <dl className="space-y-3 p-5">
              <Field label="Department">{data.departmentName}</Field>
              <Field label="Currency">{data.currency}</Field>
            </dl>
          </Card>
        </div>
      </div>

      <Modal open={eventOpen} onClose={() => setEventOpen(false)} title="Record a case event"
        footer={
          <>
            <Button variant="ghost" onClick={() => setEventOpen(false)}>Cancel</Button>
            <Button variant="primary" loading={addEvent.isPending} disabled={!eventForm.title.trim()}
              onClick={() => addEvent.mutate()}>Record</Button>
          </>
        }>
        <div className="space-y-4">
          <Input label="Title" required autoFocus value={eventForm.title}
            placeholder="e.g. Response received from respondent"
            onChange={(e) => setEventForm({ ...eventForm, title: e.target.value })} />
          <Textarea label="Detail" rows={3} value={eventForm.detail}
            onChange={(e) => setEventForm({ ...eventForm, detail: e.target.value })} />
        </div>
      </Modal>

      <Modal open={Boolean(transition)} onClose={() => setTransition(null)}
        title={`Move to ${humanise(transition ?? '')}`}
        footer={
          <>
            <Button variant="ghost" onClick={() => setTransition(null)}>Cancel</Button>
            <Button variant="primary" loading={changeStatus.isPending}
              onClick={() => transition && changeStatus.mutate(transition)}>Confirm</Button>
          </>
        }>
        <Textarea label="Comment (optional)" rows={3} value={comment}
          onChange={(e) => setComment(e.target.value)} />
      </Modal>

      <ConfirmDialog open={confirmDelete} onClose={() => setConfirmDelete(false)}
        onConfirm={() => remove.mutate()} loading={remove.isPending} danger
        title="Delete this matter?" confirmLabel="Delete matter"
        message="The matter is removed from active lists. Its audit history is retained." />
    </>
  )
}
