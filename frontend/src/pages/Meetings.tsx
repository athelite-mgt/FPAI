import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { CalendarCheck, Plus } from 'lucide-react'
import { api, describeError } from '../lib/api'
import { useAuth } from '../lib/auth'
import { enumOptions, useListState, usePagedQuery, useUserLookup } from '../lib/hooks'
import { formatDateTime, humanise } from '../lib/format'
import type { MeetingListItem } from '../lib/types'
import {
  Badge, Button, Card, Checkbox, EmptyState, ErrorState, Input, Modal, PageHeader, Pagination,
  SearchInput, Select, SkeletonRows, statusTone, Table, Td, Textarea, Th, Tr, useToast,
} from '../components/ui'

const TYPES = ['Board', 'GeneralBody', 'Committee', 'Emergency'] as const
const STATUSES = ['Scheduled', 'InProgress', 'Completed', 'Cancelled'] as const

function NewMeetingModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const toast = useToast()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const users = useUserLookup()

  const [form, setForm] = useState({
    title: '', type: 'Board', scheduledAt: '', durationMinutes: 60,
    location: '', videoLink: '', agenda: '', quorumRequired: 5, chairId: '',
  })
  const [attendees, setAttendees] = useState<string[]>([])
  const [errors, setErrors] = useState<Record<string, string>>({})

  const mutation = useMutation({
    mutationFn: async () =>
      (await api.post('/meetings', {
        ...form,
        chairId: form.chairId || null,
        scheduledAt: new Date(form.scheduledAt).toISOString(),
        attendeeUserIds: attendees,
      })).data,
    onSuccess: (created: { id: string; referenceNumber: string }) => {
      toast.success(`Meeting ${created.referenceNumber} scheduled.`)
      void queryClient.invalidateQueries({ queryKey: ['meetings'] })
      onClose()
      navigate(`/meetings/${created.id}`)
    },
    onError: (e) => toast.error(describeError(e)),
  })

  function submit(e: React.FormEvent) {
    e.preventDefault()
    const next: Record<string, string> = {}
    if (!form.title.trim()) next.title = 'A title is required.'
    if (!form.scheduledAt) next.scheduledAt = 'Pick a date and time.'
    setErrors(next)
    if (Object.keys(next).length) return
    mutation.mutate()
  }

  return (
    <Modal open={open} onClose={onClose} wide title="Schedule a meeting"
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button variant="primary" loading={mutation.isPending} onClick={submit}>Schedule</Button>
        </>
      }>
      <form onSubmit={submit} className="space-y-4" noValidate>
        <Input label="Title" required value={form.title} error={errors.title}
          placeholder="e.g. Executive Committee Meeting"
          onChange={(e) => setForm({ ...form, title: e.target.value })} />

        <div className="grid gap-4 sm:grid-cols-3">
          <Select label="Type" value={form.type} options={enumOptions(TYPES)}
            onChange={(e) => setForm({ ...form, type: e.target.value })} />
          <Input label="Date and time" type="datetime-local" required
            value={form.scheduledAt} error={errors.scheduledAt}
            onChange={(e) => setForm({ ...form, scheduledAt: e.target.value })} />
          <Input label="Duration (minutes)" type="number" min={15} max={600}
            value={form.durationMinutes}
            onChange={(e) => setForm({ ...form, durationMinutes: Number(e.target.value) })} />
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <Input label="Location" value={form.location} placeholder="FPAI House, Mumbai"
            onChange={(e) => setForm({ ...form, location: e.target.value })} />
          <Input label="Video link" value={form.videoLink} placeholder="https://…"
            onChange={(e) => setForm({ ...form, videoLink: e.target.value })} />
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <Select label="Chair" value={form.chairId} placeholder="Not set"
            options={(users.data ?? []).map((u) => ({ value: u.id, label: u.label }))}
            onChange={(e) => setForm({ ...form, chairId: e.target.value })} />
          <Input label="Quorum required" type="number" min={1} max={100} value={form.quorumRequired}
            hint="Members needed for motions to be valid."
            onChange={(e) => setForm({ ...form, quorumRequired: Number(e.target.value) })} />
        </div>

        <Textarea label="Agenda" rows={4} value={form.agenda}
          placeholder={'1. Confirmation of previous minutes\n2. Department reports'}
          onChange={(e) => setForm({ ...form, agenda: e.target.value })} />

        <div>
          <p className="mb-2 text-xs font-medium text-[var(--text-muted)]">
            Invite voting members ({attendees.length} selected)
          </p>
          <div className="max-h-44 space-y-1.5 overflow-y-auto rounded-lg border p-3">
            {(users.data ?? []).map((u) => (
              <Checkbox key={u.id} label={`${u.label}${u.sub ? ` · ${u.sub}` : ''}`}
                checked={attendees.includes(u.id)}
                onChange={(e) =>
                  setAttendees((current) =>
                    e.target.checked ? [...current, u.id] : current.filter((x) => x !== u.id))
                } />
            ))}
          </div>
        </div>
      </form>
    </Modal>
  )
}

export default function Meetings() {
  const navigate = useNavigate()
  const { hasRole } = useAuth()
  const list = useListState('scheduledAt')
  const [creating, setCreating] = useState(false)

  const { data, isLoading, isError, refetch } = usePagedQuery<MeetingListItem>(
    'meetings', '/meetings', list,
  )
  const canCreate = hasRole('SuperAdmin', 'DepartmentHead')

  return (
    <>
      <PageHeader title="Meetings & Voting"
        subtitle="Board meetings, committee sittings, motions and recorded voting."
        actions={canCreate && (
          <Button variant="primary" icon={<Plus className="size-4" />} onClick={() => setCreating(true)}>
            Schedule meeting
          </Button>
        )}
      />

      <Card>
        <div className="flex flex-wrap items-center gap-2 border-b px-4 py-3">
          <SearchInput value={list.search} onChange={list.setSearch} placeholder="Search meetings…" />
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
          <ErrorState message="Meetings could not be loaded." onRetry={() => void refetch()} />
        ) : (
          <>
            <Table>
              <thead>
                <tr>
                  <Th>Reference</Th><Th>Title</Th><Th>Type</Th>
                  <Th sortable active descending={list.sortDescending}
                    onSort={() => list.toggleSort('scheduledAt')}>Scheduled</Th>
                  <Th>Location</Th><Th>Chair</Th>
                  <Th className="text-right">Attendees</Th><Th className="text-right">Motions</Th>
                  <Th>Status</Th>
                </tr>
              </thead>
              {isLoading ? <SkeletonRows cols={9} /> : (
                <tbody>
                  {data?.items.map((row) => (
                    <Tr key={row.id} onClick={() => navigate(`/meetings/${row.id}`)}>
                      <Td className="font-medium whitespace-nowrap">{row.referenceNumber}</Td>
                      <Td className="max-w-xs truncate">{row.title}</Td>
                      <Td><Badge tone="info">{humanise(row.type)}</Badge></Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">{formatDateTime(row.scheduledAt)}</Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">{row.location ?? '—'}</Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">{row.chairName ?? '—'}</Td>
                      <Td className="tabular text-right">{row.attendeeCount}</Td>
                      <Td className="tabular text-right">{row.motionCount}</Td>
                      <Td><Badge tone={statusTone(row.status)}>{humanise(row.status)}</Badge></Td>
                    </Tr>
                  ))}
                </tbody>
              )}
            </Table>

            {!isLoading && data?.items.length === 0 && (
              <EmptyState icon={<CalendarCheck className="size-5" />} title="No meetings match these filters"
                action={canCreate && <Button variant="primary" onClick={() => setCreating(true)}>Schedule meeting</Button>} />
            )}
            {data && (
              <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount}
                totalPages={data.totalPages} onPage={list.setPage} />
            )}
          </>
        )}
      </Card>

      <NewMeetingModal open={creating} onClose={() => setCreating(false)} />
    </>
  )
}
