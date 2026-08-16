import { useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Download, FileText, Trash2, Upload } from 'lucide-react'
import { api, describeError } from '../lib/api'
import { useAuth } from '../lib/auth'
import { enumOptions, useDepartments, useListState, usePagedQuery } from '../lib/hooks'
import { formatBytes, formatDate, humanise } from '../lib/format'
import type { DocumentItem } from '../lib/types'
import {
  Badge, Button, Card, Checkbox, ConfirmDialog, EmptyState, ErrorState, Input, Modal, PageHeader,
  Pagination, SearchInput, Select, SkeletonRows, Table, Td, Textarea, Th, Tr, useToast,
} from '../components/ui'
import { downloadDocument } from '../components/DocumentPanel'

const CATEGORIES = [
  'Contract', 'Legal', 'Medical', 'Financial', 'Policy', 'Minutes', 'Identity', 'Other',
] as const

function UploadModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const toast = useToast()
  const queryClient = useQueryClient()
  const departments = useDepartments()
  const { user, isSuperAdmin } = useAuth()
  const fileRef = useRef<HTMLInputElement>(null)

  const [file, setFile] = useState<File | null>(null)
  const [form, setForm] = useState({
    title: '', category: 'Other', departmentId: user?.departmentId ?? '',
    description: '', isConfidential: false,
  })
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function submit(e: React.FormEvent) {
    e.preventDefault()
    if (!file) { setError('Choose a file to upload.'); return }
    if (!form.departmentId) { setError('Select a department.'); return }

    setBusy(true)
    setError(null)
    try {
      const body = new FormData()
      body.append('file', file)
      body.append('title', form.title.trim() || file.name)
      body.append('category', form.category)
      body.append('departmentId', form.departmentId)
      body.append('description', form.description)
      body.append('isConfidential', String(form.isConfidential))

      await api.post('/documents', body, { headers: { 'Content-Type': 'multipart/form-data' } })
      toast.success('Document uploaded.')
      void queryClient.invalidateQueries({ queryKey: ['documents'] })
      setFile(null)
      setForm({ ...form, title: '', description: '', isConfidential: false })
      onClose()
    } catch (err) {
      setError(describeError(err))
    } finally {
      setBusy(false)
    }
  }

  const departmentOptions = (departments.data ?? [])
    .filter((d) => isSuperAdmin || d.id === user?.departmentId)
    .map((d) => ({ value: d.id, label: d.name }))

  return (
    <Modal open={open} onClose={onClose} title="Upload a document"
      description="Files up to 25 MB. Executables are rejected."
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button variant="primary" loading={busy} onClick={submit}>Upload</Button>
        </>
      }>
      <form onSubmit={submit} className="space-y-4" noValidate>
        {error && (
          <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-800 dark:border-red-500/30 dark:bg-red-950/40 dark:text-red-200">
            {error}
          </p>
        )}

        <div>
          <p className="mb-1.5 text-xs font-medium text-[var(--text-muted)]">File</p>
          <button type="button" onClick={() => fileRef.current?.click()}
            className="flex w-full items-center justify-center gap-2 rounded-lg border border-dashed py-6 text-sm text-[var(--text-muted)] hover:border-[var(--accent-solid)] hover:text-[var(--text)]">
            <Upload className="size-4" />
            {file ? `${file.name} (${formatBytes(file.size)})` : 'Choose a file…'}
          </button>
          <input ref={fileRef} type="file" className="sr-only" aria-label="Choose a file"
            onChange={(e) => { setFile(e.target.files?.[0] ?? null); setError(null) }} />
        </div>

        <Input label="Title" value={form.title} placeholder="Defaults to the file name"
          onChange={(e) => setForm({ ...form, title: e.target.value })} />

        <div className="grid gap-4 sm:grid-cols-2">
          <Select label="Category" value={form.category} options={enumOptions(CATEGORIES)}
            onChange={(e) => setForm({ ...form, category: e.target.value })} />
          <Select label="Department" required value={form.departmentId} placeholder="Select…"
            options={departmentOptions}
            onChange={(e) => setForm({ ...form, departmentId: e.target.value })} />
        </div>

        <Textarea label="Description" rows={2} value={form.description}
          onChange={(e) => setForm({ ...form, description: e.target.value })} />

        <Checkbox label="Confidential — restrict to the owning department and heads"
          checked={form.isConfidential}
          onChange={(e) => setForm({ ...form, isConfidential: e.target.checked })} />
      </form>
    </Modal>
  )
}

export default function Documents() {
  const toast = useToast()
  const queryClient = useQueryClient()
  const { hasRole, canWriteDepartment } = useAuth()
  const list = useListState('createdAt')
  const departments = useDepartments()

  const [uploading, setUploading] = useState(false)
  const [pendingDelete, setPendingDelete] = useState<DocumentItem | null>(null)
  const [deleting, setDeleting] = useState(false)

  const { data, isLoading, isError, refetch } = usePagedQuery<DocumentItem>(
    'documents', '/documents', list,
  )
  const canUpload = hasRole('SuperAdmin', 'DepartmentHead', 'Staff')

  async function confirmDelete() {
    if (!pendingDelete) return
    setDeleting(true)
    try {
      await api.delete(`/documents/${pendingDelete.id}`)
      toast.success('Document removed.')
      void queryClient.invalidateQueries({ queryKey: ['documents'] })
      setPendingDelete(null)
    } catch (error) {
      toast.error(describeError(error))
    } finally {
      setDeleting(false)
    }
  }

  return (
    <>
      <PageHeader title="Documents"
        subtitle="Contracts, medical reports, financial records, policies and minutes."
        actions={canUpload && (
          <Button variant="primary" icon={<Upload className="size-4" />} onClick={() => setUploading(true)}>
            Upload document
          </Button>
        )}
      />

      <Card>
        <div className="flex flex-wrap items-center gap-2 border-b px-4 py-3">
          <SearchInput value={list.search} onChange={list.setSearch}
            placeholder="Search title or file name…" />
          <Select className="h-9 w-auto" placeholder="All categories" value={list.filters.category ?? ''}
            options={enumOptions(CATEGORIES)} onChange={(e) => list.setFilter('category', e.target.value)} />
          <Select className="h-9 w-auto" placeholder="All departments" value={list.filters.departmentId ?? ''}
            options={(departments.data ?? []).map((d) => ({ value: d.id, label: d.name }))}
            onChange={(e) => list.setFilter('departmentId', e.target.value)} />
          {(list.search || Object.keys(list.filters).length > 0) && (
            <Button size="sm" variant="ghost" onClick={list.reset}>Clear</Button>
          )}
        </div>

        {isError ? (
          <ErrorState message="Documents could not be loaded." onRetry={() => void refetch()} />
        ) : (
          <>
            <Table>
              <thead>
                <tr>
                  <Th sortable active={list.sortBy === 'title'} descending={list.sortDescending}
                    onSort={() => list.toggleSort('title')}>Document</Th>
                  <Th>Category</Th><Th>Department</Th><Th>Linked to</Th>
                  <Th sortable active={list.sortBy === 'size'} descending={list.sortDescending}
                    onSort={() => list.toggleSort('size')} className="text-right">Size</Th>
                  <Th>Uploaded by</Th>
                  <Th sortable active={list.sortBy === 'createdAt'} descending={list.sortDescending}
                    onSort={() => list.toggleSort('createdAt')}>Uploaded</Th>
                  <Th className="text-right">Actions</Th>
                </tr>
              </thead>
              {isLoading ? <SkeletonRows cols={8} /> : (
                <tbody>
                  {data?.items.map((doc) => (
                    <Tr key={doc.id}>
                      <Td>
                        <div className="flex items-center gap-2">
                          <FileText className="size-4 shrink-0 text-[var(--text-subtle)]" aria-hidden />
                          <div className="min-w-0">
                            <p className="truncate font-medium">{doc.title}</p>
                            <p className="truncate text-xs text-[var(--text-subtle)]">{doc.fileName}</p>
                          </div>
                          {doc.isConfidential && <Badge tone="danger">Confidential</Badge>}
                        </div>
                      </Td>
                      <Td><Badge>{doc.category}</Badge></Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">{doc.departmentName ?? '—'}</Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">
                        {doc.linkedTo ? humanise(doc.linkedTo) : '—'}
                      </Td>
                      <Td className="tabular text-right text-[var(--text-muted)]">{formatBytes(doc.sizeBytes)}</Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">{doc.uploadedByName ?? '—'}</Td>
                      <Td className="whitespace-nowrap text-[var(--text-muted)]">{formatDate(doc.createdAt)}</Td>
                      <Td>
                        <div className="flex justify-end gap-1">
                          <button aria-label={`Download ${doc.fileName}`}
                            onClick={() => void downloadDocument(doc).catch((e) => toast.error(describeError(e)))}
                            className="rounded p-1.5 text-[var(--text-subtle)] hover:bg-[var(--surface-sunken)] hover:text-[var(--text)]">
                            <Download className="size-4" />
                          </button>
                          {canWriteDepartment(doc.departmentId) && (
                            <button aria-label={`Delete ${doc.fileName}`} onClick={() => setPendingDelete(doc)}
                              className="rounded p-1.5 text-[var(--text-subtle)] hover:bg-red-50 hover:text-red-600 dark:hover:bg-red-500/10">
                              <Trash2 className="size-4" />
                            </button>
                          )}
                        </div>
                      </Td>
                    </Tr>
                  ))}
                </tbody>
              )}
            </Table>

            {!isLoading && data?.items.length === 0 && (
              <EmptyState icon={<FileText className="size-5" />} title="No documents match these filters"
                action={canUpload && <Button variant="primary" onClick={() => setUploading(true)}>Upload document</Button>} />
            )}
            {data && (
              <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount}
                totalPages={data.totalPages} onPage={list.setPage} />
            )}
          </>
        )}
      </Card>

      <UploadModal open={uploading} onClose={() => setUploading(false)} />
      <ConfirmDialog open={Boolean(pendingDelete)} onClose={() => setPendingDelete(null)}
        onConfirm={confirmDelete} loading={deleting} danger title="Delete this document?"
        confirmLabel="Delete"
        message={`"${pendingDelete?.title}" will be removed and the stored file deleted.`} />
    </>
  )
}
