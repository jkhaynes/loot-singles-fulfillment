<#
.SYNOPSIS
Starts the backend API and frontend dev server for local manual testing.

.DESCRIPTION
Opens two separate PowerShell windows - one running the backend API
(dotnet run, https launch profile - required, since the frontend's Vite
proxy targets https://localhost:7166) and one running the frontend Vite
dev server (http://localhost:5173). Close either window, or Ctrl+C inside
it, to stop that server independently.

Uses whichever database connection string is already configured via
`dotnet user-secrets` for LootSingles.Api - it is not overridden here, so
this points at whatever you've set up (e.g. the Azure dev database).
Run `dotnet user-secrets list` from backend/src/LootSingles.Api to check.
#>

$repoRoot = Split-Path -Parent $PSScriptRoot

Start-Process powershell -ArgumentList @(
    '-NoExit', '-Command',
    "Set-Location '$repoRoot\backend\src\LootSingles.Api'; dotnet run --launch-profile https"
)

Start-Process powershell -ArgumentList @(
    '-NoExit', '-Command',
    "Set-Location '$repoRoot\frontend'; npm run dev"
)

Write-Host "Backend starting at https://localhost:7166 and frontend at http://localhost:5173, each in its own window."
