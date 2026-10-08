<#
.SYNOPSIS
    Read-only probe of the TCGplayer Seller API, for feature 020. A PERSON runs this, never an AI tool.

.DESCRIPTION
    Makes about ten calls with the store's EXISTING credentials and writes a local report describing
    the SHAPE of each response: field paths and JSON types. Values are hidden, except the reference
    vocabulary the plan needs to confirm (research.md §14):
      - order status names from the manifest
      - catalog extendedData field NAMES (not their values)
      - the distinct wording of categoryName, condition, printing, language and rarity on order lines
      - per order, three numbers: productCount, the sum of line quantities and the number of lines
      - which host image URLs point at

    It never writes keys, tokens, order numbers, customer or shipping fields, product names or prices.

    Rules (CLAUDE.md, "TCGplayer API Agreement"):
      - Read-only. The only POST is /token, which exchanges the EXISTING keys for a short-lived bearer
        token. It never calls /app/authorize or anything else that creates credentials.
      - Sends the required User-Agent. About ten calls, far under 300 a minute.
      - Do NOT paste the report into any AI tool. Read it, then describe what you found in your own words.

.PARAMETER OutFile
    Where to write the report. Defaults to your temp folder, outside the repository so it is never
    committed.

.PARAMETER SampleOrders
    How many open orders to sample line items from (default 3).

.PARAMETER OpenStatus
    The status name to search for (default 'Ready to Ship').

.EXAMPLE
    # Keys are read from these environment variables if set, otherwise you are prompted (input hidden).
    $env:TCGPLAYER_PUBLIC_KEY = '...'; $env:TCGPLAYER_PRIVATE_KEY = '...'; $env:TCGPLAYER_ACCESS_TOKEN = '...'
    ./specs/020-tcgplayer-api-import/probe/Probe-Tcgplayer.ps1
#>
[CmdletBinding()]
param(
    [string] $OutFile = (Join-Path ([IO.Path]::GetTempPath()) 'tcgplayer-probe-report.txt'),
    [int] $SampleOrders = 3,
    [string] $OpenStatus = 'Ready to Ship',
    [string] $ApiVersion = 'v1.39.0'
)

$ErrorActionPreference = 'Stop'

$BaseUrl = 'https://api.tcgplayer.com'
$UserAgent = 'LootSinglesFulfillment/0.0.0-probe (Loot Investments LLC)'
$script:CallCount = 0
$report = [System.Collections.Generic.List[string]]::new()

# Values saved by Authorize-Store.ps1 into the API project's user-secrets, read without printing them.
$script:UserSecrets = @{}
try {
    $apiProject = Join-Path $PSScriptRoot '../../../backend/src/LootSingles.Api'
    foreach ($line in (dotnet user-secrets list --project $apiProject 2>$null)) {
        $parts = $line -split ' = ', 2
        if ($parts.Count -eq 2) { $script:UserSecrets[$parts[0]] = $parts[1] }
    }
} catch { }

function Get-Secret([string] $envName, [string] $label, [string] $secretKey, [switch] $Optional) {
    $value = [Environment]::GetEnvironmentVariable($envName)
    if ([string]::IsNullOrWhiteSpace($value)) { $value = $script:UserSecrets[$secretKey] }
    if ($Optional) { return $value }
    if ([string]::IsNullOrWhiteSpace($value)) {
        $secure = Read-Host -Prompt "$label (input hidden)" -AsSecureString
        $value = [System.Net.NetworkCredential]::new('', $secure).Password
    }
    if ([string]::IsNullOrWhiteSpace($value)) { throw "$label is required." }
    return $value
}

function Add-Line([string] $text = '') { $report.Add($text) }

# TCGplayer's envelope carries success and errors. Error strings describe the request (for example
# authorization problems), not order data, so they are shown.
function Add-Outcome($response) {
    $b = $response.Body
    if ($null -eq $b) { Add-Line '   body: empty'; return }
    $results = @($b.results)
    Add-Line "   success = $($b.success); results returned = $($results.Count)"
    foreach ($e in @($b.errors)) { if ($e) { Add-Line "   error: $e" } }
}

# Describe a JSON value's structure as path: type lines, hiding every value.
function Get-Shape($node, [string] $path, [System.Collections.Generic.SortedSet[string]] $into) {
    if ($null -eq $node) { [void]$into.Add("$path : null"); return }
    if ($node -is [System.Management.Automation.PSCustomObject]) {
        [void]$into.Add("$path : object")
        foreach ($p in $node.PSObject.Properties) { Get-Shape $p.Value "$path.$($p.Name)" $into }
        return
    }
    if ($node -is [System.Array] -or $node -is [System.Collections.IList]) {
        [void]$into.Add("$path : array")
        foreach ($item in $node) { Get-Shape $item "$path[]" $into }
        return
    }
    $type = switch ($node) {
        { $_ -is [bool] } { 'boolean'; break }
        { $_ -is [int] -or $_ -is [long] -or $_ -is [double] -or $_ -is [decimal] } { 'number'; break }
        { $_ -is [datetime] } { 'date-time'; break }
        default { 'string' }
    }
    [void]$into.Add("$path : $type")
}

function Write-Shape([string] $title, $body) {
    $set = [System.Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    Get-Shape $body '$' $set
    Add-Line "--- ${title}: response shape (values hidden) ---"
    $set | ForEach-Object { Add-Line "  $_" }
    Add-Line
}

function Invoke-Tcg([string] $method, [string] $path, [hashtable] $headers, $body, [string] $contentType) {
    $script:CallCount++
    $params = @{
        Method             = $method
        Uri                = "$BaseUrl$path"
        Headers            = $headers
        UserAgent          = $UserAgent
        SkipHttpErrorCheck = $true
        StatusCodeVariable = 'status'
    }
    if ($null -ne $body) { $params.Body = $body; $params.ContentType = $contentType }
    $result = Invoke-RestMethod @params
    return [pscustomobject]@{ Status = [int]$status; Body = $result }
}

$publicKey = Get-Secret 'TCGPLAYER_PUBLIC_KEY' 'TCGplayer public key' 'Tcgplayer:PublicKey'
$privateKey = Get-Secret 'TCGPLAYER_PRIVATE_KEY' 'TCGplayer private key' 'Tcgplayer:PrivateKey'
# The store access token is optional. Without it, the token request uses the two keys alone, which shows
# whether the keys reach Loot's store by themselves. To get one, a person runs the Store Authorization
# Workflow once (research.md §1), then sets TCGPLAYER_ACCESS_TOKEN.
$accessToken = Get-Secret 'TCGPLAYER_ACCESS_TOKEN' 'TCGplayer store access token' 'Tcgplayer:AccessToken' -Optional

try {
    Add-Line "TCGplayer read-only probe, $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
    Add-Line 'Do not paste this report into any AI tool. Describe findings in your own words.'
    Add-Line

    # 1. Bearer token from the EXISTING credentials (the only POST).
    $form = "grant_type=client_credentials&client_id=$([uri]::EscapeDataString($publicKey))&client_secret=$([uri]::EscapeDataString($privateKey))"
    $tokenHeaders = @{}
    if (-not [string]::IsNullOrWhiteSpace($accessToken)) { $tokenHeaders['X-Tcg-Access-Token'] = $accessToken }
    $token = Invoke-Tcg 'POST' '/token' $tokenHeaders $form 'application/x-www-form-urlencoded'
    Add-Line "1. POST /token: HTTP $($token.Status) (store access token supplied: $($tokenHeaders.Count -gt 0))"
    if ($token.Status -ne 200) { Add-Line '   Token request failed. Stopping: check the keys, but do NOT create new ones.'; return }
    Write-Shape 'token' ($token.Body | Select-Object * -ExcludeProperty access_token)
    $auth = @{ Authorization = "bearer $($token.Body.access_token)" }
    Add-Line "   token lifetime fields: expires_in present = $($null -ne $token.Body.expires_in); .expires present = $($null -ne $token.Body.'.expires')"
    Add-Line

    # 2. Store identity.
    $self = Invoke-Tcg 'GET' "/$ApiVersion/stores/self" $auth $null $null
    Add-Line "2. GET /stores/self: HTTP $($self.Status)"; Add-Outcome $self
    if ($self.Status -in 401, 403) {
        Add-Line '   The keys alone do not reach a store. Stopping here. Next step:'
        Add-Line '   run the Store Authorization Workflow once (research.md §1), then rerun with TCGPLAYER_ACCESS_TOKEN set.'
        return
    }
    Write-Shape 'stores/self' $self.Body
    $store = @($self.Body.results) | Select-Object -First 1
    $storeKey = if ($store) { $store.storeKey ?? $store.sellerKey ?? $store.SellerKey } else { $null }
    Add-Line "   store key found = $([bool]$storeKey) (value hidden; read it from the shape above if it is under another name)"
    Add-Line
    if (-not $storeKey) {
        Add-Line '   No store came back for these keys. Stopping here. Next step:'
        Add-Line '   check the keys belong to Loot''s seller account.'
        return
    }

    # 3. Manifest: status names are reference data, so they are shown.
    $manifest = Invoke-Tcg 'GET' "/$ApiVersion/stores/$storeKey/orders/manifest" $auth $null $null
    Add-Line "3. GET /orders/manifest: HTTP $($manifest.Status)"; Add-Outcome $manifest
    $statuses = @(@($manifest.Body.results) | Select-Object -First 1 | ForEach-Object { $_.orderStatusTypes })
    Add-Line '   order status names (id: name):'
    $statuses | ForEach-Object { Add-Line "     $($_.id): $($_.name)" }
    $openStatus = $statuses | Where-Object { $_.name -eq $OpenStatus } | Select-Object -First 1
    Add-Line "   '$OpenStatus' found = $([bool]$openStatus)"
    Add-Line
    if (-not $openStatus) { return }

    # 4. Search open orders (order numbers are hidden).
    $search = Invoke-Tcg 'GET' "/$ApiVersion/stores/$storeKey/orders?orderStatusIds=$($openStatus.id)&offset=0&limit=$SampleOrders" $auth $null $null
    Add-Line "4. GET /orders?orderStatusIds=...: HTTP $($search.Status)"; Add-Outcome $search
    Write-Shape 'order search' ($search.Body | Select-Object * -ExcludeProperty results)
    $orderNumbers = @($search.Body.results)
    Add-Line "   totalItems = $($search.Body.totalItems); returned on this page = $($orderNumbers.Count)"
    Add-Line
    if ($orderNumbers.Count -eq 0) { Add-Line 'No open orders right now: rerun when some exist.'; return }

    # 5. Order details: shape only (it contains customer fields), plus productCount.
    $details = Invoke-Tcg 'GET' "/$ApiVersion/stores/$storeKey/orders/$($orderNumbers -join ',')" $auth $null $null
    Add-Line "5. GET /orders/{numbers}: HTTP $($details.Status)"; Add-Outcome $details
    Write-Shape 'order details' $details.Body
    $productCounts = @{}
    foreach ($d in @($details.Body.results)) { $productCounts[$d.orderNumber] = $d.productCount }

    # 6. Line items for each sampled order.
    $vocab = @{ categoryName = @{}; condition = @{}; printing = @{}; language = @{}; rarity = @{} }
    $imageHosts = @{}
    $skuIds = [System.Collections.Generic.List[long]]::new()
    $itemShapeWritten = $false
    $i = 0
    foreach ($n in $orderNumbers) {
        $i++
        $items = Invoke-Tcg 'GET' "/$ApiVersion/stores/$storeKey/orders/$n/items?includeItemDetails=true&offset=0&limit=100" $auth $null $null
        if (-not $itemShapeWritten) { Add-Line "6. GET /orders/{n}/items: HTTP $($items.Status)"; Add-Outcome $items; Write-Shape 'order items' $items.Body; $itemShapeWritten = $true }
        $lines = @($items.Body.results)
        $qtySum = ($lines | Measure-Object -Property quantity -Sum).Sum
        Add-Line "   order $i : productCount = $($productCounts[$n]); sum of line quantities = $qtySum; lines returned = $($lines.Count); items totalItems = $($items.Body.totalItems)"
        foreach ($l in $lines) {
            foreach ($k in @($vocab.Keys)) { $v = $l.$k; if ($v) { $vocab[$k][[string]$v] = $true } }
            if ($l.isFoil) { $vocab['printing']["(isFoil=true with printing '$($l.printing)')"] = $true }
            foreach ($u in @($l.productImageUrl, $l.mainImageUrl)) { if ($u) { $imageHosts[([uri]$u).Host] = $true } }
            if ($l.skuId) { $skuIds.Add([long]$l.skuId) }
        }
    }
    Add-Line
    Add-Line '   distinct wording on order lines (product vocabulary, no customer data):'
    foreach ($k in $vocab.Keys | Sort-Object) { Add-Line "     $k : $((@($vocab[$k].Keys) | Sort-Object) -join ' | ')" }
    Add-Line "   item image hosts: $((@($imageHosts.Keys)) -join ', ')"
    Add-Line

    # 7–8. Catalog: SKU to product, then the product's extendedData names.
    $skuSample = @($skuIds | Select-Object -Unique -First 10)
    if ($skuSample.Count -gt 0) {
        $skus = Invoke-Tcg 'GET' "/$ApiVersion/catalog/skus/$($skuSample -join ',')" $auth $null $null
        Add-Line "7. GET /catalog/skus/{ids}: HTTP $($skus.Status)"; Add-Outcome $skus
        Write-Shape 'catalog skus' $skus.Body
        $productIds = @(@($skus.Body.results) | ForEach-Object { $_.productId } | Select-Object -Unique)
        if ($productIds.Count -gt 0) {
            $products = Invoke-Tcg 'GET' "/$ApiVersion/catalog/products/$($productIds -join ',')?getExtendedFields=true" $auth $null $null
            Add-Line "8. GET /catalog/products/{ids}?getExtendedFields=true: HTTP $($products.Status)"; Add-Outcome $products
            Write-Shape 'catalog products' $products.Body
            $extNames = @{}
            $withNumber = 0
            foreach ($p in @($products.Body.results)) {
                foreach ($e in @($p.extendedData)) { if ($e.name) { $extNames["$($e.name) (displayName: $($e.displayName))"] = $true } }
                if (@($p.extendedData | Where-Object { $_.name -eq 'Number' }).Count -gt 0) { $withNumber++ }
            }
            Add-Line "   extendedData field names seen: $((@($extNames.Keys) | Sort-Object) -join ' | ')"
            Add-Line "   products with a 'Number' entry: $withNumber of $(@($products.Body.results).Count)"
            $prodHosts = @{}; foreach ($p in @($products.Body.results)) { if ($p.imageUrl) { $prodHosts[([uri]$p.imageUrl).Host] = $true } }
            Add-Line "   product image hosts: $((@($prodHosts.Keys)) -join ', ')"
        }
    }
    Add-Line
    Add-Line "Total TCGplayer calls made: $script:CallCount"
}
catch {
    Add-Line
    Add-Line "PROBE FAILED at script line $($_.InvocationInfo.ScriptLineNumber): $($_.Exception.Message)"
}
finally {
    $report | Set-Content -Path $OutFile -Encoding utf8
    Write-Host "Probe stopped after $script:CallCount calls. Report written to $OutFile"
    Write-Host 'Read it yourself; do not paste it into an AI tool.'
}
