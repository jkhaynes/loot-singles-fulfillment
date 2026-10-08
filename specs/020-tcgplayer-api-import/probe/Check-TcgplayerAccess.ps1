<#
.SYNOPSIS
    Lists which TCGplayer API endpoints Loot's app is allowed to use. A PERSON runs this.

.DESCRIPTION
    Calls every documented read-only endpoint (TCGplayer docs v1.39.0) once and records only its HTTP
    status and TCGplayer's error text, never response data. The report is therefore safe to share.

    - 401 or 403            not authorized
    - 200, 207, 400 or 404  authorized: the request got past the permission check, even when a
                            placeholder id matched nothing
    - anything else         unclear (for example TCGplayer briefly down); rerun

    Endpoints that change data (price, quantity, buylist and status updates, adding tracking, creating
    product lists, /app/authorize) are NOT called. They are listed at the end so you can ask about them.

    Credentials come from environment variables or the API project's user-secrets, the same as
    Probe-Tcgplayer.ps1. About 75 calls, paced at about 3 a second (far under 300 a minute).
#>
[CmdletBinding()]
param(
    [string] $OutFile = (Join-Path ([IO.Path]::GetTempPath()) 'tcgplayer-access-report.txt')
)

$ErrorActionPreference = 'Stop'
$BaseUrl = 'https://api.tcgplayer.com'
$UserAgent = 'LootSinglesFulfillment/0.0.0-probe (Loot Investments LLC)'

$secrets = @{}
try {
    foreach ($line in (dotnet user-secrets list --project (Join-Path $PSScriptRoot '../../../backend/src/LootSingles.Api') 2>$null)) {
        $parts = $line -split ' = ', 2
        if ($parts.Count -eq 2) { $secrets[$parts[0]] = $parts[1] }
    }
} catch { }
function Get-Value([string] $envName, [string] $secretKey) {
    $v = [Environment]::GetEnvironmentVariable($envName)
    if ([string]::IsNullOrWhiteSpace($v)) { $v = $secrets[$secretKey] }
    return $v
}
$publicKey = Get-Value 'TCGPLAYER_PUBLIC_KEY' 'Tcgplayer:PublicKey'
$privateKey = Get-Value 'TCGPLAYER_PRIVATE_KEY' 'Tcgplayer:PrivateKey'
$accessToken = Get-Value 'TCGPLAYER_ACCESS_TOKEN' 'Tcgplayer:AccessToken'
if (-not $publicKey -or -not $privateKey) { throw 'Public and private keys not found in environment variables or user-secrets.' }

function Invoke-Tcg([string] $method, [string] $path, [hashtable] $headers, $body, [string] $contentType) {
    Start-Sleep -Milliseconds 350
    $params = @{ Method = $method; Uri = "$BaseUrl$path"; Headers = $headers; UserAgent = $UserAgent
                 SkipHttpErrorCheck = $true; StatusCodeVariable = 'status' }
    if ($null -ne $body) { $params.Body = $body; $params.ContentType = $contentType }
    try { $result = Invoke-RestMethod @params } catch { return [pscustomobject]@{ Status = 0; Body = $null; Error = $_.Exception.Message } }
    return [pscustomobject]@{ Status = [int]$status; Body = $result; Error = $null }
}

# Bearer token, with the store access token when one is saved.
$form = "grant_type=client_credentials&client_id=$([uri]::EscapeDataString($publicKey))&client_secret=$([uri]::EscapeDataString($privateKey))"
$tokenHeaders = @{}
if ($accessToken) { $tokenHeaders['X-Tcg-Access-Token'] = $accessToken }
$token = Invoke-Tcg 'POST' '/token' $tokenHeaders $form 'application/x-www-form-urlencoded'
if ($token.Status -ne 200) { throw "Token request failed: HTTP $($token.Status)" }
$auth = @{ Authorization = "bearer $($token.Body.access_token)" }

function First($response) { @($response.Body.results) | Select-Object -First 1 }

# Real ids where they can be looked up; placeholders otherwise (a 404 on a placeholder still proves access).
$self = First (Invoke-Tcg 'GET' '/stores/self' $auth $null $null)
$storeKey = $self.storeKey ?? $self.sellerKey ?? $self.SellerKey
if (-not $storeKey) { throw 'Could not read the store key from /stores/self.' }
$category = First (Invoke-Tcg 'GET' '/catalog/categories?limit=1' $auth $null $null)
$categoryId = $category.categoryId ?? 1
$group = First (Invoke-Tcg 'GET' "/catalog/categories/$categoryId/groups?limit=1" $auth $null $null)
$groupId = $group.groupId ?? 1
$product = First (Invoke-Tcg 'GET' "/catalog/products?categoryId=$categoryId&groupId=$groupId&limit=1" $auth $null $null)
$productId = $product.productId ?? 1
$sku = First (Invoke-Tcg 'GET' "/catalog/products/$productId/skus" $auth $null $null)
$skuId = $sku.skuId ?? 1

$id = @{
    storeKey = $storeKey; categoryId = $categoryId; groupId = $groupId; productId = $productId; skuId = $skuId
    gtin = '000000000000'; productListId = '0'; productListKey = 'none'; token = 'none'; orderNumber = '0'
    skuListPriceId = '0'
}

# Every non-changing endpoint from the docs: Section, Title, Method, Path.
$endpoints = @(
    ,@('Catalog', 'List All Categories', 'GET', '/catalog/categories')
    ,@('Catalog', 'Get Category Details', 'GET', '/catalog/categories/{categoryId}')
    ,@('Catalog', 'Get Category Search Manifest', 'GET', '/catalog/categories/{categoryId}/search/manifest')
    ,@('Catalog', 'Search Category Products (search)', 'POST', '/catalog/categories/{categoryId}/search')
    ,@('Catalog', 'List All Category Groups', 'GET', '/catalog/categories/{categoryId}/groups')
    ,@('Catalog', 'List All Category Rarities', 'GET', '/catalog/categories/{categoryId}/rarities')
    ,@('Catalog', 'List All Category Printings', 'GET', '/catalog/categories/{categoryId}/printings')
    ,@('Catalog', 'List All Category Conditions', 'GET', '/catalog/categories/{categoryId}/conditions')
    ,@('Catalog', 'List All Category Languages', 'GET', '/catalog/categories/{categoryId}/languages')
    ,@('Catalog', 'List All Category Media', 'GET', '/catalog/categories/{categoryId}/media')
    ,@('Catalog', 'List All Groups Details', 'GET', '/catalog/groups')
    ,@('Catalog', 'Get Group Details', 'GET', '/catalog/groups/{groupId}')
    ,@('Catalog', 'List All Group Media', 'GET', '/catalog/groups/{groupId}/media')
    ,@('Catalog', 'List All Products', 'GET', '/catalog/products')
    ,@('Catalog', 'Get Product Details', 'GET', '/catalog/products/{productId}')
    ,@('Catalog', 'Get Product Details By GTIN', 'GET', '/catalog/products/gtin/{gtin}')
    ,@('Catalog', 'List Product SKUs', 'GET', '/catalog/products/{productId}/skus')
    ,@('Catalog', 'List Related Products', 'GET', '/catalog/products/{productId}/productsalsopurchased')
    ,@('Catalog', 'List All Product Media Types', 'GET', '/catalog/products/{productId}/media')
    ,@('Catalog', 'Get SKU details', 'GET', '/catalog/skus/{skuId}')
    ,@('Catalog', 'List Conditions', 'GET', '/catalog/conditions')
    ,@('Inventory', 'Get ProductList By Id', 'GET', '/inventory/productlists/{productListId}')
    ,@('Inventory', 'Get ProductList By Key', 'GET', '/inventory/productlists/{productListKey}')
    ,@('Inventory', 'List All ProductLists', 'GET', '/inventory/productLists')
    ,@('Pricing', 'Get Market Price by SKU', 'GET', '/pricing/marketprices/{skuId}')
    ,@('Pricing', 'List Product Prices by Group', 'GET', '/pricing/group/{groupId}')
    ,@('Pricing', 'List Product Market Prices', 'GET', '/pricing/product/{productId}')
    ,@('Pricing', 'List SKU Market Prices', 'GET', '/pricing/sku/{skuId}')
    ,@('Pricing', 'List Product Buylist Prices', 'GET', '/pricing/buy/product/{productId}')
    ,@('Pricing', 'List SKU Buylist Prices', 'GET', '/pricing/buy/sku/{skuId}')
    ,@('Pricing', 'List Product Buylist Prices by Group', 'GET', '/pricing/buy/group/{groupId}')
    ,@('Stores', 'Get Buylist Categories', 'GET', '/stores/{storeKey}/buylist/categories')
    ,@('Stores', 'Get Buylist Groups', 'GET', '/stores/{storeKey}/buylist/groups')
    ,@('Stores', 'Get Store Buylist Settings', 'GET', '/stores/{storeKey}/buylist/settings')
    ,@('Stores', 'Get Buylist Products (kiosk)', 'GET', '/stores/{storeKey}/buylist/products')
    ,@('Stores', 'Get Buylist Product Conditions', 'GET', '/stores/{storeKey}/buylist/{productId}')
    ,@('Stores', 'Search Stores', 'GET', '/stores')
    ,@('Stores', 'Get Free Shipping Option', 'GET', '/stores/{storeKey}/freeshipping/settings')
    ,@('Stores', 'Get Store Address', 'GET', '/stores/{storeKey}/address')
    ,@('Stores', 'Get Store Feedback', 'GET', '/stores/{storeKey}/feedback')
    ,@('Stores', 'Get Customer Summary', 'GET', '/stores/{storeKey}/customers/{token}')
    ,@('Stores', 'Search Store Customers', 'GET', '/stores/{storeKey}/customers')
    ,@('Stores', 'Get Customer Addresses', 'GET', '/stores/{storeKey}/customers/{token}/addresses')
    ,@('Stores', 'Get Customer Orders', 'GET', '/stores/{storeKey}/customers/{token}/orders')
    ,@('Stores', 'Get Store Info (self)', 'GET', '/stores/self')
    ,@('Stores', 'Get Store Info (by key)', 'GET', '/stores/{storeKey}')
    ,@('Stores', 'Get Product Inventory Quantities', 'GET', '/stores/{storeKey}/inventory/products/{productId}/quantity')
    ,@('Stores', 'List Product Summary', 'GET', '/stores/{storeKey}/inventory/products')
    ,@('Stores', 'List Product SKUs (store)', 'GET', '/stores/{storeKey}/inventory/products/{productId}/skus')
    ,@('Stores', 'List Related Products (store)', 'GET', '/stores/{storeKey}/inventory/products/{productId}/relatedproducts')
    ,@('Stores', 'List Shipping Options', 'GET', '/stores/{storeKey}/inventory/products/{productId}/shippingoptions')
    ,@('Stores', 'Get SKU Quantity', 'GET', '/stores/{storeKey}/inventory/skus/{skuId}/quantity')
    ,@('Stores', 'List SKU List Price', 'GET', '/stores/{storeKey}/inventory/skuprices')
    ,@('Stores', 'Get SKU List Price', 'GET', '/stores/{storeKey}/inventory/skuprices/{skuListPriceId}')
    ,@('Stores', 'List All Groups (store)', 'GET', '/stores/{storeKey}/inventory/groups')
    ,@('Stores', 'List All Categories (store)', 'GET', '/stores/{storeKey}/inventory/categories')
    ,@('Stores', 'List Product Summary By Category (search)', 'POST', '/stores/{storeKey}/inventory/categories/{categoryId}/search')
    ,@('Stores', 'List Store Channels', 'GET', '/stores/{storeKey}/inventory/channels')
    ,@('Stores', 'List Top Sold Products', 'GET', '/stores/{storeKey}/inventory/topsales')
    ,@('Stores', 'Search Top Sold Products (search)', 'POST', '/stores/{storeKey}/inventory/topsalessearch')
    ,@('Stores', 'List Catalog Objects', 'GET', '/stores/{storeKey}/inventory/search?q=a')
    ,@('Stores', 'Search Custom Listings', 'GET', '/stores/{storeKey}/inventory/customListings?photoId=0')
    ,@('Orders', 'Get Order Manifest', 'GET', '/stores/{storeKey}/orders/manifest')
    ,@('Orders', 'Search Orders', 'GET', '/stores/{storeKey}/orders?limit=1')
    ,@('Orders', 'Get Order Details', 'GET', '/stores/{storeKey}/orders/{orderNumber}')
    ,@('Orders', 'Get Order Items', 'GET', '/stores/{storeKey}/orders/{orderNumber}/items')
    ,@('Orders', 'Get Order Feedback', 'GET', '/stores/{storeKey}/orders/{orderNumber}/feedback')
    ,@('Orders', 'Get Order Tracking Numbers', 'GET', '/stores/{storeKey}/orders/{orderNumber}/tracking')
)

$notTested = @(
    'POST /app/authorize/{authCode}                               Authorize an Application',
    'POST /inventory/productLists                                 Create ProductList',
    'POST /stores/{storeKey}/buylist/skus/batch                   Batch Update Store Buylist Prices',
    'PUT  /stores/{storeKey}/buylist/skus/{skuId}                 Create SKU Buylist',
    'PUT  /stores/{storeKey}/buylist/skus/{skuId}/price           Update SKU Buylist Price',
    'PUT  /stores/{storeKey}/buylist/skus/{skuId}/quantity        Update SKU Buylist Quantity',
    'PUT  /stores/{storeKey}/status/{status}                      Set Store Status',
    'POST /stores/{storeKey}/inventory/skus/{skuId}/quantity      Increment SKU Inventory Quantity',
    'PUT  /stores/{storeKey}/inventory/skus/{skuId}               Update SKU inventory',
    'POST /stores/{storeKey}/inventory/skus/batch                 Batch Update Store Sku Prices',
    'PUT  /stores/{storeKey}/inventory/skus/{skuId}/price         Update SKU Inventory Price',
    'POST /stores/{storeKey}/orders/{orderNumber}/tracking        Add Order Tracking Number (also changes order status)'
)

$rows = foreach ($e in $endpoints) {
    $section, $title, $method, $template = $e
    $path = $template
    foreach ($k in $id.Keys) { $path = $path.Replace("{$k}", [uri]::EscapeDataString([string]$id[$k])) }
    $body = if ($method -eq 'POST') { '{}' } else { $null }
    $r = Invoke-Tcg $method $path $auth $body 'application/json'
    $result = switch ($r.Status) {
        { $_ -in 401, 403 } { 'NOT AUTHORIZED'; break }
        { $_ -in 200, 207, 400, 404 } { 'authorized'; break }
        default { 'unclear' }
    }
    $err = if ($r.Error) { $r.Error } else { (@($r.Body.errors) | Where-Object { $_ }) -join '; ' }
    [pscustomobject]@{ Section = $section; Endpoint = $title; Method = $method; Path = ($template -replace '\?.*$', '')
                       Http = $r.Status; Result = $result; Error = $err }
}

$out = [System.Collections.Generic.List[string]]::new()
$out.Add("TCGplayer API access check, $(Get-Date -Format 'yyyy-MM-dd HH:mm'). Store access token used: $([bool]$accessToken)")
$out.Add('Statuses and TCGplayer error text only; no response data. Safe to share.')
$out.Add('')
foreach ($label in 'NOT AUTHORIZED', 'unclear', 'authorized') {
    $set = @($rows | Where-Object Result -eq $label)
    $out.Add("== $label ($($set.Count)) ==")
    foreach ($r in $set) {
        $line = '{0,-9} {1,-4} {2,-66} {3,-42} HTTP {4}' -f $r.Section, $r.Method, $r.Path, $r.Endpoint, $r.Http
        if ($r.Error -and $label -ne 'authorized') { $line += "  ($($r.Error))" }
        $out.Add($line)
    }
    $out.Add('')
}
$out.Add('== NOT TESTED: these change data ==')
$notTested | ForEach-Object { $out.Add($_) }

$out | Set-Content -Path $OutFile -Encoding utf8
Write-Host "Checked $($rows.Count) endpoints. Report: $OutFile"
