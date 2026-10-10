<#
.SYNOPSIS
    Counts-only diagnostic for the order-status filter, feature 020. A PERSON runs this, never an AI tool.

.DESCRIPTION
    The app's first live import pulled far more orders than the store has open, so the order search
    is not filtering by status the way the app assumes. This script asks TCGplayer the same search in
    a few ways and prints ONLY numbers, status names and field names, never order data:
      - the manifest's status ids and names, and the names of the manifest's other lists
      - totalItems for the search with no filter, with the app's filter, and with alternative spellings
      - for the first page of the app's filtered search, how many orders of each status the details say

    Safe to paste: every line it prints is a count, a status name or a field name. About 15 calls.
    Read-only: the only POST is /token, which exchanges the EXISTING keys for a short-lived token.

.EXAMPLE
    ./specs/020-tcgplayer-api-import/probe/Count-OpenOrders.ps1
#>
[CmdletBinding()]
param(
    [string] $OpenStatus = 'Ready To Ship',
    [string] $ApiVersion = 'v1.39.0'
)

$ErrorActionPreference = 'Stop'
$BaseUrl = 'https://api.tcgplayer.com'
$Prefix = "/$ApiVersion"
$UserAgent = 'LootSinglesFulfillment/0.0.0-probe (Loot Investments LLC)'

$secrets = @{}
$apiProject = Join-Path $PSScriptRoot '../../../backend/src/LootSingles.Api'
foreach ($line in (dotnet user-secrets list --project $apiProject 2>$null)) {
    $parts = $line -split ' = ', 2
    if ($parts.Count -eq 2) { $secrets[$parts[0]] = $parts[1] }
}
foreach ($name in 'Tcgplayer:PublicKey', 'Tcgplayer:PrivateKey') {
    if ([string]::IsNullOrWhiteSpace($secrets[$name])) { throw "$name is not in the API project's user-secrets." }
}

function Invoke-Tcg([string] $method, [string] $path, [hashtable] $headers, $body, [string] $contentType) {
    $params = @{
        Method = $method; Uri = "$BaseUrl$path"; Headers = $headers; UserAgent = $UserAgent
        SkipHttpErrorCheck = $true; StatusCodeVariable = 'status'
    }
    if ($null -ne $body) { $params.Body = $body; $params.ContentType = $contentType }
    $result = Invoke-RestMethod @params
    [pscustomobject]@{ Status = [int]$status; Body = $result }
}

$form = "grant_type=client_credentials&client_id=$([uri]::EscapeDataString($secrets['Tcgplayer:PublicKey']))&client_secret=$([uri]::EscapeDataString($secrets['Tcgplayer:PrivateKey']))"
$tokenHeaders = @{}
if ($secrets['Tcgplayer:AccessToken']) { $tokenHeaders['X-Tcg-Access-Token'] = $secrets['Tcgplayer:AccessToken'] }
$token = Invoke-Tcg 'POST' '/token' $tokenHeaders $form 'application/x-www-form-urlencoded'
if ($token.Status -ne 200) { throw "Token request failed: HTTP $($token.Status)" }
$auth = @{ Authorization = "bearer $($token.Body.access_token)" }

$self = Invoke-Tcg 'GET' "$Prefix/stores/self" $auth $null $null
$first = @($self.Body.results)[0]
"stores/self result field names: $(($first.PSObject.Properties.Name) -join ', ')"
$storeKey = $first.storeKey ?? $first.sellerKey ?? $first.SellerKey
if (-not $storeKey) { throw 'No store key in /stores/self.' }

$manifest = Invoke-Tcg 'GET' "$Prefix/stores/$storeKey/orders/manifest" $auth $null $null
$m = @($manifest.Body.results)[0]
"manifest: HTTP $($manifest.Status); lists: " + (($m.PSObject.Properties | ForEach-Object { "$($_.Name) ($(@($_.Value).Count))" }) -join ', ')
$statuses = @($m.orderStatusTypes)
# Every other manifest list (for example search ranges or sort options) by its entries' names only.
foreach ($p in $m.PSObject.Properties | Where-Object { $_.Name -ne 'orderStatusTypes' }) {
    $entries = @($p.Value) | ForEach-Object { if ($_.PSObject.Properties['name']) { "$($_.id)=$($_.name)" } else { "$_" } }
    "  $($p.Name): $($entries -join '; ')"
}
'status ids and names:'
$statuses | ForEach-Object { "  $($_.id) = $($_.name)" }

function Total([string] $label, [string] $query) {
    $r = Invoke-Tcg 'GET' "$Prefix/stores/$storeKey/orders?$query" $auth $null $null
    $errors = (@($r.Body.errors) | Where-Object { $_ }) -join ' | '
    Write-Host ('{0,-78} HTTP {1}  totalItems = {2}  {3}' -f $label, $r.Status, $r.Body.totalItems, $errors)
    return [int]($r.Body.totalItems ?? -1)
}
function Ids($list, [string[]] $names) {
    @(@($list) | Where-Object { $names -contains $_.name } | ForEach-Object { $_.id }) -join ','
}

# Documented Search Orders filters (docs.tcgplayer.com/reference/stores_getstoreorders-1), each a
# comma-separated id list, and the manifest list its ids come from.
$filters = [ordered]@{
    orderStatusIds   = 'orderStatusTypes'
    deliveryTypeIds  = 'orderDeliveryTypes'
    channelIds       = 'orderChannelTypes'
    orderTypeIds     = 'orderTypes'
    pickupStatusIds  = 'orderPickupStatusTypes'
    presaleStatusIds = 'orderPresaleStatusTypes'
}

''
'order search totals, each filter alone with each id (limit=1, one call each):'
[void](Total 'no filter' 'offset=0&limit=1')
if ($env:TCG_COUNT_SINGLES) { foreach ($param in $filters.Keys) {
    foreach ($entry in @($m.($filters[$param]))) {
        [void](Total "$param=$($entry.id) ($($entry.name))" "$param=$($entry.id)&offset=0&limit=1")
    }
} }

# The seller portal's open-orders view (2026-10-10): fulfillment Normal or In-Store Pickup; status
# Processing or Ready to Ship for online orders, Received for in-store pickup.
$statusIds = Ids $m.orderStatusTypes 'Processing', 'Ready To Ship'
$typeIds = Ids $m.orderTypes 'Normal'
$pickupIds = Ids $m.orderPickupStatusTypes 'Received'
''
"portal ids: orderStatusIds=$statusIds  orderTypeIds=$typeIds  pickupStatusIds=$pickupIds"
# Finding from the previous run: orderStatusIds filters only shipped orders and leaves every in-store
# pickup order in; pickupStatusIds filters only pickup orders and leaves every shipped order in. So the
# two together should give (shipped orders in the statuses) + (pickup orders in the pickup status).
'portal-style combinations:'
$combos = [ordered]@{
    'statuses + pickup Received'               = "orderStatusIds=$statusIds&pickupStatusIds=$pickupIds"
    'statuses + pickup Received + Normal type' = "orderStatusIds=$statusIds&pickupStatusIds=$pickupIds&orderTypeIds=$typeIds"
}
$totals = @{}
foreach ($k in $combos.Keys) { $totals[$k] = Total $k "$($combos[$k])&offset=0&limit=1" }

# What the main combination really returns: every order's own status, delivery, channel, type and
# pickup status, as counts per combination (all pages if 300 or fewer, else the first 300).
$lookup = @{}
foreach ($p in $m.PSObject.Properties) {
    $map = @{}; @($p.Value) | Where-Object { $_ -and $_.PSObject.Properties['id'] } | ForEach-Object { $map[[string]$_.id] = $_.name }
    $lookup[$p.Name.ToLowerInvariant()] = $map
}
function Named($row, [string] $field) {
    $v = [string]$row.$field
    $map = $lookup[($field -replace 'Id$', 's').ToLowerInvariant()]
    if ($map -and $map.ContainsKey($v)) { "$($field -replace 'TypeId$|Id$','')=$($map[$v])" } else { "$field=$v" }
}
function Profile([string] $label, [string] $query, [int] $total) {
    ''
    "$label (totalItems $total): own fields of the returned orders, counts only"
    $numbers = @()
    for ($offset = 0; $offset -lt [Math]::Min($total, 300); $offset += 50) {
        $numbers += @((Invoke-Tcg 'GET' "$Prefix/stores/$storeKey/orders?$query&offset=$offset&limit=50" $auth $null $null).Body.results)
    }
    $rows = @()
    for ($i = 0; $i -lt $numbers.Count; $i += 50) {
        $batch = $numbers[$i..([Math]::Min($i + 49, $numbers.Count - 1))] -join ','
        $rows += @((Invoke-Tcg 'GET' "$Prefix/stores/$storeKey/orders/$([uri]::EscapeDataString($batch))" $auth $null $null).Body.results)
    }
    if ($rows.Count -eq 0) { '  no orders'; return }
    $fields = 'orderStatusTypeId', 'orderDeliveryTypeId', 'orderChannelTypeId', 'orderTypeId', 'orderPickupStatusTypeId' |
        Where-Object { $f = $_; @($rows | Where-Object { $_.PSObject.Properties[$f] }).Count -gt 0 }
    $rows | ForEach-Object { $row = $_; ($fields | ForEach-Object { Named $row $_ }) -join ' | ' } |
        Group-Object | Sort-Object Count -Descending | ForEach-Object { '  {0,4} orders: {1}' -f $_.Count, $_.Name }
    $rows | Group-Object { ([datetime]$_.orderedOn).ToString('yyyy-MM') } | Sort-Object Name |
        ForEach-Object { '  ordered {0}: {1}' -f $_.Name, $_.Count }
}
Profile 'statuses + pickup Received + Normal type' $combos['statuses + pickup Received + Normal type'] $totals['statuses + pickup Received + Normal type']
