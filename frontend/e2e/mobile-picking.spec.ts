import { test, expect } from '@playwright/test'
import type { Page } from '@playwright/test'

// 016-mobile-picking T034 — the focused view on a phone (spec US2).
test.use({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true })

async function claimTheOrder(page: Page) {
  await page.goto('/')
  await page.getByLabel(/username/i).fill('e2epickerfive')
  await page.getByLabel(/pin/i).fill('1234')
  await page.getByRole('button', { name: /log in/i }).click()
  await expect(page.getByRole('heading', { name: /E2E/i })).toBeVisible()

  await page.goto('/orders')
  const row = page.getByRole('article', { name: /E2E-ORDER-00007/i })
  await row.getByRole('button', { name: /claim/i }).click()
  await expect(page).toHaveURL(/\/orders\/\d+$/)
}

function pickedButton(page: Page) {
  return page.getByRole('button', { name: /^picked$/i })
}

test('picks through an order one card at a time, and never records by moving', async ({ page }) => {
  await claimTheOrder(page)

  // Phone-sized screen, so the focused view is the default (FR-008).
  await expect(page.getByRole('heading', { name: 'First Card', level: 2 })).toBeVisible()
  await expect(page.getByText(/Aaa Set/)).toBeVisible()
  await expect(page.getByText(/1 of 2 in this box/i)).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Second Card' })).toHaveCount(0)

  // ---- The rule this whole view exists to keep (FR-011) ----
  // Move forward, then back, and prove the outcome is unchanged. Asserted on the control's
  // pressed state, not on the absence of an error: a component that recorded silently would
  // still show no error.
  await expect(pickedButton(page)).toHaveAttribute('aria-pressed', 'false')
  await page.getByRole('button', { name: /next/i }).click()
  await expect(page.getByRole('heading', { name: 'Second Card', level: 2 })).toBeVisible()
  await page.getByRole('button', { name: /previous/i }).click()
  await expect(page.getByRole('heading', { name: 'First Card', level: 2 })).toBeVisible()
  await expect(pickedButton(page)).toHaveAttribute('aria-pressed', 'false')

  // Quantity greater than one is emphasised, and survives a monochrome screen (PRD §5.3, §15).
  await page.getByRole('button', { name: /next/i }).click()
  await expect(page.getByText(/pull 2 copies/i)).toBeVisible()
  await page.addStyleTag({ content: 'html { filter: grayscale(1) }' })
  await expect(page.getByText('2', { exact: true })).toHaveCSS('font-weight', '800')

  // ---- Recording is explicit ----
  await pickedButton(page).click()
  await expect(pickedButton(page)).toHaveAttribute('aria-pressed', 'true')

  // ---- The unfinished-box guard (FR-016, FR-017) ----
  // Second Card is done but First Card was skipped, so leaving this box must not be silent.
  await page.getByRole('button', { name: /next/i }).click()
  const guard = page.getByRole('alert')
  await expect(guard).toBeVisible()
  await expect(guard).toContainText('Aaa Set')
  await expect(page.getByRole('list')).toContainText('First Card')
  // Exactly three ways out, and no way past without taking one.
  await expect(page.getByRole('button', { name: /back to First Card/i })).toBeVisible()
  await expect(page.getByRole('button', { name: /report what is missing/i })).toBeVisible()
  await expect(page.getByRole('button', { name: /leave the box anyway/i })).toBeVisible()

  // Go back and close the gap.
  await page.getByRole('button', { name: /back to First Card/i }).click()
  await expect(page.getByRole('heading', { name: 'First Card', level: 2 })).toBeVisible()
  await pickedButton(page).click()
  await expect(pickedButton(page)).toHaveAttribute('aria-pressed', 'true')

  // ---- The finished-box transition (FR-015) ----
  await page.getByRole('button', { name: /next/i }).click()
  await expect(page.getByRole('heading', { name: 'Second Card', level: 2 })).toBeVisible()
  await page.getByRole('button', { name: /next/i }).click()
  // The box hands over cleanly now that nothing is outstanding, naming the next one.
  await expect(page.getByRole('alert')).toHaveCount(0)
  await expect(page.getByText(/box finished/i)).toBeVisible()
  await expect(page.getByRole('button', { name: /start Bbb Set/i })).toBeVisible()

  await page.getByRole('button', { name: /start Bbb Set/i }).click()
  await expect(page.getByRole('heading', { name: 'Third Card', level: 2 })).toBeVisible()
})

// Deliberately claims nothing and uses a different order from the test above: the suite runs
// fully parallel, so two tests sharing one picker and one order would race for the claim.
test('lets a picker switch to the whole order and keeps that choice', async ({ page }) => {
  await page.goto('/')
  await page.getByLabel(/username/i).fill('e2emanager')
  await page.getByLabel(/pin/i).fill('1234')
  await page.getByRole('button', { name: /log in/i }).click()
  await page.getByRole('link', { name: 'E2E-ORDER-00006' }).click()
  await expect(page.getByRole('heading', { name: /E2E-ORDER-00006/i })).toBeVisible()

  // Viewing is safe: the focused view opens without claiming, and offers no way to record.
  await expect(page.getByRole('heading', { name: 'Hare Apparent', level: 2 })).toBeVisible()
  await expect(page.getByRole('button', { name: /^picked$/i })).toHaveCount(0)

  await page.getByRole('button', { name: /whole order/i }).click()

  // Every product at once, grouped into its boxes.
  await expect(page.getByRole('article')).toHaveCount(5)
  await expect(page.getByRole('group', { name: 'Magic · Aetherdrift' })).toBeVisible()
  await expect(page.getByRole('group', { name: 'Pokemon · Set not recorded' })).toBeVisible()

  // The choice survives a reload, because it is remembered on this device (FR-010).
  await page.reload()
  await expect(page.getByRole('article')).toHaveCount(5)
  await expect(page.getByRole('button', { name: /one card at a time/i })).toBeVisible()
})
