import { test, expect } from '@playwright/test'
import path from 'node:path'
test.describe.configure({ mode: 'serial' })

const partialBatchFixture = path.resolve(
  '../backend/tests/LootSingles.Fixtures/PackingSlips/partial-batch-one-bad-order.pdf',
)
const multiOrderFixture = path.resolve(
  '../backend/tests/LootSingles.Fixtures/PackingSlips/valid-multi-order-batch.pdf',
)

async function login(page: import('@playwright/test').Page) {
  await page.goto('/')
  await page.getByLabel(/username/i).fill('e2emanager')
  await page.getByLabel(/pin/i).fill('1234')
  await page.getByRole('button', { name: /log in/i }).click()
  await expect(page.getByRole('heading', { name: /E2E/i })).toBeVisible()
}

test('guards navigation and distinguishes deliberate cancellation from interruption', async ({
  page,
}) => {
  test.setTimeout(60_000)
  await login(page)
  await page.getByRole('link', { name: /import orders/i }).click()
  await page.getByLabel(/packing slip/i).setInputFiles(partialBatchFixture)
  await page.getByRole('button', { name: /import orders/i }).click()
  await expect(page.getByText(/1 of 3 orders processed/i)).toBeVisible()

  expect(
    await page.evaluate(() => {
      const event = new Event('beforeunload', { cancelable: true })
      window.dispatchEvent(event)
      return event.defaultPrevented
    }),
  ).toBe(true)

  await page.getByRole('link', { name: /back to dashboard/i }).click()
  await expect(page.getByRole('alertdialog')).toContainText(/completed orders remain imported/i)
  await page.getByRole('button', { name: /stay and continue/i }).click()
  await expect(page).toHaveURL(/\/import$/)
  await expect(page.getByText(/2 of 3 orders processed/i)).toBeVisible()

  await page.goBack()
  await expect(page.getByRole('alertdialog')).toBeVisible()
  await page.getByRole('button', { name: /leave and stop/i }).click()
  await expect(page).toHaveURL(/\/$/)

  await page.getByRole('link', { name: /browse orders/i }).click()
  await expect(page.getByRole('article', { name: /PARTIAL-BATCH-VALID-1/i })).toBeVisible()
  await expect(page.getByRole('article', { name: /PARTIAL-BATCH-VALID-2/i })).toHaveCount(0)

  await page.getByRole('link', { name: /import orders/i }).click()
  await page.getByLabel(/packing slip/i).setInputFiles(partialBatchFixture)
  await page.getByRole('button', { name: /import orders/i }).click()
  await expect(page.getByText(/3 of 3 orders processed/i)).toBeVisible()
  await expect(page.getByRole('button', { name: /import orders/i })).toBeEnabled({
    timeout: 15_000,
  })
  await expect(
    page.locator('[data-outcome="rejected"]').filter({ hasText: 'PARTIAL-BATCH-VALID-1' }),
  ).toContainText(/already imported/i)
  await expect(
    page.locator('[data-outcome="succeeded"]').filter({ hasText: 'PARTIAL-BATCH-VALID-2' }),
  ).toBeVisible()

  await page.getByLabel(/packing slip/i).setInputFiles(multiOrderFixture)
  await page.getByRole('button', { name: /import orders/i }).click()
  await expect(page.getByText(/1 of 13 orders processed/i)).toBeVisible()
  await page.getByRole('button', { name: /cancel import/i }).click()
  await page.getByRole('button', { name: /stop import/i }).click()
  await expect(page.getByText(/import cancelled/i)).toBeVisible()
  await expect(page.getByText(/remaining processing stopped/i)).toBeVisible()
  await expect(page.getByText(/connection lost/i)).toHaveCount(0)
  await expect(page.getByRole('button', { name: /retry import/i })).toBeVisible()
})

for (const viewport of [
  { width: 1280, height: 800 },
  { width: 390, height: 844 },
]) {
  test(`imports a mixed packing slip at ${viewport.width}px`, async ({ page }) => {
    await page.setViewportSize(viewport)
    await login(page)

    await page.getByRole('link', { name: /import orders/i }).click()
    await expect(page).toHaveURL(/\/import$/)
    await expect(page.getByRole('link', { name: /back to dashboard/i })).toBeVisible()
    await expect(page.getByRole('link', { name: /browse orders/i })).toHaveCount(0)

    await page.getByRole('link', { name: /back to dashboard/i }).click()
    await expect(page).toHaveURL(/\/$/)
    await expect(page.getByRole('heading', { name: /E2E/i })).toBeVisible()

    await page.getByRole('link', { name: /import orders/i }).click()
    await expect(page).toHaveURL(/\/import$/)
    await page.getByLabel(/packing slip/i).setInputFiles(partialBatchFixture)
    await page.getByRole('button', { name: /import orders/i }).click()
    await expect(page.getByText(/[12] of 3 orders processed/i)).toBeVisible()
    await expect(page.getByText(/3 of 3/)).toBeVisible()
    await expect(page.locator('[data-outcome="rejected"]').first()).toBeVisible()
    await page.getByRole('link', { name: /back to dashboard/i }).click()
    await page.getByRole('link', { name: /browse orders/i }).click()
    await expect(page).toHaveURL(/\/orders$/)
    await expect(page.getByRole('heading', { name: /browse orders/i })).toBeVisible()

    const seededOrder = page.getByRole('article', { name: /E2E-ORDER-00001/i })
    await expect(seededOrder).toContainText('ready')
    await expect(seededOrder.getByRole('time')).toBeVisible()

    const importedOrder = page.getByRole('article', {
      name: /PARTIAL-BATCH-VALID-1/i,
    })
    await expect(importedOrder).toContainText('ready')
    await expect(importedOrder.getByRole('time')).toBeVisible()
    await expect(page.getByText(/customer|shipping address/i)).toHaveCount(0)
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
      ),
    ).toBe(true)
  })
}
