# ─────────────────────────────────────────────────────────────────
# Skanyxx — Start the application (Windows)
# Usage: .\run.ps1 [-Port 5282] [-Env Development]
# ─────────────────────────────────────────────────────────────────
param(
    [int]    $Port = 5282,
    [string] $Env  = "Development"
)

$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Definition
$HostProject = Join-Path $ScriptDir "src\Skanyxx.Host"

Write-Host ""
Write-Host "  Starting Skanyxx SRE Platform..."
Write-Host "  Environment : $Env"
Write-Host "  URL         : http://localhost:$Port"
Write-Host "  Press Ctrl+C to stop."
Write-Host ""

$env:ASPNETCORE_ENVIRONMENT = $Env
dotnet run --project $HostProject --configuration Release --no-build --urls "http://localhost:$Port"
