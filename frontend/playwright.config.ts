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
  // The whole suite shares one E2EHost and one SQL Server container, so parallelism is bounded
  // by that single backend rather than by CPU count. Left to default (one worker per core) the
  // suite failed 7 of 23 on claim and navigation timeouts purely under load, while passing
  // every time at 3 — failures that look exactly like real regressions and are not.
  workers: 3,
  reporter: 'html',
  use: {
    baseURL: E2E_WEB_URL,
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      // `dotnet run` (not the prebuilt DLL) so the host is always current: running a stale binary
      // silently tests old backend code, which has twice made a real defect read as a pass.
      //
      // --artifacts-path keeps that build out of backend/src/LootSingles.Api/bin. The E2E host
      // references the API project, so building it also builds the API — and a running dev server
      // IS LootSingles.Api.exe holding that folder's DLLs open, which failed the build with
      // MSB3027 no matter which ports each stack used. Separate ports were never enough on their
      // own: the contended resource is the build output directory, not a socket.
      command:
        'dotnet run --project ../backend/tests/LootSingles.E2EHost --artifacts-path ../backend/artifacts/e2e',
      url: `${E2E_API_URL}/health`,
      reuseExistingServer: false,
      timeout: 180_000,
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
