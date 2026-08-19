import { lazy, Suspense } from 'react'
import { NavLink, Navigate, Route, Routes } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Building2, History, Palette, Truck, UserCheck, Users } from 'lucide-react'
import { api } from '../lib/api'
import { useAuth } from '../lib/auth'
import type { PendingUser, Role } from '../lib/types'
import { Badge, EmptyState, PageHeader, Spinner } from '../components/ui'

const Appearance = lazy(() => import('./settings/Appearance'))
const Approvals = lazy(() => import('./settings/Approvals'))
const Departments = lazy(() => import('./settings/Departments'))
const Directory = lazy(() => import('./settings/Directory'))
const Users_ = lazy(() => import('./Users'))
const AuditLog = lazy(() => import('./AuditLog'))

interface Section {
  path: string
  label: string
  icon: typeof Palette
  description: string
  /** Empty means everyone; Appearance is a personal setting. */
  roles: Role[]
}

const SECTIONS: Section[] = [
  {
    path: 'appearance',
    label: 'Appearance',
    icon: Palette,
    description: 'Colour scheme, typeface and light or dark',
    roles: [],
  },
  {
    path: 'users',
    label: 'Users & roles',
    icon: Users,
    description: 'Accounts, roles and department assignment',
    roles: ['SuperAdmin', 'DepartmentHead'],
  },
  {
    path: 'approvals',
    label: 'Access requests',
    icon: UserCheck,
    description: 'Approve or decline people who have signed up',
    roles: ['SuperAdmin'],
  },
  {
    path: 'departments',
    label: 'Departments',
    icon: Building2,
    description: 'The departments that scope every record',
    roles: ['SuperAdmin'],
  },
  {
    path: 'directory',
    label: 'Clubs & vendors',
    icon: Truck,
    description: 'Shared reference data',
    roles: ['SuperAdmin', 'DepartmentHead'],
  },
  {
    path: 'audit-log',
    label: 'Audit log',
    icon: History,
    description: 'Every recorded change, across every module',
    roles: ['SuperAdmin'],
  },
]

/** Badge on the Access requests tab so a waiting queue is visible from anywhere in Settings. */
function usePendingCount(enabled: boolean) {
  const { data } = useQuery({
    queryKey: ['pending-users'],
    queryFn: async () => (await api.get<PendingUser[]>('/users/pending')).data,
    enabled,
    refetchInterval: 60_000,
  })
  return data?.length ?? 0
}

export default function Settings() {
  const { user, isSuperAdmin } = useAuth()
  const pendingCount = usePendingCount(isSuperAdmin)

  const visible = SECTIONS.filter(
    (section) => section.roles.length === 0 || section.roles.some((r) => user?.roles.includes(r)),
  )

  return (
    <>
      <PageHeader
        title="Settings"
        subtitle="Your appearance preferences, and — for administrators — everything that governs access."
      />

      <div className="grid gap-4 lg:grid-cols-[15rem_1fr]">
        <nav className="space-y-1" aria-label="Settings sections">
          {visible.map(({ path, label, icon: Icon, description }) => (
            <NavLink
              key={path}
              to={path}
              className={({ isActive }) =>
                `flex items-start gap-3 rounded-xl border px-3 py-2.5 transition-colors ${
                  isActive
                    ? 'border-[var(--accent-solid)] bg-[var(--accent-soft-bg)]'
                    : 'border-transparent hover:bg-[var(--surface-sunken)]'
                }`
              }
            >
              {({ isActive }) => (
                <>
                  <Icon
                    className={`mt-0.5 size-4 shrink-0 ${
                      isActive ? 'text-[var(--accent-text)]' : 'text-[var(--text-subtle)]'
                    }`}
                    aria-hidden
                  />
                  <span className="min-w-0 flex-1">
                    <span className="flex items-center gap-2 text-sm font-medium">
                      {label}
                      {path === 'approvals' && pendingCount > 0 && (
                        <Badge tone="warning">{pendingCount}</Badge>
                      )}
                    </span>
                    <span className="mt-0.5 block text-xs leading-snug text-[var(--text-muted)]">
                      {description}
                    </span>
                  </span>
                </>
              )}
            </NavLink>
          ))}
        </nav>

        <div className="min-w-0">
          <Suspense fallback={<Spinner />}>
            <Routes>
              <Route index element={<Navigate to="appearance" replace />} />
              <Route path="appearance" element={<Appearance />} />
              <Route
                path="users"
                element={
                  isSuperAdmin || user?.roles.includes('DepartmentHead') ? (
                    <Users_ embedded />
                  ) : (
                    <EmptyState title="You do not have access to this section" />
                  )
                }
              />
              <Route
                path="approvals"
                element={
                  isSuperAdmin ? (
                    <Approvals />
                  ) : (
                    <EmptyState
                      title="Administrators only"
                      description="Only a Super Admin can approve new accounts."
                    />
                  )
                }
              />
              <Route
                path="departments"
                element={
                  isSuperAdmin ? (
                    <Departments />
                  ) : (
                    <EmptyState
                      title="Administrators only"
                      description="Only a Super Admin can change departments."
                    />
                  )
                }
              />
              <Route
                path="directory"
                element={
                  isSuperAdmin || user?.roles.includes('DepartmentHead') ? (
                    <Directory />
                  ) : (
                    <EmptyState title="You do not have access to this section" />
                  )
                }
              />
              <Route
                path="audit-log"
                element={
                  isSuperAdmin ? (
                    <AuditLog />
                  ) : (
                    <EmptyState
                      title="Administrators only"
                      description="Only a Super Admin can view the audit log."
                    />
                  )
                }
              />
              <Route path="*" element={<Navigate to="appearance" replace />} />
            </Routes>
          </Suspense>
        </div>
      </div>
    </>
  )
}
