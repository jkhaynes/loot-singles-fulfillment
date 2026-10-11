import { test, expect } from '@playwright/test'
import type { Browser, Page } from '@playwright/test'

// 021 T030. The E2E host runs the production rule (OrderDeletion:AllowTouchedOrders=false), so
// an order that has ever been claimed cannot be deleted. "Delete orders…" is deliberately not run
// here: it would remove other specs' orders while the suite runs in parallel (see T017/T019/T026).

async function login(page: Page, username: string) {
  await page.goto('/')
  await page.getByLabel(/username/i).fill(username)
  await page.getByLabel(/pin/i).fill('1234')
  await page.getByRole('button', { name: /log in/i }).click()
  await expect(page.getByRole('heading', { name: /E2E/i })).toBeVisible()
}

async function newLoggedInPage(browser: Browser, username: string): Promise<Page> {
  const context = await browser.newContext()
  const page = await context.newPage()
  await login(page, username)
  return page
}

test('a claimed order cannot be deleted on production', async ({ browser }) => {
  const managerPage = await newLoggedInPage(browser, 'e2emanager')
  await managerPage.goto('/orders')

  const row = managerPage.getByRole('article', { name: /E2E-DELETE-00002/i })
  await expect(row).toContainText(/picking by e2e picker seventeen/i)
  await row.getByRole('button', { name: /actions for order E2E-DELETE-00002/i }).click()

  await expect(managerPage.getByText(/can't delete on production/i)).toBeVisible()
  await expect(managerPage.getByRole('menuitem', { name: /delete order/i })).toHaveCount(0)
})

test('a manager deletes an untouched order through the menu and confirmation', async ({
  browser,
}) => {
  const managerPage = await newLoggedInPage(browser, 'e2emanager')
  await managerPage.goto('/orders')

  const row = managerPage.getByRole('article', { name: /E2E-DELETE-00001/i })
  await row.getByRole('button', { name: /actions for order E2E-DELETE-00001/i }).click()
  await managerPage.getByRole('menuitem', { name: /delete order/i }).click()

  await expect(managerPage.getByText('Delete order E2E-DELETE-00001?')).toBeVisible()
  const deleted = managerPage.waitForResponse(
    (response) =>
      response.request().method() === 'DELETE' &&
      /\/api\/orders\/\d+$/.test(new URL(response.url()).pathname),
  )
  await managerPage.getByRole('button', { name: /^delete order$/i }).click()
  expect((await deleted).ok()).toBe(true)

  await expect(managerPage.getByRole('article', { name: /E2E-DELETE-00001/i })).toHaveCount(0)

  // Gone from the server's list too, not only from the page's copy of it.
  await managerPage.reload()
  await expect(managerPage.getByRole('article', { name: /E2E-DELETE-00002/i })).toBeVisible()
  await expect(managerPage.getByRole('article', { name: /E2E-DELETE-00001/i })).toHaveCount(0)
})

test('a picker has no delete option on Browse Orders', async ({ browser }) => {
  const pickerPage = await newLoggedInPage(browser, 'e2epickerseventeen')
  await pickerPage.goto('/orders')

  const row = pickerPage.getByRole('article', { name: /E2E-DELETE-00002/i })
  await expect(row).toBeVisible()
  await expect(row.getByRole('button', { name: /actions for order/i })).toHaveCount(0)
  await expect(pickerPage.getByRole('button', { name: /delete order/i })).toHaveCount(0)
  await expect(pickerPage.getByRole('menuitem')).toHaveCount(0)
})
