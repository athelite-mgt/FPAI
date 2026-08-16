import { expect, test } from '@playwright/test'
import { ACCOUNTS, PASSWORD, signIn, trackConsoleErrors } from './helpers'

test.describe('Authentication', () => {
  test('rejects a wrong password without revealing whether the account exists', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Email address').fill(ACCOUNTS.admin.email)
    await page.getByLabel('Password').fill('definitely-not-the-password')
    await page.getByRole('button', { name: 'Sign in' }).click()

    const alert = page.getByRole('alert')
    await expect(alert).toBeVisible()
    await expect(alert).toContainText(/incorrect/i)
    // Still on the sign-in page.
    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible()
  })

  test('rejects an unknown account with the same message', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Email address').fill('nobody@example.com')
    await page.getByLabel('Password').fill(PASSWORD)
    await page.getByRole('button', { name: 'Sign in' }).click()

    await expect(page.getByRole('alert')).toContainText(/incorrect/i)
  })

  test('signs in and lands on the dashboard', async ({ page }) => {
    const errors = trackConsoleErrors(page)
    await signIn(page, ACCOUNTS.admin)

    await expect(page.getByText('Active welfare')).toBeVisible()
    await expect(page.getByText('Pending approvals')).toBeVisible()
    expect(errors, `console errors: ${errors.join(' | ')}`).toHaveLength(0)
  })

  test('an unauthenticated visitor is redirected to sign-in', async ({ page }) => {
    await page.goto('/welfare')
    await expect(page).toHaveURL(/\/login/)
  })

  test('the session survives a page reload', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin)
    await page.reload()
    await expect(page.getByRole('heading', { level: 1 }))
      .toContainText(/Good (morning|afternoon|evening)/)
  })

  test('signing out clears the session', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin)

    await page.getByRole('button', { name: new RegExp(ACCOUNTS.admin.name) }).click()
    await page.getByRole('button', { name: 'Sign out' }).click()
    await expect(page).toHaveURL(/\/login/)

    // The protected route must not be reachable again.
    await page.goto('/welfare')
    await expect(page).toHaveURL(/\/login/)
  })
})
