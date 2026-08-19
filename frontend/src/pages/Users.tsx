import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { KeyRound, Pencil, Plus, Trash2, Users as UsersIcon } from 'lucide-react'
import { api, describeError } from '../lib/api'
import { useAuth } from '../lib/auth'
import { enumOptions, useDepartments, useListState, usePagedQuery } from '../lib/hooks'
import { formatDate, formatRelative, humanise } from '../lib/format'
import type { RoleInfo, UserListItem } from '../lib/types'
import {
  Badge, Button, Card, CardHeader, ConfirmDialog, EmptyState, ErrorState, Input, Modal,
  PageHeader, Pagination, SearchInput, Select, SkeletonRows, StatTile, statusTone, Table, Td,
  Th, Tr, useToast,
} from '../components/ui'

const ROLES = ['SuperAdmin', 'DepartmentHead', 'Staff', 'ExternalAccountant'] as const
const STATUSES = ['Invited', 'Active', 'Suspended'] as const

function UserModal({
  open, onClose, user,
}: { open: boolean; onClose: () => void; user: UserListItem | null }) {
  const toast = useToast()
  const queryClient = useQueryClient()
  const departments = useDepartments()

  const [form, setForm] = useState({
    fullName: user?.fullName ?? '',
    email: user?.email ?? '',
    jobTitle: user?.jobTitle ?? '',
    departmentId: user?.departmentId ?? '',
    role: user?.roles?.[0] ?? 'Staff',
    status: user?.status ?? 'Invited',
    password: '',
  })
  const [errors, setErrors] = useState<Record<string, string>>({})

  const mutation = useMutation({
    mutationFn: async () => {
      if (user) {
        return api.put(`/users/${user.id}`, {
          fullName: form.fullName, jobTitle: form.jobTitle || null,
          departmentId: form.departmentId || null, role: form.role, status: form.status,
        })
      }
      return api.post('/users', {
        fullName: form.fullName, email: form.email, jobTitle: form.jobTitle || null,
        departmentId: form.departmentId || null, role: form.role,
        password: form.password || null,
      })
    },
    onSuccess: () => {
      toast.success(user ? 'User updated. Their sessions were ended.' : 'User created.')
      void queryClient.invalidateQueries({ queryKey: ['users'] })
      void queryClient.invalidateQueries({ queryKey: ['roles'] })
      onClose()
    },
    onError: (e) => toast.error(describeError(e)),
  })

  function submit(e: React.FormEvent) {
    e.preventDefault()
    const next: Record<string, string> = {}
    if (!form.fullName.trim()) next.fullName = 'A name is required.'
    if (!user && !/^\S+@\S+\.\S+$/.test(form.email)) next.email = 'Enter a valid email address.'
    if (form.role !== 'SuperAdmin' && !form.departmentId) {
      next.departmentId = `A department is required for the ${humanise(form.role)} role.`
    }
    if (form.password && form.password.length < 10) {
      next.password = 'Passwords must be at least 10 characters.'
    }
    setErrors(next)
    if (Object.keys(next).length) return
    mutation.mutate()
  }

  return (
    <Modal open={open} onClose={onClose} title={user ? `Edit ${user.fullName}` : 'Add a user'}
      description={user ? user.email : 'Leave the password blank to invite them via Google sign-in.'}
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button variant="primary" loading={mutation.isPending} onClick={submit}>
            {user ? 'Save changes' : 'Create user'}
          </Button>
        </>
      }>
      <form onSubmit={submit} className="space-y-4" noValidate>
        <Input label="Full name" required value={form.fullName} error={errors.fullName}
          onChange={(e) => setForm({ ...form, fullName: e.target.value })} />
        {!user && (
          <Input label="Email address" type="email" required value={form.email} error={errors.email}
            onChange={(e) => setForm({ ...form, email: e.target.value })} />
        )}
        <Input label="Job title" value={form.jobTitle}
          onChange={(e) => setForm({ ...form, jobTitle: e.target.value })} />

        <div className="grid gap-4 sm:grid-cols-2">
          <Select label="Role" required value={form.role} options={enumOptions(ROLES)}
            onChange={(e) => setForm({ ...form, role: e.target.value as typeof form.role })} />
          <Select label="Department" value={form.departmentId} error={errors.departmentId}
            placeholder={form.role === 'SuperAdmin' ? 'Optional' : 'Select…'}
            options={(departments.data ?? []).map((d) => ({ value: d.id, label: d.name }))}
            onChange={(e) => setForm({ ...form, departmentId: e.target.value })} />
        </div>

        {user ? (
          <Select label="Status" value={form.status} options={enumOptions(STATUSES)}
            hint="Suspending a user ends their sessions immediately."
            onChange={(e) => setForm({ ...form, status: e.target.value as typeof form.status })} />
        ) : (
          <Input label="Initial password" type="password" value={form.password} error={errors.password}
            hint="Optional. Without one the account waits for its first Google sign-in."
            onChange={(e) => setForm({ ...form, password: e.target.value })} />
        )}
      </form>
    </Modal>
  )
}

/** `embedded` hides the page title when rendered inside the Settings shell. */
export default function Users({ embedded = false }: { embedded?: boolean } = {}) {
  const toast = useToast()
  const queryClient = useQueryClient()
  const { isSuperAdmin, user: me } = useAuth()
  const list = useListState('fullName')
  const departments = useDepartments()

  const [creating, setCreating] = useState(false)
  const [editing, setEditing] = useState<UserListItem | null>(null)
  const [resetting, setResetting] = useState<UserListItem | null>(null)
  const [newPassword, setNewPassword] = useState('')
  const [deleting, setDeleting] = useState<UserListItem | null>(null)

  const { data, isLoading, isError, refetch } = usePagedQuery<UserListItem>('users', '/users', list)
  const roles = useQuery({
    queryKey: ['roles'],
    queryFn: async () => (await api.get<RoleInfo[]>('/users/roles')).data,
  })

  const resetPassword = useMutation({
    mutationFn: async () =>
      api.post(`/users/${resetting!.id}/reset-password`, { newPassword }),
    onSuccess: () => {
      toast.success('Password reset. Their sessions were ended.')
      setResetting(null)
      setNewPassword('')
    },
    onError: (e) => toast.error(describeError(e)),
  })

  const remove = useMutation({
    mutationFn: async () => api.delete(`/users/${deleting!.id}`),
    onSuccess: () => {
      toast.success('User deactivated.')
      void queryClient.invalidateQueries({ queryKey: ['users'] })
      setDeleting(null)
    },
    onError: (e) => toast.error(describeError(e)),
  })

  return (
    <>
      {embedded ? (
        isSuperAdmin && (
          <div className="mb-4 flex justify-end">
            <Button variant="primary" icon={<Plus className="size-4" />} onClick={() => setCreating(true)}>
              Add user
            </Button>
          </div>
        )
      ) : (
        <PageHeader title="User Management"
          subtitle="Accounts, roles and department assignment. Roles are enforced server-side."
          actions={isSuperAdmin && (
            <Button variant="primary" icon={<Plus className="size-4" />} onClick={() => setCreating(true)}>
              Add user
            </Button>
          )}
        />
      )}

      <div className="mb-4 grid grid-cols-2 gap-4 lg:grid-cols-4">
        {roles.data?.map((role) => (
          <StatTile key={role.name} label={humanise(role.name)} value={role.userCount}
            sub={role.description} tone={role.name === 'SuperAdmin' ? 'danger' : 'info'} />
        ))}
      </div>

      <Card>
        <div className="flex flex-wrap items-center gap-2 border-b px-4 py-3">
          <SearchInput value={list.search} onChange={list.setSearch} placeholder="Search name or email…" />
          <Select className="h-9 w-auto" placeholder="All roles" value={list.filters.role ?? ''}
            options={enumOptions(ROLES)} onChange={(e) => list.setFilter('role', e.target.value)} />
          <Select className="h-9 w-auto" placeholder="All departments" value={list.filters.departmentId ?? ''}
            options={(departments.data ?? []).map((d) => ({ value: d.id, label: d.name }))}
            onChange={(e) => list.setFilter('departmentId', e.target.value)} />
          <Select className="h-9 w-auto" placeholder="All statuses" value={list.filters.status ?? ''}
            options={enumOptions(STATUSES)} onChange={(e) => list.setFilter('status', e.target.value)} />
          {(list.search || Object.keys(list.filters).length > 0) && (
            <Button size="sm" variant="ghost" onClick={list.reset}>Clear</Button>
          )}
        </div>

        {isError ? (
          <ErrorState message="Users could not be loaded." onRetry={() => void refetch()} />
        ) : (
          <>
            <Table>
              <thead>
                <tr>
                  <Th sortable active={list.sortBy === 'fullName'} descending={list.sortDescending}
                    onSort={() => list.toggleSort('fullName')}>Name</Th>
                  <Th sortable active={list.sortBy === 'email'} descending={list.sortDescending}
                    onSort={() => list.toggleSort('email')}>Email</Th>
                  <Th>Role</Th><Th>Department</Th><Th>Created</Th>
                  <Th sortable active={list.sortBy === 'lastlogin'} descending={list.sortDescending}
                    onSort={() => list.toggleSort('lastlogin')}>Last sign-in</Th>
                  <Th>Status</Th>
                  {isSuperAdmin && <Th className="text-right">Actions</Th>}
                </tr>
              </thead>
              {isLoading ? <SkeletonRows cols={isSuperAdmin ? 8 : 7} /> : (
                <tbody>
                  {data?.items.map((row) => (
                    <Tr key={row.id}>
                      <Td className="font-medium whitespace-nowrap">
                        {row.fullName}
                        {row.id === me?.id && <Badge tone="success" className="ml-1.5">You</Badge>}
                        {row.jobTitle && (
                          <p className="text-xs font-normal text-[var(--text-subtle)]">{row.jobTitle}</p>
                        )}
                      </Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">
                        {row.email}
                        {row.hasGoogleLinked && <Badge className="ml-1.5">Google</Badge>}
                      </Td>
                      <Td>
                        {row.roles?.length ? row.roles.map((r) => (
                          <Badge key={r} tone={r === 'SuperAdmin' ? 'danger' : 'info'}>{humanise(r)}</Badge>
                        )) : <span className="text-[var(--text-subtle)]">No role</span>}
                      </Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">{row.departmentName ?? '—'}</Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">{formatDate(row.createdAt)}</Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">
                        {row.lastLoginAt ? formatRelative(row.lastLoginAt) : 'Never'}
                      </Td>
                      <Td><Badge tone={statusTone(row.status)}>{row.status}</Badge></Td>
                      {isSuperAdmin && (
                        <Td>
                          <div className="flex justify-end gap-1">
                            <button aria-label={`Edit ${row.fullName}`} onClick={() => setEditing(row)}
                              className="rounded p-1.5 text-[var(--text-subtle)] hover:bg-[var(--surface-sunken)] hover:text-[var(--text)]">
                              <Pencil className="size-4" />
                            </button>
                            <button aria-label={`Reset password for ${row.fullName}`}
                              onClick={() => { setResetting(row); setNewPassword('') }}
                              className="rounded p-1.5 text-[var(--text-subtle)] hover:bg-[var(--surface-sunken)] hover:text-[var(--text)]">
                              <KeyRound className="size-4" />
                            </button>
                            {row.id !== me?.id && (
                              <button aria-label={`Deactivate ${row.fullName}`} onClick={() => setDeleting(row)}
                                className="rounded p-1.5 text-[var(--text-subtle)] hover:bg-red-50 hover:text-red-600 dark:hover:bg-red-500/10">
                                <Trash2 className="size-4" />
                              </button>
                            )}
                          </div>
                        </Td>
                      )}
                    </Tr>
                  ))}
                </tbody>
              )}
            </Table>

            {!isLoading && data?.items.length === 0 && (
              <EmptyState icon={<UsersIcon className="size-5" />} title="No users match these filters" />
            )}
            {data && (
              <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount}
                totalPages={data.totalPages} onPage={list.setPage} />
            )}
          </>
        )}
      </Card>

      <Card className="mt-4">
        <CardHeader title="What each role can do"
          subtitle="Enforced on every API endpoint, not just hidden in the interface." />
        <Table>
          <thead>
            <tr><Th>Role</Th><Th>Read</Th><Th>Write</Th><Th>Approve</Th></tr>
          </thead>
          <tbody>
            <Tr>
              <Td className="font-medium">Super Admin</Td>
              <Td className="text-[var(--text-muted)]">Everything, all departments</Td>
              <Td className="text-[var(--text-muted)]">Everything, all departments</Td>
              <Td className="text-[var(--text-muted)]">Everything</Td>
            </Tr>
            <Tr>
              <Td className="font-medium">Department Head</Td>
              <Td className="text-[var(--text-muted)]">All modules, all departments</Td>
              <Td className="text-[var(--text-muted)]">Own department only</Td>
              <Td className="text-[var(--text-muted)]">Own department only</Td>
            </Tr>
            <Tr>
              <Td className="font-medium">Staff</Td>
              <Td className="text-[var(--text-muted)]">Own department only</Td>
              <Td className="text-[var(--text-muted)]">Own department only</Td>
              <Td className="text-[var(--text-muted)]">Never</Td>
            </Tr>
            <Tr>
              <Td className="font-medium">External Accountant</Td>
              <Td className="text-[var(--text-muted)]">Finance and documents only</Td>
              <Td className="text-[var(--text-muted)]">Queries and reconciliation only</Td>
              <Td className="text-[var(--text-muted)]">Never</Td>
            </Tr>
          </tbody>
        </Table>
      </Card>

      {creating && <UserModal open onClose={() => setCreating(false)} user={null} />}
      {editing && <UserModal open onClose={() => setEditing(null)} user={editing} />}

      <Modal open={Boolean(resetting)} onClose={() => setResetting(null)} title="Reset password"
        description={`A new password for ${resetting?.fullName}. Their sessions will end.`}
        footer={
          <>
            <Button variant="ghost" onClick={() => setResetting(null)}>Cancel</Button>
            <Button variant="primary" loading={resetPassword.isPending}
              disabled={newPassword.length < 10} onClick={() => resetPassword.mutate()}>
              Reset password
            </Button>
          </>
        }>
        <Input label="New password" type="password" value={newPassword} autoFocus
          hint="At least 10 characters, with upper case, lower case, a digit and a symbol."
          onChange={(e) => setNewPassword(e.target.value)} />
      </Modal>

      <ConfirmDialog open={Boolean(deleting)} onClose={() => setDeleting(null)}
        onConfirm={() => remove.mutate()} loading={remove.isPending} danger
        title="Deactivate this user?" confirmLabel="Deactivate"
        message={`${deleting?.fullName} will lose access immediately. Their past work stays attributed to them.`} />
    </>
  )
}
