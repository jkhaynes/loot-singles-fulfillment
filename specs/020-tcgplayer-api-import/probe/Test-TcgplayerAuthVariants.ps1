<#
.SYNOPSIS
    Tries every reported way of getting a store-scoped token, then reads the order manifest with each.
    A PERSON runs this. Only HTTP statuses are recorded, so the output is safe to share.

.DESCRIPTION
    Variants, from TCGplayer's docs and open-source clients:
      A  POST /token (keys) + X-Tcg-Access-Token header           (what the probe does today)
      B  POST /token/access, X-Tcg-Access-Token + keys-only bearer  (open-source tcgplayer-python client)
      C  POST /token/access, X-Tcg-Access-Token only
      D  POST /v1.39.0/token (keys) + X-Tcg-Access-Token header    (community note: version before /token)
      E  keys-only bearer, with X-Tcg-Access-Token sent on the order call itself
      F  variant A's bearer, with X-Tcg-Access-Token also sent on the order call
    Each token request only exchanges existing credentials; nothing is created or changed on the store.
#>
$ErrorActionPreference = 'Stop'
$BaseUrl = 'https://api.tcgplayer.com'
$UserAgent = 'LootSinglesFulfillment/0.0.0-probe (Loot Investments LLC)'

$secrets = @{}
foreach ($line in (dotnet user-secrets list --project (Join-Path $PSScriptRoot '../../../backend/src/LootSingles.Api') 2>$null)) {
    $p = $line -split ' = ', 2; if ($p.Count -eq 2) { $secrets[$p[0]] = $p[1] }
}
$pub = $env:TCGPLAYER_PUBLIC_KEY ?? $secrets['Tcgplayer:PublicKey']
$priv = $env:TCGPLAYER_PRIVATE_KEY ?? $secrets['Tcgplayer:PrivateKey']
$access = $env:TCGPLAYER_ACCESS_TOKEN ?? $secrets['Tcgplayer:AccessToken']
if (-not $pub -or -not $priv -or -not $access) { throw 'Need public key, private key and store access token (user-secrets or environment).' }

function Call([string] $method, [string] $path, [hashtable] $headers, $body) {
    Start-Sleep -Milliseconds 400
    $params = @{ Method = $method; Uri = "$BaseUrl$path"; Headers = $headers; UserAgent = $UserAgent
                 SkipHttpErrorCheck = $true; StatusCodeVariable = 'status' }
    if ($null -ne $body) { $params.Body = $body; $params.ContentType = 'application/x-www-form-urlencoded' }
    try { $r = Invoke-RestMethod @params } catch { return [pscustomobject]@{ Status = 0; Body = $null } }
    [pscustomobject]@{ Status = [int]$status; Body = $r }
}
function Bearer($resp) { $resp.Body.access_token ?? $resp.Body.bearer_token ?? $resp.Body.AccessToken }

$form = "grant_type=client_credentials&client_id=$([uri]::EscapeDataString($pub))&client_secret=$([uri]::EscapeDataString($priv))"
$xtcg = @{ 'X-Tcg-Access-Token' = $access }

$plainTok = Call 'POST' '/token' @{} $form
$plain = Bearer $plainTok
$self = @((Call 'GET' '/stores/self' @{ Authorization = "bearer $plain" } $null).Body.results) | Select-Object -First 1
$storeKey = $self.storeKey ?? $self.sellerKey ?? $self.SellerKey
$manifestPath = "/stores/$storeKey/orders/manifest"

$out = [System.Collections.Generic.List[string]]::new()
$out.Add("keys-only token: HTTP $($plainTok.Status); store key found: $([bool]$storeKey)")

function Try-Variant([string] $label, $tokenResp, [hashtable] $extraOnCall) {
    $b = Bearer $tokenResp
    $tokenStatus = if ($tokenResp) { $tokenResp.Status } else { 'n/a' }
    if (-not $b) { $out.Add(('{0,-62} token HTTP {1,-4} (no bearer token returned)' -f $label, $tokenStatus)); return }
    $h = @{ Authorization = "bearer $b" }
    if ($extraOnCall) { $extraOnCall.GetEnumerator() | ForEach-Object { $h[$_.Key] = $_.Value } }
    $m = Call 'GET' $manifestPath $h $null
    $out.Add(('{0,-62} token HTTP {1,-4} orders/manifest HTTP {2}' -f $label, $tokenStatus, $m.Status))
}

$a = Call 'POST' '/token' $xtcg $form
Try-Variant 'A  /token + X-Tcg-Access-Token' $a $null
Try-Variant 'B  /token/access + X-Tcg-Access-Token + keys-only bearer' (Call 'POST' '/token/access' (@{ Authorization = "bearer $plain" } + $xtcg) $null) $null
Try-Variant 'C  /token/access + X-Tcg-Access-Token only' (Call 'POST' '/token/access' $xtcg $null) $null
Try-Variant 'D  /v1.39.0/token + X-Tcg-Access-Token' (Call 'POST' '/v1.39.0/token' $xtcg $form) $null
Try-Variant 'E  keys-only bearer, header on the order call' $plainTok $xtcg
Try-Variant 'F  variant A bearer, header on the order call' $a $xtcg

$out | ForEach-Object { Write-Host $_ }
