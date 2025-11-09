# Installing Skanyxx on macOS

## Installation Steps

1. **Download** the DMG file from [Releases](https://github.com/skanyxx/skanyxx/releases)
2. **Open** the DMG file
3. **Drag** Skanyxx.app to Applications folder

## Security Notice

Since this app is not code-signed with an Apple Developer Certificate, macOS will prevent it from opening by default.

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
