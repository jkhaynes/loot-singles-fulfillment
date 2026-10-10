import { test, expect } from '@playwright/test'
import type { Page } from '@playwright/test'

// 020-tcgplayer-api-import T040 — quickstart.md §B steps 1–3. The E2E host serves the synthetic
// fixtures through a stub TCGplayer handler (never the live API), listing the eight fixture orders
// that import cleanly. SYN-0006 and SYN-0007 are left out: they are rejected, so they would be
// attempted again on every press and the second press could never read "No new orders".
const syntheticOrders = [1, 2, 3, 4, 5, 8, 9, 10].map(
  (index) => `SYN-${String(index).padStart(4, '0')}-A1`,
)

// The import writes to the one database the whole suite shares, and only once per host: a retry
// would find every order already imported and could not observe the first import again. A retry
// here would only fail confusingly, so this file never retries.
test.describe.configure({ retries: 0 })

async function login(page: Page, username: string) {
  await page.goto('/')
  await page.getByLabel(/username/i).fill(username)
  await page.getByLabel(/pin/i).fill('1234')
  await page.getByRole('button', { name: /log in/i }).click()
  await expect(page.getByRole('heading', { name: /E2E/i })).toBeVisible()
}

test('Get new orders imports every open TCGplayer order once and lists them as Ready', async ({
  page,
}) => {
  test.setTimeout(60_000)
  await login(page, 'e2epicker')
  await page.getByRole('link', { name: /import orders/i }).click()
  await expect(page).toHaveURL(/\/import$/)

  // Step 2: progress shows mid-import, then every synthetic order is reported imported.
  await page.getByRole('button', { name: 'Get new orders' }).click()
  await expect(page.getByText(/^[1-7] of 8 orders processed$/)).toBeVisible()
  await expect(page.getByText('8 of 8 orders processed')).toBeVisible({ timeout: 20_000 })
  for (const order of syntheticOrders) {
    await expect(
      page.locator('[data-outcome="succeeded"]').filter({ hasText: order }),
    ).toContainText('Imported successfully')
  }
  await expect(page.locator('[data-outcome="rejected"]')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Get new orders' })).toBeEnabled()

  // Step 3: nothing new the second time.
  await page.getByRole('button', { name: 'Get new orders' }).click()
  await expect(page.getByText('No new orders.')).toBeVisible({ timeout: 20_000 })
  await expect(page.locator('[data-outcome="succeeded"]')).toHaveCount(0)

  // The dashboard lists the new orders as ready to pick.
  await page.getByRole('link', { name: /back to dashboard/i }).click()
  await expect(page).toHaveURL(/\/$/)
  const available = page.locator('section').filter({
    has: page.getByRole('heading', { name: 'Available Orders' }),
  })
  for (const order of syntheticOrders) {
    await expect(available.getByRole('link', { name: order, exact: true })).toBeVisible()
  }
})
