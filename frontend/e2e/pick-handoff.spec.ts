import { test, expect } from '@playwright/test'
import type { Page } from '@playwright/test'

// 017-pick-completion-handoff T030 — quickstart.md scenarios 1 and 2.
//
// The endings live on the phone path, because that is where OrderFinish and the focused view
// live (016). Each test uses its own picker and its own order so Playwright workers never
// contend for a claim.
test.use({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true })

async function claim(page: Page, username: string, order: string) {
  await page.goto('/')
  await page.getByLabel(/username/i).fill(username)
  await page.getByLabel(/pin/i).fill('1234')
  await page.getByRole('button', { name: /log in/i }).click()
  await expect(page.getByRole('heading', { name: /E2E/i })).toBeVisible()

  await page.goto('/orders')
  await page
    .getByRole('article', { name: new RegExp(order, 'i') })
    .getByRole('button', { name: /claim/i })
    .click()
  await expect(page).toHaveURL(/\/orders\/\d+$/)
}

const next = (page: Page) => page.getByRole('button', { name: /next card/i })

test('a completed pick ends on a screen stating the card count, and prints a label', async ({
  page,
}) => {
  await claim(page, 'e2epickereight', 'E2E-ORDER-00009')

  // Three copies of one card and five of another: eight physical cards across two products.
  await page.getByRole('button', { name: 'Pulled all 3' }).click()
  await expect(page.getByRole('button', { name: /picked ✓/i })).toBeVisible()
  await next(page).click()
  await page.getByRole('button', { name: 'Pulled all 5' }).click()
  await expect(page.getByRole('button', { name: /picked ✓/i })).toBeVisible()

  // Past the last card is 016's review screen — "count the sleeve" — which this feature leaves
  // exactly as it was.
  await next(page).click()
  await expect(page.getByText(/count the sleeve/i)).toBeVisible()
  await page.getByRole('button', { name: /finish picking/i }).click()

  // ---- The ending (FR-001, FR-002) ----
  await expect(page.getByRole('heading', { name: 'Pick complete' })).toBeVisible()
  await expect(page.getByText('8', { exact: true })).toBeVisible()
  await expect(page.getByText(/cards in the sleeve/i)).toBeVisible()

  // FR-004: the count of product lines is deliberately absent. Only the number that can be
  // checked against a sleeve of loose cards appears.
  await expect(page.getByText(/product/i)).toHaveCount(0)

  // The picker's last instruction before the sleeve leaves their hand.
  await expect(page.getByText(/ready-to-pack/i)).toBeVisible()

  // ---- The label carries both codes and no customer data (FR-011, FR-012, FR-015) ----
  const label = page.getByLabel('Ready to pack label')
  await expect(label).toContainText('ORDER')
  await expect(label).toContainText('8 cards')
  // JsBarcode prints the identifier under the bars, and that printed text IS the human-readable
  // full identifier the label must carry — so the two can never drift apart.
  await expect(label).toContainText('E2E-ORDER-00009')
  await expect(label.locator('svg')).toHaveCount(2)

  // ---- Printing is a tap, never automatic (FR-005, FR-006) ----
  // Playwright cannot observe a print dialog, so the button's own state stands in for it: it
  // says "Print label" until asked, and moving on only becomes primary afterwards.
  const printButton = page.getByRole('button', { name: /^print label$/i })
  await expect(printButton).toBeVisible()
  // headless Chromium treats window.print() as a no-op, so the click is safe to make.
  await printButton.click()
  await expect(page.getByRole('button', { name: /print again/i })).toBeVisible()
})

test('a pick with an unresolved product ends held, and prints a hold label', async ({ page }) => {
  await claim(page, 'e2epickernine', 'E2E-ORDER-00010')

  await page.getByRole('button', { name: 'Pulled all 7' }).click()
  await expect(page.getByRole('button', { name: /picked ✓/i })).toBeVisible()

  await next(page).click()
  await page.getByRole('button', { name: /report an issue/i }).click()
  await page.getByLabel('Issue type').selectOption('cardNotFound')
  await page.getByRole('button', { name: /submit issue/i }).click()

  await next(page).click()
  await expect(page.getByText(/count the sleeve/i)).toBeVisible()
  await page.getByRole('button', { name: /finish picking/i }).click()

  // ---- The other ending (FR-003) ----
  await expect(page.getByRole('heading', { name: /needs a manager/i })).toBeVisible()
  await expect(page.getByText('7', { exact: true })).toBeVisible()
  await expect(page.getByText(/cards pulled/i)).toBeVisible()
  await expect(page.getByText('Hold Missing')).toBeVisible()

  // A held sleeve goes somewhere different, and the screen says so in words.
  await expect(page.getByText(/review area/i)).toBeVisible()

  // FR-046 — nothing records a set-aside card yet, so the screen says nothing about one rather
  // than printing a zero that would read as "no cards set aside".
  await expect(page.getByText(/set aside/i)).toHaveCount(0)

  // ---- The hold label is distinguishable without colour (FR-013) ----
  const label = page.getByLabel('Hold label')
  await expect(label).toContainText('HOLD')
  await expect(label).toContainText('7 cards')

  // The band is ink, not colour: it survives a monochrome thermal printer, so it has to survive
  // greyscale here too.
  await page.addStyleTag({ content: 'html { filter: grayscale(1) }' })
  await expect(label).toContainText('HOLD')
  await expect(page.getByRole('button', { name: /print hold label/i })).toBeVisible()
})
