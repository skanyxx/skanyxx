#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────
# Skanyxx Installer
# Sets up the Skanyxx SRE Platform on macOS / Linux
# Usage: ./install.sh
# ─────────────────────────────────────────────────────────────────
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HOST_DIR="$SCRIPT_DIR/src/Skanyxx.Host"
CONFIG_TEMPLATE="$SCRIPT_DIR/appsettings.template.json"
SLN="$SCRIPT_DIR/Skanyxx.sln"

GREEN='\033[0;32m'; YELLOW='\033[1;33m'; RED='\033[0;31m'; NC='\033[0m'
info()  { echo -e "${GREEN}[✓]${NC} $*"; }
warn()  { echo -e "${YELLOW}[!]${NC} $*"; }
error() { echo -e "${RED}[✗]${NC} $*"; exit 1; }
step()  { echo -e "\n${GREEN}▶${NC} $*"; }

echo ""
echo "  ╔══════════════════════════════════════╗"
echo "  ║     Skanyxx SRE Platform Install     ║"
echo "  ╚══════════════════════════════════════╝"
echo ""

# ── Step 1: Check prerequisites ──────────────────────────────────
step "Checking prerequisites..."

command -v dotnet >/dev/null 2>&1 \
  || error ".NET SDK not found. Install 8.0+ from https://dotnet.microsoft.com/download"

DOTNET_VERSION=$(dotnet --version)
DOTNET_MAJOR=$(echo "$DOTNET_VERSION" | cut -d'.' -f1)
[[ "$DOTNET_MAJOR" -ge 8 ]] \
  || error ".NET SDK 8.0+ required. Installed: $DOTNET_VERSION"
info ".NET SDK $DOTNET_VERSION"

command -v git >/dev/null 2>&1 && info "git $(git --version | awk '{print $3}')" \
  || warn "git not found — not required to run, but needed for updates"

# ── Step 2: Create configuration files ───────────────────────────
step "Setting up configuration files..."

create_config() {
  local dest="$1"
  local label="$2"
  if [[ -f "$dest" ]]; then
    info "$label already exists — skipping"
  else
    cp "$CONFIG_TEMPLATE" "$dest"
    warn "$label created from template → edit it and add your API keys"
  fi
}

# Root-level config (used by legacy entry point)
create_config "$SCRIPT_DIR/appsettings.json"             "appsettings.json (root)"

# Host project configs (used by the modular host — this is the one that matters)
create_config "$HOST_DIR/appsettings.json"               "src/Skanyxx.Host/appsettings.json"
create_config "$HOST_DIR/appsettings.Development.json"   "src/Skanyxx.Host/appsettings.Development.json"

# ── Step 3: Restore NuGet packages ───────────────────────────────
step "Restoring NuGet packages..."
dotnet restore "$SLN" || error "Package restore failed"
info "Packages restored"

# ── Step 4: Build the solution ────────────────────────────────────
# IRREVERSIBLE NOTE: The CopyToModules MSBuild target in each module's
# .csproj copies compiled DLLs into src/Skanyxx.Host/modules/.
# These files are overwritten on every build — running build again is safe.
step "Building solution (all 13 modules)..."
dotnet build "$SLN" --configuration Release --no-restore \
  || error "Build failed — check the output above for errors"
info "Build complete"
info "Module DLLs copied to: $HOST_DIR/modules/"

# ── Step 5: Verify modules directory ─────────────────────────────
step "Verifying modules..."
MODULE_COUNT=$(ls -1 "$HOST_DIR/modules/"*.dll 2>/dev/null | wc -l | tr -d ' ')
if [[ "$MODULE_COUNT" -gt 0 ]]; then
  info "$MODULE_COUNT module DLL(s) found in modules/"
else
  warn "No module DLLs found in $HOST_DIR/modules/ — build may have issues"
fi

# ── Step 6: Warn about irreversible operations ────────────────────
echo ""
echo -e "${YELLOW}  ⚠  Irreversible operations (happen on first run):${NC}"
echo "     • Database: skanyxx.db is created via EnsureCreated() on startup."
echo "       It cannot be rolled back — delete the .db file to start fresh."
echo "     • Default data seeded (theme, KAgent connection defaults)."
echo "       Changing seed data requires deleting and recreating the database."
echo ""

# ── Done ──────────────────────────────────────────────────────────
echo -e "${GREEN}══════════════════════════════════════════${NC}"
echo -e "${GREEN}  Installation complete!${NC}"
echo -e "${GREEN}══════════════════════════════════════════${NC}"
echo ""
echo "  Next steps:"
echo "  1. Add your Anthropic API key:"
echo "     edit src/Skanyxx.Host/appsettings.json → set Anthropic.ApiKey"
echo ""
echo "  2. Start the app:"
echo "     ./run.sh"
echo ""
echo "  3. Open in browser:"
echo "     http://localhost:5282"
echo ""
echo "  4. Swagger API docs (Development only):"
echo "     http://localhost:5282/swagger"
echo ""
echo "  To uninstall:  ./uninstall.sh"
echo ""
