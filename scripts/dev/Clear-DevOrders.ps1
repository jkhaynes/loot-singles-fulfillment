<#
.SYNOPSIS
DEV ONLY. Deletes every order from a development database so the import can be re-tested.

.DESCRIPTION
Deletes all orders and everything that hangs off an order: order lines, picking issues,
packing slips and their access log, import attempts and their per-order results. Claims,
pick outcomes and pack records are columns on those rows, so they go too. Employees (and
their PIN hashes), employee audit events and data-protection keys are left alone.

The deletes live in clear-dev-orders.sql next to this script and run in one transaction.
ClearDevOrders.cs (a .NET file-based app, run with `dotnet run`) executes that SQL, because
Microsoft.Data.SqlClient accepts the Entra connection strings that PowerShell's built-in
System.Data.SqlClient rejects.

The connection string is found the way the API finds it in Development: -ConnectionString if
given, otherwise ConnectionStrings:LootSingles from the API project's user-secrets, otherwise
the LocalDB string in appsettings.Development.json. Only the server and database names are
printed, never the string itself.

Refuses to run unless "dev" is a whole word of the database name (any case; split by - _ or .)
or the server is LocalDB. The runner checks the same rule again against what the server reports.
-Force skips the typed confirmation, never that check.

.PARAMETER ConnectionString
The database to clear. Defaults to the API's Development connection string (see above).

.PARAMETER Force
Skip typing the database name to confirm. The dev-database check still applies.

.EXAMPLE
pwsh -File scripts/dev/Clear-DevOrders.ps1
#>
param(
    [string]$ConnectionString,
    [switch]$Force
)

# Same rule as ClearDevOrders.cs, which re-checks what the server reports after connecting:
# "dev" as a whole word of the database name (separated by - _ . or the ends), or a LocalDB server.
function Test-DevDatabase {
    param([string]$Server, [string]$Database)
    $isLocalDb = $Server -match '^\s*(np:)?\(localdb\)'
    $isDevName = $Database -match '(^|[-_.])dev([-_.]|$)'
    return [bool]($isLocalDb -or $isDevName)
}

function Get-ConnectionTarget {
    param([string]$Value)
    # The generic builder parses any keyword set, including Entra's "Authentication=...". Unlike
    # SqlClient it keeps synonyms (Database / Initial Catalog) as separate keys, so a string that
    # names the target twice with different values is refused rather than guessed at.
    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $builder.set_ConnectionString($Value)
    $pick = {
        param([string]$What, [string[]]$Synonyms)
        $values = @($Synonyms | Where-Object { $builder.ContainsKey($_) } | ForEach-Object { [string]$builder[$_] } | Select-Object -Unique)
        if ($values.Count -gt 1) { throw "the connection string names the $What more than once, with different values" }
        return $values | Select-Object -First 1
    }
    $server = & $pick 'server' @('Server', 'Data Source', 'Address', 'Addr', 'Network Address')
    $database = & $pick 'database' @('Database', 'Initial Catalog')
    return [pscustomobject]@{ Server = $server; Database = $database }
}

# Dot-sourcing (as the tests do) loads the functions above and stops here.
if ($MyInvocation.InvocationName -eq '.') { return }

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$apiProject = Join-Path $repoRoot 'backend/src/LootSingles.Api'

$source = '-ConnectionString'
if (-not $ConnectionString) {
    $source = 'API user-secrets'
    $secretLine = & dotnet user-secrets list --project $apiProject 2>$null |
        Where-Object { $_ -like 'ConnectionStrings:LootSingles = *' } |
        Select-Object -First 1
    if ($secretLine) {
        $ConnectionString = $secretLine.Substring('ConnectionStrings:LootSingles = '.Length)
    }
}
if (-not $ConnectionString) {
    $source = 'appsettings.Development.json'
    $settings = Get-Content (Join-Path $apiProject 'appsettings.Development.json') -Raw | ConvertFrom-Json
    $ConnectionString = $settings.ConnectionStrings.LootSingles
}
if (-not $ConnectionString) {
    Write-Host 'No connection string found.' -ForegroundColor Red
    exit 1
}

try {
    $target = Get-ConnectionTarget $ConnectionString
} catch {
    # Only the message of our own throw, or the builder's parse error; never the string itself.
    $reason = if ($_.Exception.Message -like 'the connection string names*') { $_.Exception.Message } else { 'the connection string could not be parsed' }
    Write-Host "Refusing: $reason." -ForegroundColor Red
    exit 1
}

Write-Host "Connection string from: $source"
Write-Host "Server:   $($target.Server)"
Write-Host "Database: $($target.Database)"

if (-not (Test-DevDatabase -Server $target.Server -Database $target.Database)) {
    Write-Host "Refusing: 'dev' is not a word of the database name and the server is not LocalDB. This script only clears development databases." -ForegroundColor Red
    exit 1
}

$sqlPath = Join-Path $PSScriptRoot 'clear-dev-orders.sql'
$runner = Join-Path $PSScriptRoot 'ClearDevOrders.cs'

function Invoke-ClearSql {
    param([string]$Mode)
    & dotnet run --file $runner -- $Mode $sqlPath
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Stopped: the $Mode step failed. If it failed before committing, its transaction rolled back. Re-run to see the current counts." -ForegroundColor Red
        exit 1
    }
}

$env:LOOT_CLEAR_DEV_ORDERS_CONNECTION = $ConnectionString
try {
    Write-Host ''
    Write-Host 'Rows that will be deleted (counted in a transaction that is rolled back):'
    Invoke-ClearSql -Mode preview

    if (-not $Force) {
        Write-Host ''
        $typed = Read-Host "Type the database name ($($target.Database)) to delete these rows"
        if ($typed -cne $target.Database) {
            Write-Host 'Name did not match. Nothing was deleted.' -ForegroundColor Yellow
            exit 1
        }
    }

    Write-Host ''
    Write-Host 'Deleted:'
    Invoke-ClearSql -Mode delete
    Write-Host 'Done. Employees, audit events and data-protection keys were kept.' -ForegroundColor Green
} finally {
    Remove-Item Env:LOOT_CLEAR_DEV_ORDERS_CONNECTION -ErrorAction SilentlyContinue
}
