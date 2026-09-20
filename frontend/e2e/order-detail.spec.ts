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
  const heading = page.getByRole('heading', { name: /E2E-ORDER-00001/i })
  await expect(heading).toBeVisible()
  await expect(page.locator('header').filter({ has: heading })).toContainText('Ready')
  const line = page.getByRole('article', { name: /Pikachu/i })
  await expect(line).toContainText('Pokemon')
  await expect(line).toContainText('Base Set')
  await expect(line).toContainText('#58/102')
  await expect(line).toContainText('Near Mint')
  await expect(line).toContainText('2')
  await expect(line.getByText('2', { exact: true })).toHaveAttribute('data-emphasis', 'high')
  await page.addStyleTag({ content: 'html { filter: grayscale(1) }' })
  await expect(line.getByText('2', { exact: true })).toBeVisible()
  await expect(line.getByText('2', { exact: true })).toHaveCSS('font-weight', '700')
  await expect(line.getByLabel('Card image unavailable')).toHaveCount(0)
  await expect(line.getByRole('img', { name: /Pikachu/i })).toHaveAttribute(
    'src',
    'https://static.e2e-fixtures.local/pikachu.png',
  )
  await expect(page.getByRole('button', { name: /claim|pick|complete/i })).toHaveCount(0)

  await page.getByRole('link', { name: 'Dashboard' }).click()
  await page.getByRole('link', { name: /browse orders/i }).click()
  await page.getByRole('link', { name: 'E2E-ORDER-00001' }).click()
  await expect(page.getByRole('heading', { name: /E2E-ORDER-00001/i })).toBeVisible()

  await page.goto('/orders/2147483647')
  await expect(page.getByRole('alert')).toContainText(/order not found/i)
})

test('falls back to the placeholder for a line whose provider fails, without an error state', async ({
  page,
}) => {
  await login(page)

  await page.getByRole('link', { name: 'E2E-ORDER-00001' }).click()

  await expect(page).toHaveURL(/\/orders\/\d+$/)
  await expect(page.getByRole('heading', { name: /E2E-ORDER-00001/i })).toBeVisible()
  await expect(page.getByRole('alert')).toHaveCount(0)

  const failingLine = page.getByRole('article', { name: /Simulated Provider Failure/i })
  await expect(failingLine.getByLabel('Card image unavailable')).toBeVisible()
  await expect(failingLine.getByRole('img')).toHaveCount(0)

  const workingLine = page.getByRole('article', { name: /Pikachu/i })
  await expect(workingLine.getByRole('img', { name: /Pikachu/i })).toHaveAttribute(
    'src',
    'https://static.e2e-fixtures.local/pikachu.png',
  )
})

test('shows a Magic card image resolved by its own provider', async ({ page }) => {
  await login(page)

  await page.getByRole('link', { name: 'E2E-ORDER-00001' }).click()

  const line = page.getByRole('article', { name: /Lightning Bolt/i })
  await expect(line).toContainText('Magic')
  await expect(line).toContainText('Alpha')
  await expect(line).toContainText('#161')
  await expect(line.getByLabel('Card image unavailable')).toHaveCount(0)
  await expect(line.getByRole('img', { name: /Lightning Bolt/i })).toHaveAttribute(
    'src',
    'https://static.e2e-fixtures.local/lightning-bolt.png',
  )
})

test('shows a Lorcana card image resolved by its own provider', async ({ page }) => {
  await login(page)

  await page.getByRole('link', { name: 'E2E-ORDER-00001' }).click()

  const line = page.getByRole('article', { name: /Elsa/i })
  await expect(line).toContainText('Lorcana TCG')
  await expect(line).toContainText('The First Chapter')
  await expect(line).toContainText('#207')
  await expect(line.getByLabel('Card image unavailable')).toHaveCount(0)
  await expect(line.getByRole('img', { name: /Elsa/i })).toHaveAttribute(
    'src',
    'https://static.e2e-fixtures.local/elsa.png',
  )
})

// 016-mobile-picking T015 — set-aware picking (spec US1, PRD §13).
test('groups an order by game and set, and loses no line doing it', async ({ page }) => {
  await login(page)

  await page.getByRole('link', { name: 'E2E-ORDER-00006' }).click()
  await expect(page.getByRole('heading', { name: /E2E-ORDER-00006/i })).toBeVisible()

  // Magic before Pokemon; sets alphabetical within each game. The seed deliberately supplies
  // its lines in neither order, so passing here cannot be an accident of input order.
  const groups = page.getByRole('group')
  await expect(groups).toHaveCount(5)
  await expect(groups.nth(0)).toHaveAttribute('aria-label', 'Magic · Aetherdrift')
  await expect(groups.nth(1)).toHaveAttribute('aria-label', 'Magic · Bloomburrow')
  await expect(groups.nth(2)).toHaveAttribute('aria-label', 'Pokemon · Black Bolt')
  await expect(groups.nth(3)).toHaveAttribute('aria-label', 'Pokemon · Surging Sparks')
  // A line whose set was never recorded still gets a box, sorted last within its game.
  await expect(groups.nth(4)).toHaveAttribute('aria-label', 'Pokemon · Set not recorded')

  // Totality: five seeded lines in, five rendered out. A grouping bug that dropped one would
  // hide work the picker must do.
  await expect(page.getByRole('article')).toHaveCount(5)
  await expect(page.getByRole('article', { name: /Mystery Promo/i })).toBeVisible()

  // One product line, three physical cards — the distinction that matters at the box.
  const blackBolt = page.getByRole('group', { name: 'Pokemon · Black Bolt' })
  await expect(blackBolt).toContainText('1 product')
  await expect(blackBolt).toContainText('3 cards')
})
