import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, Check, Minus, Plus, Video, X } from 'lucide-react'
import { api, describeError } from '../lib/api'
import { useAuth } from '../lib/auth'
import { formatDateTime, humanise } from '../lib/format'
import type { MeetingDetail, Motion, VoteChoice } from '../lib/types'
import {
  Badge, Button, Card, CardHeader, Checkbox, ErrorState, Field, Input, Modal, PageHeader,
  Spinner, statusTone, Textarea, useToast,
} from '../components/ui'
import DocumentPanel from '../components/DocumentPanel'

function MotionCard({
  motion, canManage, onChanged,
}: { motion: Motion; canManage: boolean; onChanged: () => void }) {
  const toast = useToast()
  const total = motion.votesFor + motion.votesAgainst + motion.votesAbstain
  const decisive = motion.votesFor + motion.votesAgainst
  const forShare = decisive === 0 ? 0 : (motion.votesFor / decisive) * 100

  const vote = useMutation({
    mutationFn: async (choice: VoteChoice) => api.post(`/motions/${motion.id}/vote`, { choice }),
    onSuccess: (_, choice) => { toast.success(`Your vote (${choice}) was recorded.`); onChanged() },
    onError: (e) => toast.error(describeError(e)),
  })

  const openVoting = useMutation({
    mutationFn: async () => api.post(`/motions/${motion.id}/open`),
    onSuccess: () => { toast.success('Voting is open.'); onChanged() },
    onError: (e) => toast.error(describeError(e)),
  })

  const closeVoting = useMutation({
    mutationFn: async () => api.post(`/motions/${motion.id}/close`),
    onSuccess: () => { toast.success('Voting closed and the result recorded.'); onChanged() },
    onError: (e) => toast.error(describeError(e)),
  })

  const withdraw = useMutation({
    mutationFn: async () => api.post(`/motions/${motion.id}/withdraw`),
    onSuccess: () => { toast.success('Motion withdrawn.'); onChanged() },
    onError: (e) => toast.error(describeError(e)),
  })

  return (
    <div className="border-b px-5 py-4 last:border-0">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <p className="text-sm font-medium">
            <span className="text-[var(--text-subtle)]">{motion.sequenceNumber}.</span> {motion.title}
          </p>
          {motion.description && (
            <p className="mt-1 text-sm text-[var(--text-muted)]">{motion.description}</p>
          )}
          <div className="mt-1.5 flex flex-wrap gap-1.5">
            <Badge tone={statusTone(motion.status)}>{motion.status}</Badge>
            {motion.isSecretBallot && <Badge tone="accent">Secret ballot</Badge>}
            <Badge>
              {motion.passThreshold >= 0.6 ? 'Special resolution' : 'Simple majority'}
            </Badge>
          </div>
        </div>
      </div>

      {/* Tally */}
      <div className="mt-3">
        <div className="flex h-2 overflow-hidden rounded-full bg-[var(--surface-sunken)]">
          {total > 0 && (
            <>
              <div className="bg-[var(--accent-solid)]" style={{ width: `${(motion.votesFor / total) * 100}%` }} />
              <div className="bg-red-500" style={{ width: `${(motion.votesAgainst / total) * 100}%` }} />
              <div className="bg-[var(--text-subtle)]" style={{ width: `${(motion.votesAbstain / total) * 100}%` }} />
            </>
          )}
        </div>
        <p className="tabular mt-1.5 text-xs text-[var(--text-muted)]">
          <span className="font-medium text-[var(--accent-text)]">{motion.votesFor} for</span> ·{' '}
          <span className="font-medium text-red-600">{motion.votesAgainst} against</span> ·{' '}
          {motion.votesAbstain} abstain · {total}/{motion.eligibleVoters} voted
          {decisive > 0 && ` · ${forShare.toFixed(0)}% in favour`}
        </p>
      </div>

      {/* Voting */}
      {motion.canVote && (
        <div className="mt-3 flex flex-wrap gap-2">
          {(['For', 'Against', 'Abstain'] as VoteChoice[]).map((choice) => (
            <Button key={choice} size="sm" loading={vote.isPending}
              variant={motion.myVote === choice ? 'primary' : 'secondary'}
              onClick={() => vote.mutate(choice)}
              icon={choice === 'For' ? <Check className="size-3.5" />
                : choice === 'Against' ? <X className="size-3.5" />
                  : <Minus className="size-3.5" />}>
              {choice}
            </Button>
          ))}
          {motion.myVote && (
            <span className="self-center text-xs text-[var(--text-subtle)]">
              You voted {motion.myVote}. You may change it while voting is open.
            </span>
          )}
        </div>
      )}

      {canManage && (
        <div className="mt-3 flex flex-wrap gap-2">
          {motion.status === 'Draft' && (
            <Button size="sm" variant="primary" loading={openVoting.isPending}
              onClick={() => openVoting.mutate()}>Open voting</Button>
          )}
          {motion.status === 'Open' && (
            <>
              <Button size="sm" variant="primary" loading={closeVoting.isPending}
                onClick={() => closeVoting.mutate()}>Close voting</Button>
              <Button size="sm" variant="ghost" loading={withdraw.isPending}
                onClick={() => withdraw.mutate()}>Withdraw</Button>
            </>
          )}
        </div>
      )}

      {/* Recorded votes, unless the ballot is secret */}
      {motion.votes.length > 0 && (
        <details className="mt-3">
          <summary className="cursor-pointer text-xs text-[var(--text-muted)] hover:text-[var(--text)]">
            View recorded votes ({motion.votes.length})
          </summary>
          <ul className="mt-2 space-y-1">
            {motion.votes.map((v) => (
              <li key={v.id} className="flex items-center justify-between text-xs">
                <span>{v.userName}</span>
                <Badge tone={v.choice === 'For' ? 'success' : v.choice === 'Against' ? 'danger' : 'neutral'}>
                  {v.choice}
                </Badge>
              </li>
            ))}
          </ul>
        </details>
      )}
      {motion.isSecretBallot && motion.votes.length === 0 && total > 0 && (
        <p className="mt-2 text-xs text-[var(--text-subtle)]">
          Individual votes are hidden on a secret ballot.
        </p>
      )}
    </div>
  )
}

export default function MeetingDetailPage() {
  const { id = '' } = useParams()
  const toast = useToast()
  const queryClient = useQueryClient()
  const { canWriteDepartment, canApproveDepartment, user } = useAuth()

  const [motionOpen, setMotionOpen] = useState(false)
  const [motion, setMotion] = useState({
    title: '', description: '', passThreshold: '0.5', isSecretBallot: false,
  })
  const [minutesOpen, setMinutesOpen] = useState(false)
  const [minutes, setMinutes] = useState('')

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['meeting', id],
    queryFn: async () => (await api.get<MeetingDetail>(`/meetings/${id}`)).data,
    enabled: Boolean(id),
  })

  function invalidate() {
    void queryClient.invalidateQueries({ queryKey: ['meeting', id] })
    void queryClient.invalidateQueries({ queryKey: ['meetings'] })
  }

  const addMotion = useMutation({
    mutationFn: async () =>
      api.post(`/motions/meeting/${id}`, { ...motion, passThreshold: Number(motion.passThreshold) }),
    onSuccess: () => {
      toast.success('Motion added as a draft.')
      setMotion({ title: '', description: '', passThreshold: '0.5', isSecretBallot: false })
      setMotionOpen(false)
      invalidate()
    },
    onError: (e) => toast.error(describeError(e)),
  })

  const changeStatus = useMutation({
    mutationFn: async (status: string) => api.post(`/meetings/${id}/status`, { status }),
    onSuccess: (_, status) => { toast.success(`Meeting marked ${humanise(status)}.`); invalidate() },
    onError: (e) => toast.error(describeError(e)),
  })

  const setAttendance = useMutation({
    mutationFn: async (status: string) => api.post(`/meetings/${id}/attendance`, { status }),
    onSuccess: () => { toast.success('Your attendance was recorded.'); invalidate() },
    onError: (e) => toast.error(describeError(e)),
  })

  const saveMinutes = useMutation({
    mutationFn: async () => api.put(`/meetings/${id}`, {
      title: data!.title, type: data!.type, scheduledAt: data!.scheduledAt,
      durationMinutes: data!.durationMinutes, location: data!.location, videoLink: data!.videoLink,
      agenda: data!.agenda, minutes, quorumRequired: data!.quorumRequired, chairId: data!.chairId,
    }),
    onSuccess: () => { toast.success('Minutes saved.'); setMinutesOpen(false); invalidate() },
    onError: (e) => toast.error(describeError(e)),
  })

  if (isLoading) return <Spinner label="Loading meeting" />
  if (isError || !data) {
    return <ErrorState message="This meeting could not be loaded." onRetry={() => void refetch()} />
  }

  const canWrite = canWriteDepartment(data.departmentId)
  const canManage = canApproveDepartment(data.departmentId)
  const myAttendance = data.attendees.find((a) => a.userId === user?.id)

  return (
    <>
      <Link to="/meetings"
        className="mb-3 inline-flex items-center gap-1.5 text-sm text-[var(--text-muted)] hover:text-[var(--text)]">
        <ArrowLeft className="size-4" /> Back to meetings
      </Link>

      <PageHeader title={data.title} subtitle={`${data.referenceNumber} · ${formatDateTime(data.scheduledAt)}`}
        actions={
          <>
            {data.videoLink && (
              <Button icon={<Video className="size-4" />} onClick={() => window.open(data.videoLink, '_blank', 'noopener')}>
                Join
              </Button>
            )}
            {canWrite && (
              <>
                <Button onClick={() => { setMinutes(data.minutes ?? ''); setMinutesOpen(true) }}>
                  Minutes
                </Button>
                <Button icon={<Plus className="size-4" />} onClick={() => setMotionOpen(true)}>
                  Add motion
                </Button>
              </>
            )}
            {canWrite && data.allowedTransitions.map((next) => (
              <Button key={next} variant={next === 'Completed' ? 'primary' : 'secondary'}
                loading={changeStatus.isPending} onClick={() => changeStatus.mutate(next)}>
                {humanise(next)}
              </Button>
            ))}
          </>
        }
      />

      <div className="grid gap-4 lg:grid-cols-3">
        <div className="space-y-4 lg:col-span-2">
          <Card>
            <CardHeader title="Meeting details" />
            <dl className="grid grid-cols-2 gap-4 p-5 sm:grid-cols-3">
              <Field label="Status"><Badge tone={statusTone(data.status)}>{humanise(data.status)}</Badge></Field>
              <Field label="Type"><Badge tone="info">{humanise(data.type)}</Badge></Field>
              <Field label="Duration">{data.durationMinutes} minutes</Field>
              <Field label="Location">{data.location ?? '—'}</Field>
              <Field label="Chair">{data.chairName ?? '—'}</Field>
              <Field label="Quorum">
                <span className={data.quorumMet ? 'text-[var(--accent-text)]' : 'text-amber-600'}>
                  {data.quorumRequired} required · {data.quorumMet ? 'met' : 'not met'}
                </span>
              </Field>
            </dl>
            {data.agenda && (
              <div className="border-t px-5 py-4">
                <p className="mb-1 text-xs font-medium text-[var(--text-muted)]">Agenda</p>
                <p className="text-sm leading-relaxed whitespace-pre-wrap">{data.agenda}</p>
              </div>
            )}
            {data.minutes && (
              <div className="border-t px-5 py-4">
                <p className="mb-1 text-xs font-medium text-[var(--text-muted)]">Minutes</p>
                <p className="text-sm leading-relaxed whitespace-pre-wrap">{data.minutes}</p>
              </div>
            )}
          </Card>

          <Card>
            <CardHeader title="Motions" subtitle={`${data.motions.length} on the agenda`} />
            {data.motions.length === 0 ? (
              <p className="px-5 py-8 text-center text-sm text-[var(--text-muted)]">
                No motions have been tabled.
              </p>
            ) : (
              data.motions.map((m) => (
                <MotionCard key={m.id} motion={m} canManage={canManage} onChanged={invalidate} />
              ))
            )}
          </Card>
        </div>

        <div className="space-y-4">
          {myAttendance && data.status !== 'Completed' && (
            <Card className="p-4">
              <p className="mb-2 text-xs font-medium text-[var(--text-muted)]">Your attendance</p>
              <div className="flex flex-wrap gap-2">
                {['Accepted', 'Declined', 'Attended'].map((s) => (
                  <Button key={s} size="sm" loading={setAttendance.isPending}
                    variant={myAttendance.status === s ? 'primary' : 'secondary'}
                    onClick={() => setAttendance.mutate(s)}>
                    {s}
                  </Button>
                ))}
              </div>
            </Card>
          )}

          <Card>
            <CardHeader title="Attendees" subtitle={`${data.attendees.length} invited`} />
            <ul className="max-h-80 divide-y overflow-y-auto">
              {data.attendees.map((a) => (
                <li key={a.id} className="flex items-center justify-between gap-2 px-5 py-2.5">
                  <div className="min-w-0">
                    <p className="truncate text-sm">{a.userName}</p>
                    <p className="text-xs text-[var(--text-subtle)]">{a.departmentName ?? '—'}</p>
                  </div>
                  <Badge tone={statusTone(a.status)}>{a.status}</Badge>
                </li>
              ))}
            </ul>
          </Card>

          <DocumentPanel documents={data.documents} departmentId={data.departmentId}
            linkField="meetingId" linkId={data.id} canUpload={canWrite} onChanged={invalidate} />
        </div>
      </div>

      <Modal open={motionOpen} onClose={() => setMotionOpen(false)} title="Table a motion"
        description="Motions start as a draft; open voting when the meeting reaches that item."
        footer={
          <>
            <Button variant="ghost" onClick={() => setMotionOpen(false)}>Cancel</Button>
            <Button variant="primary" loading={addMotion.isPending} disabled={!motion.title.trim()}
              onClick={() => addMotion.mutate()}>Add motion</Button>
          </>
        }>
        <div className="space-y-4">
          <Input label="Title" required autoFocus value={motion.title}
            placeholder="e.g. Approve the revised player welfare grant policy"
            onChange={(e) => setMotion({ ...motion, title: e.target.value })} />
          <Textarea label="Description" rows={3} value={motion.description}
            onChange={(e) => setMotion({ ...motion, description: e.target.value })} />
          <Input label="Pass threshold" type="number" min={0.5} max={1} step={0.001}
            value={motion.passThreshold}
            hint="0.5 for a simple majority, 0.667 for a special resolution. Abstentions are excluded."
            onChange={(e) => setMotion({ ...motion, passThreshold: e.target.value })} />
          <Checkbox label="Secret ballot (hide who voted which way)" checked={motion.isSecretBallot}
            onChange={(e) => setMotion({ ...motion, isSecretBallot: e.target.checked })} />
        </div>
      </Modal>

      <Modal open={minutesOpen} onClose={() => setMinutesOpen(false)} wide title="Meeting minutes"
        footer={
          <>
            <Button variant="ghost" onClick={() => setMinutesOpen(false)}>Cancel</Button>
            <Button variant="primary" loading={saveMinutes.isPending} onClick={() => saveMinutes.mutate()}>
              Save minutes
            </Button>
          </>
        }>
        <Textarea label="Minutes" rows={14} value={minutes} onChange={(e) => setMinutes(e.target.value)}
          placeholder="Record of the discussion and resolutions." />
      </Modal>
    </>
  )
}
