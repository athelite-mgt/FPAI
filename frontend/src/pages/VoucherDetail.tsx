import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, MessageCircleQuestion } from 'lucide-react'
import { api, describeError } from '../lib/api'
import { useAuth } from '../lib/auth'
import { formatCurrency, formatDateTime, humanise } from '../lib/format'
import type { VoucherDetail } from '../lib/types'
import {
  Badge, Button, Card, CardHeader, ErrorState, Field, Modal, PageHeader, Spinner,
  statusTone, Textarea, useToast, WorkflowRail,
} from '../components/ui'
import DocumentPanel from '../components/DocumentPanel'
import QueryThread from '../components/QueryThread'

const FLOW = ['Draft', 'Pending', 'Approved', 'Reconciled', 'Closed']

export default function VoucherDetailPage() {
  const { id = '' } = useParams()
  const toast = useToast()
  const queryClient = useQueryClient()
  const { canWriteDepartment, canApproveDepartment, hasRole, isSuperAdmin } = useAuth()

  const [transition, setTransition] = useState<string | null>(null)
  const [comment, setComment] = useState('')
  const [queryOpen, setQueryOpen] = useState(false)
  const [question, setQuestion] = useState('')

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['voucher', id],
    queryFn: async () => (await api.get<VoucherDetail>(`/finance/vouchers/${id}`)).data,
    enabled: Boolean(id),
  })

  function invalidate() {
    void queryClient.invalidateQueries({ queryKey: ['voucher', id] })
    void queryClient.invalidateQueries({ queryKey: ['vouchers'] })
    void queryClient.invalidateQueries({ queryKey: ['finance-summary'] })
  }

  const changeStatus = useMutation({
    mutationFn: async (status: string) =>
      api.post(`/finance/vouchers/${id}/status`, { status, comment: comment || undefined }),
    onSuccess: (_, status) => {
      toast.success(`Voucher moved to ${humanise(status)}.`)
      setTransition(null)
      setComment('')
      invalidate()
    },
    onError: (e) => toast.error(describeError(e)),
  })

  const raiseQuery = useMutation({
    mutationFn: async () => api.post('/finance/queries', { voucherId: id, question }),
    onSuccess: () => {
      toast.success('Query raised with the department.')
      setQuestion('')
      setQueryOpen(false)
      invalidate()
    },
    onError: (e) => toast.error(describeError(e)),
  })

  if (isLoading) return <Spinner label="Loading voucher" />
  if (isError || !data) {
    return <ErrorState message="This voucher could not be loaded." onRetry={() => void refetch()} />
  }

  const isAccountant = hasRole('ExternalAccountant')
  const canWrite = canWriteDepartment(data.departmentId)
  const canApprove = canApproveDepartment(data.departmentId)

  // Each transition has its own authority; mirror the server so we never offer a button that 403s.
  function mayTransition(next: string) {
    if (next === 'Approved' || next === 'Rejected') return canApprove
    if (next === 'Reconciled' || next === 'Closed') return isSuperAdmin || isAccountant
    return canWrite
  }
  const available = data.allowedTransitions.filter(mayTransition)
  const rejectionAvailable = data.status === 'Pending' && canApprove

  return (
    <>
      <Link to="/finance"
        className="mb-3 inline-flex items-center gap-1.5 text-sm text-[var(--text-muted)] hover:text-[var(--text)]">
        <ArrowLeft className="size-4" /> Back to finance
      </Link>

      <PageHeader title={data.voucherNumber} subtitle={`${data.vendorName} · ${data.departmentName}`}
        actions={
          <>
            {(isAccountant || isSuperAdmin) && (
              <Button icon={<MessageCircleQuestion className="size-4" />} onClick={() => setQueryOpen(true)}>
                Raise query
              </Button>
            )}
            {rejectionAvailable && (
              <Button variant="danger" onClick={() => setTransition('Rejected')}>Reject</Button>
            )}
          </>
        }
      />

      <Card className="mb-4 p-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <WorkflowRail steps={FLOW} current={data.status === 'Rejected' ? 'Pending' : data.status} />
          <div className="flex flex-wrap gap-2">
            {available.filter((s) => s !== 'Rejected').map((next) => (
              <Button key={next} size="sm" variant="primary" onClick={() => setTransition(next)}>
                {next === 'Pending' ? 'Submit for approval' : `Mark ${humanise(next)}`}
              </Button>
            ))}
          </div>
        </div>
        {data.status === 'Rejected' && data.rejectionReason && (
          <p className="mt-3 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-800 dark:border-red-500/30 dark:bg-red-950/40 dark:text-red-200">
            <strong>Rejected:</strong> {data.rejectionReason}
          </p>
        )}
        {data.queries.some((q) => q.status === 'Open') && (
          <p className="mt-3 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-900 dark:border-amber-500/30 dark:bg-amber-950/40 dark:text-amber-200">
            Open accountant queries must be resolved before this voucher can be reconciled.
          </p>
        )}
      </Card>

      <div className="grid gap-4 lg:grid-cols-3">
        <div className="space-y-4 lg:col-span-2">
          <Card>
            <CardHeader title="Voucher details" />
            <dl className="grid grid-cols-2 gap-4 p-5 sm:grid-cols-3">
              <Field label="Status"><Badge tone={statusTone(data.status)}>{humanise(data.status)}</Badge></Field>
              <Field label="Vendor">{data.vendorName}</Field>
              <Field label="Department">{data.departmentName}</Field>
              <Field label="Amount"><span className="tabular">{formatCurrency(data.amount)}</span></Field>
              <Field label="Tax"><span className="tabular">{formatCurrency(data.taxAmount)}</span></Field>
              <Field label="Total">
                <span className="tabular font-semibold">{formatCurrency(data.totalAmount)}</span>
              </Field>
              <Field label="Voucher date">{formatDateTime(data.voucherDate)}</Field>
              <Field label="Approved by">{data.approvedByName ?? '—'}</Field>
              <Field label="Approved at">{formatDateTime(data.approvedAt)}</Field>
              <Field label="Reconciled by">{data.reconciledByName ?? '—'}</Field>
              <Field label="Reconciled at">{formatDateTime(data.reconciledAt)}</Field>
            </dl>
            {data.description && (
              <div className="border-t px-5 py-4">
                <p className="mb-1 text-xs font-medium text-[var(--text-muted)]">Description</p>
                <p className="text-sm leading-relaxed whitespace-pre-wrap">{data.description}</p>
              </div>
            )}
          </Card>

          <QueryThread queries={data.queries} onChanged={invalidate}
            canAnswer={canWrite} canResolve={isAccountant || isSuperAdmin} />
        </div>

        <div className="space-y-4">
          <DocumentPanel documents={data.documents} departmentId={data.departmentId}
            linkField="voucherId" linkId={data.id} canUpload={canWrite} onChanged={invalidate} />
        </div>
      </div>

      <Modal open={Boolean(transition)} onClose={() => setTransition(null)}
        title={transition === 'Rejected' ? 'Reject this voucher' : `Mark ${humanise(transition ?? '')}`}
        description={transition === 'Rejected' ? 'A reason is required and is shown to the requester.' : undefined}
        footer={
          <>
            <Button variant="ghost" onClick={() => setTransition(null)}>Cancel</Button>
            <Button variant={transition === 'Rejected' ? 'danger' : 'primary'}
              loading={changeStatus.isPending}
              disabled={transition === 'Rejected' && !comment.trim()}
              onClick={() => transition && changeStatus.mutate(transition)}>
              Confirm
            </Button>
          </>
        }>
        <Textarea rows={3} value={comment} onChange={(e) => setComment(e.target.value)}
          label={transition === 'Rejected' ? 'Reason for rejection' : 'Comment (optional)'}
          required={transition === 'Rejected'} />
      </Modal>

      <Modal open={queryOpen} onClose={() => setQueryOpen(false)} title="Raise an accountant query"
        description="The department is asked to respond before reconciliation can proceed."
        footer={
          <>
            <Button variant="ghost" onClick={() => setQueryOpen(false)}>Cancel</Button>
            <Button variant="primary" loading={raiseQuery.isPending} disabled={!question.trim()}
              onClick={() => raiseQuery.mutate()}>Raise query</Button>
          </>
        }>
        <Textarea label="Question" rows={3} value={question} autoFocus
          placeholder="e.g. Please share the GST invoice and purchase order."
          onChange={(e) => setQuestion(e.target.value)} />
      </Modal>
    </>
  )
}
