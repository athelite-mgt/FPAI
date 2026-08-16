import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Building2, Lock, Pencil, Plus, Trash2 } from 'lucide-react'
import { api, describeError } from '../../lib/api'
import { useDepartments } from '../../lib/hooks'
import type { Department } from '../../lib/types'
import {
  Badge, Button, Card, CardHeader, ConfirmDialog, EmptyState, ErrorState, Input, Modal,
  Spinner, Table, Td, Textarea, Th, Tr, useToast,
} from '../../components/ui'

/** Seeded departments are referenced by code when creating records, so their code is fixed. */
const BUILT_IN = ['WELFARE', 'LEGAL', 'FINANCE', 'GOVERNANCE', 'OPERATIONS', 'EXECUTIVE']

export default function Departments() {
  const toast = useToast()
  const queryClient = useQueryClient()
  const { data, isLoading, isError, refetch } = useDepartments()

  const [editing, setEditing] = useState<Department | null>(null)
  const [creating, setCreating] = useState(false)
  const [deleting, setDeleting] = useState<Department | null>(null)
  const [form, setForm] = useState({ code: '', name: '', description: '' })
  const [errors, setErrors] = useState<Record<string, string>>({})

  function invalidate() {
    void queryClient.invalidateQueries({ queryKey: ['departments'] })
  }

  const save = useMutation({
    mutationFn: async () => {
      const body = { code: form.code, name: form.name, description: form.description || null }
      return editing ? api.put(`/departments/${editing.id}`, body) : api.post('/departments', body)
    },
    onSuccess: () => {
      toast.success(editing ? 'Department updated.' : 'Department created.')
      setEditing(null)
      setCreating(false)
      invalidate()
    },
    onError: (e) => toast.error(describeError(e)),
  })

  const remove = useMutation({
    mutationFn: async () => api.delete(`/departments/${deleting!.id}`),
    onSuccess: () => {
      toast.success('Department removed.')
      setDeleting(null)
      invalidate()
    },
    onError: (e) => toast.error(describeError(e)),
  })

  function open(department: Department | null) {
    setErrors({})
    if (department) {
      setEditing(department)
      setForm({
        code: department.code,
        name: department.name,
        description: department.description ?? '',
      })
    } else {
      setCreating(true)
      setForm({ code: '', name: '', description: '' })
    }
  }

  function submit(e: React.FormEvent) {
    e.preventDefault()
    const next: Record<string, string> = {}
    if (!form.code.trim()) next.code = 'A code is required.'
    else if (!/^[A-Za-z0-9_-]+$/.test(form.code.trim())) {
      next.code = 'Letters, numbers, hyphens and underscores only.'
    }
    if (!form.name.trim()) next.name = 'A name is required.'
    setErrors(next)
    if (Object.keys(next).length) return
    save.mutate()
  }

  if (isLoading) return <Spinner label="Loading departments" />
  if (isError) {
    return <ErrorState message="Departments could not be loaded." onRetry={() => void refetch()} />
  }

  const isOpen = creating || Boolean(editing)
  const lockedCode = editing !== null && BUILT_IN.includes(editing.code)

  return (
    <>
      <Card>
        <CardHeader
          title="Departments"
          subtitle="Departments drive row-level access: people can only write to their own."
          action={
            <Button size="sm" variant="primary" icon={<Plus className="size-3.5" />}
              onClick={() => open(null)}>
              New department
            </Button>
          }
        />

        {!data?.length ? (
          <EmptyState icon={<Building2 className="size-5" />} title="No departments yet" />
        ) : (
          <Table>
            <thead>
              <tr>
                <Th>Code</Th><Th>Name</Th><Th>Description</Th>
                <Th className="text-right">People</Th><Th className="text-right">Actions</Th>
              </tr>
            </thead>
            <tbody>
              {data.map((department) => {
                const builtIn = BUILT_IN.includes(department.code)
                return (
                  <Tr key={department.id}>
                    <Td>
                      <span className="font-mono text-xs font-medium">{department.code}</span>
                      {builtIn && (
                        <Badge className="ml-2">
                          <Lock className="size-3" aria-hidden /> Built-in
                        </Badge>
                      )}
                    </Td>
                    <Td className="font-medium">{department.name}</Td>
                    <Td className="max-w-md truncate text-[var(--text-muted)]">
                      {department.description ?? '—'}
                    </Td>
                    <Td className="tabular text-right">{department.userCount}</Td>
                    <Td>
                      <div className="flex justify-end gap-1">
                        <button aria-label={`Edit ${department.name}`} onClick={() => open(department)}
                          className="rounded p-1.5 text-[var(--text-subtle)] hover:bg-[var(--surface-sunken)] hover:text-[var(--text)]">
                          <Pencil className="size-4" />
                        </button>
                        {!builtIn && (
                          <button aria-label={`Delete ${department.name}`}
                            onClick={() => setDeleting(department)}
                            className="rounded p-1.5 text-[var(--text-subtle)] hover:bg-red-50 hover:text-red-600 dark:hover:bg-red-500/10">
                            <Trash2 className="size-4" />
                          </button>
                        )}
                      </div>
                    </Td>
                  </Tr>
                )
              })}
            </tbody>
          </Table>
        )}
      </Card>

      <Modal
        open={isOpen}
        onClose={() => { setEditing(null); setCreating(false) }}
        title={editing ? `Edit ${editing.name}` : 'New department'}
        footer={
          <>
            <Button variant="ghost" onClick={() => { setEditing(null); setCreating(false) }}>
              Cancel
            </Button>
            <Button variant="primary" loading={save.isPending} onClick={submit}>
              {editing ? 'Save changes' : 'Create department'}
            </Button>
          </>
        }
      >
        <form onSubmit={submit} className="space-y-4" noValidate>
          <Input
            label="Code"
            required
            value={form.code}
            error={errors.code}
            disabled={lockedCode}
            hint={lockedCode
              ? 'Built-in codes cannot be changed; records are created against them.'
              : 'Short uppercase key, e.g. MEDIA.'}
            onChange={(e) => setForm({ ...form, code: e.target.value.toUpperCase() })}
          />
          <Input
            label="Name"
            required
            value={form.name}
            error={errors.name}
            onChange={(e) => setForm({ ...form, name: e.target.value })}
          />
          <Textarea
            label="Description"
            rows={2}
            value={form.description}
            onChange={(e) => setForm({ ...form, description: e.target.value })}
          />
        </form>
      </Modal>

      <ConfirmDialog
        open={Boolean(deleting)}
        onClose={() => setDeleting(null)}
        onConfirm={() => remove.mutate()}
        loading={remove.isPending}
        danger
        title="Delete this department?"
        confirmLabel="Delete"
        message={`${deleting?.name} will be removed. This is refused if anyone is assigned to it or it has records on file.`}
      />
    </>
  )
}
