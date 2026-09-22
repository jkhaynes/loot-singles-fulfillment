import { test, expect } from '@playwright/test'
import type { Page } from '@playwright/test'
import path from 'node:path'

// Same shape order-import.spec.ts uses: resolved from the working directory Playwright runs in.
const importedBatchFixture = path.resolve(
  '../backend/tests/LootSingles.Fixtures/PackingSlips/valid-multi-order-batch.pdf',
)

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

  // In the way a packer would: from the dashboard tile that counts the sleeves waiting for them
  // (T115). Every test here used to open /packing directly, which is how nobody noticed that
  // nothing in the application linked to it.
  await page.goto('/')
  await page
    .getByRole('article', { name: 'Awaiting Packing' })
    .getByRole('link', { name: /awaiting packing/i })
    .click()
  await expect(page).toHaveURL(/\/packing$/)
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

// T064 / quickstart scenario 6, and T068 / scenario 7 — the two smaller stories, exercised
// against the same picked order so they need no seeds of their own.
test('reprints a label and shows the queue falling when an order is packed', async ({ page }) => {
  await login(page, 'e2epickertwelve')

  const orderId = await pickWholeOrder(page, 'E2E-ORDER-00013', 'Reprint Card')

  // ---- Scenario 7: picked and unpacked means awaiting packing ----
  // Asserted against this order rather than the dashboard total. The total is shared, and
  // other workers pick and pack their own orders throughout the run — an absolute number
  // would fail whenever one of them happened to be mid-flight, which is a race in the test
  // rather than anything wrong with the count.
  await page.goto('/packing')
  const queue = page.getByRole('region', { name: 'Awaiting packing' })
  await expect(queue).toContainText(`Order ${orderId}`)

  // ---- Scenario 6: the label can be produced again from the desk ----
  await scanBox(page).fill(orderId)
  await scanBox(page).press('Enter')

  const order = page.getByRole('region', { name: new RegExp(`Order ${orderId}`) })
  await expect(order).toBeVisible()
  await order.getByRole('button', { name: /print label again/i }).click()

  // The label carries the original picker, not whoever asked for the reprint (FR-016).
  const label = page.getByLabel('Ready to pack label')
  await expect(label).toContainText(`ORDER ${orderId}`)
  await expect(label).toContainText('E2E Picker Twelve')

  // The reprint goes through the same printable-label component as the ending screen, but that is
  // an assumption until the output is checked — and the desk is a busier page to isolate a label
  // from. One page, not the whole desk paginated at 1⅛ inches.
  const pdf = await page.pdf({ preferCSSPageSize: true })
  const pageCount = (pdf.toString('latin1').match(/\/Type\s*\/Page(?!s)/g) ?? []).length
  expect(pageCount).toBe(1)

  // ---- Scenario 7 again: and leaves the queue once packed ----
  await order.getByRole('button', { name: /mark packed/i }).click()
  await expect(order).toContainText(/already been packed/i)
  await expect(queue).not.toContainText(`Order ${orderId}`)

  // The dashboard tile counts the same thing, so it is checked for its label rather than a
  // number other workers are moving.
  await page.goto('/')
  await expect(page.getByRole('article', { name: 'Awaiting Packing' })).toBeVisible()
})

// T095/T096 / BR-005 — the happy path of US2's central action, end to end.
//
// Every other order in this suite is seeded directly, so none of them has a stored slip and all of
// them exercise scenario 5 instead. A slip only exists for an order that came through the importer,
// so this test imports one and then packs it.
test('prints the stored packing slip for an order that was imported', async ({ page }) => {
  test.setTimeout(60_000)

  // The importer is manager-only, and the fixture is the same batch the backend tests slice.
  await login(page, 'e2emanager')
  await page.getByRole('link', { name: /import orders/i }).click()
  await page.getByLabel(/packing slip/i).setInputFiles(importedBatchFixture)
  await page.getByRole('button', { name: /import orders/i }).click()
  await expect(page.getByText(/13 of 13 orders processed/i)).toBeVisible({ timeout: 30_000 })

  // Pick it so it reaches the bench.
  await page.goto('/orders')
  // Any order from the batch will do; the first one is stable across runs.
  const row = page.getByRole('article', { name: /F0000001-ABC001-00001/i })
  await row.getByRole('button', { name: /claim/i }).click()
  await expect(page).toHaveURL(/\/orders\/\d+$/)
  const orderId = page.url().split('/').pop()!

  // Re-query each time rather than collecting handles up front: recording a pick re-renders the
  // list and relabels the button to 'Picked ✓', so handles taken before the first click go stale
  // and the rest of the loop silently clicks nothing.
  // Wait for the list before counting anything in it: a count taken mid-render is zero, the
  // loop then does nothing, and the failure surfaces later as a status that never changed.
  await expect(page.getByRole('article').first()).toBeVisible()

  // A line with more than one copy reads 'Pulled all N', not 'Picked' — matching only the
  // latter silently clicks nothing and leaves the order In Progress.
  const unpicked = () => page.getByRole('button', { name: /^Picked$|^Pulled all/ })
  for (let remaining = await unpicked().count(); remaining > 0; remaining--) {
    await unpicked().first().click()
    await expect(unpicked()).toHaveCount(remaining - 1)
  }
  await expect(page.getByLabel(/Order status: Picked/)).toBeVisible()

  // ---- quickstart scenario 3, the half the seeded orders cannot reach ----
  await page.goto('/packing')
  await scanBox(page).fill(orderId)
  await scanBox(page).press('Enter')

  const order = page.getByRole('region', { name: new RegExp(`Order ${orderId}`) })
  await expect(order).toBeVisible()
  await expect(order).not.toContainText(/no packing slip/i)

  const slip = order.getByRole('link', { name: /print packing slip/i })
  await expect(slip).toBeVisible()
  await expect(slip).toHaveAttribute('href', `/api/orders/${orderId}/packing-slip`)

  // The slip really is served, and really is a PDF.
  const response = await page.request.get(`/api/orders/${orderId}/packing-slip`)
  expect(response.status()).toBe(200)
  expect(response.headers()['content-type']).toContain('application/pdf')

  // Pack it rather than leaving it on a queue other specs also look at.
  await order.getByRole('button', { name: /mark packed/i }).click()
  await expect(order).toContainText(/already been packed/i)
})
