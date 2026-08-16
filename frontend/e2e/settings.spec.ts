import { expect, test } from '@playwright/test'
import { ACCOUNTS, PASSWORD, signIn, trackConsoleErrors } from './helpers'

test.describe('Appearance', () => {
  test('changing the scheme and font applies at once and survives a reload', async ({ page }) => {
    await signIn(page, ACCOUNTS.legalHead)
    await page.goto('/settings/appearance')

    await expect(page.getByRole('heading', { name: 'Colour scheme' })).toBeVisible()

    await page.getByRole('button', { name: /Deep Violet/ }).click()
    await expect(page.locator('html')).toHaveAttribute('data-scheme', 'violet')

    await page.getByRole('button', { name: /Monospace/ }).click()
    await expect(page.locator('html')).toHaveAttribute('data-font', 'mono')

    // Give the optimistic save time to reach the server, then prove it was persisted
    // rather than merely cached in this tab.
    await expect(page.getByText('Saving…')).toHaveCount(0)
    await page.evaluate(() => localStorage.removeItem('fpai.preferences'))
    await page.reload()

    await expect(page.locator('html')).toHaveAttribute('data-scheme', 'violet')
    await expect(page.locator('html')).toHaveAttribute('data-font', 'mono')
  })

  test('preferences follow the account, not the browser', async ({ page, context }) => {
    await signIn(page, ACCOUNTS.financeHead)
    await page.goto('/settings/appearance')
    await page.getByRole('button', { name: /Royal Blue/ }).click()
    await expect(page.locator('html')).toHaveAttribute('data-scheme', 'royal')
    await expect(page.getByText('Saving…')).toHaveCount(0)

    // A completely clean browser state, signing in as the same person.
    await context.clearCookies()
    await page.evaluate(() => localStorage.clear())
    await signIn(page, ACCOUNTS.financeHead)

    await expect(page.locator('html')).toHaveAttribute('data-scheme', 'royal')
  })

  test('light and dark can be forced', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin)
    await page.goto('/settings/appearance')

    await page.getByRole('button', { name: /Dark Always dark/ }).click()
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')

    await page.getByRole('button', { name: /Light Always light/ }).click()
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'light')
  })

  test('every scheme renders without a console error', async ({ page }) => {
    const errors = trackConsoleErrors(page)
    await signIn(page, ACCOUNTS.admin)
    await page.goto('/settings/appearance')

    for (const scheme of ['Indian Saffron', 'Royal Blue', 'Deep Violet', 'Slate Monochrome', 'Crimson', 'Pitch Green']) {
      await page.getByRole('button', { name: new RegExp(scheme) }).click()
      await expect(page.locator('html')).not.toHaveAttribute('data-scheme', '')
    }

    expect(errors, `console errors: ${errors.join(' | ')}`).toHaveLength(0)
  })
})

test.describe('Settings access', () => {
  test('a super admin sees every section', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin)
    await page.goto('/settings')

    const nav = page.getByRole('navigation', { name: 'Settings sections' })
    for (const label of ['Appearance', 'Users & roles', 'Access requests', 'Departments', 'Clubs & vendors']) {
      await expect(nav.getByRole('link', { name: new RegExp(label) })).toBeVisible()
    }
  })

  test('staff see only Appearance', async ({ page }) => {
    await signIn(page, ACCOUNTS.welfareStaff)
    await page.goto('/settings')

    const nav = page.getByRole('navigation', { name: 'Settings sections' })
    await expect(nav.getByRole('link', { name: /Appearance/ })).toBeVisible()
    await expect(nav.getByRole('link', { name: /Access requests/ })).toHaveCount(0)
    await expect(nav.getByRole('link', { name: /Departments/ })).toHaveCount(0)
  })

  test('the old /users link still resolves into Settings', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin)
    await page.goto('/users')
    await expect(page).toHaveURL(/\/settings\/users/)
  })

  test('departments can be created and removed', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin)
    await page.goto('/settings/departments')

    const code = `MEDIA${Date.now().toString().slice(-5)}`
    await page.getByRole('button', { name: 'New department' }).click()

    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Code').fill(code)
    await dialog.getByLabel('Name').fill('Media & Communications')
    await dialog.getByRole('button', { name: 'Create department' }).click()

    const row = page.locator('tbody tr', { hasText: code })
    await expect(row).toBeVisible()

    await row.getByRole('button', { name: /Delete/ }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Delete' }).click()
    await expect(page.locator('tbody tr', { hasText: code })).toHaveCount(0)
  })

  test('a built-in department cannot be deleted', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin)
    await page.goto('/settings/departments')

    const row = page.locator('tbody tr', { hasText: 'WELFARE' })
    await expect(row.getByText('Built-in')).toBeVisible()
    // No delete control is offered at all for a built-in department.
    await expect(row.getByRole('button', { name: /Delete/ })).toHaveCount(0)
  })
})

test.describe('Self-registration and approval', () => {
  test('a stranger can request access but gets no session', async ({ page }) => {
    const email = `applicant.${Date.now()}@example.com`

    await page.goto('/register')
    await page.getByLabel('Full name').fill('Ravi Deshmukh')
    await page.getByLabel('Email address').fill(email)
    await page.getByLabel('Job title').fill('Player Agent')
    await page.getByLabel(/^Password/).fill('Str0ng!Passw0rd')
    await page.getByLabel(/^Confirm password/).fill('Str0ng!Passw0rd')
    await page.getByLabel('Why do you need access?').fill('I represent three FPAI members.')
    await page.getByRole('button', { name: 'Request access' }).click()

    await expect(page.getByRole('heading', { name: 'Waiting for approval' })).toBeVisible()

    // Signing in now must show the holding screen, never the dashboard.
    await page.getByRole('button', { name: 'Back to sign in' }).click()
    await page.getByLabel('Email address').fill(email)
    await page.getByLabel('Password').fill('Str0ng!Passw0rd')
    await page.getByRole('button', { name: 'Sign in' }).click()

    await expect(page.getByRole('heading', { name: 'Waiting for approval' })).toBeVisible()

    // And no token was stored, so nothing can be called on their behalf.
    const token = await page.evaluate(() => localStorage.getItem('fpai.access'))
    expect(token).toBeNull()
  })

  test('registration rejects a mismatched confirmation', async ({ page }) => {
    await page.goto('/register')
    await page.getByLabel('Full name').fill('Mismatch')
    await page.getByLabel('Email address').fill(`mismatch.${Date.now()}@example.com`)
    await page.getByLabel(/^Password/).fill('Str0ng!Passw0rd')
    await page.getByLabel(/^Confirm password/).fill('Different!Passw0rd')
    await page.getByRole('button', { name: 'Request access' }).click()

    await expect(page.getByText('The passwords do not match.')).toBeVisible()
  })

  test('an admin approves a request and the applicant can then work', async ({ page }) => {
    const email = `approved.${Date.now()}@example.com`

    // Register through the API to keep the test focused on the approval journey.
    // A relative fetch needs a real origin, so load the app first.
    await page.goto('/login')
    const registered = await page.evaluate(async (address) => {
      const response = await fetch('/api/auth/register', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          fullName: 'Approved Applicant', email: address, password: 'Str0ng!Passw0rd',
        }),
      })
      return response.status
    }, email)
    expect(registered).toBe(202)

    await signIn(page, ACCOUNTS.admin)
    await page.goto('/settings/approvals')

    const request = page.locator('li', { hasText: email })
    await expect(request).toBeVisible()
    await request.getByRole('button', { name: 'Approve' }).click()

    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Role').selectOption('Staff')
    await dialog.getByLabel('Department').selectOption({ index: 1 })
    await dialog.getByRole('button', { name: 'Approve and grant access' }).click()

    await expect(page.locator('li', { hasText: email })).toHaveCount(0)

    // The applicant can now sign in and reach the dashboard.
    await page.getByRole('button', { name: new RegExp(ACCOUNTS.admin.name) }).click()
    await page.getByRole('button', { name: 'Sign out' }).click()

    await page.getByLabel('Email address').fill(email)
    await page.getByLabel('Password').fill('Str0ng!Passw0rd')
    await page.getByRole('button', { name: 'Sign in' }).click()

    await expect(page.getByRole('heading', { level: 1 }))
      .toContainText(/Good (morning|afternoon|evening)/)
  })

  test('approving requires a department for a scoped role', async ({ page }) => {
    const email = `nodept.${Date.now()}@example.com`
    await page.goto('/login')
    await page.evaluate(async (address) => {
      await fetch('/api/auth/register', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          fullName: 'No Department', email: address, password: 'Str0ng!Passw0rd',
        }),
      })
    }, email)

    await signIn(page, ACCOUNTS.admin)
    await page.goto('/settings/approvals')

    const request = page.locator('li', { hasText: email })
    await request.getByRole('button', { name: 'Approve' }).click()

    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Role').selectOption('Staff')
    await dialog.getByRole('button', { name: 'Approve and grant access' }).click()

    await expect(page.getByText(/A department is required/)).toBeVisible()
  })

  test('a declined applicant is told why', async ({ page }) => {
    const email = `declined.${Date.now()}@example.com`
    await page.goto('/login')
    await page.evaluate(async (address) => {
      await fetch('/api/auth/register', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          fullName: 'Declined Applicant', email: address, password: 'Str0ng!Passw0rd',
        }),
      })
    }, email)

    await signIn(page, ACCOUNTS.admin)
    await page.goto('/settings/approvals')

    const request = page.locator('li', { hasText: email })
    await request.getByRole('button', { name: 'Decline' }).click()

    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Reason').fill('This address is not associated with an FPAI member.')
    await dialog.getByRole('button', { name: 'Decline request' }).click()
    await expect(page.locator('li', { hasText: email })).toHaveCount(0)

    await page.getByRole('button', { name: new RegExp(ACCOUNTS.admin.name) }).click()
    await page.getByRole('button', { name: 'Sign out' }).click()

    await page.getByLabel('Email address').fill(email)
    await page.getByLabel('Password').fill('Str0ng!Passw0rd')
    await page.getByRole('button', { name: 'Sign in' }).click()

    await expect(page.getByRole('heading', { name: 'Request declined' })).toBeVisible()
    await expect(page.getByText(/not associated with an FPAI member/)).toBeVisible()
  })

  test('only an administrator sees the approvals queue', async ({ page }) => {
    await signIn(page, ACCOUNTS.welfareHead)
    await page.goto('/settings/approvals')

    await expect(page.getByText('Administrators only')).toBeVisible()

    const status = await page.evaluate(async () => {
      const response = await fetch('/api/users/pending', {
        headers: { Authorization: `Bearer ${localStorage.getItem('fpai.access')}` },
      })
      return response.status
    })
    expect(status).toBe(403)
  })
})

test.describe('Sign-in page', () => {
  test('offers a link to request access', async ({ page }) => {
    await page.goto('/login')
    await expect(page.getByRole('link', { name: 'Request access' })).toBeVisible()
    await page.getByRole('link', { name: 'Request access' }).click()
    await expect(page.getByRole('heading', { name: 'Request an account' })).toBeVisible()
  })

  test('a wrong password is still refused for a real account', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Email address').fill(ACCOUNTS.admin.email)
    await page.getByLabel('Password').fill(`not-${PASSWORD}`)
    await page.getByRole('button', { name: 'Sign in' }).click()

    await expect(page.getByRole('alert')).toContainText(/incorrect/i)
  })
})
