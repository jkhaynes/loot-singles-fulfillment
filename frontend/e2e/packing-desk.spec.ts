import { test, expect } from '@playwright/test'
import type { Page } from '@playwright/test'

// 017-pick-completion-handoff T060 — quickstart.md scenarios 3, 4 and 5.
// A desktop surface: packing is a seated job at a bench.
test.use({ viewport: { width: 1280, height: 900 } })

async function login(page: Page, username: string) {
  await page.goto('/')
  await page.getByLabel(/username/i).fill(username)
  await page.getByLabel(/pin/i).fill('1234')
  await page.getByRole('button', { name: /log in/i }).click()
  await expect(page.getByRole('heading', { name: /E2E/i })).toBeVisible()
}

/** Claims an order, picks every card, and finishes — leaving a sleeve awaiting packing. */
async function pickWholeOrder(page: Page, order: string, productName: string) {
  await page.goto('/orders')
  await page
    .getByRole('article', { name: new RegExp(order, 'i') })
    .getByRole('button', { name: /claim/i })
    .click()
  await expect(page).toHaveURL(/\/orders\/\d+$/)
  const orderUrl = page.url()

  // Wait for the list to render before touching it: collecting handles first would find
  // nothing and click nothing, leaving the order In Progress and the failure looking like a
  // status bug rather than a race.
  const card = page.getByRole('article', { name: new RegExp(productName, 'i') })
  await expect(card).toBeVisible()
  await card.getByRole('button', { name: 'Picked' }).click()
  await expect(page.getByLabel(/Order status: Picked/)).toBeVisible()

  return orderUrl.split('/').pop()!
}

const scanBox = (page: Page) => page.getByLabel(/scan a label/i)

test('scans a picked order at the desk, packs it, and refuses a second attempt', async ({
  page,
}) => {
  await login(page, 'e2epickerten')
  const orderId = await pickWholeOrder(page, 'E2E-ORDER-00011', 'Desk Packable')

  await page.goto('/packing')
  await expect(page.getByRole('heading', { name: 'Packing', exact: true })).toBeVisible()

  // ---- Scenario 3: resolve, see the counts and the picker ----
  await scanBox(page).fill(orderId)
  await scanBox(page).press('Enter')

  const order = page.getByRole('region', { name: new RegExp(`Order ${orderId}`) })
  await expect(order).toBeVisible()
  await expect(order).toContainText('E2E-ORDER-00011')
  await expect(order).toContainText('E2E Picker Ten')

  // ---- Scenario 5: an order imported before this feature has no stored slip ----
  // These seeds are created directly rather than imported, so none of them has one. The desk has
  // to stay usable anyway (FR-022) — that is the state every pre-existing order is in.
  await expect(order).toContainText(/no packing slip/i)

  // ---- Mark packed, and it leaves the queue ----
  await order.getByRole('button', { name: /mark packed/i }).click()
  await expect(order).toContainText(/already been packed/i)
  await expect(order.getByRole('button', { name: /mark packed/i })).toHaveCount(0)

  // ---- Packing it twice records nothing twice ----
  await scanBox(page).fill(orderId)
  await scanBox(page).press('Enter')
  await expect(page.getByText(/already been packed/i).first()).toBeVisible()
})

test('refuses a held order and names the product blocking it', async ({ page }) => {
  await login(page, 'e2epickereleven')

  await page.goto('/orders')
  await page
    .getByRole('article', { name: /E2E-ORDER-00012/i })
    .getByRole('button', { name: /claim/i })
    .click()
  await expect(page).toHaveURL(/\/orders\/\d+$/)
  const orderId = page.url().split('/').pop()!

  const blocked = page.getByRole('article', { name: /Desk Blocker/i })
  await blocked.getByRole('button', { name: 'Report Issue' }).click()
  await blocked.getByLabel('Issue type').selectOption('cardNotFound')
  await blocked.getByRole('button', { name: 'Submit Issue' }).click()
  await expect(page.getByLabel(/Order status: Needs Attention/)).toBeVisible()

  // ---- Scenario 4 ----
  await page.goto('/packing')
  await scanBox(page).fill(orderId)
  await scanBox(page).press('Enter')

  const order = page.getByRole('region', { name: new RegExp(`Order ${orderId}`) })
  await expect(order).toBeVisible()
  // FR-028 — naming the product is what tells the packer this is not theirs to fix, and that the
  // sleeve belongs in the review area rather than on the bench.
  await expect(order).toContainText('Desk Blocker')
  await expect(order.getByRole('button', { name: /mark packed/i })).toHaveCount(0)
})

test('says plainly when a code matches no order', async ({ page }) => {
  await login(page, 'e2epickerten')
  await page.goto('/packing')

  await scanBox(page).fill('NOT-A-REAL-ORDER')
  await scanBox(page).press('Enter')

  await expect(page.getByRole('alert')).toContainText(/no order/i)
  // The desk stays ready for the next scan rather than becoming a dead end (FR-029).
  await expect(scanBox(page)).toBeFocused()
})
