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

test('Pick Next Order claims exclusively, is visible to another employee, and can be released', async ({
  browser,
}) => {
  const pickerPage = await newLoggedInPage(browser, 'e2epicker')
  const managerPage = await newLoggedInPage(browser, 'e2emanager')

  await pickerPage.getByRole('button', { name: /pick next order/i }).click()
  await expect(pickerPage).toHaveURL(/\/orders\/\d+$/)
  await expect(pickerPage.getByRole('heading', { name: /E2E-ORDER-00002/i })).toBeVisible()
  await expect(pickerPage.getByText(/in progress.*picking by e2e picker/i)).toBeVisible()

  await managerPage.goto('/orders')
  const row = managerPage.getByRole('article', { name: /E2E-ORDER-00002/i })
  await expect(row).toContainText(/in progress.*picking by e2e picker/i)
  await expect(row.getByRole('button', { name: /claim/i })).toHaveCount(0)

  await pickerPage.getByRole('button', { name: /^release$/i }).click()
  await expect(pickerPage.getByText(/in progress/i)).toHaveCount(0)

  await managerPage.reload()
  await expect(
    managerPage.getByRole('article', { name: /E2E-ORDER-00002/i }).getByRole('button', {
      name: /claim/i,
    }),
  ).toBeVisible()
})

test('Choose Order claims a specific order, and a manager can force-release it', async ({
  browser,
}) => {
  const pickerPage = await newLoggedInPage(browser, 'e2epickertwo')
  const managerPage = await newLoggedInPage(browser, 'e2emanager')

  await pickerPage.goto('/orders')
  const row = pickerPage.getByRole('article', { name: /E2E-ORDER-00003/i })
  await row.getByRole('button', { name: /claim/i }).click()
  await expect(pickerPage).toHaveURL(/\/orders\/\d+$/)
  await expect(pickerPage.getByRole('heading', { name: /E2E-ORDER-00003/i })).toBeVisible()
  await expect(pickerPage.getByText(/in progress.*picking by e2e picker two/i)).toBeVisible()
  await expect(pickerPage.getByRole('button', { name: /force-release/i })).toHaveCount(0)

  const orderUrl = pickerPage.url()
  await managerPage.goto(orderUrl)
  await expect(managerPage.getByText(/in progress.*picking by e2e picker two/i)).toBeVisible()
  await managerPage.getByRole('button', { name: /force-release/i }).click()
  await expect(managerPage.getByText(/in progress/i)).toHaveCount(0)

  await pickerPage.goto('/orders')
  await expect(
    pickerPage.getByRole('article', { name: /E2E-ORDER-00003/i }).getByRole('button', {
      name: /claim/i,
    }),
  ).toBeVisible()
})
