# T009-T011 report

## T009 (frontend guard)
`frontend/tests/import/ImportPage.synthetic.test.tsx`: mocks `getNewOrdersFromTcgplayer` with two presses shaped like the synthetic source (2 succeeded, then 2 duplicateOrder) and asserts no "mock"/"synthetic" text in the rendered page. Passed at once (guard; no frontend change). Vitest 21 files / 334 tests pass; `npm run build` clean; `npm run lint` only pre-existing warnings (AuthContext.tsx, PackingDeskPage.tsx).

## T010 (Production and Testing)
Theories for each environment: no keys -> `tcgplayerNotConfigured` and zero orders; synthetic BaseUrl + fake keys (UseSetting) -> primary handler (via IHttpMessageHandlerFactory, unwrapping DelegatingHandler) is not SyntheticTcgplayerHandler; startup log lacks "synthetic orders are active" (CapturingLoggerProvider). Plus a Development test that the line IS logged.

RED (IsDevelopment() gate replaced with `true`): 6 failed, 8 passed:
- primary handler: `Assert.IsNotType() Failure: Value is the exact type ... SyntheticTcgplayerHandler` (Production, Testing)
- startup log: `Assert.DoesNotContain() Failure: Filter matched in collection` (Production, Testing)
- no keys: `Expected: "failed" Actual: "completed"` (Production, Testing)
GREEN after restore: 14/14 SyntheticTcgplayerSourceTests.

## T011 (Development with real keys and real BaseUrl)
Keys and `https://api.tcgplayer.com/` set via `UseSetting` (reaches builder.Configuration before Program's code, like user secrets/env vars; ConfigureAppConfiguration would arrive after eager option binding). Asserts synthetic BaseUrl/keys in resolved TcgplayerOptions, import completed with 8 succeeded, synthetic handler Requests non-empty (proves it is the instance the import used; the factory caches the chain per name), every recorded UserAgent contains "Loot Investments LLC" (real delegating handlers in front), and HttpClient BaseAddress is the synthetic host.

RED (override moved to `Sources.Insert(0, ...)`, ahead of UseSetting values): 1 failed, 7 passed: `Expected: "https://tcgplayer-synthetic.invalid/" Actual: "https://api.tcgplayer.com/"`. Restored; passes.

## Credential scan
The first full run failed `DeploymentConfigurationTests.No_tracked_file_contains_a_tcgplayer_secret_value_assignment` because the test file assigned literals to a key name. My reshaping edit was denied by the auto-mode classifier twice; the controller applied the placeholder edit (`UsePlaceholderKeys` helper, `real-looking-*-key` values). Scan and its ignore list untouched.
Full IntegrationTests: 341/341 pass, credential scan included.
