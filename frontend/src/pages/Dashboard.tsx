import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import {
  Area, AreaChart, Bar, BarChart, CartesianGrid, Cell, Legend, Line, LineChart,
  ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts'
import { Banknote, CalendarCheck, CheckSquare, Gavel, ListChecks, ShieldCheck } from 'lucide-react'
import { api } from '../lib/api'
import { useAuth } from '../lib/auth'
import { useChartColors } from '../lib/preferences'
import { formatCompactCurrency, formatRelative, humanise } from '../lib/format'
import type { Dashboard as DashboardData } from '../lib/types'
import { Card, CardHeader, ErrorState, PageHeader, Spinner, StatTile } from '../components/ui'

const AXIS = { fontSize: 11, fill: 'var(--text-subtle)' }

function ChartTooltip({ active, payload, label, currency }: any) {
  if (!active || !payload?.length) return null
  return (
    <div className="rounded-lg border bg-[var(--surface-raised)] px-3 py-2 text-xs shadow-lg">
      <p className="mb-1 font-medium text-[var(--text)]">{label}</p>
      {payload.map((entry: any) => (
        <p key={entry.name} className="flex items-center gap-2 text-[var(--text-muted)]">
          <span className="size-2 rounded-full" style={{ background: entry.color }} />
          {entry.name}:{' '}
          <span className="tabular font-medium text-[var(--text)]">
            {currency ? formatCompactCurrency(entry.value) : entry.value}
          </span>
        </p>
      ))}
    </div>
  )
}

export default function Dashboard() {
  const { user } = useAuth()
  // Charts need real colour values, so they are resolved from the active scheme.
  const colors = useChartColors()
  const { data, isLoading, error, refetch } = useQuery({
    queryKey: ['dashboard'],
    queryFn: async () => (await api.get<DashboardData>('/dashboard')).data,
  })

  if (isLoading) return <Spinner label="Loading your dashboard" />
  if (error || !data) {
    return <ErrorState message="The dashboard could not be loaded." onRetry={() => void refetch()} />
  }

  const hour = new Date().getHours()
  const greeting = hour < 12 ? 'Good morning' : hour < 17 ? 'Good afternoon' : 'Good evening'

  return (
    <>
      <PageHeader
        title={`${greeting}, ${user?.fullName.split(' ')[0] ?? 'there'}`}
        subtitle="Live position across welfare, legal, finance, governance and operations."
      />

      <div className="grid grid-cols-2 gap-4 lg:grid-cols-3 xl:grid-cols-6">
        <StatTile label="Active welfare" value={data.activeWelfareCases} tone="success"
          icon={<ShieldCheck className="size-4" />} sub="Open cases" />
        <StatTile label="Legal matters" value={data.activeLegalMatters} tone="accent"
          icon={<Gavel className="size-4" />} sub="Not yet closed" />
        <StatTile label="Monthly expense" value={formatCompactCurrency(data.monthlyExpense)} tone="warning"
          icon={<Banknote className="size-4" />} sub="Current month" />
        <StatTile label="Upcoming meetings" value={data.upcomingMeetings} tone="info"
          icon={<CalendarCheck className="size-4" />} sub="Scheduled" />
        <StatTile label="Pending tasks" value={data.pendingTasks} tone="neutral"
          icon={<ListChecks className="size-4" />} sub="Not done" />
        <StatTile label="Pending approvals" value={data.pendingApprovals} tone="danger"
          icon={<CheckSquare className="size-4" />} sub="Awaiting decision" />
      </div>

      <div className="mt-4 grid gap-4 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          <CardHeader title="Income vs expense" subtitle="Last six months, all departments in your scope" />
          <div className="h-64 p-4">
            <ResponsiveContainer width="100%" height="100%">
              <AreaChart data={data.financeTrend} margin={{ top: 4, right: 8, left: 0, bottom: 0 }}>
                <defs>
                  <linearGradient id="income" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stopColor={colors.accent} stopOpacity={0.35} />
                    <stop offset="100%" stopColor={colors.accent} stopOpacity={0} />
                  </linearGradient>
                  <linearGradient id="expense" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stopColor={colors.contrast} stopOpacity={0.3} />
                    <stop offset="100%" stopColor={colors.contrast} stopOpacity={0} />
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" stroke="var(--border)" vertical={false} />
                <XAxis dataKey="label" tick={AXIS} axisLine={false} tickLine={false} />
                <YAxis tick={AXIS} axisLine={false} tickLine={false}
                  tickFormatter={(v) => formatCompactCurrency(v)} width={56} />
                <Tooltip content={<ChartTooltip currency />} />
                <Legend wrapperStyle={{ fontSize: 12 }} />
                <Area type="monotone" dataKey="income" name="Income" stroke={colors.accent} fill="url(#income)" strokeWidth={2} />
                <Area type="monotone" dataKey="expense" name="Expense" stroke={colors.contrast} fill="url(#expense)" strokeWidth={2} />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        </Card>

        <Card>
          <CardHeader title="Welfare cases by status" />
          <div className="h-64 p-4">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={data.welfareByStatus} margin={{ top: 4, right: 8, left: 0, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="var(--border)" vertical={false} />
                <XAxis dataKey="label" tick={{ ...AXIS, fontSize: 10 }} axisLine={false} tickLine={false}
                  tickFormatter={(v) => humanise(v)} interval={0} angle={-30} textAnchor="end" height={54} />
                <YAxis tick={AXIS} axisLine={false} tickLine={false} allowDecimals={false} width={28} />
                <Tooltip content={<ChartTooltip />} />
                <Bar dataKey="count" name="Cases" radius={[4, 4, 0, 0]}>
                  {data.welfareByStatus.map((_, i) => (
                    <Cell key={i} fill={colors.categorical[i % colors.categorical.length]} />
                  ))}
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          </div>
        </Card>
      </div>

      <div className="mt-4 grid gap-4 lg:grid-cols-3">
        <Card>
          <CardHeader title="Legal matters by forum" subtitle="Open matters only" />
          <div className="h-56 p-4">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart layout="vertical" data={data.legalByType} margin={{ top: 4, right: 16, left: 8, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="var(--border)" horizontal={false} />
                <XAxis type="number" tick={AXIS} axisLine={false} tickLine={false} allowDecimals={false} />
                <YAxis type="category" dataKey="label" tick={AXIS} axisLine={false} tickLine={false}
                  width={78} tickFormatter={(v) => humanise(v)} />
                <Tooltip content={<ChartTooltip />} />
                <Bar dataKey="count" name="Matters" radius={[0, 4, 4, 0]}>
                  {data.legalByType.map((_, i) => (
                    <Cell key={i} fill={colors.categorical[i % colors.categorical.length]} />
                  ))}
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          </div>
        </Card>

        <Card>
          <CardHeader title="Voting participation" subtitle="Share of eligible members who voted" />
          <div className="h-56 p-4">
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={data.votingTrend} margin={{ top: 4, right: 8, left: 0, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="var(--border)" vertical={false} />
                <XAxis dataKey="label" tick={AXIS} axisLine={false} tickLine={false} />
                <YAxis tick={AXIS} axisLine={false} tickLine={false} domain={[0, 100]}
                  tickFormatter={(v) => `${v}%`} width={38} />
                <Tooltip content={<ChartTooltip />} />
                <Line type="monotone" dataKey="participationRate" name="Participation %"
                  stroke={colors.accent} strokeWidth={2} dot={{ r: 3 }} />
              </LineChart>
            </ResponsiveContainer>
          </div>
        </Card>

        <Card>
          <CardHeader title="Recent activity" subtitle="Latest changes across the system" />
          <div className="max-h-56 divide-y overflow-y-auto">
            {data.recentActivity.length === 0 ? (
              <p className="px-5 py-8 text-center text-sm text-[var(--text-muted)]">No activity yet.</p>
            ) : (
              data.recentActivity.map((item, i) => (
                <div key={i} className="flex items-start gap-3 px-5 py-2.5">
                  <span className="mt-1.5 size-1.5 shrink-0 rounded-full bg-[var(--accent-solid)]" />
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm text-[var(--text)]">
                      {item.action} {humanise(item.entityName)}
                    </p>
                    <p className="text-xs text-[var(--text-subtle)]">
                      {item.userName ?? 'System'} · {formatRelative(item.timestamp)}
                    </p>
                  </div>
                </div>
              ))
            )}
          </div>
        </Card>
      </div>

      <div className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        {[
          { to: '/welfare', label: 'Open a welfare case', icon: ShieldCheck },
          { to: '/legal', label: 'Register a legal matter', icon: Gavel },
          { to: '/finance', label: 'Raise a voucher', icon: Banknote },
          { to: '/tasks', label: 'Review approvals', icon: CheckSquare },
        ].map(({ to, label, icon: Icon }) => (
          <Link key={to} to={to}
            className="flex items-center gap-3 rounded-xl border bg-[var(--surface-raised)] px-4 py-3 text-sm font-medium shadow-[var(--shadow-card)] transition-colors hover:border-[var(--accent-solid)] hover:bg-[var(--surface-sunken)]">
            <Icon className="size-4 text-[var(--accent-text)]" aria-hidden />
            {label}
          </Link>
        ))}
      </div>
    </>
  )
}
