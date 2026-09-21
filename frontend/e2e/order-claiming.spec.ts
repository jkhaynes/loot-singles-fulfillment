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
  // Releasing returns the picker to the order list (015 T050). Scope the assertion to the row
  // they just released — other specs run in parallel and their orders may read "In Progress".
  await expect(pickerPage).toHaveURL(/\/orders$/)
  const releasedRow = pickerPage.getByRole('article', { name: /E2E-ORDER-00002/i })
  await expect(releasedRow).not.toContainText(/in progress/i)
  await expect(releasedRow.getByRole('button', { name: /claim/i })).toBeVisible()

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

// 016-mobile-picking T051 (US3): claiming is an explicit act on the order itself, and the
// dashboard offers back an order already held. Before this, opening an order from the dashboard
// was a dead end — the endpoint existed but nothing surfaced it there.
test('claims from the order page, and the dashboard then offers to resume it', async ({
  browser,
}) => {
  const picker = await newLoggedInPage(browser, 'e2epickersix')
  const onlooker = await newLoggedInPage(browser, 'e2epickerseven')

  await picker.goto('/orders')
  await picker.getByRole('link', { name: 'E2E-ORDER-00008' }).click()
  await expect(picker.getByRole('heading', { name: /E2E-ORDER-00008/i })).toBeVisible()

  // Viewing did not claim it: a second employee still sees it as free.
  await onlooker.goto('/orders')
  await expect(
    onlooker
      .getByRole('article', { name: /E2E-ORDER-00008/i })
      .getByRole('button', { name: /claim/i }),
  ).toBeVisible()

  await picker.getByRole('button', { name: /^claim$/i }).click()
  await expect(picker.getByText(/in progress.*picking by e2e picker six/i)).toBeVisible()
  await expect(picker.getByRole('button', { name: /^release$/i })).toBeVisible()

  // The dashboard offers the held order back instead of offering to start another.
  await picker.goto('/')
  const resume = picker.getByRole('link', { name: /resume/i })
  await expect(resume).toBeVisible()
  await expect(resume).toContainText('E2E-ORDER-00008')
  await expect(picker.getByRole('button', { name: /pick next order/i })).toHaveCount(0)

  // The other employee's dashboard is unaffected — activeClaim is only ever your own.
  await onlooker.goto('/')
  await expect(onlooker.getByRole('link', { name: /resume/i })).toHaveCount(0)
  await expect(onlooker.getByRole('button', { name: /pick next order/i })).toBeVisible()

  // And claiming it now explains, rather than offering an action that fails.
  await onlooker.goto('/orders')
  await expect(
    onlooker
      .getByRole('article', { name: /E2E-ORDER-00008/i })
      .getByRole('button', { name: /claim/i }),
  ).toHaveCount(0)
})
