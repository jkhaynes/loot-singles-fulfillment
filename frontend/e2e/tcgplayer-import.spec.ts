import { test, expect } from '@playwright/test'
import type { Page } from '@playwright/test'
import path from 'node:path'

// 020-tcgplayer-api-import T040 — quickstart.md §B steps 1–3. The E2E host serves the synthetic
// fixtures through a stub TCGplayer handler (never the live API), listing the eight fixture orders
// that import cleanly. SYN-0006 and SYN-0007 are left out: they are rejected, so they would be
// attempted again on every press and the second press could never read "No new orders".
const syntheticOrders = [1, 2, 3, 4, 5, 8, 9, 10].map(
  (index) => `SYN-${String(index).padStart(4, '0')}-A1`,
)

// A single-order packing slip no other spec imports, so it can never collide as a duplicate.
const fallbackSlip = path.resolve(
  '../backend/tests/LootSingles.Fixtures/PackingSlips/multi-page-order-no-total-on-continuation-pages.pdf',
)
const fallbackSlipOrder = 'F8433182-69FC9F-7D725'

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

  // quickstart.md §B step 4: imported orders show TCGplayer's image, "No number" and the language.
  // The fixture orders: SYN-0009 is a sealed box (no collector number), SYN-0004 a Japanese line,
  // SYN-0002 has a line of two copies (the fixtures hold no line of three).
  async function openOrder(order: string) {
    await page.goto('/')
    await available.getByRole('link', { name: order, exact: true }).click()
    await expect(page.getByRole('heading', { name: new RegExp(order) })).toBeVisible()
  }

  await openOrder('SYN-0009-A1')
  const sealed = page.getByRole('article', { name: /Synthetic Booster Box/ })
  await expect(sealed.locator('img')).toHaveAttribute('src', /^https:\/\/img\.example\.test\//)
  await expect(sealed.getByText('No number')).toBeVisible()

  await openOrder('SYN-0004-A1')
  await expect(page.getByRole('article').first()).toContainText('Japanese')

  await openOrder('SYN-0002-A1')
  // Scoped to a line's Quantity cell: the set heading's "3 cards" count carries the marker too.
  await expect(
    page.getByRole('article').locator('dd > strong[data-emphasis="high"]').first(),
  ).toHaveText('2')

  // quickstart.md section B step 5 (T049): an API order has no stored packing slip, so the packing desk
  // says to print it from TCGplayer. SYN-0009 is the sealed box: one line of one, so one click
  // picks the whole order. Opened by number rather than Pick Next, which would take the oldest
  // Ready order, a seeded one other specs expect to find. Packed in this same test so the order
  // never lingers in the packing queue other specs count.
  await page.goto('/orders')
  await page
    .getByRole('article', { name: /SYN-0009-A1/i })
    .getByRole('button', { name: /claim/i })
    .click()
  await expect(page).toHaveURL(/\/orders\/\d+$/)
  const orderId = page.url().split('/').pop()!
  const boxCard = page.getByRole('article', { name: /Synthetic Booster Box/ })
  await expect(boxCard).toBeVisible()
  await boxCard.getByRole('button', { name: 'Picked' }).click()
  await expect(page.getByLabel(/Order status: Picked/)).toBeVisible()

  await page.goto('/packing')
  const scanBox = page.getByLabel(/scan a label/i)
  await scanBox.fill(orderId)
  await scanBox.press('Enter')
  const desk = page.getByRole('region', { name: new RegExp(`Order ${orderId}`) })
  await expect(desk).toContainText('SYN-0009-A1')
  await expect(desk).toContainText(
    'No packing slip is stored for this order. Print it from TCGplayer using the order number above.',
  )
  await expect(desk.getByRole('link', { name: /print packing slip/i })).toHaveCount(0)

  await desk.getByRole('button', { name: /mark packed/i }).click()
  await expect(desk).toContainText(/already been packed/i)
})

// T047 (US3, FR-016): the E2E host's stub TCGplayer answers 503 to any request carrying this
// header, and only to that request, so the outage stays on this page and never reaches the import
// above when the two run at the same time.
test('When TCGplayer is down, Get new orders says so and a packing slip uploads on the same screen', async ({
  page,
}) => {
  test.setTimeout(60_000)
  await page.route('**/api/imports/tcgplayer', (route) =>
    route.continue({
      headers: { ...route.request().headers(), 'x-e2e-tcgplayer-outage': 'unavailable' },
    }),
  )
  await login(page, 'e2epicker')
  await page.getByRole('link', { name: /import orders/i }).click()
  await expect(page).toHaveURL(/\/import$/)

  await page.getByRole('button', { name: 'Get new orders' }).click()
  const outage = page.getByRole('alert').filter({ hasText: "Couldn't reach TCGplayer" })
  await expect(outage).toContainText(
    "Couldn't reach TCGplayer. Orders already imported are kept. Try again in a few minutes, or upload a packing slip.",
    { timeout: 20_000 },
  )
  await expect(outage).toContainText('Packing-slip PDF import below still works.')

  await page.getByLabel(/packing slip/i).setInputFiles(fallbackSlip)
  await page.getByRole('button', { name: 'Import orders', exact: true }).click()
  await expect(
    page.locator('[data-outcome="succeeded"]').filter({ hasText: fallbackSlipOrder }),
  ).toContainText('Imported successfully', { timeout: 20_000 })
  await expect(page.getByText("Couldn't reach TCGplayer")).toHaveCount(0)

  // The uploaded order is on the dashboard, ready to pick.
  await page.getByRole('link', { name: /back to dashboard/i }).click()
  const available = page.locator('section').filter({
    has: page.getByRole('heading', { name: 'Available Orders' }),
  })
  await expect(available.getByRole('link', { name: fallbackSlipOrder, exact: true })).toBeVisible()
})
