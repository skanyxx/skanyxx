#!/bin/bash

# Script to extract Linux build from Docker container
# Run this after the Docker build completes

echo "🐧 Extracting Linux build from Docker..."

# Create container from image
docker create --name skanyxx-linux-extract skanyxx-linux:v0.1.0

# Extract the binary
docker cp skanyxx-linux-extract:/app/src-tauri/target/release/skanyxx ./skanyxx-linux-binary

# Package it
cd /Users/anatolipakastrofski/Dev/Denis/Skanyxx/Skanyxx
zip Skanyxx-v0.1.0-Linux-x64.zip skanyxx-linux-binary/skanyxx
chmod +x skanyxx-linux-binary/skanyxx

# Create AppImage-style package (simple zip with binary)
mkdir -p Skanyxx.AppDir/usr/bin
cp skanyxx-linux-binary/skanyxx Skanyxx.AppDir/usr/bin/
tar -czf Skanyxx-v0.1.0-Linux-x64.tar.gz -C Skanyxx.AppDir usr/

# Cleanup
docker rm skanyxx-linux-extract
rm -rf skanyxx-linux-binary Skanyxx.AppDir

echo "✅ Linux packages created:"
ls -lh Skanyxx-v0.1.0-Linux-x64.*

echo ""
echo "📦 Files ready to upload:"
echo "  - Skanyxx-v0.1.0-Linux-x64.zip"
echo "  - Skanyxx-v0.1.0-Linux-x64.tar.gz"
