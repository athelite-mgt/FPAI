import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { CheckSquare, ListChecks, Plus } from 'lucide-react'
import { api, describeError } from '../lib/api'
import { useAuth } from '../lib/auth'
import { enumOptions, useDepartments, useListState, usePagedQuery, useUserLookup } from '../lib/hooks'
import { formatCurrency, formatDate, formatRelative, humanise } from '../lib/format'
import type { ApprovalRequestItem, WorkTask } from '../lib/types'
import {
  Badge, Button, Card, EmptyState, ErrorState, Input, Modal, PageHeader, Pagination,
  priorityTone, SearchInput, Select, SkeletonRows, statusTone, Table, Td, Textarea, Th, Tr,
  useToast,
} from '../components/ui'

const TASK_STATUSES = ['Todo', 'InProgress', 'Blocked', 'Done', 'Cancelled'] as const
const APPROVAL_STATUSES = ['Pending', 'Approved', 'Rejected', 'Cancelled'] as const
const PRIORITIES = ['Low', 'Medium', 'High', 'Critical'] as const

function NewTaskModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const toast = useToast()
  const queryClient = useQueryClient()
  const departments = useDepartments()
  const users = useUserLookup()
  const { user, isSuperAdmin } = useAuth()

  const [form, setForm] = useState({
    title: '', description: '', departmentId: user?.departmentId ?? '',
    priority: 'Medium', assigneeId: '', dueDate: '',
  })
  const [errors, setErrors] = useState<Record<string, string>>({})

  const mutation = useMutation({
    mutationFn: async () =>
      api.post('/tasks', {
        ...form,
        assigneeId: form.assigneeId || null,
        dueDate: form.dueDate ? new Date(form.dueDate).toISOString() : null,
      }),
    onSuccess: () => {
      toast.success('Task created.')
      void queryClient.invalidateQueries({ queryKey: ['tasks'] })
      onClose()
    },
    onError: (e) => toast.error(describeError(e)),
  })

  function submit(e: React.FormEvent) {
    e.preventDefault()
    const next: Record<string, string> = {}
    if (!form.title.trim()) next.title = 'A title is required.'
    if (!form.departmentId) next.departmentId = 'Select a department.'
    setErrors(next)
    if (Object.keys(next).length) return
    mutation.mutate()
  }

  const departmentOptions = (departments.data ?? [])
    .filter((d) => isSuperAdmin || d.id === user?.departmentId)
    .map((d) => ({ value: d.id, label: d.name }))

  return (
    <Modal open={open} onClose={onClose} title="Create a task"
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button variant="primary" loading={mutation.isPending} onClick={submit}>Create task</Button>
        </>
      }>
      <form onSubmit={submit} className="space-y-4" noValidate>
        <Input label="Title" required value={form.title} error={errors.title}
          onChange={(e) => setForm({ ...form, title: e.target.value })} />
        <Textarea label="Description" rows={2} value={form.description}
          onChange={(e) => setForm({ ...form, description: e.target.value })} />
        <div className="grid gap-4 sm:grid-cols-2">
          <Select label="Department" required value={form.departmentId} error={errors.departmentId}
            placeholder="Select…" options={departmentOptions}
            onChange={(e) => setForm({ ...form, departmentId: e.target.value })} />
          <Select label="Priority" value={form.priority} options={enumOptions(PRIORITIES)}
            onChange={(e) => setForm({ ...form, priority: e.target.value })} />
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          <Select label="Assignee" value={form.assigneeId} placeholder="Unassigned"
            options={(users.data ?? []).map((u) => ({ value: u.id, label: u.label }))}
            onChange={(e) => setForm({ ...form, assigneeId: e.target.value })} />
          <Input label="Due date" type="date" value={form.dueDate}
            onChange={(e) => setForm({ ...form, dueDate: e.target.value })} />
        </div>
      </form>
    </Modal>
  )
}

function TasksTab() {
  const toast = useToast()
  const queryClient = useQueryClient()
  const { hasRole, user } = useAuth()
  const list = useListState('createdAt')
  const departments = useDepartments()
  const [creating, setCreating] = useState(false)

  const { data, isLoading, isError, refetch } = usePagedQuery<WorkTask>('tasks', '/tasks', list)

  const changeStatus = useMutation({
    mutationFn: async ({ id, status }: { id: string; status: string }) =>
      api.post(`/tasks/${id}/status`, { status }),
    onSuccess: () => {
      toast.success('Task updated.')
      void queryClient.invalidateQueries({ queryKey: ['tasks'] })
    },
    onError: (e) => toast.error(describeError(e)),
  })

  const canCreate = hasRole('SuperAdmin', 'DepartmentHead', 'Staff')

  return (
    <>
      <div className="flex flex-wrap items-center gap-2 border-b px-4 py-3">
        <SearchInput value={list.search} onChange={list.setSearch} placeholder="Search tasks…" />
        <Select className="h-9 w-auto" placeholder="All statuses" value={list.filters.status ?? ''}
          options={enumOptions(TASK_STATUSES)} onChange={(e) => list.setFilter('status', e.target.value)} />
        <Select className="h-9 w-auto" placeholder="All priorities" value={list.filters.priority ?? ''}
          options={enumOptions(PRIORITIES)} onChange={(e) => list.setFilter('priority', e.target.value)} />
        <Select className="h-9 w-auto" placeholder="All departments" value={list.filters.departmentId ?? ''}
          options={(departments.data ?? []).map((d) => ({ value: d.id, label: d.name }))}
          onChange={(e) => list.setFilter('departmentId', e.target.value)} />
        <Button size="sm" variant={list.filters.mine === 'true' ? 'primary' : 'secondary'}
          onClick={() => list.setFilter('mine', list.filters.mine === 'true' ? '' : 'true')}>
          Assigned to me
        </Button>
        <Button size="sm" variant={list.filters.overdue === 'true' ? 'danger' : 'secondary'}
          onClick={() => list.setFilter('overdue', list.filters.overdue === 'true' ? '' : 'true')}>
          Overdue
        </Button>
        {canCreate && (
          <Button size="sm" variant="primary" className="ml-auto" icon={<Plus className="size-3.5" />}
            onClick={() => setCreating(true)}>New task</Button>
        )}
      </div>

      {isError ? (
        <ErrorState message="Tasks could not be loaded." onRetry={() => void refetch()} />
      ) : (
        <>
          <Table>
            <thead>
              <tr>
                <Th>Reference</Th><Th>Task</Th><Th>Department</Th><Th>Assignee</Th>
                <Th sortable active={list.sortBy === 'priority'} descending={list.sortDescending}
                  onSort={() => list.toggleSort('priority')}>Priority</Th>
                <Th sortable active={list.sortBy === 'dueDate'} descending={list.sortDescending}
                  onSort={() => list.toggleSort('dueDate')}>Due</Th>
                <Th>Status</Th><Th className="text-right">Move to</Th>
              </tr>
            </thead>
            {isLoading ? <SkeletonRows cols={8} /> : (
              <tbody>
                {data?.items.map((task) => {
                  const mine = task.assigneeId === user?.id
                  return (
                    <Tr key={task.id}>
                      <Td className="font-medium whitespace-nowrap">{task.referenceNumber}</Td>
                      <Td className="max-w-xs">
                        <p className="truncate">{task.title}</p>
                        {task.description && (
                          <p className="truncate text-xs text-[var(--text-subtle)]">{task.description}</p>
                        )}
                      </Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">{task.departmentName}</Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">
                        {task.assigneeName ?? 'Unassigned'}
                        {mine && <Badge tone="success" className="ml-1">You</Badge>}
                      </Td>
                      <Td><Badge tone={priorityTone(task.priority)}>{task.priority}</Badge></Td>
                      <Td className="whitespace-nowrap">
                        <span className={task.isOverdue ? 'font-medium text-red-600' : 'text-[var(--text-muted)]'}>
                          {formatDate(task.dueDate)}
                        </span>
                      </Td>
                      <Td><Badge tone={statusTone(task.status)}>{humanise(task.status)}</Badge></Td>
                      <Td>
                        <div className="flex justify-end gap-1">
                          {task.allowedTransitions.slice(0, 2).map((next) => (
                            <Button key={next} size="sm" variant="ghost" loading={changeStatus.isPending}
                              onClick={() => changeStatus.mutate({ id: task.id, status: next })}>
                              {humanise(next)}
                            </Button>
                          ))}
                        </div>
                      </Td>
                    </Tr>
                  )
                })}
              </tbody>
            )}
          </Table>

          {!isLoading && data?.items.length === 0 && (
            <EmptyState icon={<ListChecks className="size-5" />} title="No tasks match these filters"
              action={canCreate && <Button variant="primary" onClick={() => setCreating(true)}>New task</Button>} />
          )}
          {data && (
            <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount}
              totalPages={data.totalPages} onPage={list.setPage} />
          )}
        </>
      )}

      <NewTaskModal open={creating} onClose={() => setCreating(false)} />
    </>
  )
}

function ApprovalsTab() {
  const toast = useToast()
  const queryClient = useQueryClient()
  const list = useListState('createdAt')
  const departments = useDepartments()

  const [deciding, setDeciding] = useState<{ item: ApprovalRequestItem; approve: boolean } | null>(null)
  const [comment, setComment] = useState('')

  const { data, isLoading, isError, refetch } = usePagedQuery<ApprovalRequestItem>(
    'approvals', '/approvals', list,
  )

  const decide = useMutation({
    mutationFn: async () =>
      api.post(`/approvals/${deciding!.item.id}/decision`, {
        approve: deciding!.approve,
        comment: comment || undefined,
      }),
    onSuccess: () => {
      toast.success(`Request ${deciding!.approve ? 'approved' : 'rejected'}.`)
      setDeciding(null)
      setComment('')
      void queryClient.invalidateQueries({ queryKey: ['approvals'] })
      void queryClient.invalidateQueries({ queryKey: ['vouchers'] })
      void queryClient.invalidateQueries({ queryKey: ['expenses'] })
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] })
    },
    onError: (e) => toast.error(describeError(e)),
  })

  return (
    <>
      <div className="flex flex-wrap items-center gap-2 border-b px-4 py-3">
        <SearchInput value={list.search} onChange={list.setSearch} placeholder="Search approvals…" />
        <Select className="h-9 w-auto" placeholder="All statuses" value={list.filters.status ?? ''}
          options={enumOptions(APPROVAL_STATUSES)}
          onChange={(e) => list.setFilter('status', e.target.value)} />
        <Select className="h-9 w-auto" placeholder="All departments" value={list.filters.departmentId ?? ''}
          options={(departments.data ?? []).map((d) => ({ value: d.id, label: d.name }))}
          onChange={(e) => list.setFilter('departmentId', e.target.value)} />
        <Button size="sm" variant={list.filters.awaitingMe === 'true' ? 'primary' : 'secondary'}
          onClick={() => list.setFilter('awaitingMe', list.filters.awaitingMe === 'true' ? '' : 'true')}>
          Awaiting my decision
        </Button>
        {(list.search || Object.keys(list.filters).length > 0) && (
          <Button size="sm" variant="ghost" onClick={list.reset}>Clear</Button>
        )}
      </div>

      {isError ? (
        <ErrorState message="Approvals could not be loaded." onRetry={() => void refetch()} />
      ) : (
        <>
          <Table>
            <thead>
              <tr>
                <Th>Reference</Th><Th>Request</Th><Th>Type</Th><Th>Department</Th>
                <Th className="text-right">Amount</Th><Th>Requested by</Th><Th>Raised</Th>
                <Th>Status</Th><Th className="text-right">Decision</Th>
              </tr>
            </thead>
            {isLoading ? <SkeletonRows cols={9} /> : (
              <tbody>
                {data?.items.map((item) => (
                  <Tr key={item.id}>
                    <Td className="font-medium whitespace-nowrap">{item.referenceNumber}</Td>
                    <Td className="max-w-xs truncate">{item.title}</Td>
                    <Td><Badge tone="info">{item.entityType}</Badge></Td>
                    <Td className="whitespace-nowrap text-[var(--text-muted)]">{item.departmentName}</Td>
                    <Td className="tabular text-right">{formatCurrency(item.amount)}</Td>
                    <Td className="whitespace-nowrap text-[var(--text-muted)]">{item.requestedByName ?? '—'}</Td>
                    <Td className="whitespace-nowrap text-[var(--text-muted)]">{formatRelative(item.createdAt)}</Td>
                    <Td>
                      <Badge tone={statusTone(item.status)}>{item.status}</Badge>
                      {item.decidedByName && (
                        <p className="mt-0.5 text-xs text-[var(--text-subtle)]">by {item.decidedByName}</p>
                      )}
                    </Td>
                    <Td>
                      {item.canDecide ? (
                        <div className="flex justify-end gap-1">
                          <Button size="sm" variant="primary"
                            onClick={() => { setDeciding({ item, approve: true }); setComment('') }}>
                            Approve
                          </Button>
                          <Button size="sm" variant="danger"
                            onClick={() => { setDeciding({ item, approve: false }); setComment('') }}>
                            Reject
                          </Button>
                        </div>
                      ) : (
                        <p className="text-right text-xs text-[var(--text-subtle)]">
                          {item.status === 'Pending' ? 'Not yours to decide' : '—'}
                        </p>
                      )}
                    </Td>
                  </Tr>
                ))}
              </tbody>
            )}
          </Table>

          {!isLoading && data?.items.length === 0 && (
            <EmptyState icon={<CheckSquare className="size-5" />} title="No approval requests match these filters" />
          )}
          {data && (
            <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount}
              totalPages={data.totalPages} onPage={list.setPage} />
          )}
        </>
      )}

      <Modal open={Boolean(deciding)} onClose={() => setDeciding(null)}
        title={deciding?.approve ? 'Approve this request' : 'Reject this request'}
        description={deciding?.item.title}
        footer={
          <>
            <Button variant="ghost" onClick={() => setDeciding(null)}>Cancel</Button>
            <Button variant={deciding?.approve ? 'primary' : 'danger'} loading={decide.isPending}
              disabled={!deciding?.approve && !comment.trim()}
              onClick={() => decide.mutate()}>
              {deciding?.approve ? 'Approve' : 'Reject'}
            </Button>
          </>
        }>
        <p className="mb-3 text-sm text-[var(--text-muted)]">
          Approving also advances the underlying {deciding?.item.entityType.toLowerCase()}.
        </p>
        <Textarea rows={3} value={comment} onChange={(e) => setComment(e.target.value)}
          label={deciding?.approve ? 'Comment (optional)' : 'Reason for rejection'}
          required={!deciding?.approve} />
      </Modal>
    </>
  )
}

export default function Tasks() {
  const [tab, setTab] = useState<'tasks' | 'approvals'>('tasks')

  return (
    <>
      <PageHeader title="Tasks & Approvals"
        subtitle="Departmental work queue and single-step approval decisions." />

      <Card>
        <div className="border-b px-4 pt-3">
          <div className="inline-flex rounded-lg bg-[var(--surface-sunken)] p-0.5">
            {(['tasks', 'approvals'] as const).map((key) => (
              <button key={key} onClick={() => setTab(key)}
                className={`rounded-md px-3 py-1.5 text-sm font-medium capitalize transition-colors ${
                  tab === key ? 'bg-[var(--surface-raised)] text-[var(--text)] shadow-sm' : 'text-[var(--text-muted)]'
                }`}>
                {key}
              </button>
            ))}
          </div>
        </div>
        {tab === 'tasks' ? <TasksTab /> : <ApprovalsTab />}
      </Card>
    </>
  )
}
