import { expect, test } from '@playwright/test'
import { ACCOUNTS, signIn } from './helpers'

/*
 * Form fields are always queried inside `getByRole('dialog')`. The list toolbars carry
 * aria-labels such as "Search voucher or vendor…" which would otherwise match a bare
 * getByLabel('Vendor') and make these tests ambiguous.
 */

test.describe('Welfare casework', () => {
  test('opens a case, advances it and records a note', async ({ page }) => {
    await signIn(page, ACCOUNTS.welfareHead)
    await page.goto('/welfare')

    await page.getByRole('button', { name: 'New welfare case' }).first().click()
    const dialog = page.getByRole('dialog')

    const title = `E2E salary dispute ${Date.now()}`
    await dialog.getByLabel('Title').fill(title)
    await dialog.getByLabel('Member').selectOption({ index: 1 })
    await dialog.getByLabel('Category').selectOption('Salary')
    await dialog.getByLabel('Priority').selectOption('High')
    await dialog.getByRole('button', { name: 'Open case' }).click()

    await expect(page.getByRole('heading', { level: 1 })).toContainText(/WEL\/\d{4}\/\d+/)
    await expect(page.getByText(title)).toBeVisible()

    // Advance through a legal transition.
    await page.getByRole('button', { name: 'Move to Under Review' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Confirm' }).click()
    await expect(page.getByText(/Status changed from New to UnderReview/)).toBeVisible()

    // Add a note.
    await page.getByRole('button', { name: 'Add note' }).click()
    const noteDialog = page.getByRole('dialog')
    await noteDialog.getByLabel('Note').fill('Club contacted; awaiting payroll confirmation.')
    await noteDialog.getByRole('button', { name: 'Add note' }).click()
    await expect(page.getByText('Club contacted; awaiting payroll confirmation.')).toBeVisible()
  })

  test('a required field blocks submission', async ({ page }) => {
    await signIn(page, ACCOUNTS.welfareHead)
    await page.goto('/welfare')

    await page.getByRole('button', { name: 'New welfare case' }).first().click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('button', { name: 'Open case' }).click()

    await expect(page.getByText('A short title is required.')).toBeVisible()
    await expect(page.getByText('Select the member this case concerns.')).toBeVisible()
    // The dialog stays open so the user can correct the form.
    await expect(dialog).toBeVisible()
  })
})

test.describe('Legal matters', () => {
  test('registers a matter and records an event', async ({ page }) => {
    await signIn(page, ACCOUNTS.legalHead)
    await page.goto('/legal')

    await page.getByRole('button', { name: 'New legal matter' }).first().click()
    const dialog = page.getByRole('dialog')

    const title = `E2E unpaid wages ${Date.now()}`
    await dialog.getByLabel('Title').fill(title)
    await dialog.getByLabel('Member').selectOption({ index: 1 })
    await dialog.getByLabel('Forum').selectOption('FifaDrc')
    await dialog.getByLabel('Claim amount (INR)').fill('450000')
    await dialog.getByRole('button', { name: 'Register matter' }).click()

    await expect(page.getByRole('heading', { level: 1 })).toContainText(/FIFA\/DRC\/\d{4}\/\d+/)
    await expect(page.getByText('Case Registered')).toBeVisible()

    await page.getByRole('button', { name: 'Record event' }).click()
    const eventDialog = page.getByRole('dialog')
    await eventDialog.getByLabel('Title').fill('Statement of claim served')
    await eventDialog.getByRole('button', { name: 'Record', exact: true }).click()
    await expect(page.getByText('Statement of claim served')).toBeVisible()
  })
})

test.describe('Finance', () => {
  test('raises a voucher and submits it for approval', async ({ page }) => {
    await signIn(page, ACCOUNTS.financeHead)
    await page.goto('/finance')

    await page.getByRole('button', { name: 'New voucher' }).click()
    const dialog = page.getByRole('dialog')

    await dialog.getByLabel('Vendor').selectOption({ index: 1 })
    await dialog.getByLabel('Department').selectOption({ index: 1 })
    await dialog.getByLabel('Amount (INR)').fill('50000')
    await dialog.getByLabel('Tax (INR)').fill('9000')
    // Amount + tax is totalled for the user before they commit.
    await expect(dialog.getByText(/Total payable/)).toContainText('59,000')
    await dialog.getByRole('button', { name: 'Create draft' }).click()

    await expect(page.getByRole('heading', { level: 1 })).toContainText(/V-\d+/)

    await page.getByRole('button', { name: 'Submit for approval' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Confirm' }).click()
    await expect(page.getByRole('button', { name: 'Reject' })).toBeVisible()
  })

  test('an expense claim validates its amount', async ({ page }) => {
    await signIn(page, ACCOUNTS.financeHead)
    await page.goto('/finance')

    await page.getByRole('button', { name: 'New expense' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('button', { name: 'Create claim' }).click()

    await expect(page.getByText('A title is required.')).toBeVisible()
    await expect(page.getByText('Enter an amount greater than zero.')).toBeVisible()
  })

  test('rejecting a voucher requires a reason', async ({ page }) => {
    // Signed in as the super admin, who may approve or reject in any department.
    await signIn(page, ACCOUNTS.admin)
    await page.goto('/finance?status=Pending')

    const firstRow = page.locator('tbody tr').first()
    await expect(firstRow).toBeVisible()
    await firstRow.click()

    const reject = page.getByRole('button', { name: 'Reject' })
    await expect(reject).toBeVisible()
    await reject.click()
    const dialog = page.getByRole('dialog')
    const confirm = dialog.getByRole('button', { name: 'Confirm' })
    await expect(confirm).toBeDisabled()
    await dialog.getByLabel('Reason for rejection').fill('Supporting invoice missing.')
    await expect(confirm).toBeEnabled()
  })
})

test.describe('Tasks', () => {
  test('creates a task and moves it along', async ({ page }) => {
    await signIn(page, ACCOUNTS.welfareHead)
    await page.goto('/tasks')

    await page.getByRole('button', { name: 'New task' }).click()
    const dialog = page.getByRole('dialog')

    const title = `E2E collect medical reports ${Date.now()}`
    await dialog.getByLabel('Title').fill(title)
    await dialog.getByLabel('Department').selectOption({ index: 1 })
    await dialog.getByRole('button', { name: 'Create task' }).click()

    await page.getByPlaceholder('Search tasks…').fill(title)
    const row = page.locator('tbody tr', { hasText: title })
    await expect(row).toBeVisible()
    await expect(row.getByText('Todo')).toBeVisible()

    await row.getByRole('button', { name: 'In Progress' }).click()
    await expect(row.getByText('In Progress').first()).toBeVisible()
  })
})

test.describe('Meetings and voting', () => {
  test('shows a motion tally and the quorum position', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin)
    await page.goto('/meetings')

    await page.locator('tbody tr').first().click()
    await expect(page.getByRole('heading', { name: 'Meeting details' })).toBeVisible()
    await expect(page.getByText(/required ·/)).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Motions' })).toBeVisible()
  })
})

test.describe('Reports', () => {
  test('runs a report and shows a breakdown', async ({ page }) => {
    await signIn(page, ACCOUNTS.admin)
    await page.goto('/reports')

    await expect(page.getByText('Welfare Casework Summary').first()).toBeVisible()
    await page.getByRole('button', { name: 'Legal Matters Summary' }).click()
    await expect(page.getByRole('heading', { name: 'Legal Matters Summary' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Breakdown' })).toBeVisible()
  })
})
