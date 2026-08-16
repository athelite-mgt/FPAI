import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Building, Pencil, Plus, Trash2, Truck } from 'lucide-react'
import { api, describeError } from '../../lib/api'
import { useListState, usePagedQuery } from '../../lib/hooks'
import type { Club, Vendor } from '../../lib/types'
import {
  Button, Card, CardHeader, ConfirmDialog, EmptyState, ErrorState, Input, Modal, Pagination,
  SearchInput, SkeletonRows, Table, Td, Th, Tr, useToast,
} from '../../components/ui'

function Clubs() {
  const toast = useToast()
  const queryClient = useQueryClient()
  const list = useListState()
  const { data, isLoading, isError, refetch } = usePagedQuery<Club>('clubs', '/clubs', list)

  const [editing, setEditing] = useState<Club | null>(null)
  const [creating, setCreating] = useState(false)
  const [deleting, setDeleting] = useState<Club | null>(null)
  const [form, setForm] = useState({ name: '', city: '', league: '' })
  const [error, setError] = useState('')

  function invalidate() {
    void queryClient.invalidateQueries({ queryKey: ['clubs'] })
  }

  const save = useMutation({
    mutationFn: async () => {
      const body = { name: form.name, city: form.city || null, league: form.league || null }
      return editing ? api.put(`/clubs/${editing.id}`, body) : api.post('/clubs', body)
    },
    onSuccess: () => {
      toast.success(editing ? 'Club updated.' : 'Club added.')
      setEditing(null)
      setCreating(false)
      invalidate()
    },
    onError: (e) => toast.error(describeError(e)),
  })

  const remove = useMutation({
    mutationFn: async () => api.delete(`/clubs/${deleting!.id}`),
    onSuccess: () => {
      toast.success('Club removed.')
      setDeleting(null)
      invalidate()
    },
    onError: (e) => toast.error(describeError(e)),
  })

  function open(club: Club | null) {
    setError('')
    if (club) {
      setEditing(club)
      setForm({ name: club.name, city: club.city ?? '', league: club.league ?? '' })
    } else {
      setCreating(true)
      setForm({ name: '', city: '', league: '' })
    }
  }

  function submit(e: React.FormEvent) {
    e.preventDefault()
    if (!form.name.trim()) { setError('A name is required.'); return }
    save.mutate()
  }

  return (
    <Card>
      <CardHeader
        title="Clubs"
        subtitle="Referenced by member records and as the opposing party on legal matters."
        action={
          <Button size="sm" variant="primary" icon={<Plus className="size-3.5" />} onClick={() => open(null)}>
            New club
          </Button>
        }
      />
      <div className="border-b px-4 py-3">
        <SearchInput value={list.search} onChange={list.setSearch} placeholder="Search clubs…" />
      </div>

      {isError ? (
        <ErrorState message="Clubs could not be loaded." onRetry={() => void refetch()} />
      ) : (
        <>
          <Table>
            <thead>
              <tr>
                <Th>Name</Th><Th>City</Th><Th>League</Th>
                <Th className="text-right">Members</Th><Th className="text-right">Actions</Th>
              </tr>
            </thead>
            {isLoading ? <SkeletonRows cols={5} /> : (
              <tbody>
                {data?.items.map((club) => (
                  <Tr key={club.id}>
                    <Td className="font-medium">{club.name}</Td>
                    <Td className="text-[var(--text-muted)]">{club.city ?? '—'}</Td>
                    <Td className="text-[var(--text-muted)]">{club.league ?? '—'}</Td>
                    <Td className="tabular text-right">{club.playerCount}</Td>
                    <Td>
                      <div className="flex justify-end gap-1">
                        <button aria-label={`Edit ${club.name}`} onClick={() => open(club)}
                          className="rounded p-1.5 text-[var(--text-subtle)] hover:bg-[var(--surface-sunken)] hover:text-[var(--text)]">
                          <Pencil className="size-4" />
                        </button>
                        <button aria-label={`Delete ${club.name}`} onClick={() => setDeleting(club)}
                          className="rounded p-1.5 text-[var(--text-subtle)] hover:bg-red-50 hover:text-red-600 dark:hover:bg-red-500/10">
                          <Trash2 className="size-4" />
                        </button>
                      </div>
                    </Td>
                  </Tr>
                ))}
              </tbody>
            )}
          </Table>
          {!isLoading && data?.items.length === 0 && (
            <EmptyState icon={<Building className="size-5" />} title="No clubs match this search" />
          )}
          {data && (
            <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount}
              totalPages={data.totalPages} onPage={list.setPage} />
          )}
        </>
      )}

      <Modal
        open={creating || Boolean(editing)}
        onClose={() => { setEditing(null); setCreating(false) }}
        title={editing ? `Edit ${editing.name}` : 'New club'}
        footer={
          <>
            <Button variant="ghost" onClick={() => { setEditing(null); setCreating(false) }}>Cancel</Button>
            <Button variant="primary" loading={save.isPending} onClick={submit}>
              {editing ? 'Save changes' : 'Add club'}
            </Button>
          </>
        }
      >
        <form onSubmit={submit} className="space-y-4" noValidate>
          <Input label="Name" required value={form.name} error={error}
            onChange={(e) => setForm({ ...form, name: e.target.value })} />
          <div className="grid gap-4 sm:grid-cols-2">
            <Input label="City" value={form.city}
              onChange={(e) => setForm({ ...form, city: e.target.value })} />
            <Input label="League" value={form.league} placeholder="Indian Super League"
              onChange={(e) => setForm({ ...form, league: e.target.value })} />
          </div>
        </form>
      </Modal>

      <ConfirmDialog open={Boolean(deleting)} onClose={() => setDeleting(null)}
        onConfirm={() => remove.mutate()} loading={remove.isPending} danger
        title="Delete this club?" confirmLabel="Delete"
        message={`${deleting?.name} will be removed. This is refused while members are still assigned to it.`} />
    </Card>
  )
}

function Vendors() {
  const toast = useToast()
  const queryClient = useQueryClient()
  const list = useListState()
  const { data, isLoading, isError, refetch } = usePagedQuery<Vendor>('vendors', '/vendors', list)

  const [editing, setEditing] = useState<Vendor | null>(null)
  const [creating, setCreating] = useState(false)
  const [deleting, setDeleting] = useState<Vendor | null>(null)
  const [form, setForm] = useState({ name: '', gstNumber: '', contactEmail: '', contactPhone: '' })
  const [error, setError] = useState('')

  function invalidate() {
    void queryClient.invalidateQueries({ queryKey: ['vendors'] })
  }

  const save = useMutation({
    mutationFn: async () => {
      const body = {
        name: form.name,
        gstNumber: form.gstNumber || null,
        contactEmail: form.contactEmail || null,
        contactPhone: form.contactPhone || null,
      }
      return editing ? api.put(`/vendors/${editing.id}`, body) : api.post('/vendors', body)
    },
    onSuccess: () => {
      toast.success(editing ? 'Vendor updated.' : 'Vendor added.')
      setEditing(null)
      setCreating(false)
      invalidate()
    },
    onError: (e) => toast.error(describeError(e)),
  })

  const remove = useMutation({
    mutationFn: async () => api.delete(`/vendors/${deleting!.id}`),
    onSuccess: () => {
      toast.success('Vendor removed.')
      setDeleting(null)
      invalidate()
    },
    onError: (e) => toast.error(describeError(e)),
  })

  function open(vendor: Vendor | null) {
    setError('')
    if (vendor) {
      setEditing(vendor)
      setForm({
        name: vendor.name,
        gstNumber: vendor.gstNumber ?? '',
        contactEmail: vendor.contactEmail ?? '',
        contactPhone: vendor.contactPhone ?? '',
      })
    } else {
      setCreating(true)
      setForm({ name: '', gstNumber: '', contactEmail: '', contactPhone: '' })
    }
  }

  function submit(e: React.FormEvent) {
    e.preventDefault()
    if (!form.name.trim()) { setError('A name is required.'); return }
    save.mutate()
  }

  return (
    <Card>
      <CardHeader
        title="Vendors"
        subtitle="Payees on vouchers and invoices."
        action={
          <Button size="sm" variant="primary" icon={<Plus className="size-3.5" />} onClick={() => open(null)}>
            New vendor
          </Button>
        }
      />
      <div className="border-b px-4 py-3">
        <SearchInput value={list.search} onChange={list.setSearch} placeholder="Search vendors or GST…" />
      </div>

      {isError ? (
        <ErrorState message="Vendors could not be loaded." onRetry={() => void refetch()} />
      ) : (
        <>
          <Table>
            <thead>
              <tr>
                <Th>Name</Th><Th>GST number</Th><Th>Contact</Th>
                <Th className="text-right">Vouchers</Th><Th className="text-right">Actions</Th>
              </tr>
            </thead>
            {isLoading ? <SkeletonRows cols={5} /> : (
              <tbody>
                {data?.items.map((vendor) => (
                  <Tr key={vendor.id}>
                    <Td className="font-medium">{vendor.name}</Td>
                    <Td className="font-mono text-xs text-[var(--text-muted)]">
                      {vendor.gstNumber ?? '—'}
                    </Td>
                    <Td className="text-[var(--text-muted)]">
                      {vendor.contactEmail ?? vendor.contactPhone ?? '—'}
                    </Td>
                    <Td className="tabular text-right">{vendor.voucherCount}</Td>
                    <Td>
                      <div className="flex justify-end gap-1">
                        <button aria-label={`Edit ${vendor.name}`} onClick={() => open(vendor)}
                          className="rounded p-1.5 text-[var(--text-subtle)] hover:bg-[var(--surface-sunken)] hover:text-[var(--text)]">
                          <Pencil className="size-4" />
                        </button>
                        <button aria-label={`Delete ${vendor.name}`} onClick={() => setDeleting(vendor)}
                          className="rounded p-1.5 text-[var(--text-subtle)] hover:bg-red-50 hover:text-red-600 dark:hover:bg-red-500/10">
                          <Trash2 className="size-4" />
                        </button>
                      </div>
                    </Td>
                  </Tr>
                ))}
              </tbody>
            )}
          </Table>
          {!isLoading && data?.items.length === 0 && (
            <EmptyState icon={<Truck className="size-5" />} title="No vendors match this search" />
          )}
          {data && (
            <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount}
              totalPages={data.totalPages} onPage={list.setPage} />
          )}
        </>
      )}

      <Modal
        open={creating || Boolean(editing)}
        onClose={() => { setEditing(null); setCreating(false) }}
        title={editing ? `Edit ${editing.name}` : 'New vendor'}
        footer={
          <>
            <Button variant="ghost" onClick={() => { setEditing(null); setCreating(false) }}>Cancel</Button>
            <Button variant="primary" loading={save.isPending} onClick={submit}>
              {editing ? 'Save changes' : 'Add vendor'}
            </Button>
          </>
        }
      >
        <form onSubmit={submit} className="space-y-4" noValidate>
          <Input label="Name" required value={form.name} error={error}
            onChange={(e) => setForm({ ...form, name: e.target.value })} />
          <Input label="GST number" value={form.gstNumber} placeholder="27AAECB5678B1Z2"
            onChange={(e) => setForm({ ...form, gstNumber: e.target.value })} />
          <div className="grid gap-4 sm:grid-cols-2">
            <Input label="Contact email" type="email" value={form.contactEmail}
              onChange={(e) => setForm({ ...form, contactEmail: e.target.value })} />
            <Input label="Contact phone" value={form.contactPhone}
              onChange={(e) => setForm({ ...form, contactPhone: e.target.value })} />
          </div>
        </form>
      </Modal>

      <ConfirmDialog open={Boolean(deleting)} onClose={() => setDeleting(null)}
        onConfirm={() => remove.mutate()} loading={remove.isPending} danger
        title="Delete this vendor?" confirmLabel="Delete"
        message={`${deleting?.name} will be removed. This is refused while vouchers reference them.`} />
    </Card>
  )
}

export default function Directory() {
  const [tab, setTab] = useState<'clubs' | 'vendors'>('clubs')

  return (
    <div className="space-y-4">
      <div className="inline-flex rounded-lg bg-[var(--surface-sunken)] p-0.5">
        {(['clubs', 'vendors'] as const).map((key) => (
          <button
            key={key}
            onClick={() => setTab(key)}
            className={`rounded-md px-3 py-1.5 text-sm font-medium capitalize transition-colors ${
              tab === key
                ? 'bg-[var(--surface-raised)] text-[var(--text)] shadow-sm'
                : 'text-[var(--text-muted)]'
            }`}
          >
            {key}
          </button>
        ))}
      </div>
      {tab === 'clubs' ? <Clubs /> : <Vendors />}
    </div>
  )
}
