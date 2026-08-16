import { expect, type Page } from '@playwright/test'

export const ACCOUNTS = {
  admin: { email: 'admin@fpai.in', name: 'Arjun Nair' },
  welfareHead: { email: 'welfare.head@fpai.in', name: 'Priya Menon' },
  legalHead: { email: 'legal.head@fpai.in', name: 'Vikram Shetty' },
  financeHead: { email: 'finance.head@fpai.in', name: 'Ananya Bose' },
  welfareStaff: { email: 'welfare.staff@fpai.in', name: 'Sameer Khan' },
  accountant: { email: 'accountant@external-ca.in', name: 'Meera Krishnan' },
} as const

export const PASSWORD = process.env.E2E_PASSWORD ?? 'Fpai@Connect2025!'

export async function signIn(page: Page, account: { email: string }) {
  await page.goto('/login')
  await page.getByLabel('Email address').fill(account.email)
  await page.getByLabel('Password').fill(PASSWORD)
  await page.getByRole('button', { name: 'Sign in' }).click()
  // The dashboard greeting is the first thing rendered after a successful sign-in.
  await expect(page.getByRole('heading', { level: 1 })).toContainText(/Good (morning|afternoon|evening)/)
}

export async function signOut(page: Page) {
  await page.getByRole('button', { name: /Arjun|Priya|Vikram|Ananya|Sameer|Meera/ }).first().click()
  await page.getByRole('button', { name: 'Sign out' }).click()
  await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible()
}

/** Fails the test if the browser console logged an error during the run. */
export function trackConsoleErrors(page: Page): string[] {
  const errors: string[] = []
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(message.text())
  })
  page.on('pageerror', (error) => errors.push(error.message))
  return errors
}
