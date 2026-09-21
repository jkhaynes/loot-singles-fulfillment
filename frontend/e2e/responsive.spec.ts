import { test, expect } from '@playwright/test'

test.use({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true })

test('login screen has no horizontal scroll on a mobile viewport', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Log in' })).toBeVisible()

  const hasHorizontalScroll = await page.evaluate(
    () => document.documentElement.scrollWidth > document.documentElement.clientWidth,
  )
  expect(hasHorizontalScroll).toBe(false)
})

test('dashboard has no horizontal scroll on a mobile viewport', async ({ page }) => {
  await page.goto('/')
  await page.getByLabel(/username/i).fill('e2emanager')
  await page.getByLabel(/pin/i).fill('1234')
  await page.getByRole('button', { name: /log in/i }).click()
  await expect(page.getByText(/E2E-ORDER-00001/)).toBeVisible()

  const hasHorizontalScroll = await page.evaluate(
    () => document.documentElement.scrollWidth > document.documentElement.clientWidth,
  )
  expect(hasHorizontalScroll).toBe(false)
})

async function hasHorizontalScroll(page: import('@playwright/test').Page): Promise<boolean> {
  return page.evaluate(
    () => document.documentElement.scrollWidth > document.documentElement.clientWidth,
  )
}

async function openFirstOrder(page: import('@playwright/test').Page) {
  await page.goto('/')
  await page.getByLabel(/username/i).fill('e2emanager')
  await page.getByLabel(/pin/i).fill('1234')
  await page.getByRole('button', { name: /log in/i }).click()
  await page.getByRole('link', { name: 'E2E-ORDER-00001' }).click()
}

// 016-mobile-picking. A phone gets the card view and nothing else, so the first product on
// screen is the first of the first box after grouping — Lorcana sorts before Magic and Pokemon
// — not whichever line TCGplayer happened to list first.
test('order detail shows one card on a mobile viewport, without horizontal scroll', async ({
  page,
}) => {
  await openFirstOrder(page)

  await expect(page.getByRole('heading', { name: 'Elsa', level: 2 })).toBeVisible()
  await expect(page.getByRole('article')).toHaveCount(1)
  expect(await hasHorizontalScroll(page)).toBe(false)
})

// There is deliberately no "the whole order also fits a phone" test any more: a phone cannot
// reach that view. The Product Owner removed the toggle on 2026-09-21 so the control it
// occupied could become the way out to the dashboard, which pickers did ask for.
test('a phone offers no way to the whole-order view', async ({ page }) => {
  await openFirstOrder(page)

  await expect(page.getByRole('heading', { name: 'Elsa', level: 2 })).toBeVisible()
  await expect(page.getByRole('button', { name: /whole order/i })).toHaveCount(0)
  await expect(page.getByRole('link', { name: /dashboard/i })).toBeVisible()
})

test.describe('on a desktop viewport', () => {
  test.use({ viewport: { width: 1280, height: 800 }, isMobile: false, hasTouch: false })

  test('order detail shows the whole order, and offers no card view', async ({ page }) => {
    await openFirstOrder(page)

    // Seated at a bench, the whole order is the more useful shape (PRD §8).
    await expect(page.getByRole('article')).toHaveCount(4)
    await expect(page.getByRole('article', { name: /Pikachu/i })).toBeVisible()
    await expect(page.getByRole('button', { name: /one card at a time/i })).toHaveCount(0)
  })
})
