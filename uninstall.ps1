# ─────────────────────────────────────────────────────────────────
# Skanyxx Uninstaller — Windows (PowerShell)
# Usage: .\uninstall.ps1 [-All]
#
# Flags:
#   (no flag)   Removes build artifacts only (bin\, obj\, modules\)
#   -All        Also removes config files and the SQLite database
#               ⚠ WARNING: -All deletes all your settings and data
# ─────────────────────────────────────────────────────────────────
param([switch]$All)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$HostDir   = Join-Path $ScriptDir "src\Skanyxx.Host"

function Write-Step { param($msg) Write-Host "`n▶ $msg" -ForegroundColor Green }
function Write-Ok   { param($msg) Write-Host "[✓] $msg" -ForegroundColor Green }
function Write-Warn { param($msg) Write-Host "[!] $msg" -ForegroundColor Yellow }

Write-Host ""
Write-Host "  ╔══════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "  ║     Skanyxx SRE Platform Uninstall   ║" -ForegroundColor Cyan
Write-Host "  ╚══════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# ── Step 1: Remove build artifacts ───────────────────────────────
# Safe — generated files, fully reproducible by running install.ps1 again.
Write-Step "Removing build artifacts (bin\, obj\)..."
Get-ChildItem -Path $ScriptDir -Recurse -Directory -Include 'bin','obj' |
    Where-Object { $_.FullName -notlike '*\.git\*' } |
    ForEach-Object { Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
Write-Ok "Build artifacts removed"

Write-Step "Removing compiled module DLLs from modules\..."
$modulesDir = Join-Path $HostDir "modules"
if (Test-Path $modulesDir) {
    Remove-Item (Join-Path $modulesDir "*.dll")       -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $modulesDir "*.deps.json") -Force -ErrorAction SilentlyContinue
    Write-Ok "Module DLLs removed"
} else {
    Write-Ok "modules\ not found — skipping"
}

# ── Step 2: Optional — remove data and config ─────────────────────
if ($All) {
    Write-Host ""
    Write-Host "  ⚠  -All flag set. This will permanently delete:" -ForegroundColor Red
    Write-Host "     • appsettings.json (your API keys / config)"
    Write-Host "     • src\Skanyxx.Host\appsettings.json"
    Write-Host "     • src\Skanyxx.Host\appsettings.Development.json"
    Write-Host "     • skanyxx.db (all stored data — CANNOT BE RECOVERED)"
    Write-Host ""

    $confirm = Read-Host "  Are you sure? Type 'yes' to confirm"
    if ($confirm -ne "yes") {
        Write-Host "  Aborted."
        exit 0
    }

    Write-Step "Removing configuration files..."
    @(
        (Join-Path $ScriptDir "appsettings.json"),
        (Join-Path $ScriptDir "appsettings.Development.json"),
        (Join-Path $HostDir   "appsettings.json"),
        (Join-Path $HostDir   "appsettings.Development.json"),
        (Join-Path $HostDir   "appsettings.Local.json")
    ) | ForEach-Object { Remove-Item $_ -Force -ErrorAction SilentlyContinue }
    Write-Ok "Configuration files removed"

    # IRREVERSIBLE: Database deletion permanently destroys all stored data.
    Write-Step "Removing database..."
    @("skanyxx.db","skanyxx.db-shm","skanyxx.db-wal") | ForEach-Object {
        Remove-Item (Join-Path $ScriptDir $_) -Force -ErrorAction SilentlyContinue
        Remove-Item (Join-Path $HostDir   $_) -Force -ErrorAction SilentlyContinue
    }
    Write-Ok "Database removed"
}

# ── Done ──────────────────────────────────────────────────────────
Write-Host ""
Write-Host "══════════════════════════════════════════" -ForegroundColor Green
if ($All) {
    Write-Host "  Full uninstall complete." -ForegroundColor Green
} else {
    Write-Host "  Build artifacts cleaned." -ForegroundColor Green
    Write-Host ""
    Write-Host "  Config and database were kept."
    Write-Host "  Run .\install.ps1 to rebuild and start fresh."
    Write-Host "  Run .\uninstall.ps1 -All to remove everything."
}
Write-Host "══════════════════════════════════════════" -ForegroundColor Green
Write-Host ""
