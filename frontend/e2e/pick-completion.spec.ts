import { test, expect } from '@playwright/test'
import type { Browser, Page } from '@playwright/test'

// 015-pick-completion T046: the full picking loop end to end (SC-001, SC-007, SC-008).

async function newLoggedInPage(
  browser: Browser,
  username: string,
  viewport?: { width: number; height: number },
): Promise<Page> {
  const context = await browser.newContext(viewport ? { viewport } : undefined)
  const page = await context.newPage()
  await page.goto('/')
  await page.getByLabel(/username/i).fill(username)
  await page.getByLabel(/pin/i).fill('1234')
  await page.getByRole('button', { name: /log in/i }).click()
  await expect(page.getByRole('heading', { name: /E2E/i })).toBeVisible()
  return page
}

async function claimOrder(page: Page, tcgplayerOrderId: string) {
  await page.goto('/orders')
  await page
    .getByRole('article', { name: new RegExp(tcgplayerOrderId, 'i') })
    .getByRole('button', { name: /claim/i })
    .click()
  await expect(page).toHaveURL(/\/orders\/\d+$/)
}

function lineCard(page: Page, productName: string) {
  return page.getByRole('article', { name: new RegExp(`Product ${productName}`, 'i') })
}

test('confirming every line takes an order to Picked without a separate complete step', async ({
  browser,
}) => {
  const page = await newLoggedInPage(browser, 'e2epickerthree')

  await claimOrder(page, 'E2E-ORDER-00004')
  await expect(page.getByText('0 of 2 lines confirmed')).toBeVisible()

  // The card image is resolved when the order opens and must survive recording an outcome
  // (015 T060, branch review BR-003) — the write response deliberately carries no image URL.
  const charizardImage = lineCard(page, 'Charizard').getByRole('img', { name: /Charizard/i })
  const imageSource = await charizardImage.getAttribute('src')
  expect(imageSource).toBeTruthy()

  await lineCard(page, 'Charizard').getByRole('button', { name: 'Picked' }).click()
  await expect(charizardImage).toHaveAttribute('src', imageSource!)
  await expect(page.getByText('1 of 2 lines confirmed')).toBeVisible()
  await expect(page.getByLabel(/Order status: In Progress/)).toBeVisible()

  await lineCard(page, 'Blastoise').getByRole('button', { name: 'Picked' }).click()
  await expect(page.getByText('2 of 2 lines confirmed')).toBeVisible()
  await expect(page.getByLabel(/Order status: Picked/)).toBeVisible()

  await page.goto('/')
  await expect(
    page.getByRole('article', { name: 'Picked' }).getByText('1', { exact: true }),
  ).toBeVisible()
})

test('a reported issue survives release and re-claim, then resolves to Picked on mobile', async ({
  browser,
}) => {
  // Simulated mobile viewport (SC-008): identical controls, no device-specific workaround.
  const page = await newLoggedInPage(browser, 'e2epickerfour', { width: 375, height: 812 })

  await claimOrder(page, 'E2E-ORDER-00005')
  const orderUrl = page.url()

  // 016-mobile-picking made the focused view the default on a phone-sized screen, so this test
  // asks for the whole order explicitly. What it checks is unchanged: the same controls work at
  // a small viewport with no device-specific workaround (SC-008).
  await page.getByRole('button', { name: /whole order/i }).click()

  await lineCard(page, 'Venusaur').getByRole('button', { name: 'Report Issue' }).click()
  await lineCard(page, 'Venusaur').getByLabel('Issue type').selectOption('cardNotFound')
  await lineCard(page, 'Venusaur').getByLabel('Note (optional)').fill('Not in the bin')
  await lineCard(page, 'Venusaur').getByRole('button', { name: 'Submit Issue' }).click()

  await expect(page.getByLabel(/Order status: Needs Attention/)).toBeVisible()
  await expect(lineCard(page, 'Venusaur')).toContainText('Card Not Found')
  await expect(lineCard(page, 'Venusaur')).toContainText('Not in the bin')

  await lineCard(page, 'Mewtwo').getByRole('button', { name: 'Picked' }).click()
  await expect(page.getByText('1 of 2 lines confirmed')).toBeVisible()
  await expect(page.getByLabel(/Order status: Needs Attention/)).toBeVisible()

  // The dashboard names the flagged product without anyone opening the order.
  await page.goto('/')
  const needsAttentionTile = page.getByRole('article', { name: 'Needs Attention' })
  await expect(needsAttentionTile).toContainText('E2E-ORDER-00005')
  await expect(needsAttentionTile).toContainText('Venusaur')

  // Released, it stays Needs Attention rather than looking like fresh work.
  await page.goto(orderUrl)
  const releaseButton = page.getByRole('button', { name: /^release$/i })
  await expect(releaseButton).toBeVisible()
  const [releaseResponse] = await Promise.all([
    page.waitForResponse((response) => response.url().endsWith('/release')),
    releaseButton.click(),
  ])
  expect(releaseResponse.status()).toBe(200)
  // Releasing returns the picker straight to the order list (PO decision 2026-09-19).
  await expect(page).toHaveURL(/\/orders$/)
  await expect(page.getByRole('alert')).toHaveCount(0)
  await expect(page.getByRole('article', { name: /E2E-ORDER-00005/i })).toContainText(
    'Needs Attention',
  )

  // Re-claimed and resolved, it reaches Picked with no special-case action.
  await claimOrder(page, 'E2E-ORDER-00005')
  await expect(page.getByLabel(/Order status: Needs Attention/)).toBeVisible()
  await lineCard(page, 'Venusaur').getByRole('button', { name: 'Picked' }).click()
  await expect(page.getByLabel(/Order status: Picked/)).toBeVisible()
})
