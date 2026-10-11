import type { BrowserContext } from '@playwright/test'

// A 1×1 PNG. The E2E fixtures point card images at hosts that do not exist, and a card image that
// fails to load now falls back to the no-image state (R31), so a test that expects an image must
// serve one. Nothing leaves the browser.
export const onePixelPng = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==',
  'base64',
)

export async function serveFixtureImages(context: BrowserContext) {
  await context.route('https://static.e2e-fixtures.local/**', (route) =>
    route.fulfill({ contentType: 'image/png', body: onePixelPng }),
  )
}
