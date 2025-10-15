#!/bin/bash

# Skanyxx Direct Installation Script
echo "🚀 Installing Skanyxx directly to Applications..."

# Get the directory where this script is located
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_PATH="$SCRIPT_DIR/src-tauri/target/aarch64-apple-darwin/release/bundle/macos/skanyxx.app"
APPLICATIONS_DIR="/Applications"

# Check if the app exists
if [ ! -d "$APP_PATH" ]; then
    echo "❌ Error: skanyxx.app not found at $APP_PATH"
    echo "Please run 'npm run build:mac' first to build the application."
    exit 1
fi

# Check if app is already installed
if [ -d "$APPLICATIONS_DIR/skanyxx.app" ]; then
    echo "⚠️  Skanyxx is already installed. Removing old version..."
    rm -rf "$APPLICATIONS_DIR/skanyxx.app"
fi

# Copy the app to Applications
echo "📦 Copying Skanyxx to Applications..."
cp -R "$APP_PATH" "$APPLICATIONS_DIR/"

# Set proper permissions
echo "🔐 Setting permissions..."
chmod +x "$APPLICATIONS_DIR/skanyxx.app/Contents/MacOS/skanyxx"

# Verify installation
if [ -d "$APPLICATIONS_DIR/skanyxx.app" ]; then
    echo "✅ Skanyxx successfully installed to $APPLICATIONS_DIR/skanyxx.app"
    echo ""
    echo "🎉 Installation complete! You can now:"
    echo "   • Find Skanyxx in your Applications folder"
    echo "   • Launch it from Spotlight (Cmd+Space, then type 'Skanyxx')"
    echo "   • Add it to your Dock for quick access"
    echo ""
    echo "🚀 Launching Skanyxx..."
    open "$APPLICATIONS_DIR/skanyxx.app"
else
    echo "❌ Installation failed. Please check permissions and try again."
    exit 1
fi
