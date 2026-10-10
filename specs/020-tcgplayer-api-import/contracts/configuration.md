# Contract: TCGplayer Configuration

**Feature**: `020-tcgplayer-api-import`. Configuration section `Tcgplayer` is bound once at startup into `TcgplayerOptions`, following the `LockoutOptions` precedent of binding manually and validating by hand.

| Key | Secret? | Default | Notes |
|---|---|---|---|
| `Tcgplayer:PublicKey` | **Yes** | none | The **existing** client id. |
| `Tcgplayer:PrivateKey` | **Yes** | none | The **existing** client secret. |
| `Tcgplayer:AccessToken` | **Yes** | none | The store's **existing** access token, sent as `X-Tcg-Access-Token`. |
| `Tcgplayer:StoreKey` | No | none | Optional. When unset, resolved once with `GET /stores/self`. |
| `Tcgplayer:OpenOrderStatuses` | No | `["Ready To Ship"]` | Status **names** (FR-004, provisional). Change them without a code change: `Tcgplayer__OpenOrderStatuses__0`, `__1`, … |
| `Tcgplayer:CallsPerMinute` | No | `120` | Per process. Stage and production share one set of keys, so startup **fails** if the value is below 1 or above **150**: the two environments together can never exceed 300, whatever is configured. |
| `Tcgplayer:PageSize` | No | `50` | Search and item paging. |
| `Tcgplayer:CollectorNumberField` | No | `Number` | The catalog `extendedData` name (research.md §6, to be verified live). |
| `Tcgplayer:RarityField` | No | `Rarity` | As above. |
| `Tcgplayer:BaseUrl` | No | `https://api.tcgplayer.com/` | Tests point this at the stub. |
| `Tcgplayer:ApiVersion` | No | `v1.39.0` | Path prefix for every API call except the token request. |

## Configured vs not configured

- **Configured** means all three secrets are present and non-blank. If any is missing, the feature is **not configured**:
  - the app still starts;
  - PDF import works;
  - "Get new orders" returns `tcgplayerNotConfigured` without making any call.
- Development, CI and E2E have no real keys. E2E and integration tests supply **fake** values plus a stub handler.

## Where values live

| Environment | Secrets | Non-secrets |
|---|---|---|
| Local development | `dotnet user-secrets set "Tcgplayer:PublicKey" "…" --project backend/src/LootSingles.Api` (and the other two) | `appsettings.json` defaults |
| Stage and production | Container Apps **secrets** `tcgplayer-public-key`, `tcgplayer-private-key` and `tcgplayer-access-token`, referenced by the env vars `Tcgplayer__PublicKey=secretref:tcgplayer-public-key`, etc. | Env vars, set by hand in the portal like every other setting (019 runbook) |
| Repository, tests, fixtures, logs, AI tools | **Never** | n/a |

The credential-pattern scan run by `DeploymentConfigurationTests` and the PR quality gate is extended with `PrivateKey`, `client_secret` and `X-Tcg-Access-Token` assignment patterns (FR-023, SC-008). Test code uses obviously fake values such as `fake-public-key`, built so they don't match the scan, following the 019 precedent (`ff87871`).
