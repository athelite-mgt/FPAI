import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Gavel, Plus } from 'lucide-react'
import { api, describeError } from '../lib/api'
import { useAuth } from '../lib/auth'
import { enumOptions, useListState, usePagedQuery, usePlayerLookup, useUserLookup } from '../lib/hooks'
import { formatCompactCurrency, formatDate, humanise } from '../lib/format'
import type { Club, LegalCaseListItem, PagedResult } from '../lib/types'
import {
  Badge, Button, Card, EmptyState, ErrorState, Input, Modal, PageHeader, Pagination,
  SearchInput, Select, SkeletonRows, StatTile, statusTone, Table, Td, Textarea,
  Th, Tr, useToast,
} from '../components/ui'

const TYPES = ['FifaDrc', 'Cas', 'Psc', 'Arbitration'] as const
const STATUSES = [
  'Registered', 'DocumentsPending', 'Filed', 'HearingScheduled', 'DecisionReceived', 'Closed',
] as const
const OUTCOMES = ['Pending', 'Won', 'Lost', 'Settled', 'Withdrawn'] as const
const PRIORITIES = ['Low', 'Medium', 'High', 'Critical'] as const

interface Stats {
  active: number; closed: number; upcomingHearings: number
  totalClaimed: number; totalAwarded: number
}

function NewCaseModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const toast = useToast()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const players = usePlayerLookup()
  const counsel = useUserLookup()

  const clubs = useQuery({
    queryKey: ['clubs', 'all'],
    queryFn: async () =>
      (await api.get<PagedResult<Club>>('/clubs', { params: { pageSize: 200 } })).data.items,
    staleTime: 5 * 60_000,
  })

  const [form, setForm] = useState({
    title: '', playerId: '', opposingClubId: '', type: 'FifaDrc', priority: 'Medium',
    lawyerName: '', lawyerFirm: '', assignedCounselId: '', claimAmount: '', description: '',
  })
  const [errors, setErrors] = useState<Record<string, string>>({})

  const mutation = useMutation({
    mutationFn: async () =>
      (await api.post('/legal/cases', {
        ...form,
        opposingClubId: form.opposingClubId || null,
        assignedCounselId: form.assignedCounselId || null,
        claimAmount: form.claimAmount ? Number(form.claimAmount) : null,
      })).data,
    onSuccess: (created: { id: string; caseNumber: string }) => {
      toast.success(`Matter ${created.caseNumber} registered.`)
      void queryClient.invalidateQueries({ queryKey: ['legal'] })
      void queryClient.invalidateQueries({ queryKey: ['legal-stats'] })
      onClose()
      navigate(`/legal/${created.id}`)
    },
    onError: (e) => toast.error(describeError(e)),
  })

  function submit(e: React.FormEvent) {
    e.preventDefault()
    const next: Record<string, string> = {}
    if (!form.title.trim()) next.title = 'A title is required.'
    if (!form.playerId) next.playerId = 'Select the member this matter concerns.'
    if (form.claimAmount && Number(form.claimAmount) < 0) next.claimAmount = 'Cannot be negative.'
    setErrors(next)
    if (Object.keys(next).length) return
    mutation.mutate()
  }

  return (
    <Modal open={open} onClose={onClose} wide title="Register a legal matter"
      description="The case number is allocated automatically from the forum and year."
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button variant="primary" loading={mutation.isPending} onClick={submit}>Register matter</Button>
        </>
      }>
      <form onSubmit={submit} className="space-y-4" noValidate>
        <Input label="Title" required value={form.title} error={errors.title}
          placeholder="e.g. Unpaid wages and termination with just cause"
          onChange={(e) => setForm({ ...form, title: e.target.value })} />

        <div className="grid gap-4 sm:grid-cols-2">
          <Select label="Member" required value={form.playerId} error={errors.playerId}
            placeholder="Select a member…"
            options={(players.data ?? []).map((p) => ({ value: p.id, label: p.label }))}
            onChange={(e) => setForm({ ...form, playerId: e.target.value })} />
          <Select label="Opposing club" value={form.opposingClubId} placeholder="None"
            options={(clubs.data ?? []).map((c) => ({ value: c.id, label: c.name }))}
            onChange={(e) => setForm({ ...form, opposingClubId: e.target.value })} />
        </div>

        <div className="grid gap-4 sm:grid-cols-3">
          <Select label="Forum" value={form.type} options={enumOptions(TYPES)}
            onChange={(e) => setForm({ ...form, type: e.target.value })} />
          <Select label="Priority" value={form.priority} options={enumOptions(PRIORITIES)}
            onChange={(e) => setForm({ ...form, priority: e.target.value })} />
          <Input label="Claim amount (INR)" type="number" min={0} value={form.claimAmount}
            error={errors.claimAmount}
            onChange={(e) => setForm({ ...form, claimAmount: e.target.value })} />
        </div>

        <div className="grid gap-4 sm:grid-cols-3">
          <Input label="Lawyer" value={form.lawyerName} placeholder="Adv. Mehta"
            onChange={(e) => setForm({ ...form, lawyerName: e.target.value })} />
          <Input label="Firm" value={form.lawyerFirm} placeholder="Mehta & Associates"
            onChange={(e) => setForm({ ...form, lawyerFirm: e.target.value })} />
          <Select label="Internal counsel" value={form.assignedCounselId} placeholder="Unassigned"
            options={(counsel.data ?? []).map((u) => ({ value: u.id, label: u.label }))}
            onChange={(e) => setForm({ ...form, assignedCounselId: e.target.value })} />
        </div>

        <Textarea label="Description" rows={3} value={form.description}
          onChange={(e) => setForm({ ...form, description: e.target.value })} />
      </form>
    </Modal>
  )
}

export default function Legal() {
  const navigate = useNavigate()
  const { hasRole } = useAuth()
  const list = useListState('filedAt')
  const [creating, setCreating] = useState(false)

  const { data, isLoading, isError, refetch } = usePagedQuery<LegalCaseListItem>(
    'legal', '/legal/cases', list,
  )
  const stats = useQuery({
    queryKey: ['legal-stats'],
    queryFn: async () => (await api.get<Stats>('/legal/cases/stats')).data,
  })

  const canCreate = hasRole('SuperAdmin', 'DepartmentHead', 'Staff')

  return (
    <>
      <PageHeader
        title="Legal Affairs"
        subtitle="FIFA DRC, CAS, PSC and arbitration matters brought on behalf of members."
        actions={canCreate && (
          <Button variant="primary" icon={<Plus className="size-4" />} onClick={() => setCreating(true)}>
            New legal matter
          </Button>
        )}
      />

      <div className="mb-4 grid grid-cols-2 gap-4 lg:grid-cols-4">
        <StatTile label="Active matters" value={stats.data?.active ?? '—'} tone="warning" />
        <StatTile label="Closed" value={stats.data?.closed ?? '—'} tone="success" />
        <StatTile label="Upcoming hearings" value={stats.data?.upcomingHearings ?? '—'} tone="info" />
        <StatTile label="Total claimed"
          value={stats.data ? formatCompactCurrency(stats.data.totalClaimed) : '—'} tone="accent"
          sub={stats.data ? `${formatCompactCurrency(stats.data.totalAwarded)} awarded` : undefined} />
      </div>

      <Card>
        <div className="flex flex-wrap items-center gap-2 border-b px-4 py-3">
          <SearchInput value={list.search} onChange={list.setSearch}
            placeholder="Search case number, member or lawyer…" />
          <Select className="h-9 w-auto" placeholder="All forums" value={list.filters.type ?? ''}
            options={enumOptions(TYPES)} onChange={(e) => list.setFilter('type', e.target.value)} />
          <Select className="h-9 w-auto" placeholder="All statuses" value={list.filters.status ?? ''}
            options={enumOptions(STATUSES)} onChange={(e) => list.setFilter('status', e.target.value)} />
          <Select className="h-9 w-auto" placeholder="All outcomes" value={list.filters.outcome ?? ''}
            options={enumOptions(OUTCOMES)} onChange={(e) => list.setFilter('outcome', e.target.value)} />
          {(list.search || Object.keys(list.filters).length > 0) && (
            <Button size="sm" variant="ghost" onClick={list.reset}>Clear</Button>
          )}
        </div>

        {isError ? (
          <ErrorState message="Legal matters could not be loaded." onRetry={() => void refetch()} />
        ) : (
          <>
            <Table>
              <thead>
                <tr>
                  <Th sortable active={list.sortBy === 'caseNumber'} descending={list.sortDescending}
                    onSort={() => list.toggleSort('caseNumber')}>Case</Th>
                  <Th>Member</Th>
                  <Th>Opposing club</Th>
                  <Th>Forum</Th>
                  <Th>Lawyer</Th>
                  <Th sortable active={list.sortBy === 'claim'} descending={list.sortDescending}
                    onSort={() => list.toggleSort('claim')} className="text-right">Claim</Th>
                  <Th sortable active={list.sortBy === 'hearing'} descending={list.sortDescending}
                    onSort={() => list.toggleSort('hearing')}>Hearing</Th>
                  <Th>Status</Th>
                </tr>
              </thead>
              {isLoading ? (
                <SkeletonRows cols={8} />
              ) : (
                <tbody>
                  {data?.items.map((row) => (
                    <Tr key={row.id} onClick={() => navigate(`/legal/${row.id}`)}>
                      <Td className="font-medium whitespace-nowrap">
                        {row.caseNumber}
                        <p className="mt-0.5 max-w-xs truncate text-xs font-normal text-[var(--text-muted)]">
                          {row.title}
                        </p>
                      </Td>
                      <Td className="whitespace-nowrap">{row.playerName}</Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">{row.opposingClubName ?? '—'}</Td>
                      <Td><Badge tone="info">{humanise(row.type)}</Badge></Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">{row.lawyerName ?? '—'}</Td>
                      <Td className="tabular text-right whitespace-nowrap">
                        {formatCompactCurrency(row.claimAmount)}
                      </Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">{formatDate(row.hearingDate)}</Td>
                      <Td>
                        <Badge tone={statusTone(row.status)}>{humanise(row.status)}</Badge>
                        {row.outcome !== 'Pending' && (
                          <Badge tone={statusTone(row.outcome)} className="ml-1">{row.outcome}</Badge>
                        )}
                      </Td>
                    </Tr>
                  ))}
                </tbody>
              )}
            </Table>

            {!isLoading && data?.items.length === 0 && (
              <EmptyState icon={<Gavel className="size-5" />} title="No legal matters match these filters"
                description="Adjust the filters, or register a new matter."
                action={canCreate && <Button variant="primary" onClick={() => setCreating(true)}>New legal matter</Button>} />
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
