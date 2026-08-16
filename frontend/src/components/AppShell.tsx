import clsx from 'clsx'
import { useState } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Banknote, Bell, Briefcase, CalendarCheck, ChevronDown, FileBarChart, FileText, Gavel,
  LayoutDashboard, ListChecks, LogOut, Menu, Moon, Palette, Settings, ShieldCheck, Sun,
  UsersRound, X,
} from 'lucide-react'
import { useAuth } from '../lib/auth'
import { usePreferences } from '../lib/preferences'
import { api } from '../lib/api'
import { humanise, initialsOf, formatRelative } from '../lib/format'
import type { NotificationItem, Role } from '../lib/types'
import { Badge } from './ui'

interface NavItem {
  to: string
  label: string
  icon: typeof LayoutDashboard
  /** Roles allowed to see this item; mirrors the server-side module policies. */
  roles: Role[]
}

const ALL: Role[] = ['SuperAdmin', 'DepartmentHead', 'Staff', 'ExternalAccountant']
const NO_ACCOUNTANT: Role[] = ['SuperAdmin', 'DepartmentHead', 'Staff']

const NAV: NavItem[] = [
  { to: '/', label: 'Dashboard', icon: LayoutDashboard, roles: ALL },
  { to: '/welfare', label: 'Player Welfare', icon: ShieldCheck, roles: NO_ACCOUNTANT },
  { to: '/legal', label: 'Legal', icon: Gavel, roles: NO_ACCOUNTANT },
  { to: '/finance', label: 'Finance', icon: Banknote, roles: ALL },
  { to: '/meetings', label: 'Meetings & Voting', icon: CalendarCheck, roles: NO_ACCOUNTANT },
  { to: '/events', label: 'Events & Operations', icon: Briefcase, roles: NO_ACCOUNTANT },
  { to: '/documents', label: 'Documents', icon: FileText, roles: ALL },
  { to: '/tasks', label: 'Tasks & Approvals', icon: ListChecks, roles: ALL },
  { to: '/players', label: 'Member Directory', icon: UsersRound, roles: NO_ACCOUNTANT },
  { to: '/reports', label: 'Reports', icon: FileBarChart, roles: ['SuperAdmin', 'DepartmentHead', 'ExternalAccountant'] },
  { to: '/settings', label: 'Settings', icon: Settings, roles: ALL },
]

function Brand() {
  return (
    <div className="flex items-center gap-2.5">
      <div className="flex size-8 items-center justify-center rounded-lg bg-[var(--accent-solid)] font-bold text-white">
        F
      </div>
      <div className="leading-tight">
        <p className="text-sm font-semibold text-white">FPAI Connect</p>
        <p className="text-[11px] text-[var(--chrome-muted)]">Players Association of India</p>
      </div>
    </div>
  )
}

function NotificationBell() {
  const [open, setOpen] = useState(false)
  const queryClient = useQueryClient()

  const { data } = useQuery({
    queryKey: ['notifications'],
    queryFn: async () => (await api.get<NotificationItem[]>('/notifications')).data,
    refetchInterval: 60_000,
  })

  const unread = data?.filter((n) => !n.isRead).length ?? 0

  async function markAllRead() {
    await api.post('/notifications/read-all')
    void queryClient.invalidateQueries({ queryKey: ['notifications'] })
  }

  return (
    <div className="relative">
      <button
        onClick={() => setOpen((v) => !v)}
        aria-label={`Notifications${unread ? `, ${unread} unread` : ''}`}
        className="relative rounded-lg p-2 text-[var(--text-muted)] hover:bg-[var(--surface-sunken)] hover:text-[var(--text)]"
      >
        <Bell className="size-4.5" />
        {unread > 0 && (
          <span className="absolute top-1 right-1 flex size-4 items-center justify-center rounded-full bg-red-500 text-[10px] font-semibold text-white">
            {unread > 9 ? '9+' : unread}
          </span>
        )}
      </button>

      {open && (
        <>
          <div className="fixed inset-0 z-10" onClick={() => setOpen(false)} />
          <div className="animate-in absolute right-0 z-20 mt-2 w-80 rounded-xl border bg-[var(--surface-raised)] shadow-xl">
            <div className="flex items-center justify-between border-b px-4 py-2.5">
              <p className="text-sm font-semibold">Notifications</p>
              {unread > 0 && (
                <button onClick={markAllRead} className="text-xs text-[var(--accent-text)] hover:underline">
                  Mark all read
                </button>
              )}
            </div>
            <div className="max-h-80 overflow-y-auto">
              {!data?.length ? (
                <p className="px-4 py-8 text-center text-sm text-[var(--text-muted)]">Nothing yet.</p>
              ) : (
                data.slice(0, 20).map((n) => (
                  <div key={n.id}
                    className={clsx('border-b px-4 py-2.5 last:border-0', !n.isRead && 'bg-[var(--accent-soft-bg)] ')}>
                    <p className="text-sm font-medium text-[var(--text)]">{n.title}</p>
                    {n.body && <p className="mt-0.5 line-clamp-2 text-xs text-[var(--text-muted)]">{n.body}</p>}
                    <p className="mt-1 text-[11px] text-[var(--text-subtle)]">{formatRelative(n.createdAt)}</p>
                  </div>
                ))
              )}
            </div>
          </div>
        </>
      )}
    </div>
  )
}

function UserMenu() {
  const { user, signOut } = useAuth()
  const [open, setOpen] = useState(false)
  const navigate = useNavigate()
  if (!user) return null

  return (
    <div className="relative">
      <button onClick={() => setOpen((v) => !v)}
        className="flex items-center gap-2 rounded-lg py-1 pr-2 pl-1 hover:bg-[var(--surface-sunken)]">
        <span className="flex size-7 items-center justify-center rounded-full bg-[var(--accent-solid)] text-xs font-semibold text-white">
          {initialsOf(user.fullName)}
        </span>
        <span className="hidden text-left sm:block">
          <span className="block text-xs font-medium text-[var(--text)]">{user.fullName}</span>
          <span className="block text-[11px] text-[var(--text-muted)]">{humanise(user.roles[0])}</span>
        </span>
        <ChevronDown className="size-3.5 text-[var(--text-subtle)]" />
      </button>

      {open && (
        <>
          <div className="fixed inset-0 z-10" onClick={() => setOpen(false)} />
          <div className="animate-in absolute right-0 z-20 mt-2 w-60 rounded-xl border bg-[var(--surface-raised)] p-1 shadow-xl">
            <div className="border-b px-3 py-2.5">
              <p className="truncate text-sm font-medium">{user.fullName}</p>
              <p className="truncate text-xs text-[var(--text-muted)]">{user.email}</p>
              <div className="mt-1.5 flex flex-wrap gap-1">
                {user.roles.map((r) => <Badge key={r} tone="success">{humanise(r)}</Badge>)}
                {user.departmentName && <Badge>{user.departmentName}</Badge>}
              </div>
            </div>
            <button onClick={() => { setOpen(false); navigate('/settings/appearance') }}
              className="flex w-full items-center gap-2 rounded-lg px-3 py-2 text-sm hover:bg-[var(--surface-sunken)]">
              <Palette className="size-4" /> Appearance
            </button>
            <button onClick={() => { setOpen(false); navigate('/profile') }}
              className="flex w-full items-center gap-2 rounded-lg px-3 py-2 text-sm hover:bg-[var(--surface-sunken)]">
              <Settings className="size-4" /> Profile & password
            </button>
            <button onClick={async () => { await signOut(); navigate('/login') }}
              className="flex w-full items-center gap-2 rounded-lg px-3 py-2 text-sm text-red-600 hover:bg-red-50 dark:hover:bg-red-500/10">
              <LogOut className="size-4" /> Sign out
            </button>
          </div>
        </>
      )}
    </div>
  )
}

export default function AppShell() {
  const { user } = useAuth()
  const { resolvedTheme, toggleMode } = usePreferences()
  const [mobileOpen, setMobileOpen] = useState(false)

  const visible = NAV.filter((item) => item.roles.some((r) => user?.roles.includes(r)))

  return (
    <div className="flex h-full">
      {/* Sidebar */}
      <aside
        className={clsx(
          'fixed inset-y-0 left-0 z-40 flex w-64 flex-col bg-[var(--chrome)] transition-transform lg:static lg:translate-x-0',
          mobileOpen ? 'translate-x-0' : '-translate-x-full',
        )}
      >
        <div className="flex h-14 items-center justify-between px-4">
          <Brand />
          <button onClick={() => setMobileOpen(false)} className="text-[var(--chrome-muted)] lg:hidden" aria-label="Close menu">
            <X className="size-5" />
          </button>
        </div>

        <nav className="flex-1 space-y-0.5 overflow-y-auto px-3 py-3">
          {visible.map(({ to, label, icon: Icon }) => (
            <NavLink
              key={to}
              to={to}
              end={to === '/'}
              onClick={() => setMobileOpen(false)}
              className={({ isActive }) =>
                clsx(
                  'flex items-center gap-3 rounded-lg px-3 py-2 text-sm transition-colors',
                  isActive
                    ? 'bg-[var(--accent-solid)] font-medium text-white'
                    : 'text-[var(--chrome-muted)] hover:bg-[var(--chrome-hover)] hover:text-white',
                )
              }
            >
              <Icon className="size-4.5 shrink-0" aria-hidden />
              <span className="truncate">{label}</span>
            </NavLink>
          ))}
        </nav>

        <div className="border-t border-[var(--chrome-border)] px-4 py-3">
          <p className="text-[11px] text-[var(--chrome-muted)]">
            {user?.departmentName ?? 'No department'}
          </p>
        </div>
      </aside>

      {mobileOpen && (
        <div className="fixed inset-0 z-30 bg-black/50 lg:hidden" onClick={() => setMobileOpen(false)} />
      )}

      {/* Main column */}
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-20 flex h-14 items-center justify-between gap-3 border-b bg-[var(--surface-raised)] px-4">
          <button onClick={() => setMobileOpen(true)} className="rounded-lg p-2 hover:bg-[var(--surface-sunken)] lg:hidden"
            aria-label="Open menu">
            <Menu className="size-5" />
          </button>

          <div className="flex-1" />

          <button onClick={toggleMode}
            aria-label={`Switch to ${resolvedTheme === 'dark' ? 'light' : 'dark'} theme`}
            className="rounded-lg p-2 text-[var(--text-muted)] hover:bg-[var(--surface-sunken)] hover:text-[var(--text)]">
            {resolvedTheme === 'dark' ? <Sun className="size-4.5" /> : <Moon className="size-4.5" />}
          </button>
          <NotificationBell />
          <UserMenu />
        </header>

        <main className="flex-1 overflow-y-auto p-4 sm:p-6">
          <div className="mx-auto max-w-7xl">
            <Outlet />
          </div>
        </main>
      </div>
    </div>
  )
}
