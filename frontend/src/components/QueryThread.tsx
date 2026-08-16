import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { api, describeError } from '../lib/api'
import { formatRelative } from '../lib/format'
import type { AccountantQuery } from '../lib/types'
import { Badge, Button, Card, CardHeader, Modal, statusTone, Textarea, useToast } from './ui'

/** Accountant queries raised against a voucher or expense, with the department's replies. */
export default function QueryThread({
  queries, onChanged, canAnswer, canResolve,
}: {
  queries: AccountantQuery[]
  onChanged: () => void
  canAnswer: boolean
  canResolve: boolean
}) {
  const toast = useToast()
  const [answering, setAnswering] = useState<AccountantQuery | null>(null)
  const [response, setResponse] = useState('')

  const answer = useMutation({
    mutationFn: async () => api.post(`/finance/queries/${answering!.id}/answer`, { response }),
    onSuccess: () => {
      toast.success('Response sent to the accountant.')
      setAnswering(null)
      setResponse('')
      onChanged()
    },
    onError: (e) => toast.error(describeError(e)),
  })

  const resolve = useMutation({
    mutationFn: async (queryId: string) => api.post(`/finance/queries/${queryId}/resolve`),
    onSuccess: () => {
      toast.success('Query resolved.')
      onChanged()
    },
    onError: (e) => toast.error(describeError(e)),
  })

  return (
    <Card>
      <CardHeader title="Accountant queries" subtitle={`${queries.length} raised`} />
      {queries.length === 0 ? (
        <p className="px-5 py-8 text-center text-sm text-[var(--text-muted)]">
          No queries have been raised.
        </p>
      ) : (
        <ul className="divide-y">
          {queries.map((query) => (
            <li key={query.id} className="px-5 py-4">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0 flex-1">
                  <p className="text-sm leading-relaxed">{query.question}</p>
                  <p className="mt-1 text-xs text-[var(--text-subtle)]">
                    {query.raisedByName ?? 'Accountant'} · {formatRelative(query.createdAt)}
                  </p>
                </div>
                <Badge tone={query.status === 'Open' ? 'danger' : statusTone(query.status)}>
                  {query.status}
                </Badge>
              </div>

              {query.response && (
                <div className="mt-3 rounded-lg border-l-2 border-[var(--accent-solid)] bg-[var(--surface-sunken)] py-2 pl-3">
                  <p className="text-sm leading-relaxed">{query.response}</p>
                  <p className="mt-1 text-xs text-[var(--text-subtle)]">
                    {query.answeredByName ?? 'Department'} · {formatRelative(query.answeredAt)}
                  </p>
                </div>
              )}

              <div className="mt-3 flex gap-2">
                {canAnswer && query.status === 'Open' && (
                  <Button size="sm" onClick={() => { setAnswering(query); setResponse('') }}>
                    Respond
                  </Button>
                )}
                {canResolve && query.status !== 'Resolved' && (
                  <Button size="sm" variant="primary" loading={resolve.isPending}
                    onClick={() => resolve.mutate(query.id)}>
                    Mark resolved
                  </Button>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}

      <Modal open={Boolean(answering)} onClose={() => setAnswering(null)} title="Respond to the query"
        footer={
          <>
            <Button variant="ghost" onClick={() => setAnswering(null)}>Cancel</Button>
            <Button variant="primary" loading={answer.isPending} disabled={!response.trim()}
              onClick={() => answer.mutate()}>Send response</Button>
          </>
        }>
        <p className="mb-3 rounded-lg bg-[var(--surface-sunken)] px-3 py-2 text-sm text-[var(--text-muted)]">
          {answering?.question}
        </p>
        <Textarea label="Your response" rows={4} value={response} autoFocus
          onChange={(e) => setResponse(e.target.value)} />
      </Modal>
    </Card>
  )
}
