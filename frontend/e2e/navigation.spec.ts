import { expect, test } from '@playwright/test'
import { ACCOUNTS, signIn, trackConsoleErrors } from './helpers'

const ROUTES = [
  { path: '/', heading: /Good (morning|afternoon|evening)/ },
  { path: '/welfare', heading: 'Player Welfare & Liaison' },
  { path: '/legal', heading: 'Legal Affairs' },
  { path: '/finance', heading: 'Finance & Accounts' },
  { path: '/meetings', heading: 'Meetings & Voting' },
  { path: '/events', heading: 'Events & Operations' },
  { path: '/documents', heading: 'Documents' },
  { path: '/tasks', heading: 'Tasks & Approvals' },
  { path: '/players', heading: 'Member Directory' },
  { path: '/reports', heading: 'Reports' },
  { path: '/settings', heading: 'Settings' },
  { path: '/profile', heading: 'Profile' },
]

test.describe('Navigation', () => {
  test('every module renders without a console error', async ({ page }) => {
    const errors = trackConsoleErrors(page)
    await signIn(page, ACCOUNTS.admin)

    for (const route of ROUTES) {
      await page.goto(route.path)
      await expect(
        page.getByRole('heading', { level: 1 }),
        `heading for ${route.path}`,
      ).toContainText(route.heading)
      // Nothing should have fallen back to the error boundary.
      await expect(page.getByText('Something went wrong')).toHaveCount(0)
    }

    expect(errors, `console errors: ${errors.join(' | ')}`).toHaveLength(0)
  })

  test('an unknown route shows the not-found state', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin)
    await page.goto('/this-route-does-not-exist')
    await expect(page.getByText('Page not found')).toBeVisible()
  })

  test('list filters are reflected in the URL and survive a reload', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin)
    await page.goto('/welfare')

    await page.getByRole('combobox').first().selectOption('Resolved')
    await expect(page).toHaveURL(/status=Resolved/)

    await page.reload()
    await expect(page.getByRole('combobox').first()).toHaveValue('Resolved')
  })

  test('the sidebar hides modules the role cannot reach', async ({ page }) => {
    await signIn(page, ACCOUNTS.accountant)

    const nav = page.getByRole('navigation')
    await expect(nav.getByRole('link', { name: 'Finance' })).toBeVisible()
    // The external accountant has no welfare, legal or governance access.
    await expect(nav.getByRole('link', { name: 'Player Welfare' })).toHaveCount(0)
    await expect(nav.getByRole('link', { name: 'Legal' })).toHaveCount(0)
    await expect(nav.getByRole('link', { name: 'Player Welfare' })).toHaveCount(0)
  })
})
