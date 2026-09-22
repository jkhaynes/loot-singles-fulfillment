import { test, expect } from '@playwright/test'
import type { Browser, Page } from '@playwright/test'

// 018-phone-issue-card — quickstart.md scenarios 1–5, on a phone.
//
// The defect this feature exists for: a product reported as 3 of 4 kept a dock button reading
// "Pulled all 4", and one tap on it replaced the report. E2E-ORDER-00016 and e2epickerfourteen
// are seeded for this file alone, so parallel specs never contend for the claim.
test.use({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true })

async function login(page: Page, username: string) {
  await page.goto('/')
  await page.getByLabel(/username/i).fill(username)
  await page.getByLabel(/pin/i).fill('1234')
  await page.getByRole('button', { name: /log in/i }).click()
  await expect(page.getByRole('heading', { name: /E2E/i })).toBeVisible()
}

const chip = (page: Page, issue: string) => page.getByRole('button', { name: issue, exact: true })
const sheet = (page: Page) => page.getByRole('dialog', { name: 'Reported issue' })

async function report(
  page: Page,
  required: string,
  found: string,
  note: string,
  issueType = 'cardNotFound',
) {
  await page.getByRole('button', { name: /report an issue/i }).click()
  await page.getByLabel('Issue type').selectOption(issueType)
  if (required) await page.getByLabel('Quantity required').fill(required)
  if (found) await page.getByLabel('Quantity found').fill(found)
  if (note) await page.getByLabel('Note (optional)').fill(note)
  await page.getByRole('button', { name: 'Submit Issue' }).click()
}

async function asOnlooker(browser: Browser, url: string) {
  const context = await browser.newContext({
    viewport: { width: 390, height: 844 },
    isMobile: true,
    hasTouch: true,
  })
  const page = await context.newPage()
  await login(page, 'e2emanager')
  await page.goto(url)
  return page
}

test('a reported product shows its issue, and is corrected from the sheet', async ({
  page,
  browser,
}) => {
  await login(page, 'e2epickerfourteen')
  await page.goto('/orders')
  await page
    .getByRole('article', { name: /E2E-ORDER-00016/i })
    .getByRole('button', { name: /claim/i })
    .click()
  await expect(page).toHaveURL(/\/orders\/\d+$/)
  await expect(page.getByRole('heading', { name: 'Issue Card Four', level: 2 })).toBeVisible()

  // ---- Scenario 1: reporting 3 of 4 takes the pick action away ----
  await report(page, '4', '3', 'Only 3 in the binder slot')
  await expect(chip(page, 'Card Not Found')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Next card ›' })).toBeVisible()
  // The defect, asserted directly: nothing on the screen offers to pull all 4.
  await expect(page.getByRole('button', { name: /pulled all/i })).toHaveCount(0)
  await expect(page.getByRole('button', { name: /^picked/i })).toHaveCount(0)
  await expect(page.getByRole('button', { name: /report an issue/i })).toHaveCount(0)

  // ---- Scenario 2: the sheet shows what was reported ----
  await chip(page, 'Card Not Found').click()
  await expect(sheet(page)).toContainText('Required 4 · Found 3')
  // Amended 2026-09-22: nothing assumes the issue was a shortage.
  await expect(sheet(page)).not.toContainText(/pulled|short/i)
  await expect(sheet(page)).toContainText('Only 3 in the binder slot')
  await expect(sheet(page)).toContainText('E2E Picker Fourteen')

  // ---- Scenario 5: someone without the claim can read it, and do nothing else ----
  const onlooker = await asOnlooker(browser, page.url())
  await chip(onlooker, 'Card Not Found').click()
  await expect(sheet(onlooker).getByRole('button')).toHaveText(['Close'])
  await expect(onlooker.getByRole('button', { name: 'Next card ›' })).toHaveCount(0)
  await onlooker.context().close()

  // ---- Scenario 4: change the report ----
  await sheet(page).getByRole('button', { name: 'Edit report' }).click()
  await expect(page.getByLabel('Quantity found')).toHaveValue('3')
  await page.getByLabel('Issue type').selectOption('wrongVariant')
  await page.getByRole('button', { name: 'Submit Issue' }).click()
  await expect(chip(page, 'Wrong Variant')).toBeVisible()

  // ---- Scenario 3: found them all after all ----
  await chip(page, 'Wrong Variant').click()
  await sheet(page).getByRole('button', { name: 'Resolved' }).click()
  await expect(page.getByRole('button', { name: /picked ✓/i })).toBeVisible()
  await expect(sheet(page)).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Wrong Variant' })).toHaveCount(0)

  // ---- Any issue type: a damaged card, reported with a note and no counts ----
  await page.getByRole('button', { name: 'Next card', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Issue Card One', level: 2 })).toBeVisible()
  await report(page, '', '', 'Corner crease', 'damaged')
  await chip(page, 'Damaged').click()
  await expect(sheet(page)).toContainText('Corner crease')
  await expect(sheet(page)).not.toContainText(/Required|pulled|short/i)
  await expect(sheet(page).getByRole('button', { name: 'Resolved' })).toBeVisible()
})
