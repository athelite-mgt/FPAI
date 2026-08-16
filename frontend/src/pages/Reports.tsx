import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  Bar, BarChart, CartesianGrid, Cell, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts'
import { Download, FileBarChart } from 'lucide-react'
import { api, describeError } from '../lib/api'
import { formatNumber, humanise } from '../lib/format'
import { useChartColors } from '../lib/preferences'
import type { ReportDefinition, ReportSummary } from '../lib/types'
import {
  Button, Card, CardHeader, EmptyState, ErrorState, Input, PageHeader, Spinner, Table, Td, Th, Tr,
  useToast,
} from '../components/ui'

export default function Reports() {
  const toast = useToast()
  const colors = useChartColors()
  const [selected, setSelected] = useState<string>('welfare-summary')
  const [range, setRange] = useState({ from: '', to: '' })
  const [exporting, setExporting] = useState(false)

  const definitions = useQuery({
    queryKey: ['report-definitions'],
    queryFn: async () => (await api.get<ReportDefinition[]>('/reports')).data,
    staleTime: 10 * 60_000,
  })

  const params = {
    ...(range.from ? { from: new Date(range.from).toISOString() } : {}),
    ...(range.to ? { to: new Date(range.to).toISOString() } : {}),
  }

  const report = useQuery({
    queryKey: ['report', selected, params],
    queryFn: async () => (await api.get<ReportSummary>(`/reports/${selected}`, { params })).data,
    enabled: Boolean(selected),
  })

  async function exportCsv() {
    setExporting(true)
    try {
      const response = await api.get(`/reports/${selected}/export`, {
        params, responseType: 'blob',
      })
      const url = URL.createObjectURL(response.data as Blob)
      const anchor = document.createElement('a')
      anchor.href = url
      anchor.download = `${selected}-${new Date().toISOString().slice(0, 10)}.csv`
      anchor.click()
      URL.revokeObjectURL(url)
      toast.success('Report exported.')
    } catch (error) {
      toast.error(describeError(error))
    } finally {
      setExporting(false)
    }
  }

  return (
    <>
      <PageHeader title="Reports"
        subtitle="Cross-department summaries with CSV export for the board pack."
        actions={
          <Button variant="primary" loading={exporting} icon={<Download className="size-4" />}
            onClick={exportCsv}>
            Export CSV
          </Button>
        }
      />

      <div className="grid gap-4 lg:grid-cols-4">
        <Card className="lg:col-span-1">
          <CardHeader title="Available reports" />
          <nav className="p-2">
            {definitions.data?.map((definition) => (
              <button key={definition.key} onClick={() => setSelected(definition.key)}
                className={`w-full rounded-lg px-3 py-2.5 text-left transition-colors ${
                  selected === definition.key
                    ? 'bg-[var(--accent-solid)] text-white'
                    : 'hover:bg-[var(--surface-sunken)]'
                }`}>
                <p className="text-sm font-medium">{definition.title}</p>
                <p className={`mt-0.5 text-xs ${
                  selected === definition.key ? 'text-[var(--accent-on-solid)]' : 'text-[var(--text-muted)]'
                }`}>
                  {definition.description}
                </p>
              </button>
            ))}
          </nav>

          <div className="space-y-3 border-t p-4">
            <p className="text-xs font-medium text-[var(--text-muted)]">Reporting period</p>
            <Input label="From" type="date" value={range.from}
              onChange={(e) => setRange({ ...range, from: e.target.value })} />
            <Input label="To" type="date" value={range.to}
              onChange={(e) => setRange({ ...range, to: e.target.value })} />
            {(range.from || range.to) && (
              <Button size="sm" variant="ghost" onClick={() => setRange({ from: '', to: '' })}>
                Clear dates
              </Button>
            )}
          </div>
        </Card>

        <div className="space-y-4 lg:col-span-3">
          {report.isLoading ? (
            <Card><Spinner label="Running report" /></Card>
          ) : report.isError ? (
            <Card>
              <ErrorState message="This report could not be run." onRetry={() => void report.refetch()} />
            </Card>
          ) : report.data ? (
            <>
              <Card>
                <CardHeader title={report.data.title} subtitle={report.data.description} />
                {report.data.rows.length === 0 ? (
                  <EmptyState icon={<FileBarChart className="size-5" />}
                    title="No data in this period"
                    description="Widen the reporting period to include more records." />
                ) : (
                  <div className="h-72 p-4">
                    <ResponsiveContainer width="100%" height="100%">
                      <BarChart data={report.data.rows} margin={{ top: 4, right: 8, left: 0, bottom: 0 }}>
                        <CartesianGrid strokeDasharray="3 3" stroke="var(--border)" vertical={false} />
                        <XAxis dataKey="label" tick={{ fontSize: 10, fill: 'var(--text-subtle)' }}
                          axisLine={false} tickLine={false} interval={0} angle={-35}
                          textAnchor="end" height={90} tickFormatter={(v) => humanise(v)} />
                        <YAxis tick={{ fontSize: 11, fill: 'var(--text-subtle)' }} axisLine={false}
                          tickLine={false} allowDecimals={false} width={32} />
                        <Tooltip
                          contentStyle={{
                            background: 'var(--surface-raised)', border: '1px solid var(--border)',
                            borderRadius: 8, fontSize: 12,
                          }}
                          labelFormatter={(v) => humanise(String(v))} />
                        <Bar dataKey="count" name="Records" radius={[4, 4, 0, 0]}>
                          {report.data.rows.map((_, i) => (
                            <Cell key={i} fill={colors.categorical[i % colors.categorical.length]} />
                          ))}
                        </Bar>
                      </BarChart>
                    </ResponsiveContainer>
                  </div>
                )}
              </Card>

              <Card>
                <CardHeader title="Breakdown"
                  subtitle={report.data.total !== undefined && report.data.total !== null
                    ? `Total: ${formatNumber(report.data.total)}`
                    : undefined} />
                <Table>
                  <thead>
                    <tr><Th>Series</Th><Th className="text-right">Count</Th></tr>
                  </thead>
                  <tbody>
                    {report.data.rows.map((row) => (
                      <Tr key={row.label}>
                        <Td>{humanise(row.label)}</Td>
                        <Td className="tabular text-right font-medium">{formatNumber(row.count)}</Td>
                      </Tr>
                    ))}
                  </tbody>
                </Table>
              </Card>
            </>
          ) : null}
        </div>
      </div>
    </>
  )
}
