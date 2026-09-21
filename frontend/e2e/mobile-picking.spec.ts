import { test, expect } from '@playwright/test'
import type { Page } from '@playwright/test'

// 016-mobile-picking — the card-at-a-time view on a phone (spec US2).
test.use({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true })

async function claimTheOrder(page: Page) {
  await page.goto('/')
  await page.getByLabel(/username/i).fill('e2epickerfive')
  await page.getByLabel(/pin/i).fill('1234')
  await page.getByRole('button', { name: /log in/i }).click()
  await expect(page.getByRole('heading', { name: /E2E/i })).toBeVisible()

  await page.goto('/orders')
  await page
    .getByRole('article', { name: /E2E-ORDER-00007/i })
    .getByRole('button', { name: /claim/i })
    .click()
  await expect(page).toHaveURL(/\/orders\/\d+$/)
}

const next = (page: Page) => page.getByRole('button', { name: /next card/i })
const previous = (page: Page) => page.getByRole('button', { name: /previous card/i })
const recordButton = (page: Page) => page.getByRole('button', { name: /^picked|^pulled all/i })

test('picks through an order one card at a time, and never records by moving', async ({ page }) => {
  await claimTheOrder(page)

  // A phone gets the card view with no way to switch, so the order title block and its links
  // are gone and the record action is on screen without scrolling (FR-029).
  await expect(page.getByRole('heading', { name: 'First Card', level: 2 })).toBeVisible()
  await expect(page.getByText('Aaa Set')).toBeVisible()
  await expect(page.getByText('1 of 2')).toBeVisible()
  await expect(page.getByRole('heading', { level: 1 })).toHaveCount(0)
  await expect(recordButton(page)).toBeInViewport()

  // ---- Moving records nothing (FR-011) ----
  await expect(recordButton(page)).toHaveAttribute('aria-pressed', 'false')
  await next(page).click()
  await expect(page.getByRole('heading', { name: 'Second Card', level: 2 })).toBeVisible()
  await previous(page).click()
  await expect(page.getByRole('heading', { name: 'First Card', level: 2 })).toBeVisible()
  await expect(recordButton(page)).toHaveAttribute('aria-pressed', 'false')

  // Quantity is loud, carries into the button label, and survives a monochrome screen.
  await next(page).click()
  await expect(page.getByText('copies to pull')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Pulled all 2' })).toBeVisible()
  await page.addStyleTag({ content: 'html { filter: grayscale(1) }' })
  await expect(page.getByText('2', { exact: true })).toHaveCSS('font-weight', '800')

  // ---- Recording is explicit, and visibly confirmed ----
  await recordButton(page).click()
  await expect(page.getByRole('button', { name: /picked ✓/i })).toBeVisible()

  // ---- Moving is never blocked (FR-019a) ----
  // First Card was skipped, yet crossing into the next box does not stop the picker. An earlier
  // build put a full-screen guard here, which on single-card boxes fired on nearly every card.
  await next(page).click()
  await expect(page.getByRole('heading', { name: 'Third Card', level: 2 })).toBeVisible()
  await expect(page.getByText(/new box/i)).toBeVisible()

  // ---- The one interruption: the final review (PRD §22) ----
  await next(page).click()
  await expect(page.getByText(/count the sleeve/i)).toBeVisible()
  // Two cards pulled from the quantity-2 line; First and Third were never resolved, and the
  // headline counts what is in the sleeve rather than what was ordered.
  await expect(page.getByRole('button', { name: /complete — 2 cards/i })).toBeVisible()
  await expect(page.getByText(/2 not looked at/i)).toBeVisible()

  // Every product is listed whatever its outcome, and any of them can be jumped back to.
  await page.getByRole('button', { name: /First Card/i }).click()
  await expect(page.getByRole('heading', { name: 'First Card', level: 2 })).toBeVisible()
  await recordButton(page).click()
  await expect(page.getByRole('button', { name: /picked ✓/i })).toBeVisible()
})

// Claims nothing and uses a different order from the test above: the suite runs in parallel, so
// two tests sharing one picker and one order would race for the claim.
test('offers a way out to the dashboard from every card', async ({ page }) => {
  await page.goto('/')
  await page.getByLabel(/username/i).fill('e2emanager')
  await page.getByLabel(/pin/i).fill('1234')
  await page.getByRole('button', { name: /log in/i }).click()
  await page.getByRole('link', { name: 'E2E-ORDER-00006' }).click()

  // Viewing is safe: the card opens without claiming, and offers no way to record.
  await expect(page.getByRole('heading', { name: 'Hare Apparent', level: 2 })).toBeVisible()
  await expect(page.getByRole('button', { name: /^picked/i })).toHaveCount(0)

  // Until this replaced the view toggle, a picker was stuck on an order until its end.
  await page.getByRole('link', { name: /dashboard/i }).click()
  await expect(page).toHaveURL(/localhost:\d+\/$/)
})
