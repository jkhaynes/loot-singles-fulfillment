# TCGplayer API Setup: Step by Step

**Feature**: `020-tcgplayer-api-import` | **Written**: 2026-10-08

This guide connects Loot's TCGplayer API keys to Loot's store so the app can read open orders. It covers what you do **once**, and what to do if the connection ever stops working. Nothing here repeats on a schedule; once set up, the app manages its own short-lived tokens.

## Before you start

You need:

- the **public key** and **private key** TCGplayer sent;
- access to **Loot's TCGplayer seller account**, the one that sells the orders;
- this repository checked out, PowerShell 7, and the .NET SDK (for `dotnet user-secrets`);
- for Part 3 only: access to Loot's Azure portal.

### Rules that apply to every step

These come from the TCGplayer API agreement; `CLAUDE.md` has the full list.

- **Never paste keys, tokens or API responses** into chat, an AI tool, an issue, a commit or a message. Type or paste them only where these steps say.
- **Don't create extra keys or integrations.** Loot uses one set of keys and one store authorization.
- The scripts below are **read-only**, apart from the one-time authorization in Part 1.

### Three kinds of credential

| What | Lasts | Who handles it |
|---|---|---|
| **Authorization code**: 6 characters from TCGplayer's store admin | 1 hour, used once | You, once, in Part 1 |
| **Store access token**: what the code is exchanged for | Long-lived, until the app is unapproved in the store admin | Saved once as a secret; nobody touches it again |
| **Bearer token**: a temporary pass the app fetches | About 2 weeks | The app, automatically |

---

## Part 1: Authorize the app for Loot's store (once)

Do steps 1–3 within the same hour, because the code expires.

1. **Open the approval page.** Sign in to TCGplayer as Loot's seller account, then go to:

   ```text
   https://store.tcgplayer.com/admin/Apps/<public key>
   ```

   Replace `<public key>` with the public key TCGplayer sent.

2. **Approve the app.** Approve the application on that page. TCGplayer then shows a **6-character code**. Copy it.

3. **Run the authorization script** from the repository root in PowerShell:

   ```powershell
   ./specs/020-tcgplayer-api-import/probe/Authorize-Store.ps1
   ```

   It asks for:
   - the **public key** (input hidden);
   - the **private key** (input hidden);
   - the **6-character code**.

   It then exchanges the code for the store access token and saves all three values into the API project's user-secrets. It prints nothing secret.

4. **Check it worked.** You should see:

   ```text
   Store authorized. Public key, private key and store access token saved to the API project's user-secrets.
   ```

   **If it fails** with "Authorization failed", the code has probably expired. Go back to step 1 for a new code and run the script again.

## Part 2: Run the probe and report back (once)

This confirms what TCGplayer's real responses look like before the feature is built.

1. **Run the probe** from the repository root. It reads the values you saved in Part 1, so you don't need to type anything:

   ```powershell
   ./specs/020-tcgplayer-api-import/probe/Probe-Tcgplayer.ps1
   ```

   It makes about ten read-only calls and writes a report to your temp folder.

2. **Open the report:**

   ```powershell
   notepad "$env:TEMP\tcgplayer-probe-report.txt"
   ```

3. **Check the steps.** Steps 1 to 8 should each show `HTTP 200`.
   - If step 3 still shows `403`, Part 1 didn't take effect. Check you approved the app while signed in as **Loot's** seller account.
   - If it says "No open orders right now", run it again when some orders are waiting.

4. **Describe what you found, in your own words.** Don't paste the report. Answer:
   - Is there a status named **Ready To Ship** in the status list?
   - For each sampled order, does the **productCount** match the **sum of line quantities**, or the **number of lines**?
   - What are the **extendedData field names**? Is there one called `Number`, and one called `Rarity`?
   - Roughly, how are **condition** and **printing** worded? For example, "Near Mint", "Near Mint Foil", "Holofoil".
   - Which **site** do the image links point to?

## Part 3: Add the credentials to stage, then production

Do this when the feature is ready to deploy, **stage first**. Until then the deployed app ignores these settings, so adding them early does no harm.

For each Container App: stage first, then production.

1. **Add the secrets.** In the Azure portal, open the Container App, then **Settings → Secrets → Add**. Add three secrets, each with type **Container Apps Secret**:

   | Secret name | Value |
   |---|---|
   | `tcgplayer-public-key` | the public key |
   | `tcgplayer-private-key` | the private key |
   | `tcgplayer-access-token` | the store access token |

   To read the store access token from your machine without displaying it anywhere else, run:

   ```powershell
   dotnet user-secrets list --project backend/src/LootSingles.Api
   ```

   Copy the `Tcgplayer:AccessToken` value straight into the portal field. Then clear your terminal with `Clear-Host`.

2. **Reference the secrets from environment variables.** Go to **Application → Containers → Edit and deploy**, click the container, then the **Environment variables** tab. Add three variables, each with source **Reference a secret**:

   | Name | Secret |
   |---|---|
   | `Tcgplayer__PublicKey` | `tcgplayer-public-key` |
   | `Tcgplayer__PrivateKey` | `tcgplayer-private-key` |
   | `Tcgplayer__AccessToken` | `tcgplayer-access-token` |

3. **Save.** Choose **Save → Create**. This starts a new revision.

4. **Test.** Once the feature is deployed, sign in, open **Import** and press **Get new orders**.
   - "Getting orders from TCGplayer isn't set up here" means one of the three variables is missing or misnamed.

## If it stops working later

The app tells you what kind of problem it is:

| Message | What it means | What to do |
|---|---|---|
| "Couldn't reach TCGplayer…" | TCGplayer is down or slow | Try again later, or upload a packing slip |
| "TCGplayer refused the store's connection…" | The store access token no longer works, for example because the app was unapproved in the store admin | Repeat **Part 1**, then update the `tcgplayer-access-token` secret in **Part 3** (stage and production) |
| "Getting orders from TCGplayer isn't set up here" | The secrets are missing in that environment | Do **Part 3** for that environment |

In every case, **packing-slip PDF upload keeps working**, so orders can still be imported while the problem is fixed.

## Changing which orders count as open

The app imports the orders the seller portal shows as open: **Normal** orders (not Direct) that are either shipped orders in **Processing** or **Ready To Ship**, or in-store pickup orders that are **Received**. To change that without changing code, open the Container App's environment variables and set any of these lists:

```text
Tcgplayer__OpenOrderStatuses__0 = <first order status name, for shipped orders>
Tcgplayer__OpenOrderStatuses__1 = <second, if any>
Tcgplayer__OpenPickupStatuses__0 = <first pickup status name, for in-store pickup orders>
Tcgplayer__OrderTypes__0 = <first order type name>
```

The names must match TCGplayer's names exactly (case and spacing). A name TCGplayer doesn't know makes "Get new orders" fail with a message naming it, rather than importing the wrong orders.
