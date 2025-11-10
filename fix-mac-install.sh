#!/bin/bash

# Skanyxx macOS Installation Fix Script
# This script removes the quarantine attribute that causes the "damaged" error

echo "🔧 Skanyxx macOS Installation Fix"
echo "=================================="
echo ""

APP_PATH="/Applications/skanyxx.app"

# Check if app exists
if [ ! -d "$APP_PATH" ]; then
    echo "❌ Error: skanyxx.app not found in /Applications"
    echo ""
    echo "Please drag skanyxx.app to your Applications folder first."
    exit 1
fi

echo "📍 Found skanyxx.app at: $APP_PATH"
echo ""

# Remove quarantine attribute
echo "🔓 Removing quarantine attribute..."
xattr -cr "$APP_PATH"

if [ $? -eq 0 ]; then
    echo "✅ Success! The quarantine attribute has been removed."
    echo ""
    echo "You can now open Skanyxx from:"
    echo "  • Spotlight (⌘ + Space, then type 'skanyxx')"
    echo "  • Applications folder"
    echo "  • Launchpad"
    echo ""
else
    echo "❌ Failed to remove quarantine attribute."
    echo ""
    echo "Please try manually:"
    echo "  sudo xattr -cr /Applications/skanyxx.app"
    exit 1
fi
