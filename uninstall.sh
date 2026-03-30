#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────
# Skanyxx Uninstaller
# Removes build artifacts, config files, and database
# Usage: ./uninstall.sh [--all]
#
# Flags:
#   (no flag)   Removes build artifacts only (bin/, obj/, modules/)
#   --all       Also removes config files and the SQLite database
#               ⚠ WARNING: --all deletes all your settings and data
# ─────────────────────────────────────────────────────────────────
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HOST_DIR="$SCRIPT_DIR/src/Skanyxx.Host"

GREEN='\033[0;32m'; YELLOW='\033[1;33m'; RED='\033[0;31m'; NC='\033[0m'
info()  { echo -e "${GREEN}[✓]${NC} $*"; }
warn()  { echo -e "${YELLOW}[!]${NC} $*"; }
step()  { echo -e "\n${GREEN}▶${NC} $*"; }

REMOVE_ALL=false
[[ "${1:-}" == "--all" ]] && REMOVE_ALL=true

echo ""
echo "  ╔══════════════════════════════════════╗"
echo "  ║     Skanyxx SRE Platform Uninstall   ║"
echo "  ╚══════════════════════════════════════╝"
echo ""

# ── Step 1: Remove build artifacts ───────────────────────────────
# Safe — these are generated files, fully reproducible by running install.sh again.
step "Removing build artifacts (bin/, obj/)..."
find "$SCRIPT_DIR" -type d \( -name 'bin' -o -name 'obj' \) \
  -not -path '*/.git/*' \
  -exec rm -rf {} + 2>/dev/null || true
info "Build artifacts removed"

step "Removing compiled module DLLs from modules/..."
if [[ -d "$HOST_DIR/modules" ]]; then
  rm -f "$HOST_DIR/modules/"*.dll "$HOST_DIR/modules/"*.deps.json 2>/dev/null || true
  info "Module DLLs removed"
else
  info "modules/ directory not found — skipping"
fi

# ── Step 2: Optional — remove data and config ─────────────────────
if [[ "$REMOVE_ALL" == true ]]; then
  echo ""
  echo -e "${RED}  ⚠  --all flag set. This will permanently delete:${NC}"
  echo "     • appsettings.json (your API keys / config)"
  echo "     • src/Skanyxx.Host/appsettings.json"
  echo "     • src/Skanyxx.Host/appsettings.Development.json"
  echo "     • skanyxx.db (all stored data — CANNOT BE RECOVERED)"
  echo ""
  read -r -p "  Are you sure? Type 'yes' to confirm: " CONFIRM
  if [[ "$CONFIRM" != "yes" ]]; then
    echo "  Aborted."
    exit 0
  fi

  step "Removing configuration files..."
  rm -f "$SCRIPT_DIR/appsettings.json" \
        "$SCRIPT_DIR/appsettings.Development.json" \
        "$HOST_DIR/appsettings.json" \
        "$HOST_DIR/appsettings.Development.json" \
        "$HOST_DIR/appsettings.Local.json"
  info "Configuration files removed"

  # IRREVERSIBLE: Database deletion permanently destroys all stored data.
  # There is no migration/rollback — the data is gone.
  step "Removing database..."
  rm -f "$SCRIPT_DIR/skanyxx.db" \
        "$SCRIPT_DIR/skanyxx.db-shm" \
        "$SCRIPT_DIR/skanyxx.db-wal" \
        "$HOST_DIR/skanyxx.db" \
        "$HOST_DIR/skanyxx.db-shm" \
        "$HOST_DIR/skanyxx.db-wal"
  info "Database removed"
fi

# ── Done ──────────────────────────────────────────────────────────
echo ""
echo -e "${GREEN}══════════════════════════════════════════${NC}"
if [[ "$REMOVE_ALL" == true ]]; then
  echo -e "${GREEN}  Full uninstall complete.${NC}"
else
  echo -e "${GREEN}  Build artifacts cleaned.${NC}"
  echo ""
  echo "  Config and database were kept."
  echo "  Run ./install.sh to rebuild and start fresh."
  echo "  Run ./uninstall.sh --all to remove everything."
fi
echo -e "${GREEN}══════════════════════════════════════════${NC}"
echo ""
