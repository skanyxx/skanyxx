# Installing Skanyxx on macOS

## ⚠️ Important: "Damaged" Error Fix

If you see **"skanyxx is damaged and can't be opened"**, this is a macOS Gatekeeper issue, not actual damage. Follow the fix below.

## Quick Fix (Recommended)

### Option 1: One-Command Fix
After installing, open Terminal and run:
```bash
xattr -cr /Applications/skanyxx.app
```

### Option 2: Use the Fix Script
1. Download `fix-mac-install.sh` from the repository
2. Open Terminal in the Downloads folder
3. Run:
```bash
chmod +x fix-mac-install.sh
./fix-mac-install.sh
```

## Installation Steps

1. **Download** the DMG file from [Releases](https://github.com/skanyxx/skanyxx/releases)
2. **Open** the DMG file
3. **Drag** Skanyxx.app to Applications folder
4. **Run the fix** (see Quick Fix above)

## Why This Happens

Since this app is not code-signed with an Apple Developer Certificate, macOS adds a "quarantine" attribute that prevents it from opening.

### Method 1: First-time Open (Recommended)
1. After copying to Applications, **don't double-click** the app
2. **Right-click** (or Control-click) on Skanyxx.app
3. Select **"Open"** from the menu
4. Click **"Open"** in the security dialog
5. The app will now open and remember this choice

### Method 2: Remove Quarantine Attribute
Open Terminal and run:
```bash
xattr -cr /Applications/skanyxx.app
```

### Method 3: System Settings
1. Try to open the app (it will be blocked)
2. Go to **System Settings** → **Privacy & Security**
3. Scroll down and click **"Open Anyway"** next to the Skanyxx message
4. Confirm by clicking **"Open"**

## Why This Happens

macOS Gatekeeper requires apps to be:
- **Code signed** with an Apple Developer Certificate
- **Notarized** by Apple

These require a paid Apple Developer Account ($99/year). For now, this app is distributed unsigned, which is safe but requires the extra steps above.

## Verify Installation

After installation, you can verify the app by:
```bash
ls -la /Applications/skanyxx.app
```

## Troubleshooting

### "App is damaged and can't be opened"
This means the quarantine attribute is set. Use Method 2 above:
```bash
xattr -cr /Applications/skanyxx.app
```

### "App can't be opened because it is from an unidentified developer"
Use Method 1 (right-click → Open) or Method 3 (System Settings).

### Still having issues?
Open an issue on [GitHub Issues](https://github.com/skanyxx/skanyxx/issues)
