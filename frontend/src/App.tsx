import { lazy, Suspense } from 'react'
import { BrowserRouter, Navigate, Route, Routes, useLocation } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider, useAuth } from './lib/auth'
import { PreferencesProvider } from './lib/preferences'
import { ToastProvider, Spinner, EmptyState, Button } from './components/ui'
import AppShell from './components/AppShell'
import Login from './pages/Login'
import Register from './pages/Register'
import type { Role } from './lib/types'

const Dashboard = lazy(() => import('./pages/Dashboard'))
const Welfare = lazy(() => import('./pages/Welfare'))
const WelfareDetail = lazy(() => import('./pages/WelfareDetail'))
const Legal = lazy(() => import('./pages/Legal'))
const LegalDetail = lazy(() => import('./pages/LegalDetail'))
const Finance = lazy(() => import('./pages/Finance'))
const VoucherDetail = lazy(() => import('./pages/VoucherDetail'))
const ExpenseDetail = lazy(() => import('./pages/ExpenseDetail'))
const Meetings = lazy(() => import('./pages/Meetings'))
const MeetingDetail = lazy(() => import('./pages/MeetingDetail'))
const Events = lazy(() => import('./pages/Events'))
const EventDetail = lazy(() => import('./pages/EventDetail'))
const Documents = lazy(() => import('./pages/Documents'))
const Tasks = lazy(() => import('./pages/Tasks'))
const Players = lazy(() => import('./pages/Players'))
const Reports = lazy(() => import('./pages/Reports'))
const Profile = lazy(() => import('./pages/Profile'))
const Settings = lazy(() => import('./pages/Settings'))

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: (failureCount, error) => {
        // Never retry an authorization failure; it will never succeed.
        const status = (error as { response?: { status?: number } })?.response?.status
        if (status === 401 || status === 403 || status === 404) return false
        return failureCount < 2
      },
      refetchOnWindowFocus: false,
    },
  },
})

function Protected({ roles, children }: { roles?: Role[]; children: React.ReactNode }) {
  const { user, loading } = useAuth()
  const location = useLocation()

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center">
        <Spinner label="Restoring your session" />
      </div>
    )
  }
  if (!user) return <Navigate to="/login" state={{ from: location.pathname }} replace />

  if (roles && !roles.some((r) => user.roles.includes(r))) {
    return (
      <EmptyState
        title="You do not have access to this area"
        description="Your role does not permit this module. If you believe this is wrong, contact your administrator."
        action={<Button onClick={() => window.history.back()}>Go back</Button>}
      />
    )
  }
  return <>{children}</>
}

const NO_ACCOUNTANT: Role[] = ['SuperAdmin', 'DepartmentHead', 'Staff']

function Router() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route path="/register" element={<Register />} />

      <Route
        element={
          <Protected>
            <AppShell />
          </Protected>
        }
      >
        <Route index element={<Dashboard />} />

        <Route path="welfare" element={<Protected roles={NO_ACCOUNTANT}><Welfare /></Protected>} />
        <Route path="welfare/:id" element={<Protected roles={NO_ACCOUNTANT}><WelfareDetail /></Protected>} />

        <Route path="legal" element={<Protected roles={NO_ACCOUNTANT}><Legal /></Protected>} />
        <Route path="legal/:id" element={<Protected roles={NO_ACCOUNTANT}><LegalDetail /></Protected>} />

        <Route path="finance" element={<Finance />} />
        <Route path="finance/vouchers/:id" element={<VoucherDetail />} />
        <Route path="finance/expenses/:id" element={<ExpenseDetail />} />

        <Route path="meetings" element={<Protected roles={NO_ACCOUNTANT}><Meetings /></Protected>} />
        <Route path="meetings/:id" element={<Protected roles={NO_ACCOUNTANT}><MeetingDetail /></Protected>} />

        <Route path="events" element={<Protected roles={NO_ACCOUNTANT}><Events /></Protected>} />
        <Route path="events/:id" element={<Protected roles={NO_ACCOUNTANT}><EventDetail /></Protected>} />

        <Route path="documents" element={<Documents />} />
        <Route path="tasks" element={<Tasks />} />
        <Route path="players" element={<Protected roles={NO_ACCOUNTANT}><Players /></Protected>} />

        <Route
          path="reports"
          element={
            <Protected roles={['SuperAdmin', 'DepartmentHead', 'ExternalAccountant']}>
              <Reports />
            </Protected>
          }
        />
        {/* User management moved into Settings; the old link still resolves. */}
        <Route path="users" element={<Navigate to="/settings/users" replace />} />
        <Route path="settings/*" element={<Settings />} />
        <Route path="profile" element={<Profile />} />

        <Route
          path="*"
          element={
            <EmptyState
              title="Page not found"
              description="The page you were looking for does not exist."
              action={<Button onClick={() => window.history.back()}>Go back</Button>}
            />
          }
        />
      </Route>
    </Routes>
  )
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <ToastProvider>
        <BrowserRouter>
          <AuthProvider>
            <PreferencesProvider>
              <Suspense fallback={<Spinner />}>
                <Router />
              </Suspense>
            </PreferencesProvider>
          </AuthProvider>
        </BrowserRouter>
      </ToastProvider>
    </QueryClientProvider>
  )
}
