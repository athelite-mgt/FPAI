import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, MessageSquarePlus, Trash2 } from 'lucide-react'
import { api, describeError } from '../lib/api'
import { useAuth } from '../lib/auth'
import { formatDate, formatDateTime, formatRelative, humanise } from '../lib/format'
import type { WelfareCaseDetail } from '../lib/types'
import {
  Badge, Button, Card, CardHeader, ConfirmDialog, ErrorState, Field, Modal, PageHeader,
  priorityTone, Spinner, statusTone, Textarea, useToast, WorkflowRail,
} from '../components/ui'
import DocumentPanel from '../components/DocumentPanel'

const FLOW = ['New', 'UnderReview', 'Assigned', 'InProgress', 'Resolved', 'Closed']

export default function WelfareDetailPage() {
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const toast = useToast()
  const queryClient = useQueryClient()
  const { canWriteDepartment, canApproveDepartment } = useAuth()

  const [noteOpen, setNoteOpen] = useState(false)
  const [note, setNote] = useState('')
  const [transition, setTransition] = useState<string | null>(null)
  const [comment, setComment] = useState('')
  const [confirmDelete, setConfirmDelete] = useState(false)

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['welfare', id],
    queryFn: async () => (await api.get<WelfareCaseDetail>(`/welfare/cases/${id}`)).data,
    enabled: Boolean(id),
  })

  function invalidate() {
    void queryClient.invalidateQueries({ queryKey: ['welfare'] })
    void queryClient.invalidateQueries({ queryKey: ['welfare-stats'] })
  }

  const addNote = useMutation({
    mutationFn: async () => api.post(`/welfare/cases/${id}/notes`, { note }),
    onSuccess: () => {
      toast.success('Note added.')
      setNote('')
      setNoteOpen(false)
      invalidate()
    },
    onError: (e) => toast.error(describeError(e)),
  })

  const changeStatus = useMutation({
    mutationFn: async (status: string) =>
      api.post(`/welfare/cases/${id}/status`, { status, comment: comment || undefined }),
    onSuccess: (_, status) => {
      toast.success(`Case moved to ${humanise(status)}.`)
      setTransition(null)
      setComment('')
      invalidate()
    },
    onError: (e) => toast.error(describeError(e)),
  })

  const remove = useMutation({
    mutationFn: async () => api.delete(`/welfare/cases/${id}`),
    onSuccess: () => {
      toast.success('Case deleted.')
      invalidate()
      navigate('/welfare')
    },
    onError: (e) => toast.error(describeError(e)),
  })

  if (isLoading) return <Spinner label="Loading case" />
  if (isError || !data) {
    return <ErrorState message="This case could not be loaded." onRetry={() => void refetch()} />
  }

  const canWrite = canWriteDepartment(data.departmentId)
  const canApprove = canApproveDepartment(data.departmentId)

  return (
    <>
      <Link to="/welfare"
        className="mb-3 inline-flex items-center gap-1.5 text-sm text-[var(--text-muted)] hover:text-[var(--text)]">
        <ArrowLeft className="size-4" /> Back to welfare cases
      </Link>

      <PageHeader
        title={data.caseNumber}
        subtitle={data.title}
        actions={
          <>
            {canWrite && (
              <Button icon={<MessageSquarePlus className="size-4" />} onClick={() => setNoteOpen(true)}>
                Add note
              </Button>
            )}
            {canApprove && (
              <Button variant="danger" icon={<Trash2 className="size-4" />}
                onClick={() => setConfirmDelete(true)}>
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
                <Button key={next} size="sm"
                  variant={next === 'Closed' ? 'primary' : 'secondary'}
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
            <CardHeader title="Case details" />
            <dl className="grid grid-cols-2 gap-4 p-5 sm:grid-cols-3">
              <Field label="Status"><Badge tone={statusTone(data.status)}>{humanise(data.status)}</Badge></Field>
              <Field label="Priority"><Badge tone={priorityTone(data.priority)}>{data.priority}</Badge></Field>
              <Field label="Category"><Badge>{humanise(data.category)}</Badge></Field>
              <Field label="Member">
                <Link to="/players" className="text-[var(--accent-text)] hover:underline">{data.playerName}</Link>
              </Field>
              <Field label="Club">{data.playerClub ?? '—'}</Field>
              <Field label="Kind">{data.isDispute ? 'Formal dispute' : 'Welfare request'}</Field>
              <Field label="Assigned officer">{data.assignedOfficerName ?? 'Unassigned'}</Field>
              <Field label="Opened">{formatDate(data.openedAt)}</Field>
              <Field label="Resolved">{formatDate(data.resolvedAt)}</Field>
            </dl>
            {data.description && (
              <div className="border-t px-5 py-4">
                <p className="mb-1 text-xs font-medium text-[var(--text-muted)]">Description</p>
                <p className="text-sm leading-relaxed whitespace-pre-wrap">{data.description}</p>
              </div>
            )}
            {data.resolution && (
              <div className="border-t px-5 py-4">
                <p className="mb-1 text-xs font-medium text-[var(--text-muted)]">Resolution</p>
                <p className="text-sm leading-relaxed whitespace-pre-wrap">{data.resolution}</p>
              </div>
            )}
          </Card>

          <Card>
            <CardHeader title="Case timeline" subtitle={`${data.notes.length} entries`} />
            {data.notes.length === 0 ? (
              <p className="px-5 py-8 text-center text-sm text-[var(--text-muted)]">No notes yet.</p>
            ) : (
              <ol className="p-5">
                {data.notes.map((entry, i) => (
                  <li key={entry.id} className="relative flex gap-4 pb-5 last:pb-0">
                    {i < data.notes.length - 1 && (
                      <span className="absolute top-5 left-[7px] h-full w-px bg-[var(--border)]" aria-hidden />
                    )}
                    <span className="relative mt-1.5 size-3.5 shrink-0 rounded-full border-2 border-[var(--surface-raised)] bg-[var(--accent-solid)]" />
                    <div className="min-w-0 flex-1">
                      <p className="text-sm leading-relaxed text-[var(--text)]">{entry.note}</p>
                      <p className="mt-0.5 text-xs text-[var(--text-subtle)]">
                        {entry.authorName ?? 'System'} · {formatRelative(entry.createdAt)}
                        {entry.statusAtNote && (
                          <> · <Badge tone={statusTone(entry.statusAtNote)}>{humanise(entry.statusAtNote)}</Badge></>
                        )}
                      </p>
                    </div>
                  </li>
                ))}
              </ol>
            )}
          </Card>
        </div>

        <div className="space-y-4">
          <DocumentPanel
            documents={data.documents}
            departmentId={data.departmentId}
            linkField="welfareCaseId"
            linkId={data.id}
            canUpload={canWrite}
            onChanged={() => void queryClient.invalidateQueries({ queryKey: ['welfare', id] })}
          />

          <Card>
            <CardHeader title="Record" />
            <dl className="space-y-3 p-5">
              <Field label="Department">{data.departmentName}</Field>
              <Field label="Created">{formatDateTime(data.openedAt)}</Field>
              <Field label="Closed">{formatDateTime(data.closedAt)}</Field>
            </dl>
          </Card>
        </div>
      </div>

      <Modal open={noteOpen} onClose={() => setNoteOpen(false)} title="Add a note"
        footer={
          <>
            <Button variant="ghost" onClick={() => setNoteOpen(false)}>Cancel</Button>
            <Button variant="primary" loading={addNote.isPending} disabled={!note.trim()}
              onClick={() => addNote.mutate()}>Add note</Button>
          </>
        }>
        <Textarea label="Note" rows={4} value={note} autoFocus
          placeholder="What happened, and what happens next?"
          onChange={(e) => setNote(e.target.value)} />
      </Modal>

      <Modal open={Boolean(transition)} onClose={() => setTransition(null)}
        title={`Move to ${humanise(transition ?? '')}`}
        description="A timeline entry is recorded against your name."
        footer={
          <>
            <Button variant="ghost" onClick={() => setTransition(null)}>Cancel</Button>
            <Button variant="primary" loading={changeStatus.isPending}
              onClick={() => transition && changeStatus.mutate(transition)}>Confirm</Button>
          </>
        }>
        <Textarea label="Comment (optional)" rows={3} value={comment}
          placeholder="Add context for the record."
          onChange={(e) => setComment(e.target.value)} />
      </Modal>

      <ConfirmDialog open={confirmDelete} onClose={() => setConfirmDelete(false)}
        onConfirm={() => remove.mutate()} loading={remove.isPending} danger
        title="Delete this case?" confirmLabel="Delete case"
        message="The case is removed from active lists. The record and its audit history are retained." />
    </>
  )
}
