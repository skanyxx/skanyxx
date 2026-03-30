# ─────────────────────────────────────────────────────────────────
# Skanyxx Installer — Windows (PowerShell)
# Sets up the Skanyxx SRE Platform on Windows
# Usage: .\install.ps1
# ─────────────────────────────────────────────────────────────────
$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$HostDir   = Join-Path $ScriptDir "src\Skanyxx.Host"
$Template  = Join-Path $ScriptDir "appsettings.template.json"
$Sln       = Join-Path $ScriptDir "Skanyxx.sln"

function Write-Step  { param($msg) Write-Host "`n▶ $msg" -ForegroundColor Green }
function Write-Ok    { param($msg) Write-Host "[✓] $msg" -ForegroundColor Green }
function Write-Warn  { param($msg) Write-Host "[!] $msg" -ForegroundColor Yellow }
function Write-Fail  { param($msg) Write-Host "[✗] $msg" -ForegroundColor Red; exit 1 }

Write-Host ""
Write-Host "  ╔══════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "  ║     Skanyxx SRE Platform Install     ║" -ForegroundColor Cyan
Write-Host "  ╚══════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# ── Step 1: Check prerequisites ──────────────────────────────────
Write-Step "Checking prerequisites..."

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Fail ".NET SDK not found. Install 8.0+ from https://dotnet.microsoft.com/download"
}
$dotnetVersion = (dotnet --version)
$dotnetMajor   = [int]($dotnetVersion -split '\.')[0]
if ($dotnetMajor -lt 8) {
    Write-Fail ".NET SDK 8.0+ required. Installed: $dotnetVersion"
}
Write-Ok ".NET SDK $dotnetVersion"

# ── Step 2: Create configuration files ───────────────────────────
Write-Step "Setting up configuration files..."

function Create-Config {
    param([string]$Dest, [string]$Label)
    if (Test-Path $Dest) {
        Write-Ok "$Label already exists — skipping"
    } else {
        Copy-Item $Template $Dest
        Write-Warn "$Label created from template → edit it and add your API keys"
    }
}

Create-Config (Join-Path $ScriptDir "appsettings.json")                       "appsettings.json (root)"
Create-Config (Join-Path $HostDir   "appsettings.json")                       "src\Skanyxx.Host\appsettings.json"
Create-Config (Join-Path $HostDir   "appsettings.Development.json")           "src\Skanyxx.Host\appsettings.Development.json"

# ── Step 3: Restore NuGet packages ───────────────────────────────
Write-Step "Restoring NuGet packages..."
dotnet restore $Sln
if ($LASTEXITCODE -ne 0) { Write-Fail "Package restore failed" }
Write-Ok "Packages restored"

# ── Step 4: Build the solution ────────────────────────────────────
# IRREVERSIBLE NOTE: The CopyToModules MSBuild target in each module's .csproj
# copies compiled DLLs into src\Skanyxx.Host\modules\. Re-building is safe —
# files are overwritten — but first build creates the modules\ directory.
Write-Step "Building solution (all 13 modules)..."
dotnet build $Sln --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { Write-Fail "Build failed" }
Write-Ok "Build complete"
Write-Ok "Module DLLs copied to: $HostDir\modules\"

# ── Step 5: Verify modules ────────────────────────────────────────
Write-Step "Verifying modules..."
$modulesDir = Join-Path $HostDir "modules"
if (Test-Path $modulesDir) {
    $count = (Get-ChildItem $modulesDir -Filter "*.dll").Count
    Write-Ok "$count module DLL(s) found in modules\"
} else {
    Write-Warn "modules\ directory not found — build may have issues"
}

# ── Step 6: Irreversible operations note ─────────────────────────
Write-Host ""
Write-Host "  ⚠  Irreversible operations (happen on first run):" -ForegroundColor Yellow
Write-Host "     • Database: skanyxx.db is created via EnsureCreated() on startup."
Write-Host "       It cannot be rolled back — delete the .db file to start fresh."
Write-Host "     • Default data seeded (theme, KAgent connection defaults)."
Write-Host ""

# ── Done ──────────────────────────────────────────────────────────
Write-Host "══════════════════════════════════════════" -ForegroundColor Green
Write-Host "  Installation complete!" -ForegroundColor Green
Write-Host "══════════════════════════════════════════" -ForegroundColor Green
Write-Host ""
Write-Host "  Next steps:"
Write-Host "  1. Add your Anthropic API key:"
Write-Host "     edit src\Skanyxx.Host\appsettings.json → set Anthropic.ApiKey"
Write-Host ""
Write-Host "  2. Start the app:"
Write-Host "     .\run.ps1"
Write-Host ""
Write-Host "  3. Open in browser:"
Write-Host "     http://localhost:5282"
Write-Host ""
Write-Host "  4. Swagger API docs (Development only):"
Write-Host "     http://localhost:5282/swagger"
Write-Host ""
Write-Host "  To uninstall:  .\uninstall.ps1"
Write-Host ""
