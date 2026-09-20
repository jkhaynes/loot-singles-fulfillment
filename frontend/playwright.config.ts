import { defineConfig, devices } from '@playwright/test'

// The E2E stack is deliberately isolated from the local dev stack so both can run at once:
// dev uses the API on 5098/7166 (your configured database) and Vite on 5173, while E2E uses the
// E2EHost on 5199 (its own disposable SQL Server container) and its own Vite on 5174.
const E2E_API_URL = 'http://127.0.0.1:5199'
const E2E_WEB_URL = 'http://localhost:5174'

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  reporter: 'html',
  use: {
    baseURL: E2E_WEB_URL,
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      command:
        'dotnet ../backend/tests/LootSingles.E2EHost/bin/Debug/net10.0/LootSingles.E2EHost.dll',
      url: `${E2E_API_URL}/health`,
      reuseExistingServer: false,
    },
    {
      // --strictPort so a busy 5174 fails loudly instead of silently landing on another port;
      // reuseExistingServer:false so the suite never adopts the dev server on 5173, which proxies
      // to the dev API and would make every E2E login hit the wrong backend.
      command: 'npm run dev -- --port 5174 --strictPort',
      url: E2E_WEB_URL,
      reuseExistingServer: false,
      env: {
        ...process.env,
        VITE_API_TARGET: E2E_API_URL,
      },
    },
  ],
})
