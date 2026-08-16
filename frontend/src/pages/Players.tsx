import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Pencil, Plus, UsersRound } from 'lucide-react'
import { api, describeError } from '../lib/api'
import { useAuth } from '../lib/auth'
import { enumOptions, useListState, usePagedQuery } from '../lib/hooks'
import { formatDate, humanise } from '../lib/format'
import type { Club, PagedResult, Player } from '../lib/types'
import {
  Badge, Button, Card, EmptyState, ErrorState, Input, Modal, PageHeader, Pagination,
  SearchInput, Select, SkeletonRows, statusTone, Table, Td, Th, Tr, useToast,
} from '../components/ui'

const STATUSES = ['Active', 'Retired', 'Injured', 'FreeAgent'] as const
const POSITIONS = ['Goalkeeper', 'Defender', 'Midfielder', 'Forward'] as const

const EMPTY = {
  fullName: '', dateOfBirth: '', position: '', nationality: 'India',
  currentClubId: '', jerseyNumber: '', contactEmail: '', contactPhone: '', status: 'Active',
}

function PlayerModal({
  open, onClose, player,
}: { open: boolean; onClose: () => void; player: Player | null }) {
  const toast = useToast()
  const queryClient = useQueryClient()

  const clubs = useQuery({
    queryKey: ['clubs', 'all'],
    queryFn: async () =>
      (await api.get<PagedResult<Club>>('/clubs', { params: { pageSize: 200 } })).data.items,
    staleTime: 5 * 60_000,
  })

  const [form, setForm] = useState(
    player
      ? {
          fullName: player.fullName,
          dateOfBirth: player.dateOfBirth ?? '',
          position: player.position ?? '',
          nationality: player.nationality,
          currentClubId: player.currentClubId ?? '',
          jerseyNumber: player.jerseyNumber?.toString() ?? '',
          contactEmail: player.contactEmail ?? '',
          contactPhone: player.contactPhone ?? '',
          status: player.status,
        }
      : EMPTY,
  )
  const [errors, setErrors] = useState<Record<string, string>>({})

  const mutation = useMutation({
    mutationFn: async () => {
      const body = {
        ...form,
        dateOfBirth: form.dateOfBirth || null,
        currentClubId: form.currentClubId || null,
        jerseyNumber: form.jerseyNumber ? Number(form.jerseyNumber) : null,
        contactEmail: form.contactEmail || null,
        contactPhone: form.contactPhone || null,
        position: form.position || null,
      }
      return player ? api.put(`/players/${player.id}`, body) : api.post('/players', body)
    },
    onSuccess: () => {
      toast.success(player ? 'Member updated.' : 'Member added.')
      void queryClient.invalidateQueries({ queryKey: ['players'] })
      onClose()
    },
    onError: (e) => toast.error(describeError(e)),
  })

  function submit(e: React.FormEvent) {
    e.preventDefault()
    const next: Record<string, string> = {}
    if (!form.fullName.trim()) next.fullName = 'A name is required.'
    if (form.contactEmail && !/^\S+@\S+\.\S+$/.test(form.contactEmail)) {
      next.contactEmail = 'Enter a valid email address.'
    }
    if (form.jerseyNumber && (Number(form.jerseyNumber) < 1 || Number(form.jerseyNumber) > 99)) {
      next.jerseyNumber = 'Between 1 and 99.'
    }
    setErrors(next)
    if (Object.keys(next).length) return
    mutation.mutate()
  }

  return (
    <Modal open={open} onClose={onClose} wide
      title={player ? `Edit ${player.fullName}` : 'Add a member'}
      description={player ? player.membershipId : 'A membership id is allocated automatically.'}
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button variant="primary" loading={mutation.isPending} onClick={submit}>
            {player ? 'Save changes' : 'Add member'}
          </Button>
        </>
      }>
      <form onSubmit={submit} className="space-y-4" noValidate>
        <Input label="Full name" required value={form.fullName} error={errors.fullName}
          onChange={(e) => setForm({ ...form, fullName: e.target.value })} />
        <div className="grid gap-4 sm:grid-cols-3">
          <Input label="Date of birth" type="date" value={form.dateOfBirth}
            onChange={(e) => setForm({ ...form, dateOfBirth: e.target.value })} />
          <Select label="Position" value={form.position} placeholder="Not set"
            options={enumOptions(POSITIONS)}
            onChange={(e) => setForm({ ...form, position: e.target.value })} />
          <Input label="Jersey number" type="number" min={1} max={99} value={form.jerseyNumber}
            error={errors.jerseyNumber}
            onChange={(e) => setForm({ ...form, jerseyNumber: e.target.value })} />
        </div>
        <div className="grid gap-4 sm:grid-cols-3">
          <Select label="Club" value={form.currentClubId} placeholder="Free agent"
            options={(clubs.data ?? []).map((c) => ({ value: c.id, label: c.name }))}
            onChange={(e) => setForm({ ...form, currentClubId: e.target.value })} />
          <Input label="Nationality" value={form.nationality}
            onChange={(e) => setForm({ ...form, nationality: e.target.value })} />
          <Select label="Status" value={form.status} options={enumOptions(STATUSES)}
            onChange={(e) => setForm({ ...form, status: e.target.value })} />
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          <Input label="Contact email" type="email" value={form.contactEmail} error={errors.contactEmail}
            onChange={(e) => setForm({ ...form, contactEmail: e.target.value })} />
          <Input label="Contact phone" value={form.contactPhone}
            onChange={(e) => setForm({ ...form, contactPhone: e.target.value })} />
        </div>
      </form>
    </Modal>
  )
}

export default function Players() {
  const { hasRole } = useAuth()
  const list = useListState('fullName')
  const [editing, setEditing] = useState<Player | null>(null)
  const [creating, setCreating] = useState(false)

  const { data, isLoading, isError, refetch } = usePagedQuery<Player>('players', '/players', list)
  const canManage = hasRole('SuperAdmin', 'DepartmentHead')

  return (
    <>
      <PageHeader title="Member Directory"
        subtitle="FPAI members, their clubs and their casework history."
        actions={canManage && (
          <Button variant="primary" icon={<Plus className="size-4" />} onClick={() => setCreating(true)}>
            Add member
          </Button>
        )}
      />

      <Card>
        <div className="flex flex-wrap items-center gap-2 border-b px-4 py-3">
          <SearchInput value={list.search} onChange={list.setSearch}
            placeholder="Search name or membership id…" />
          <Select className="h-9 w-auto" placeholder="All statuses" value={list.filters.status ?? ''}
            options={enumOptions(STATUSES)} onChange={(e) => list.setFilter('status', e.target.value)} />
          {(list.search || Object.keys(list.filters).length > 0) && (
            <Button size="sm" variant="ghost" onClick={list.reset}>Clear</Button>
          )}
        </div>

        {isError ? (
          <ErrorState message="Members could not be loaded." onRetry={() => void refetch()} />
        ) : (
          <>
            <Table>
              <thead>
                <tr>
                  <Th sortable active={list.sortBy === 'membershipId'} descending={list.sortDescending}
                    onSort={() => list.toggleSort('membershipId')}>Membership</Th>
                  <Th sortable active={list.sortBy === 'fullName'} descending={list.sortDescending}
                    onSort={() => list.toggleSort('fullName')}>Name</Th>
                  <Th>Club</Th><Th>Position</Th><Th>Date of birth</Th><Th>Contact</Th>
                  <Th className="text-right">Cases</Th>
                  <Th sortable active={list.sortBy === 'status'} descending={list.sortDescending}
                    onSort={() => list.toggleSort('status')}>Status</Th>
                  {canManage && <Th className="text-right">Edit</Th>}
                </tr>
              </thead>
              {isLoading ? <SkeletonRows cols={canManage ? 9 : 8} /> : (
                <tbody>
                  {data?.items.map((player) => (
                    <Tr key={player.id}>
                      <Td className="font-mono text-xs whitespace-nowrap">{player.membershipId}</Td>
                      <Td className="font-medium whitespace-nowrap">
                        {player.fullName}
                        {player.jerseyNumber && (
                          <span className="ml-1.5 text-xs text-[var(--text-subtle)]">#{player.jerseyNumber}</span>
                        )}
                      </Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">
                        {player.currentClubName ?? 'Free agent'}
                      </Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">{player.position ?? '—'}</Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">{formatDate(player.dateOfBirth)}</Td>
                      <Td className="max-w-40 truncate text-[var(--text-muted)]">
                        {player.contactEmail ?? player.contactPhone ?? '—'}
                      </Td>
                      <Td className="tabular text-right">
                        <span title="Welfare cases">{player.welfareCaseCount}</span>
                        <span className="text-[var(--text-subtle)]"> / </span>
                        <span title="Legal matters">{player.legalCaseCount}</span>
                      </Td>
                      <Td><Badge tone={statusTone(player.status)}>{humanise(player.status)}</Badge></Td>
                      {canManage && (
                        <Td>
                          <div className="flex justify-end">
                            <button aria-label={`Edit ${player.fullName}`} onClick={() => setEditing(player)}
                              className="rounded p-1.5 text-[var(--text-subtle)] hover:bg-[var(--surface-sunken)] hover:text-[var(--text)]">
                              <Pencil className="size-4" />
                            </button>
                          </div>
                        </Td>
                      )}
                    </Tr>
                  ))}
                </tbody>
              )}
            </Table>

            {!isLoading && data?.items.length === 0 && (
              <EmptyState icon={<UsersRound className="size-5" />} title="No members match these filters"
                action={canManage && <Button variant="primary" onClick={() => setCreating(true)}>Add member</Button>} />
            )}
            {data && (
              <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount}
                totalPages={data.totalPages} onPage={list.setPage} />
            )}
          </>
        )}
      </Card>

      {creating && <PlayerModal open onClose={() => setCreating(false)} player={null} />}
      {editing && <PlayerModal open onClose={() => setEditing(null)} player={editing} />}
    </>
  )
}
