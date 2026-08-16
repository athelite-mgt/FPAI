import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, FilePlus2, MessageCircleQuestion } from 'lucide-react'
import { api, describeError } from '../lib/api'
import { useAuth } from '../lib/auth'
import { formatCurrency, formatDate, formatDateTime, humanise } from '../lib/format'
import { useVendors } from '../lib/hooks'
import type { ExpenseDetail } from '../lib/types'
import {
  Badge, Button, Card, CardHeader, ErrorState, Field, Input, Modal, PageHeader, Select,
  Spinner, statusTone, Table, Td, Textarea, Th, Tr, useToast, WorkflowRail,
} from '../components/ui'
import DocumentPanel from '../components/DocumentPanel'
import QueryThread from '../components/QueryThread'

const FLOW = ['Created', 'InvoiceAttached', 'PendingApproval', 'AccountantReview', 'Reconciled', 'Closed']

export default function ExpenseDetailPage() {
  const { id = '' } = useParams()
  const toast = useToast()
  const queryClient = useQueryClient()
  const { canWriteDepartment, canApproveDepartment, hasRole, isSuperAdmin } = useAuth()
  const vendors = useVendors()

  const [transition, setTransition] = useState<string | null>(null)
  const [comment, setComment] = useState('')
  const [invoiceOpen, setInvoiceOpen] = useState(false)
  const [invoice, setInvoice] = useState({ vendorId: '', amount: '', taxAmount: '' })
  const [queryOpen, setQueryOpen] = useState(false)
  const [question, setQuestion] = useState('')

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['expense', id],
    queryFn: async () => (await api.get<ExpenseDetail>(`/finance/expenses/${id}`)).data,
    enabled: Boolean(id),
  })

  function invalidate() {
    void queryClient.invalidateQueries({ queryKey: ['expense', id] })
    void queryClient.invalidateQueries({ queryKey: ['expenses'] })
    void queryClient.invalidateQueries({ queryKey: ['finance-summary'] })
  }

  const changeStatus = useMutation({
    mutationFn: async (status: string) =>
      api.post(`/finance/expenses/${id}/status`, { status, comment: comment || undefined }),
    onSuccess: (_, status) => {
      toast.success(`Expense moved to ${humanise(status)}.`)
      setTransition(null)
      setComment('')
      invalidate()
    },
    onError: (e) => toast.error(describeError(e)),
  })

  const addInvoice = useMutation({
    mutationFn: async () =>
      api.post('/finance/invoices', {
        expenseId: id,
        vendorId: invoice.vendorId || null,
        amount: Number(invoice.amount),
        taxAmount: Number(invoice.taxAmount || 0),
      }),
    onSuccess: () => {
      toast.success('Invoice attached.')
      setInvoice({ vendorId: '', amount: '', taxAmount: '' })
      setInvoiceOpen(false)
      invalidate()
    },
    onError: (e) => toast.error(describeError(e)),
  })

  const raiseQuery = useMutation({
    mutationFn: async () => api.post('/finance/queries', { expenseId: id, question }),
    onSuccess: () => {
      toast.success('Query raised.')
      setQuestion('')
      setQueryOpen(false)
      invalidate()
    },
    onError: (e) => toast.error(describeError(e)),
  })

  if (isLoading) return <Spinner label="Loading expense" />
  if (isError || !data) {
    return <ErrorState message="This expense could not be loaded." onRetry={() => void refetch()} />
  }

  const isAccountant = hasRole('ExternalAccountant')
  const canWrite = canWriteDepartment(data.departmentId)
  const canApprove = canApproveDepartment(data.departmentId)

  function mayTransition(next: string) {
    if (next === 'AccountantReview' || next === 'Rejected') return canApprove
    if (next === 'Reconciled' || next === 'Closed') return isSuperAdmin || isAccountant
    return canWrite
  }
  const available = data.allowedTransitions.filter(mayTransition)

  return (
    <>
      <Link to="/finance"
        className="mb-3 inline-flex items-center gap-1.5 text-sm text-[var(--text-muted)] hover:text-[var(--text)]">
        <ArrowLeft className="size-4" /> Back to finance
      </Link>

      <PageHeader title={data.expenseNumber} subtitle={data.title}
        actions={
          <>
            {canWrite && (
              <Button icon={<FilePlus2 className="size-4" />} onClick={() => setInvoiceOpen(true)}>
                Attach invoice
              </Button>
            )}
            {(isAccountant || isSuperAdmin) && (
              <Button icon={<MessageCircleQuestion className="size-4" />} onClick={() => setQueryOpen(true)}>
                Raise query
              </Button>
            )}
            {data.status === 'PendingApproval' && canApprove && (
              <Button variant="danger" onClick={() => setTransition('Rejected')}>Reject</Button>
            )}
          </>
        }
      />

      <Card className="mb-4 p-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <WorkflowRail steps={FLOW} current={data.status === 'Rejected' ? 'PendingApproval' : data.status} />
          <div className="flex flex-wrap gap-2">
            {available.filter((s) => s !== 'Rejected').map((next) => (
              <Button key={next} size="sm" variant="primary" onClick={() => setTransition(next)}>
                {next === 'AccountantReview' ? 'Approve' : `Move to ${humanise(next)}`}
              </Button>
            ))}
          </div>
        </div>
        {data.status === 'Rejected' && data.rejectionReason && (
          <p className="mt-3 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-800 dark:border-red-500/30 dark:bg-red-950/40 dark:text-red-200">
            <strong>Rejected:</strong> {data.rejectionReason}
          </p>
        )}
      </Card>

      <div className="grid gap-4 lg:grid-cols-3">
        <div className="space-y-4 lg:col-span-2">
          <Card>
            <CardHeader title="Expense details" />
            <dl className="grid grid-cols-2 gap-4 p-5 sm:grid-cols-3">
              <Field label="Status"><Badge tone={statusTone(data.status)}>{humanise(data.status)}</Badge></Field>
              <Field label="Department">{data.departmentName}</Field>
              <Field label="Category">{data.category ?? '—'}</Field>
              <Field label="Amount">
                <span className="tabular font-semibold">{formatCurrency(data.amount)}</span>
              </Field>
              <Field label="Incurred">{formatDate(data.incurredOn)}</Field>
              <Field label="Submitted by">{data.submittedByName ?? '—'}</Field>
              <Field label="Approved by">{data.approvedByName ?? '—'}</Field>
              <Field label="Approved at">{formatDateTime(data.approvedAt)}</Field>
            </dl>
            {data.description && (
              <div className="border-t px-5 py-4">
                <p className="mb-1 text-xs font-medium text-[var(--text-muted)]">Description</p>
                <p className="text-sm leading-relaxed whitespace-pre-wrap">{data.description}</p>
              </div>
            )}
          </Card>

          <Card>
            <CardHeader title="Invoices" subtitle={`${data.invoices.length} attached`} />
            {data.invoices.length === 0 ? (
              <p className="px-5 py-8 text-center text-sm text-[var(--text-muted)]">
                No invoices attached yet.
              </p>
            ) : (
              <Table>
                <thead>
                  <tr>
                    <Th>Invoice</Th><Th>Vendor</Th>
                    <Th className="text-right">Amount</Th><Th className="text-right">Tax</Th>
                    <Th>Due</Th><Th>Status</Th>
                  </tr>
                </thead>
                <tbody>
                  {data.invoices.map((inv) => (
                    <Tr key={inv.id}>
                      <Td className="font-medium">{inv.invoiceNumber}</Td>
                      <Td className="text-[var(--text-muted)]">{inv.vendorName ?? '—'}</Td>
                      <Td className="tabular text-right">{formatCurrency(inv.amount)}</Td>
                      <Td className="tabular text-right text-[var(--text-muted)]">{formatCurrency(inv.taxAmount)}</Td>
                      <Td className="text-[var(--text-muted)]">{formatDate(inv.dueDate)}</Td>
                      <Td><Badge tone={statusTone(inv.status)}>{inv.status}</Badge></Td>
                    </Tr>
                  ))}
                </tbody>
              </Table>
            )}
          </Card>

          <QueryThread queries={data.queries} onChanged={invalidate}
            canAnswer={canWrite} canResolve={isAccountant || isSuperAdmin} />
        </div>

        <div className="space-y-4">
          <DocumentPanel documents={data.documents} departmentId={data.departmentId}
            linkField="expenseId" linkId={data.id} canUpload={canWrite} onChanged={invalidate} />
        </div>
      </div>

      <Modal open={Boolean(transition)} onClose={() => setTransition(null)}
        title={transition === 'Rejected' ? 'Reject this expense' : `Move to ${humanise(transition ?? '')}`}
        footer={
          <>
            <Button variant="ghost" onClick={() => setTransition(null)}>Cancel</Button>
            <Button variant={transition === 'Rejected' ? 'danger' : 'primary'}
              loading={changeStatus.isPending}
              disabled={transition === 'Rejected' && !comment.trim()}
              onClick={() => transition && changeStatus.mutate(transition)}>Confirm</Button>
          </>
        }>
        <Textarea rows={3} value={comment} onChange={(e) => setComment(e.target.value)}
          label={transition === 'Rejected' ? 'Reason for rejection' : 'Comment (optional)'}
          required={transition === 'Rejected'} />
      </Modal>

      <Modal open={invoiceOpen} onClose={() => setInvoiceOpen(false)} title="Attach an invoice"
        description="Attaching the first invoice advances the claim automatically."
        footer={
          <>
            <Button variant="ghost" onClick={() => setInvoiceOpen(false)}>Cancel</Button>
            <Button variant="primary" loading={addInvoice.isPending}
              disabled={!invoice.amount || Number(invoice.amount) <= 0}
              onClick={() => addInvoice.mutate()}>Attach</Button>
          </>
        }>
        <div className="space-y-4">
          <Select label="Vendor" value={invoice.vendorId} placeholder="None"
            options={(vendors.data ?? []).map((v) => ({ value: v.id, label: v.name }))}
            onChange={(e) => setInvoice({ ...invoice, vendorId: e.target.value })} />
          <div className="grid gap-4 sm:grid-cols-2">
            <Input label="Amount (INR)" type="number" min={0} step="0.01" required
              value={invoice.amount} onChange={(e) => setInvoice({ ...invoice, amount: e.target.value })} />
            <Input label="Tax (INR)" type="number" min={0} step="0.01"
              value={invoice.taxAmount} onChange={(e) => setInvoice({ ...invoice, taxAmount: e.target.value })} />
          </div>
        </div>
      </Modal>

      <Modal open={queryOpen} onClose={() => setQueryOpen(false)} title="Raise an accountant query"
        footer={
          <>
            <Button variant="ghost" onClick={() => setQueryOpen(false)}>Cancel</Button>
            <Button variant="primary" loading={raiseQuery.isPending} disabled={!question.trim()}
              onClick={() => raiseQuery.mutate()}>Raise query</Button>
          </>
        }>
        <Textarea label="Question" rows={3} value={question} autoFocus
          onChange={(e) => setQuestion(e.target.value)} />
      </Modal>
    </>
  )
}
