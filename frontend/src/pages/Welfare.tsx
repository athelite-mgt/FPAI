import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Plus, ShieldCheck } from 'lucide-react'
import { api, describeError } from '../lib/api'
import { useAuth } from '../lib/auth'
import { enumOptions, useListState, usePagedQuery, usePlayerLookup, useUserLookup } from '../lib/hooks'
import { formatDate, humanise } from '../lib/format'
import type { WelfareCaseListItem } from '../lib/types'
import {
  Badge, Button, Card, Checkbox, EmptyState, ErrorState, Modal, PageHeader, Pagination,
  priorityTone, SearchInput, Select, SkeletonRows, StatTile, statusTone, Table, Td, Textarea,
  Th, Tr, useToast, Input,
} from '../components/ui'

const CATEGORIES = ['Medical', 'Contract', 'Salary', 'MentalHealth', 'Travel', 'Accommodation'] as const
const STATUSES = ['New', 'UnderReview', 'Assigned', 'InProgress', 'Resolved', 'Closed'] as const
const PRIORITIES = ['Low', 'Medium', 'High', 'Critical'] as const

interface Stats {
  openCases: number; closedCases: number; disputes: number; welfareRequests: number
}

function NewCaseModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const toast = useToast()
  const queryClient = useQueryClient()
  const players = usePlayerLookup()
  const users = useUserLookup()
  const navigate = useNavigate()

  const [form, setForm] = useState({
    title: '', playerId: '', category: 'Medical', priority: 'Medium',
    assignedOfficerId: '', description: '', isDispute: false,
  })
  const [errors, setErrors] = useState<Record<string, string>>({})

  const mutation = useMutation({
    mutationFn: async () =>
      (await api.post('/welfare/cases', {
        ...form,
        assignedOfficerId: form.assignedOfficerId || null,
      })).data,
    onSuccess: (created: { id: string; caseNumber: string }) => {
      toast.success(`Case ${created.caseNumber} opened.`)
      void queryClient.invalidateQueries({ queryKey: ['welfare'] })
      void queryClient.invalidateQueries({ queryKey: ['welfare-stats'] })
      onClose()
      navigate(`/welfare/${created.id}`)
    },
    onError: (error) => toast.error(describeError(error)),
  })

  function submit(e: React.FormEvent) {
    e.preventDefault()
    const next: Record<string, string> = {}
    if (!form.title.trim()) next.title = 'A short title is required.'
    if (!form.playerId) next.playerId = 'Select the member this case concerns.'
    setErrors(next)
    if (Object.keys(next).length) return
    mutation.mutate()
  }

  return (
    <Modal open={open} onClose={onClose} title="Open a welfare case"
      description="The case is registered against a member and assigned to the welfare desk."
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button variant="primary" loading={mutation.isPending} onClick={submit}>Open case</Button>
        </>
      }>
      <form onSubmit={submit} className="space-y-4" noValidate>
        <Input label="Title" required value={form.title} error={errors.title}
          placeholder="e.g. Three months of unpaid wages"
          onChange={(e) => setForm({ ...form, title: e.target.value })} />

        <Select label="Member" required value={form.playerId} error={errors.playerId}
          placeholder="Select a member…"
          options={(players.data ?? []).map((p) => ({ value: p.id, label: `${p.label} · ${p.sub}` }))}
          onChange={(e) => setForm({ ...form, playerId: e.target.value })} />

        <div className="grid gap-4 sm:grid-cols-2">
          <Select label="Category" value={form.category} options={enumOptions(CATEGORIES)}
            onChange={(e) => setForm({ ...form, category: e.target.value })} />
          <Select label="Priority" value={form.priority} options={enumOptions(PRIORITIES)}
            onChange={(e) => setForm({ ...form, priority: e.target.value })} />
        </div>

        <Select label="Assign officer" value={form.assignedOfficerId}
          placeholder="Leave unassigned for triage"
          hint="Assigning an officer opens the case at the Assigned stage."
          options={(users.data ?? []).map((u) => ({ value: u.id, label: u.label }))}
          onChange={(e) => setForm({ ...form, assignedOfficerId: e.target.value })} />

        <Textarea label="Description" rows={3} value={form.description}
          placeholder="What did the member report?"
          onChange={(e) => setForm({ ...form, description: e.target.value })} />

        <Checkbox label="This is a formal dispute rather than a welfare request"
          checked={form.isDispute}
          onChange={(e) => setForm({ ...form, isDispute: e.target.checked })} />
      </form>
    </Modal>
  )
}

export default function Welfare() {
  const navigate = useNavigate()
  const { hasRole } = useAuth()
  const list = useListState('openedAt')
  const [creating, setCreating] = useState(false)

  const { data, isLoading, isError, refetch } = usePagedQuery<WelfareCaseListItem>(
    'welfare', '/welfare/cases', list,
  )
  const stats = useQuery({
    queryKey: ['welfare-stats'],
    queryFn: async () => (await api.get<Stats>('/welfare/cases/stats')).data,
  })

  const canCreate = hasRole('SuperAdmin', 'DepartmentHead', 'Staff')

  return (
    <>
      <PageHeader
        title="Player Welfare & Liaison"
        subtitle="Medical, contract, salary and wellbeing casework for FPAI members."
        actions={canCreate && (
          <Button variant="primary" icon={<Plus className="size-4" />} onClick={() => setCreating(true)}>
            New welfare case
          </Button>
        )}
      />

      <div className="mb-4 grid grid-cols-2 gap-4 lg:grid-cols-4">
        <StatTile label="Open cases" value={stats.data?.openCases ?? '—'} tone="warning" />
        <StatTile label="Closed cases" value={stats.data?.closedCases ?? '—'} tone="success" />
        <StatTile label="Disputes" value={stats.data?.disputes ?? '—'} tone="danger" />
        <StatTile label="Welfare requests" value={stats.data?.welfareRequests ?? '—'} tone="info" />
      </div>

      <Card>
        <div className="flex flex-wrap items-center gap-2 border-b px-4 py-3">
          <SearchInput value={list.search} onChange={list.setSearch}
            placeholder="Search case number, title or member…" />
          <Select className="h-9 w-auto" placeholder="All statuses" value={list.filters.status ?? ''}
            options={enumOptions(STATUSES)} onChange={(e) => list.setFilter('status', e.target.value)} />
          <Select className="h-9 w-auto" placeholder="All categories" value={list.filters.category ?? ''}
            options={enumOptions(CATEGORIES)} onChange={(e) => list.setFilter('category', e.target.value)} />
          <Select className="h-9 w-auto" placeholder="All priorities" value={list.filters.priority ?? ''}
            options={enumOptions(PRIORITIES)} onChange={(e) => list.setFilter('priority', e.target.value)} />
          <Select className="h-9 w-auto" placeholder="All kinds" value={list.filters.isDispute ?? ''}
            options={[{ value: 'true', label: 'Disputes' }, { value: 'false', label: 'Requests' }]}
            onChange={(e) => list.setFilter('isDispute', e.target.value)} />
          {(list.search || Object.keys(list.filters).length > 0) && (
            <Button size="sm" variant="ghost" onClick={list.reset}>Clear</Button>
          )}
        </div>

        {isError ? (
          <ErrorState message="Welfare cases could not be loaded." onRetry={() => void refetch()} />
        ) : (
          <>
            <Table>
              <thead>
                <tr>
                  <Th sortable active={list.sortBy === 'caseNumber'} descending={list.sortDescending}
                    onSort={() => list.toggleSort('caseNumber')}>Case</Th>
                  <Th>Member</Th>
                  <Th>Category</Th>
                  <Th sortable active={list.sortBy === 'priority'} descending={list.sortDescending}
                    onSort={() => list.toggleSort('priority')}>Priority</Th>
                  <Th>Officer</Th>
                  <Th sortable active={list.sortBy === 'openedAt'} descending={list.sortDescending}
                    onSort={() => list.toggleSort('openedAt')}>Opened</Th>
                  <Th sortable active={list.sortBy === 'status'} descending={list.sortDescending}
                    onSort={() => list.toggleSort('status')}>Status</Th>
                </tr>
              </thead>
              {isLoading ? (
                <SkeletonRows cols={7} />
              ) : (
                <tbody>
                  {data?.items.map((row) => (
                    <Tr key={row.id} onClick={() => navigate(`/welfare/${row.id}`)}>
                      <Td className="font-medium whitespace-nowrap">
                        {row.caseNumber}
                        {row.isDispute && <Badge tone="danger" className="ml-2">Dispute</Badge>}
                        <p className="mt-0.5 max-w-xs truncate text-xs font-normal text-[var(--text-muted)]">
                          {row.title}
                        </p>
                      </Td>
                      <Td className="whitespace-nowrap">{row.playerName}</Td>
                      <Td><Badge>{humanise(row.category)}</Badge></Td>
                      <Td><Badge tone={priorityTone(row.priority)}>{row.priority}</Badge></Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">
                        {row.assignedOfficerName ?? 'Unassigned'}
                      </Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">{formatDate(row.openedAt)}</Td>
                      <Td><Badge tone={statusTone(row.status)}>{humanise(row.status)}</Badge></Td>
                    </Tr>
                  ))}
                </tbody>
              )}
            </Table>

            {!isLoading && data?.items.length === 0 && (
              <EmptyState icon={<ShieldCheck className="size-5" />}
                title="No welfare cases match these filters"
                description="Adjust the filters, or open a new case."
                action={canCreate && <Button variant="primary" onClick={() => setCreating(true)}>New welfare case</Button>} />
            )}

            {data && (
              <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount}
                totalPages={data.totalPages} onPage={list.setPage} />
            )}
          </>
        )}
      </Card>

      <NewCaseModal open={creating} onClose={() => setCreating(false)} />
    </>
  )
}
