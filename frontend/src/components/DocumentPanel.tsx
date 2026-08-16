import { useRef, useState } from 'react'
import { Download, FileText, Paperclip, Trash2 } from 'lucide-react'
import { api, describeError } from '../lib/api'
import { formatBytes, formatDate } from '../lib/format'
import type { DocumentItem } from '../lib/types'
import { Badge, Button, Card, CardHeader, ConfirmDialog, useToast } from './ui'

/** Downloads through the API so the Authorization header is sent and access is re-checked. */
export async function downloadDocument(doc: DocumentItem) {
  const response = await api.get(`/documents/${doc.id}/download`, { responseType: 'blob' })
  const url = URL.createObjectURL(response.data as Blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = doc.fileName
  anchor.click()
  URL.revokeObjectURL(url)
}

export default function DocumentPanel({
  documents, departmentId, linkField, linkId, canUpload, onChanged,
}: {
  documents: DocumentItem[]
  departmentId: string
  /** e.g. "welfareCaseId" — links the upload to its parent record. */
  linkField: string
  linkId: string
  canUpload: boolean
  onChanged: () => void
}) {
  const toast = useToast()
  const inputRef = useRef<HTMLInputElement>(null)
  const [uploading, setUploading] = useState(false)
  const [pendingDelete, setPendingDelete] = useState<DocumentItem | null>(null)
  const [deleting, setDeleting] = useState(false)

  async function upload(file: File) {
    setUploading(true)
    try {
      const body = new FormData()
      body.append('file', file)
      body.append('title', file.name)
      body.append('departmentId', departmentId)
      body.append(linkField, linkId)

      await api.post('/documents', body, { headers: { 'Content-Type': 'multipart/form-data' } })
      toast.success(`${file.name} uploaded.`)
      onChanged()
    } catch (error) {
      toast.error(describeError(error))
    } finally {
      setUploading(false)
      if (inputRef.current) inputRef.current.value = ''
    }
  }

  async function confirmDelete() {
    if (!pendingDelete) return
    setDeleting(true)
    try {
      await api.delete(`/documents/${pendingDelete.id}`)
      toast.success('Document removed.')
      onChanged()
      setPendingDelete(null)
    } catch (error) {
      toast.error(describeError(error))
    } finally {
      setDeleting(false)
    }
  }

  return (
    <Card>
      <CardHeader
        title="Documents"
        subtitle={`${documents.length} attached`}
        action={canUpload && (
          <>
            <input ref={inputRef} type="file" className="sr-only" aria-label="Upload a document"
              onChange={(e) => { const f = e.target.files?.[0]; if (f) void upload(f) }} />
            <Button size="sm" loading={uploading} icon={<Paperclip className="size-3.5" />}
              onClick={() => inputRef.current?.click()}>
              Attach
            </Button>
          </>
        )}
      />
      {documents.length === 0 ? (
        <p className="px-5 py-8 text-center text-sm text-[var(--text-muted)]">
          No documents attached yet.
        </p>
      ) : (
        <ul className="divide-y">
          {documents.map((doc) => (
            <li key={doc.id} className="flex items-center gap-3 px-5 py-3">
              <FileText className="size-4 shrink-0 text-[var(--text-subtle)]" aria-hidden />
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-medium">{doc.title}</p>
                <p className="text-xs text-[var(--text-subtle)]">
                  {formatBytes(doc.sizeBytes)} · {formatDate(doc.createdAt)}
                  {doc.uploadedByName && ` · ${doc.uploadedByName}`}
                </p>
              </div>
              {doc.isConfidential && <Badge tone="danger">Confidential</Badge>}
              <button aria-label={`Download ${doc.fileName}`}
                onClick={() => void downloadDocument(doc).catch((e) => toast.error(describeError(e)))}
                className="rounded p-1.5 text-[var(--text-subtle)] hover:bg-[var(--surface-sunken)] hover:text-[var(--text)]">
                <Download className="size-4" />
              </button>
              {canUpload && (
                <button aria-label={`Delete ${doc.fileName}`} onClick={() => setPendingDelete(doc)}
                  className="rounded p-1.5 text-[var(--text-subtle)] hover:bg-red-50 hover:text-red-600 dark:hover:bg-red-500/10">
                  <Trash2 className="size-4" />
                </button>
              )}
            </li>
          ))}
        </ul>
      )}

      <ConfirmDialog open={Boolean(pendingDelete)} onClose={() => setPendingDelete(null)}
        onConfirm={confirmDelete} loading={deleting} danger title="Delete this document?"
        confirmLabel="Delete"
        message={`"${pendingDelete?.title}" will be removed and the stored file deleted.`} />
    </Card>
  )
}
