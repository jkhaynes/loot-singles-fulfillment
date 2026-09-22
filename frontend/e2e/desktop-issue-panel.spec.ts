import { test, expect } from '@playwright/test'
import type { Browser, Locator, Page } from '@playwright/test'

// 018-phone-issue-card US4 — quickstart.md scenarios 6–8, on the desktop list.
//
// A reported row kept Picked, which replaced the report with no hint that it would, and Report
// Issue opened a blank form, so changing a report meant entering it all again. E2E-ORDER-00017 and
// e2epickerfifteen are seeded for this file alone, so parallel specs never contend for the claim.
test.use({ viewport: { width: 1280, height: 900 } })

async function login(page: Page, username: string) {
  await page.goto('/')
  await page.getByLabel(/username/i).fill(username)
  await page.getByLabel(/pin/i).fill('1234')
  await page.getByRole('button', { name: /log in/i }).click()
  await expect(page.getByRole('heading', { name: /E2E/i })).toBeVisible()
}

const row = (page: Page, product: string) =>
  page.getByRole('article', { name: `Product ${product}` })

async function report(
  target: Locator,
  issueType: string,
  required: string,
  found: string,
  note: string,
) {
  await target.getByRole('button', { name: 'Report Issue' }).click()
  await target.getByLabel('Issue type').selectOption(issueType)
  if (required) await target.getByLabel('Quantity required').fill(required)
  if (found) await target.getByLabel('Quantity found').fill(found)
  await target.getByLabel('Note (optional)').fill(note)
  await target.getByRole('button', { name: 'Submit Issue' }).click()
}

async function asOnlooker(browser: Browser, url: string) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 } })
  const page = await context.newPage()
  await login(page, 'e2emanager')
  await page.goto(url)
  return page
}

test('a reported row shows its issue, and is edited and resolved in place', async ({
  page,
  browser,
}) => {
  await login(page, 'e2epickerfifteen')
  await page.goto('/orders')
  await page
    .getByRole('article', { name: /E2E-ORDER-00017/i })
    .getByRole('button', { name: /claim/i })
    .click()
  await expect(page).toHaveURL(/\/orders\/\d+$/)

  const four = row(page, 'Panel Card Four')
  const damaged = row(page, 'Panel Card Damaged')
  const clean = row(page, 'Panel Card Clean')

  // ---- Scenario 6: the panel, in place of Picked and Report Issue ----
  await report(four, 'cardNotFound', '4', '3', 'Only 3 in the binder slot')
  const panel = four.getByRole('status')
  await expect(panel).toContainText('Card Not Found')
  await expect(panel).toContainText('Required 4 · Found 3')
  await expect(panel).toContainText('Only 3 in the binder slot')
  await expect(panel).toContainText('E2E Picker Fifteen')
  await expect(panel).not.toContainText(/pulled|short/i)
  await expect(four.getByRole('button', { name: 'Resolved' })).toBeVisible()
  await expect(four.getByRole('button', { name: 'Edit report' })).toBeVisible()
  await expect(four.getByRole('button', { name: 'Picked' })).toHaveCount(0)
  await expect(four.getByRole('button', { name: 'Report Issue' })).toHaveCount(0)

  // Any issue type: a damaged card with a note and no counts.
  await report(damaged, 'damaged', '', '', 'Corner crease')
  await expect(damaged.getByRole('status')).toContainText('Damaged')
  await expect(damaged.getByRole('status')).toContainText('Corner crease')
  await expect(damaged.getByRole('status')).not.toContainText(/Required/)

  // ---- Scenario 8: someone without the claim sees the report and cannot change it ----
  const onlooker = await asOnlooker(browser, page.url())
  const theirs = row(onlooker, 'Panel Card Damaged')
  await expect(theirs.getByRole('status')).toContainText('Damaged')
  await expect(theirs.getByRole('button', { name: 'Resolved' })).toHaveCount(0)
  await expect(theirs.getByRole('button', { name: 'Edit report' })).toHaveCount(0)
  await onlooker.context().close()

  // ---- Scenario 7: Edit report starts from what was reported ----
  await four.getByRole('button', { name: 'Edit report' }).click()
  await expect(four.getByLabel('Quantity required')).toHaveValue('4')
  await expect(four.getByLabel('Quantity found')).toHaveValue('3')
  await expect(four.getByLabel('Note (optional)')).toHaveValue('Only 3 in the binder slot')
  await four.getByLabel('Note (optional)').fill('Found one more in the back')
  await four.getByRole('button', { name: 'Submit Issue' }).click()
  await expect(panel).toContainText('Found one more in the back')

  // ---- Resolved ----
  await four.getByRole('button', { name: 'Resolved' }).click()
  await expect(four.getByRole('button', { name: 'Picked' })).toHaveAttribute('aria-pressed', 'true')
  await expect(four.getByRole('status')).toHaveCount(0)

  // A row nobody reported keeps its ordinary actions throughout (FR-027).
  await expect(clean.getByRole('button', { name: 'Picked' })).toBeVisible()
  await expect(clean.getByRole('button', { name: 'Report Issue' })).toBeVisible()
  await expect(clean.getByRole('status')).toHaveCount(0)
})
