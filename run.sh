#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────
# Skanyxx — Start the application
# Usage: ./run.sh [--port 5282] [--env Development|Production]
# ─────────────────────────────────────────────────────────────────
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HOST_PROJECT="$SCRIPT_DIR/src/Skanyxx.Host"

PORT=5282
ENV="Development"

# Parse optional arguments
while [[ $# -gt 0 ]]; do
  case "$1" in
    --port) PORT="$2"; shift 2 ;;
    --env)  ENV="$2";  shift 2 ;;
    *) echo "Unknown argument: $1"; exit 1 ;;
  esac
done

echo ""
echo "  Starting Skanyxx SRE Platform..."
echo "  Environment : $ENV"
echo "  URL         : http://localhost:$PORT"
echo "  Press Ctrl+C to stop."
echo ""

ASPNETCORE_ENVIRONMENT="$ENV" \
  dotnet run \
    --project "$HOST_PROJECT" \
    --configuration Release \
    --no-build \
    --urls "http://localhost:$PORT"
