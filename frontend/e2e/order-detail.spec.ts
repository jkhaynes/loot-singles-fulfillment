import { test, expect } from '@playwright/test'

async function login(page: import('@playwright/test').Page) {
  await page.goto('/')
  await page.getByLabel(/username/i).fill('e2emanager')
  await page.getByLabel(/pin/i).fill('1234')
  await page.getByRole('button', { name: /log in/i }).click()
  await expect(page.getByRole('heading', { name: /E2E/i })).toBeVisible()
}

test('opens an available order and shows its picking details', async ({ page }) => {
  await login(page)

  await page.getByRole('link', { name: 'E2E-ORDER-00001' }).click()

  await expect(page).toHaveURL(/\/orders\/\d+$/)
  await expect(page.getByRole('heading', { name: /E2E-ORDER-00001/i })).toBeVisible()
  const line = page.getByRole('article', { name: /Pikachu/i })
  await expect(line).toContainText('Base Set')
  await expect(line).toContainText('Near Mint')
  await expect(line).toContainText('2')
})
