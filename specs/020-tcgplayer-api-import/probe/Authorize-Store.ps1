<#
.SYNOPSIS
    One-time Store Authorization Workflow for feature 020. A PERSON runs this once, never an AI tool.

.DESCRIPTION
    Exchanges the authorization code from TCGplayer's store admin for the store access token, then saves
    the keys and the token straight into the API project's user-secrets. Nothing secret is printed.

    Before running:
      1. Sign in to TCGplayer as Loot's seller account.
      2. Open https://store.tcgplayer.com/admin/Apps/<your public key> and approve the application.
      3. Copy the 6-character code it shows. It expires after 1 hour.

    After running, copy the same three values into the stage and production Container Apps secrets
    (contracts/configuration.md). Never paste them into chat, an issue or a commit.
#>
[CmdletBinding()]
param(
    [string] $ApiProject = (Join-Path $PSScriptRoot '../../../backend/src/LootSingles.Api')
)

$ErrorActionPreference = 'Stop'
$UserAgent = 'LootSinglesFulfillment/0.0.0-setup (Loot Investments LLC)'

function Read-Secret([string] $envName, [string] $label) {
    $value = [Environment]::GetEnvironmentVariable($envName)
    if ([string]::IsNullOrWhiteSpace($value)) {
        $secure = Read-Host -Prompt "$label (input hidden)" -AsSecureString
        $value = [System.Net.NetworkCredential]::new('', $secure).Password
    }
    if ([string]::IsNullOrWhiteSpace($value)) { throw "$label is required." }
    return $value
}

$publicKey = Read-Secret 'TCGPLAYER_PUBLIC_KEY' 'TCGplayer public key'
$privateKey = Read-Secret 'TCGPLAYER_PRIVATE_KEY' 'TCGplayer private key'
$code = (Read-Host -Prompt 'Authorization code from the store admin page').Trim()
if ($code -notmatch '^[A-Za-z0-9]{6}$') { throw 'The authorization code should be 6 letters or digits.' }

# Bearer token from the keys.
$form = "grant_type=client_credentials&client_id=$([uri]::EscapeDataString($publicKey))&client_secret=$([uri]::EscapeDataString($privateKey))"
$token = Invoke-RestMethod -Method Post -Uri 'https://api.tcgplayer.com/token' -Body $form `
    -ContentType 'application/x-www-form-urlencoded' -UserAgent $UserAgent

# Exchange the code for the store access token (TCGplayer returns it as AuthorizationKey).
$result = Invoke-RestMethod -Method Post -Uri "https://api.tcgplayer.com/app/authorize/$code" `
    -Headers @{ Authorization = "bearer $($token.access_token)" } -UserAgent $UserAgent `
    -SkipHttpErrorCheck -StatusCodeVariable status

$first = @($result.results) | Select-Object -First 1
$accessToken = $first.AuthorizationKey ?? $first.authorizationKey ?? $result.AuthorizationKey ?? $result.authorizationKey
if ($status -ne 200 -or [string]::IsNullOrWhiteSpace($accessToken)) {
    $errors = (@($result.errors) | Where-Object { $_ }) -join '; '
    throw "Authorization failed (HTTP $status). $errors The code may have expired; get a new one from the store admin page."
}

dotnet user-secrets set 'Tcgplayer:PublicKey' $publicKey --project $ApiProject | Out-Null
dotnet user-secrets set 'Tcgplayer:PrivateKey' $privateKey --project $ApiProject | Out-Null
dotnet user-secrets set 'Tcgplayer:AccessToken' $accessToken --project $ApiProject | Out-Null

Write-Host 'Store authorized. Public key, private key and store access token saved to the API project''s user-secrets.'
Write-Host 'Now rerun Probe-Tcgplayer.ps1 with no environment variables set; it reads the user-secrets.'
