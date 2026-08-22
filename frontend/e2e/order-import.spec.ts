import { test, expect } from '@playwright/test'
import path from 'node:path'
test.describe.configure({ mode: 'serial' })
for (const viewport of [
  { width: 1280, height: 800 },
  { width: 390, height: 844 },
]) {
  test(`imports a mixed packing slip at ${viewport.width}px`, async ({ page }) => {
    await page.setViewportSize(viewport)
    await page.goto('/')
    await page.getByLabel(/username/i).fill('e2emanager')
    await page.getByLabel(/pin/i).fill('1234')
    await page.getByRole('button', { name: /log in/i }).click()
    await expect(page.getByRole('heading', { name: /E2E/i })).toBeVisible()

    await page.getByRole('link', { name: /import orders/i }).click()
    await expect(page).toHaveURL(/\/import$/)
    await expect(page.getByRole('link', { name: /back to dashboard/i })).toBeVisible()
    await expect(page.getByRole('link', { name: /browse orders/i })).toHaveCount(0)

    await page.getByRole('link', { name: /back to dashboard/i }).click()
    await expect(page).toHaveURL(/\/$/)
    await expect(page.getByRole('heading', { name: /E2E/i })).toBeVisible()

    await page.getByRole('link', { name: /import orders/i }).click()
    await expect(page).toHaveURL(/\/import$/)
    await page
      .getByLabel(/packing slip/i)
      .setInputFiles(
        path.resolve(
          '../backend/tests/LootSingles.Fixtures/PackingSlips/partial-batch-one-bad-order.pdf',
        ),
      )
    await page.getByRole('button', { name: /import orders/i }).click()
    await expect(page.getByText(/3 of 3/)).toBeVisible()
    await expect(page.locator('[data-outcome="rejected"]').first()).toBeVisible()
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
      ),
    ).toBe(true)
  })
}
