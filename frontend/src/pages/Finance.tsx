import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Area, AreaChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts'
import { Banknote, Plus, Receipt } from 'lucide-react'
import { api, describeError } from '../lib/api'
import { useAuth } from '../lib/auth'
import { useChartColors } from '../lib/preferences'
import { enumOptions, useDepartments, useListState, usePagedQuery, useVendors } from '../lib/hooks'
import { formatCompactCurrency, formatCurrency, formatDate, humanise } from '../lib/format'
import type { ExpenseListItem, FinanceSummary, VoucherListItem } from '../lib/types'
import {
  Badge, Button, Card, CardHeader, EmptyState, ErrorState, Input, Modal, PageHeader, Pagination,
  SearchInput, Select, SkeletonRows, StatTile, statusTone, Table, Td, Textarea, Th, Tr, useToast,
} from '../components/ui'

const VOUCHER_STATUSES = ['Draft', 'Pending', 'Approved', 'Rejected', 'Reconciled', 'Closed'] as const
const EXPENSE_STATUSES = [
  'Created', 'InvoiceAttached', 'PendingApproval', 'AccountantReview', 'Reconciled', 'Closed', 'Rejected',
] as const

function NewVoucherModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const toast = useToast()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const vendors = useVendors()
  const departments = useDepartments()
  const { user, isSuperAdmin } = useAuth()

  const [form, setForm] = useState({
    vendorId: '', departmentId: user?.departmentId ?? '', amount: '', taxAmount: '', description: '',
  })
  const [errors, setErrors] = useState<Record<string, string>>({})

  const mutation = useMutation({
    mutationFn: async () =>
      (await api.post('/finance/vouchers', {
        vendorId: form.vendorId,
        departmentId: form.departmentId,
        amount: Number(form.amount),
        taxAmount: Number(form.taxAmount || 0),
        description: form.description || null,
      })).data,
    onSuccess: (created: { id: string; voucherNumber: string }) => {
      toast.success(`Voucher ${created.voucherNumber} created as a draft.`)
      void queryClient.invalidateQueries({ queryKey: ['vouchers'] })
      onClose()
      navigate(`/finance/vouchers/${created.id}`)
    },
    onError: (e) => toast.error(describeError(e)),
  })

  function submit(e: React.FormEvent) {
    e.preventDefault()
    const next: Record<string, string> = {}
    if (!form.vendorId) next.vendorId = 'Select a vendor.'
    if (!form.departmentId) next.departmentId = 'Select a department.'
    if (!form.amount || Number(form.amount) <= 0) next.amount = 'Enter an amount greater than zero.'
    if (form.taxAmount && Number(form.taxAmount) < 0) next.taxAmount = 'Tax cannot be negative.'
    setErrors(next)
    if (Object.keys(next).length) return
    mutation.mutate()
  }

  const total = (Number(form.amount) || 0) + (Number(form.taxAmount) || 0)
  // Staff and heads may only raise vouchers for their own department.
  const departmentOptions = (departments.data ?? [])
    .filter((d) => isSuperAdmin || d.id === user?.departmentId)
    .map((d) => ({ value: d.id, label: d.name }))

  return (
    <Modal open={open} onClose={onClose} title="New payment voucher"
      description="Vouchers start as a draft; submit for approval when the details are final."
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button variant="primary" loading={mutation.isPending} onClick={submit}>Create draft</Button>
        </>
      }>
      <form onSubmit={submit} className="space-y-4" noValidate>
        <Select label="Vendor" required value={form.vendorId} error={errors.vendorId}
          placeholder="Select a vendor…"
          options={(vendors.data ?? []).map((v) => ({ value: v.id, label: v.name }))}
          onChange={(e) => setForm({ ...form, vendorId: e.target.value })} />
        <Select label="Department" required value={form.departmentId} error={errors.departmentId}
          placeholder="Select a department…" options={departmentOptions}
          onChange={(e) => setForm({ ...form, departmentId: e.target.value })} />
        <div className="grid gap-4 sm:grid-cols-2">
          <Input label="Amount (INR)" type="number" min={0} step="0.01" required
            value={form.amount} error={errors.amount}
            onChange={(e) => setForm({ ...form, amount: e.target.value })} />
          <Input label="Tax (INR)" type="number" min={0} step="0.01"
            value={form.taxAmount} error={errors.taxAmount}
            onChange={(e) => setForm({ ...form, taxAmount: e.target.value })} />
        </div>
        <p className="rounded-lg bg-[var(--surface-sunken)] px-3 py-2 text-sm">
          Total payable: <span className="tabular font-semibold">{formatCurrency(total)}</span>
        </p>
        <Textarea label="Description" rows={2} value={form.description}
          onChange={(e) => setForm({ ...form, description: e.target.value })} />
      </form>
    </Modal>
  )
}

function NewExpenseModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const toast = useToast()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const departments = useDepartments()
  const { user, isSuperAdmin } = useAuth()

  const [form, setForm] = useState({
    title: '', departmentId: user?.departmentId ?? '', category: 'Travel', amount: '', description: '',
  })
  const [errors, setErrors] = useState<Record<string, string>>({})

  const mutation = useMutation({
    mutationFn: async () =>
      (await api.post('/finance/expenses', { ...form, amount: Number(form.amount) })).data,
    onSuccess: (created: { id: string; expenseNumber: string }) => {
      toast.success(`Expense ${created.expenseNumber} created.`)
      void queryClient.invalidateQueries({ queryKey: ['expenses'] })
      onClose()
      navigate(`/finance/expenses/${created.id}`)
    },
    onError: (e) => toast.error(describeError(e)),
  })

  function submit(e: React.FormEvent) {
    e.preventDefault()
    const next: Record<string, string> = {}
    if (!form.title.trim()) next.title = 'A title is required.'
    if (!form.departmentId) next.departmentId = 'Select a department.'
    if (!form.amount || Number(form.amount) <= 0) next.amount = 'Enter an amount greater than zero.'
    setErrors(next)
    if (Object.keys(next).length) return
    mutation.mutate()
  }

  const departmentOptions = (departments.data ?? [])
    .filter((d) => isSuperAdmin || d.id === user?.departmentId)
    .map((d) => ({ value: d.id, label: d.name }))

  return (
    <Modal open={open} onClose={onClose} title="New expense claim"
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button variant="primary" loading={mutation.isPending} onClick={submit}>Create claim</Button>
        </>
      }>
      <form onSubmit={submit} className="space-y-4" noValidate>
        <Input label="Title" required value={form.title} error={errors.title}
          placeholder="e.g. Travel for the Kolkata workshop"
          onChange={(e) => setForm({ ...form, title: e.target.value })} />
        <div className="grid gap-4 sm:grid-cols-2">
          <Select label="Department" required value={form.departmentId} error={errors.departmentId}
            placeholder="Select…" options={departmentOptions}
            onChange={(e) => setForm({ ...form, departmentId: e.target.value })} />
          <Select label="Category" value={form.category}
            options={enumOptions(['Travel', 'Legal fees', 'Medical', 'Venue hire', 'Printing', 'Software'])}
            onChange={(e) => setForm({ ...form, category: e.target.value })} />
        </div>
        <Input label="Amount (INR)" type="number" min={0} step="0.01" required
          value={form.amount} error={errors.amount}
          onChange={(e) => setForm({ ...form, amount: e.target.value })} />
        <Textarea label="Description" rows={2} value={form.description}
          onChange={(e) => setForm({ ...form, description: e.target.value })} />
      </form>
    </Modal>
  )
}

const AXIS = { fontSize: 11, fill: 'var(--text-subtle)' }

export default function Finance() {
  const navigate = useNavigate()
  const colors = useChartColors()
  const { hasRole } = useAuth()
  const [tab, setTab] = useState<'vouchers' | 'expenses'>('vouchers')
  const [newVoucher, setNewVoucher] = useState(false)
  const [newExpense, setNewExpense] = useState(false)

  const voucherList = useListState('voucherDate')
  const expenseList = useListState('incurredOn')
  const departments = useDepartments()

  const summary = useQuery({
    queryKey: ['finance-summary'],
    queryFn: async () => (await api.get<FinanceSummary>('/finance/summary')).data,
  })

  const vouchers = usePagedQuery<VoucherListItem>('vouchers', '/finance/vouchers', voucherList)
  const expenses = usePagedQuery<ExpenseListItem>('expenses', '/finance/expenses', expenseList)

  const canWrite = hasRole('SuperAdmin', 'DepartmentHead', 'Staff')
  const active = tab === 'vouchers' ? voucherList : expenseList

  return (
    <>
      <PageHeader
        title="Finance & Accounts"
        subtitle="Vouchers, expense claims, invoices and accountant reconciliation."
        actions={canWrite && (
          <>
            <Button icon={<Receipt className="size-4" />} onClick={() => setNewExpense(true)}>
              New expense
            </Button>
            <Button variant="primary" icon={<Plus className="size-4" />} onClick={() => setNewVoucher(true)}>
              New voucher
            </Button>
          </>
        )}
      />

      <div className="mb-4 grid grid-cols-2 gap-4 lg:grid-cols-4">
        <StatTile label="Monthly income" tone="success"
          value={summary.data ? formatCompactCurrency(summary.data.monthlyIncome) : '—'} />
        <StatTile label="Monthly expense" tone="warning"
          value={summary.data ? formatCompactCurrency(summary.data.monthlyExpense) : '—'} />
        <StatTile label="Pending vouchers" tone="info" value={summary.data?.pendingVouchers ?? '—'} />
        <StatTile label="Open accountant queries" tone="danger" value={summary.data?.openQueries ?? '—'} />
      </div>

      <Card className="mb-4">
        <CardHeader title="Income vs expense" subtitle="Last six months" />
        <div className="h-56 p-4">
          <ResponsiveContainer width="100%" height="100%">
            <AreaChart data={summary.data?.trend ?? []} margin={{ top: 4, right: 8, left: 0, bottom: 0 }}>
              <defs>
                <linearGradient id="fin-in" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stopColor={colors.accent} stopOpacity={0.35} />
                  <stop offset="100%" stopColor={colors.accent} stopOpacity={0} />
                </linearGradient>
                <linearGradient id="fin-out" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stopColor={colors.contrast} stopOpacity={0.3} />
                  <stop offset="100%" stopColor={colors.contrast} stopOpacity={0} />
                </linearGradient>
              </defs>
              <CartesianGrid strokeDasharray="3 3" stroke="var(--border)" vertical={false} />
              <XAxis dataKey="label" tick={AXIS} axisLine={false} tickLine={false} />
              <YAxis tick={AXIS} axisLine={false} tickLine={false} width={56}
                tickFormatter={(v) => formatCompactCurrency(v)} />
              <Tooltip formatter={(value) => formatCurrency(Number(value))}
                contentStyle={{
                  background: 'var(--surface-raised)', border: '1px solid var(--border)',
                  borderRadius: 8, fontSize: 12,
                }} />
              <Legend wrapperStyle={{ fontSize: 12 }} />
              <Area type="monotone" dataKey="income" name="Income" stroke={colors.accent} fill="url(#fin-in)" strokeWidth={2} />
              <Area type="monotone" dataKey="expense" name="Expense" stroke={colors.contrast} fill="url(#fin-out)" strokeWidth={2} />
            </AreaChart>
          </ResponsiveContainer>
        </div>
      </Card>

      <Card>
        <div className="flex flex-wrap items-center gap-2 border-b px-4 py-3">
          <div className="mr-2 flex rounded-lg bg-[var(--surface-sunken)] p-0.5">
            {(['vouchers', 'expenses'] as const).map((key) => (
              <button key={key} onClick={() => setTab(key)}
                className={`rounded-md px-3 py-1.5 text-sm font-medium capitalize transition-colors ${
                  tab === key ? 'bg-[var(--surface-raised)] text-[var(--text)] shadow-sm' : 'text-[var(--text-muted)]'
                }`}>
                {key}
              </button>
            ))}
          </div>

          <SearchInput value={active.search} onChange={active.setSearch}
            placeholder={tab === 'vouchers' ? 'Search voucher or vendor…' : 'Search expense…'} />
          <Select className="h-9 w-auto" placeholder="All statuses" value={active.filters.status ?? ''}
            options={enumOptions(tab === 'vouchers' ? VOUCHER_STATUSES : EXPENSE_STATUSES)}
            onChange={(e) => active.setFilter('status', e.target.value)} />
          <Select className="h-9 w-auto" placeholder="All departments" value={active.filters.departmentId ?? ''}
            options={(departments.data ?? []).map((d) => ({ value: d.id, label: d.name }))}
            onChange={(e) => active.setFilter('departmentId', e.target.value)} />
          {(active.search || Object.keys(active.filters).length > 0) && (
            <Button size="sm" variant="ghost" onClick={active.reset}>Clear</Button>
          )}
        </div>

        {tab === 'vouchers' ? (
          vouchers.isError ? (
            <ErrorState message="Vouchers could not be loaded." onRetry={() => void vouchers.refetch()} />
          ) : (
            <>
              <Table>
                <thead>
                  <tr>
                    <Th>Voucher</Th>
                    <Th sortable active={voucherList.sortBy === 'vendor'} descending={voucherList.sortDescending}
                      onSort={() => voucherList.toggleSort('vendor')}>Vendor</Th>
                    <Th>Department</Th>
                    <Th className="text-right">Amount</Th>
                    <Th className="text-right">Tax</Th>
                    <Th sortable active={voucherList.sortBy === 'amount'} descending={voucherList.sortDescending}
                      onSort={() => voucherList.toggleSort('amount')} className="text-right">Total</Th>
                    <Th sortable active={voucherList.sortBy === 'voucherDate'} descending={voucherList.sortDescending}
                      onSort={() => voucherList.toggleSort('voucherDate')}>Date</Th>
                    <Th>Status</Th>
                  </tr>
                </thead>
                {vouchers.isLoading ? <SkeletonRows cols={8} /> : (
                  <tbody>
                    {vouchers.data?.items.map((row) => (
                      <Tr key={row.id} onClick={() => navigate(`/finance/vouchers/${row.id}`)}>
                        <Td className="font-medium whitespace-nowrap">{row.voucherNumber}</Td>
                        <Td className="whitespace-nowrap">{row.vendorName}</Td>
                        <Td className="whitespace-nowrap text-[var(--text-muted)]">{row.departmentName}</Td>
                        <Td className="tabular text-right">{formatCurrency(row.amount)}</Td>
                        <Td className="tabular text-right text-[var(--text-muted)]">{formatCurrency(row.taxAmount)}</Td>
                        <Td className="tabular text-right font-medium">{formatCurrency(row.totalAmount)}</Td>
                        <Td className="whitespace-nowrap text-[var(--text-muted)]">{formatDate(row.voucherDate)}</Td>
                        <Td>
                          <Badge tone={statusTone(row.status)}>{humanise(row.status)}</Badge>
                          {row.openQueryCount > 0 && (
                            <Badge tone="danger" className="ml-1">{row.openQueryCount} query</Badge>
                          )}
                        </Td>
                      </Tr>
                    ))}
                  </tbody>
                )}
              </Table>
              {!vouchers.isLoading && vouchers.data?.items.length === 0 && (
                <EmptyState icon={<Banknote className="size-5" />} title="No vouchers match these filters" />
              )}
              {vouchers.data && (
                <Pagination page={vouchers.data.page} pageSize={vouchers.data.pageSize}
                  totalCount={vouchers.data.totalCount} totalPages={vouchers.data.totalPages}
                  onPage={voucherList.setPage} />
              )}
            </>
          )
        ) : expenses.isError ? (
          <ErrorState message="Expenses could not be loaded." onRetry={() => void expenses.refetch()} />
        ) : (
          <>
            <Table>
              <thead>
                <tr>
                  <Th>Expense</Th>
                  <Th>Title</Th>
                  <Th>Department</Th>
                  <Th>Category</Th>
                  <Th sortable active={expenseList.sortBy === 'amount'} descending={expenseList.sortDescending}
                    onSort={() => expenseList.toggleSort('amount')} className="text-right">Amount</Th>
                  <Th>Submitted by</Th>
                  <Th sortable active={expenseList.sortBy === 'incurredOn'} descending={expenseList.sortDescending}
                    onSort={() => expenseList.toggleSort('incurredOn')}>Incurred</Th>
                  <Th>Status</Th>
                </tr>
              </thead>
              {expenses.isLoading ? <SkeletonRows cols={8} /> : (
                <tbody>
                  {expenses.data?.items.map((row) => (
                    <Tr key={row.id} onClick={() => navigate(`/finance/expenses/${row.id}`)}>
                      <Td className="font-medium whitespace-nowrap">{row.expenseNumber}</Td>
                      <Td className="max-w-xs truncate">{row.title}</Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">{row.departmentName}</Td>
                      <Td>{row.category ? <Badge>{row.category}</Badge> : '—'}</Td>
                      <Td className="tabular text-right font-medium">{formatCurrency(row.amount)}</Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">{row.submittedByName ?? '—'}</Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">{formatDate(row.incurredOn)}</Td>
                      <Td><Badge tone={statusTone(row.status)}>{humanise(row.status)}</Badge></Td>
                    </Tr>
                  ))}
                </tbody>
              )}
            </Table>
            {!expenses.isLoading && expenses.data?.items.length === 0 && (
              <EmptyState icon={<Receipt className="size-5" />} title="No expense claims match these filters" />
            )}
            {expenses.data && (
              <Pagination page={expenses.data.page} pageSize={expenses.data.pageSize}
                totalCount={expenses.data.totalCount} totalPages={expenses.data.totalPages}
                onPage={expenseList.setPage} />
            )}
          </>
        )}
      </Card>

      <NewVoucherModal open={newVoucher} onClose={() => setNewVoucher(false)} />
      <NewExpenseModal open={newExpense} onClose={() => setNewExpense(false)} />
    </>
  )
}
