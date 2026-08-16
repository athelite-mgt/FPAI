import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Briefcase, Plus } from 'lucide-react'
import { api, describeError } from '../lib/api'
import { useAuth } from '../lib/auth'
import { enumOptions, useListState, usePagedQuery, useUserLookup } from '../lib/hooks'
import { formatCompactCurrency, formatDate, humanise } from '../lib/format'
import type { EventListItem } from '../lib/types'
import {
  Badge, Button, Card, EmptyState, ErrorState, Input, Modal, PageHeader, Pagination,
  SearchInput, Select, SkeletonRows, StatTile, statusTone, Table, Td, Textarea, Th, Tr, useToast,
} from '../components/ui'

const TYPES = ['Workshop', 'Camp', 'Outreach', 'Ceremony', 'Tournament'] as const
const STATUSES = ['Planned', 'Dispatched', 'Ongoing', 'Completed', 'Cancelled'] as const

interface Stats {
  total: number; upcoming: number; completed: number; totalBudget: number; totalSpent: number
}

function NewEventModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const toast = useToast()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const users = useUserLookup()

  const [form, setForm] = useState({
    name: '', type: 'Workshop', startDate: '', endDate: '', venue: '', city: '',
    budgetAmount: '', expectedAttendees: '', ownerId: '', description: '',
  })
  const [errors, setErrors] = useState<Record<string, string>>({})

  const mutation = useMutation({
    mutationFn: async () =>
      (await api.post('/events', {
        ...form,
        startDate: new Date(form.startDate).toISOString(),
        endDate: form.endDate ? new Date(form.endDate).toISOString() : null,
        budgetAmount: Number(form.budgetAmount || 0),
        actualCost: 0,
        expectedAttendees: Number(form.expectedAttendees || 0),
        actualAttendees: 0,
        ownerId: form.ownerId || null,
      })).data,
    onSuccess: (created: { id: string; referenceNumber: string }) => {
      toast.success(`Event ${created.referenceNumber} created.`)
      void queryClient.invalidateQueries({ queryKey: ['events'] })
      onClose()
      navigate(`/events/${created.id}`)
    },
    onError: (e) => toast.error(describeError(e)),
  })

  function submit(e: React.FormEvent) {
    e.preventDefault()
    const next: Record<string, string> = {}
    if (!form.name.trim()) next.name = 'A name is required.'
    if (!form.startDate) next.startDate = 'Pick a start date.'
    if (form.endDate && form.startDate && new Date(form.endDate) < new Date(form.startDate)) {
      next.endDate = 'The end date cannot be before the start date.'
    }
    setErrors(next)
    if (Object.keys(next).length) return
    mutation.mutate()
  }

  return (
    <Modal open={open} onClose={onClose} wide title="Plan an event"
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button variant="primary" loading={mutation.isPending} onClick={submit}>Create event</Button>
        </>
      }>
      <form onSubmit={submit} className="space-y-4" noValidate>
        <Input label="Name" required value={form.name} error={errors.name}
          placeholder="e.g. Player Rights & Contracts Workshop"
          onChange={(e) => setForm({ ...form, name: e.target.value })} />

        <div className="grid gap-4 sm:grid-cols-3">
          <Select label="Type" value={form.type} options={enumOptions(TYPES)}
            onChange={(e) => setForm({ ...form, type: e.target.value })} />
          <Input label="Start date" type="datetime-local" required value={form.startDate}
            error={errors.startDate} onChange={(e) => setForm({ ...form, startDate: e.target.value })} />
          <Input label="End date" type="datetime-local" value={form.endDate} error={errors.endDate}
            onChange={(e) => setForm({ ...form, endDate: e.target.value })} />
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <Input label="Venue" value={form.venue}
            onChange={(e) => setForm({ ...form, venue: e.target.value })} />
          <Input label="City" value={form.city}
            onChange={(e) => setForm({ ...form, city: e.target.value })} />
        </div>

        <div className="grid gap-4 sm:grid-cols-3">
          <Input label="Budget (INR)" type="number" min={0} value={form.budgetAmount}
            onChange={(e) => setForm({ ...form, budgetAmount: e.target.value })} />
          <Input label="Expected attendees" type="number" min={0} value={form.expectedAttendees}
            onChange={(e) => setForm({ ...form, expectedAttendees: e.target.value })} />
          <Select label="Owner" value={form.ownerId} placeholder="You"
            options={(users.data ?? []).map((u) => ({ value: u.id, label: u.label }))}
            onChange={(e) => setForm({ ...form, ownerId: e.target.value })} />
        </div>

        <Textarea label="Description" rows={3} value={form.description}
          onChange={(e) => setForm({ ...form, description: e.target.value })} />
      </form>
    </Modal>
  )
}

export default function Events() {
  const navigate = useNavigate()
  const { hasRole } = useAuth()
  const list = useListState('startDate')
  const [creating, setCreating] = useState(false)

  const { data, isLoading, isError, refetch } = usePagedQuery<EventListItem>('events', '/events', list)
  const stats = useQuery({
    queryKey: ['event-stats'],
    queryFn: async () => (await api.get<Stats>('/events/stats')).data,
  })

  const canCreate = hasRole('SuperAdmin', 'DepartmentHead', 'Staff')

  return (
    <>
      <PageHeader title="Events & Operations"
        subtitle="Workshops, camps, outreach programmes and member events."
        actions={canCreate && (
          <Button variant="primary" icon={<Plus className="size-4" />} onClick={() => setCreating(true)}>
            New event
          </Button>
        )}
      />

      <div className="mb-4 grid grid-cols-2 gap-4 lg:grid-cols-4">
        <StatTile label="Total events" value={stats.data?.total ?? '—'} tone="info" />
        <StatTile label="Upcoming" value={stats.data?.upcoming ?? '—'} tone="warning" />
        <StatTile label="Completed" value={stats.data?.completed ?? '—'} tone="success" />
        <StatTile label="Budget vs spend" tone="accent"
          value={stats.data ? formatCompactCurrency(stats.data.totalBudget) : '—'}
          sub={stats.data ? `${formatCompactCurrency(stats.data.totalSpent)} spent` : undefined} />
      </div>

      <Card>
        <div className="flex flex-wrap items-center gap-2 border-b px-4 py-3">
          <SearchInput value={list.search} onChange={list.setSearch} placeholder="Search events…" />
          <Select className="h-9 w-auto" placeholder="All types" value={list.filters.type ?? ''}
            options={enumOptions(TYPES)} onChange={(e) => list.setFilter('type', e.target.value)} />
          <Select className="h-9 w-auto" placeholder="All statuses" value={list.filters.status ?? ''}
            options={enumOptions(STATUSES)} onChange={(e) => list.setFilter('status', e.target.value)} />
          <Select className="h-9 w-auto" placeholder="Any time" value={list.filters.upcoming ?? ''}
            options={[{ value: 'true', label: 'Upcoming' }, { value: 'false', label: 'Past' }]}
            onChange={(e) => list.setFilter('upcoming', e.target.value)} />
          {(list.search || Object.keys(list.filters).length > 0) && (
            <Button size="sm" variant="ghost" onClick={list.reset}>Clear</Button>
          )}
        </div>

        {isError ? (
          <ErrorState message="Events could not be loaded." onRetry={() => void refetch()} />
        ) : (
          <>
            <Table>
              <thead>
                <tr>
                  <Th>Reference</Th>
                  <Th sortable active={list.sortBy === 'name'} descending={list.sortDescending}
                    onSort={() => list.toggleSort('name')}>Event</Th>
                  <Th>Type</Th><Th>City</Th>
                  <Th sortable active={list.sortBy === 'startDate'} descending={list.sortDescending}
                    onSort={() => list.toggleSort('startDate')}>Starts</Th>
                  <Th sortable active={list.sortBy === 'budget'} descending={list.sortDescending}
                    onSort={() => list.toggleSort('budget')} className="text-right">Budget</Th>
                  <Th className="text-right">Attendees</Th>
                  <Th>Status</Th>
                </tr>
              </thead>
              {isLoading ? <SkeletonRows cols={8} /> : (
                <tbody>
                  {data?.items.map((row) => (
                    <Tr key={row.id} onClick={() => navigate(`/events/${row.id}`)}>
                      <Td className="font-medium whitespace-nowrap">{row.referenceNumber}</Td>
                      <Td className="max-w-xs truncate">{row.name}</Td>
                      <Td><Badge tone="info">{row.type}</Badge></Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">{row.city ?? '—'}</Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">{formatDate(row.startDate)}</Td>
                      <Td className="tabular text-right">
                        {formatCompactCurrency(row.budgetAmount)}
                        {row.actualCost > 0 && (
                          <span className={`ml-1 text-xs ${row.actualCost > row.budgetAmount ? 'text-red-600' : 'text-[var(--text-subtle)]'}`}>
                            ({formatCompactCurrency(row.actualCost)})
                          </span>
                        )}
                      </Td>
                      <Td className="tabular text-right">
                        {row.status === 'Completed' ? row.actualAttendees : row.expectedAttendees}
                      </Td>
                      <Td><Badge tone={statusTone(row.status)}>{humanise(row.status)}</Badge></Td>
                    </Tr>
                  ))}
                </tbody>
              )}
            </Table>

            {!isLoading && data?.items.length === 0 && (
              <EmptyState icon={<Briefcase className="size-5" />} title="No events match these filters"
                action={canCreate && <Button variant="primary" onClick={() => setCreating(true)}>New event</Button>} />
            )}
            {data && (
              <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount}
                totalPages={data.totalPages} onPage={list.setPage} />
            )}
          </>
        )}
      </Card>

      <NewEventModal open={creating} onClose={() => setCreating(false)} />
    </>
  )
}
