import { expect, test } from '@playwright/test'
import { ACCOUNTS, signIn } from './helpers'

/**
 * The interface hides what a role cannot do, but the server is the authority.
 * These tests check both: the UI affordance, and the API answer when the UI is bypassed.
 */
test.describe('Role-based authorization', () => {
  test('the external accountant cannot reach welfare through the UI or the API', async ({ page }) => {
    await signIn(page, ACCOUNTS.accountant)

    await page.goto('/welfare')
    await expect(page.getByText('You do not have access to this area')).toBeVisible()

    // Bypassing the UI must still fail.
    const status = await page.evaluate(async () => {
      const response = await fetch('/api/welfare/cases', {
        headers: { Authorization: `Bearer ${localStorage.getItem('fpai.access')}` },
      })
      return response.status
    })
    expect(status).toBe(403)
  })

  test('staff cannot reach user management through the UI or the API', async ({ page }) => {
    await signIn(page, ACCOUNTS.welfareStaff)

    // /users redirects into Settings, which refuses the section for a Staff role.
    await page.goto('/users')
    await expect(page).toHaveURL(/\/settings\/users/)
    await expect(page.getByText('You do not have access to this section')).toBeVisible()

    const status = await page.evaluate(async () => {
      const response = await fetch('/api/users', {
        headers: { Authorization: `Bearer ${localStorage.getItem('fpai.access')}` },
      })
      return response.status
    })
    expect(status).toBe(403)
  })

  test('staff are offered no delete action on a welfare case', async ({ page }) => {
    await signIn(page, ACCOUNTS.welfareStaff)
    await page.goto('/welfare')

    await page.locator('tbody tr').first().click()
    await expect(page.getByRole('heading', { level: 1 })).toContainText(/WEL\//)
    // Deleting is head/admin only.
    await expect(page.getByRole('button', { name: 'Delete' })).toHaveCount(0)
  })

  test('a department head cannot write outside their own department', async ({ page }) => {
    await signIn(page, ACCOUNTS.welfareHead)

    // Ask the API directly for a department this user does not belong to.
    const result = await page.evaluate(async () => {
      const token = localStorage.getItem('fpai.access')
      const auth = { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' }

      const departments = await (await fetch('/api/departments', { headers: auth })).json()
      const legal = departments.find((d: { code: string }) => d.code === 'LEGAL')

      const response = await fetch('/api/tasks', {
        method: 'POST',
        headers: auth,
        body: JSON.stringify({ title: 'Cross-department task', departmentId: legal.id }),
      })
      return response.status
    })

    expect(result).toBe(403)
  })

  test('a super admin reaches every module', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin)

    const statuses = await page.evaluate(async () => {
      const token = localStorage.getItem('fpai.access')
      const paths = [
        '/api/welfare/cases', '/api/legal/cases', '/api/finance/vouchers',
        '/api/meetings', '/api/events', '/api/documents', '/api/tasks',
        '/api/approvals', '/api/reports', '/api/users',
      ]
      return Promise.all(
        paths.map(async (path) => {
          const response = await fetch(path, { headers: { Authorization: `Bearer ${token}` } })
          return [path, response.status] as const
        }),
      )
    })

    for (const [path, status] of statuses) {
      expect(status, `${path} should be reachable by a super admin`).toBe(200)
    }
  })

  test('an illegal workflow transition is refused by the server', async ({ page }) => {
    await signIn(page, ACCOUNTS.welfareHead)

    const status = await page.evaluate(async () => {
      const token = localStorage.getItem('fpai.access')
      const auth = { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' }

      const list = await (await fetch('/api/welfare/cases?status=New&pageSize=1', { headers: auth })).json()
      if (!list.items.length) return 'skip'

      // New -> Resolved skips the whole workflow and must be rejected.
      const response = await fetch(`/api/welfare/cases/${list.items[0].id}/status`, {
        method: 'POST',
        headers: auth,
        body: JSON.stringify({ status: 'Resolved' }),
      })
      return response.status
    })

    if (status === 'skip') test.skip(true, 'no New-status case available')
    expect(status).toBe(409)
  })
})
