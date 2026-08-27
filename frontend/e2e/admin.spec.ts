import { test, expect } from '@playwright/test'
import type { Browser, Page } from '@playwright/test'

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

async function createEmployee(page: Page, username: string, displayName: string) {
  await page.getByRole('link', { name: /manage employees/i }).click()
  await expect(page.getByRole('heading', { name: /manage employees/i })).toBeVisible()

  await page.getByLabel(/^username$/i).fill(username)
  await page.getByLabel(/display name/i).fill(displayName)
  await page.getByLabel(/initial pin/i).fill('1234')
  await page.getByRole('button', { name: /create employee/i }).click()

  await expect(page.getByRole('row', { name: new RegExp(username, 'i') })).toBeVisible()
}

test('Manager creates, sees, removes, and restores an employee', async ({ browser }) => {
  const managerPage = await newLoggedInPage(browser, 'e2emanager')

  await createEmployee(managerPage, 'e2eadmincreate', 'E2E Admin Create')

  const row = managerPage.getByRole('row', { name: /e2eadmincreate/i })
  await expect(row).toContainText(/active/i)

  await row.getByRole('button', { name: /remove/i }).click()
  await expect(managerPage.getByRole('row', { name: /e2eadmincreate/i })).toContainText(/inactive/i)

  await managerPage
    .getByRole('row', { name: /e2eadmincreate/i })
    .getByRole('button', { name: /restore/i })
    .click()
  const restoredRow = managerPage.getByRole('row', { name: /e2eadmincreate/i })
  await expect(restoredRow).toContainText(/active/i)
  await expect(restoredRow).not.toContainText('Inactive')
})

test("Changing an employee's role invalidates their already-active session", async ({
  browser,
}) => {
  const managerPage = await newLoggedInPage(browser, 'e2emanager')
  await createEmployee(managerPage, 'e2eroletarget', 'E2E Role Target')

  const targetPage = await newLoggedInPage(browser, 'e2eroletarget')
  await expect(targetPage.getByRole('link', { name: /manage employees/i })).toHaveCount(0)

  const targetRow = managerPage.getByRole('row', { name: /e2eroletarget/i })
  await targetRow.getByLabel(/change role/i).selectOption('ManagerAdmin')
  await expect(targetRow).toContainText('ManagerAdmin')

  await targetPage.reload()
  await expect(targetPage.getByRole('heading', { name: /log in/i })).toBeVisible()

  await login(targetPage, 'e2eroletarget')
  await expect(targetPage.getByRole('link', { name: /manage employees/i })).toBeVisible()
})
