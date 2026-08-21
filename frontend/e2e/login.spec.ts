import { test, expect } from '@playwright/test'

test('employee logs in with username and PIN', async ({ page }) => {
  await page.goto('/')

  await page.getByLabel(/username/i).fill('e2emanager')
  await page.getByLabel(/pin/i).fill('1234')
  await page.getByRole('button', { name: /log in/i }).click()

  await expect(page.getByText('Logged in as E2E Manager (ManagerAdmin)')).toBeVisible()

  await page.reload()
  await expect(page.getByText('Logged in as E2E Manager (ManagerAdmin)')).toBeVisible()

  await page.getByRole('button', { name: /log out/i }).click()
  await expect(page.getByRole('heading', { name: 'Log in' })).toBeVisible()
  await expect(page.getByLabel(/username/i)).toBeVisible()
})

test('wrong PIN shows the generic error and does not log in', async ({ page }) => {
  await page.goto('/')

  await page.getByLabel(/username/i).fill('e2emanager')
  await page.getByLabel(/pin/i).fill('9999')
  await page.getByRole('button', { name: /log in/i }).click()

  await expect(page.getByRole('alert')).toHaveText('Username or PIN is incorrect.')
  await expect(page.getByLabel(/username/i)).toBeVisible()
})
