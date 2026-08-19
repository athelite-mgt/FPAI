import { useQuery } from '@tanstack/react-query'
import { History } from 'lucide-react'
import { api } from '../lib/api'
import { useListState, usePagedQuery, useUserLookup } from '../lib/hooks'
import { formatDateTime, humanise } from '../lib/format'
import type { AuditEntry } from '../lib/types'
import {
  Badge, Button, Card, EmptyState, ErrorState, PageHeader, Pagination, Select, SkeletonRows,
  Table, Td, Th, Tr, type Tone,
} from '../components/ui'

const ACTIONS = ['Created', 'Updated', 'Deleted'] as const

function actionTone(action: string): Tone {
  if (action === 'Created') return 'success'
  if (action === 'Deleted') return 'danger'
  return 'info'
}

/** The Changes column stores a JSON diff of {field: {from, to}}; render it, or fall back to raw text. */
function ChangesCell({ changes }: { changes?: string }) {
  if (!changes) return <span className="text-[var(--text-subtle)]">—</span>

  let parsed: Record<string, { from?: string; to?: string }> | null = null
  try {
    parsed = JSON.parse(changes) as Record<string, { from?: string; to?: string }>
  } catch {
    return <span className="font-mono text-xs break-all">{changes}</span>
  }

  return (
    <ul className="space-y-0.5">
      {Object.entries(parsed).map(([field, diff]) => (
        <li key={field} className="text-xs leading-relaxed">
          <span className="font-medium text-[var(--text)]">{field}</span>{': '}
          <span className="text-[var(--text-subtle)] line-through">{diff.from || '—'}</span>
          {' → '}
          <span className="text-[var(--text-muted)]">{diff.to || '—'}</span>
        </li>
      ))}
    </ul>
  )
}

export default function AuditLog() {
  const list = useListState('timestamp')
  const users = useUserLookup()
  const entities = useQuery({
    queryKey: ['audit-entities'],
    queryFn: async () => (await api.get<string[]>('/users/audit/entities')).data,
    staleTime: 5 * 60_000,
  })

  const { data, isLoading, isError, refetch } = usePagedQuery<AuditEntry>('audit', '/users/audit', list)

  return (
    <>
      <PageHeader title="Audit Log"
        subtitle="Every recorded change across the system, newest first. Visible to Super Admins only." />

      <Card>
        <div className="flex flex-wrap items-center gap-2 border-b px-4 py-3">
          <Select className="h-9 w-auto" placeholder="All modules" value={list.filters.entityName ?? ''}
            options={(entities.data ?? []).map((n) => ({ value: n, label: humanise(n) }))}
            onChange={(e) => list.setFilter('entityName', e.target.value)} />
          <Select className="h-9 w-auto" placeholder="All actions" value={list.filters.action ?? ''}
            options={ACTIONS.map((a) => ({ value: a, label: a }))}
            onChange={(e) => list.setFilter('action', e.target.value)} />
          <Select className="h-9 w-auto" placeholder="Anyone" value={list.filters.userId ?? ''}
            options={(users.data ?? []).map((u) => ({ value: u.id, label: u.label }))}
            onChange={(e) => list.setFilter('userId', e.target.value)} />
          {Object.keys(list.filters).length > 0 && (
            <Button size="sm" variant="ghost" onClick={list.reset}>Clear</Button>
          )}
        </div>

        {isError ? (
          <ErrorState message="The audit log could not be loaded." onRetry={() => void refetch()} />
        ) : (
          <>
            <Table>
              <thead>
                <tr>
                  <Th>When</Th><Th>Who</Th><Th>Action</Th><Th>Record</Th><Th>Changes</Th>
                </tr>
              </thead>
              {isLoading ? <SkeletonRows cols={5} /> : (
                <tbody>
                  {data?.items.map((entry) => (
                    <Tr key={entry.id}>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">
                        {formatDateTime(entry.timestamp)}
                      </Td>
                      <Td className="whitespace-nowrap">{entry.userName ?? 'System'}</Td>
                      <Td><Badge tone={actionTone(entry.action)}>{entry.action}</Badge></Td>
                      <Td className="whitespace-nowrap">
                        {humanise(entry.entityName)}
                        <p className="font-mono text-[11px] text-[var(--text-subtle)]">{entry.entityId}</p>
                      </Td>
                      <Td className="max-w-md"><ChangesCell changes={entry.changes} /></Td>
                    </Tr>
                  ))}
                </tbody>
              )}
            </Table>

            {!isLoading && data?.items.length === 0 && (
              <EmptyState icon={<History className="size-5" />} title="No audit entries match these filters" />
            )}
            {data && (
              <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount}
                totalPages={data.totalPages} onPage={list.setPage} />
            )}
          </>
        )}
      </Card>
    </>
  )
}
