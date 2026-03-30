# Skanyxx SRE Platform — Setup Guide

Skanyxx is a modular SRE platform for Kubernetes management, agent orchestration, and monitoring.
It runs as a local web application on `http://localhost:5282`.

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Install](#install)
3. [Configure](#configure)
4. [Run](#run)
5. [Verify](#verify)
6. [Update](#update)
7. [Uninstall](#uninstall)
8. [Irreversible Operations](#irreversible-operations)
9. [Troubleshooting](#troubleshooting)

---

## Prerequisites

| Requirement | Version | Download |
|-------------|---------|----------|
| .NET SDK | 8.0 or later | https://dotnet.microsoft.com/download |
| Git | any | https://git-scm.com |
| KAgent server | — | Required for Kubernetes/agent features |

Verify your .NET SDK version:
```bash
dotnet --version   # must be 8.x or higher
```

---

## Install

### Option A — From a Release zip (recommended for end users)

Download the zip for your platform from the [Releases page](https://github.com/skanyxx/skanyxx/releases).

**macOS / Linux:**
```bash
unzip skanyxx-<platform>.zip
cd skanyxx-<platform>
./install.sh
```

**Windows (PowerShell):**
```powershell
Expand-Archive skanyxx-windows-x64.zip
cd skanyxx-windows-x64
.\install.ps1
```

---

### Option B — From source (for developers)

```bash
git clone git@github.com:skanyxx/skanyxx.git
cd skanyxx
```

**macOS / Linux:**
```bash
./install.sh
```

**Windows (PowerShell):**
```powershell
.\install.ps1
```

The installer does the following:

| Step | What happens | Reversible? |
|------|-------------|-------------|
| Check prerequisites | Verifies .NET SDK 8+ is installed | — |
| Create config files | Copies `appsettings.template.json` → `appsettings.json` | Yes — delete the file |
| Restore packages | `dotnet restore Skanyxx.sln` downloads NuGet packages | Yes — packages are cached |
| Build solution | `dotnet build` compiles all 13 modules | Yes — run again to rebuild |
| Copy module DLLs | Each module's `CopyToModules` target copies DLLs to `src/Skanyxx.Host/modules/` | Yes — overwritten on next build |

---

## Configure

After install, open and edit the configuration file for the **Host project**:

```
src/Skanyxx.Host/appsettings.json
```

### Required settings

```json
{
  "Anthropic": {
    "ApiKey": "sk-ant-api03-..."   ← paste your Anthropic API key here
  }
}
```

### Optional settings

```json
{
  "KAgent": {
    "BaseUrl":    "localhost",      ← hostname or IP of your KAgent server
    "Port":        8083,
    "Protocol":   "http",
    "Token":      "",               ← auth token if KAgent requires one
    "Timeout":    30000,            ← request timeout in milliseconds
    "IngressUrl": ""                ← external ingress URL (if behind a proxy)
  },
  "Kubernetes": {
    "ConfigPath": ""                ← path to kubeconfig (leave empty for default ~/.kube/config)
  },
  "AWS": {
    "Profile": "",
    "Region":  ""
  },
  "Azure": {
    "ConfigDir": ""
  }
}
```

> The `Modules` section controls which modules load at startup.
> Leave all entries `true` to load everything (default).

---

## Run

**macOS / Linux:**
```bash
./run.sh
# custom port:
./run.sh --port 8080
# production mode (no Swagger):
./run.sh --env Production
```

**Windows (PowerShell):**
```powershell
.\run.ps1
# custom port:
.\run.ps1 -Port 8080
# production mode:
.\run.ps1 -Env Production
```

**Or directly with dotnet:**
```bash
dotnet run --project src/Skanyxx.Host --configuration Release --no-build
```

The app runs on `http://localhost:5282` by default.

---

## Verify

Once running, open these URLs in your browser:

| URL | Description |
|-----|-------------|
| `http://localhost:5282` | Main UI |
| `http://localhost:5282/health` | Health check (JSON) |
| `http://localhost:5282/swagger` | REST API docs (Development mode only) |

Expected health check response:
```json
{ "status": "Healthy" }
```

---

## Update

Pull the latest code and rebuild:

```bash
git pull
./install.sh      # macOS / Linux
.\install.ps1     # Windows
```

The build is safe to re-run — all outputs are overwritten.

> If the database schema changed between versions, delete `skanyxx.db` and let it be
> recreated automatically on next startup. See [Irreversible Operations](#irreversible-operations).

---

## Uninstall

### Remove build artifacts only (keeps your data and config):

```bash
./uninstall.sh          # macOS / Linux
.\uninstall.ps1         # Windows
```

### Remove everything (config + database — permanent):

```bash
./uninstall.sh --all    # macOS / Linux
.\uninstall.ps1 -All    # Windows
```

You will be asked to confirm before anything is deleted with `--all` / `-All`.

---

## Irreversible Operations

These operations happen automatically during install or on first run.
They **cannot be rolled back** without manual intervention.

### 1. SQLite database creation

**When:** First time the app starts.

**What:** `db.Database.EnsureCreated()` creates the `skanyxx.db` file with the full schema
and seeds default data (dark theme, KAgent localhost connection, notification defaults).

**How to undo:** Delete the `.db` file and restart — the database will be recreated from scratch.
All stored data (custom settings, saved layouts, connections you added) will be lost.

```bash
# macOS / Linux
rm src/Skanyxx.Host/skanyxx.db

# Windows
Remove-Item src\Skanyxx.Host\skanyxx.db
```

### 2. Module DLL copy to `modules/`

**When:** Every `dotnet build`.

**What:** The `CopyToModules` MSBuild target in each module's `.csproj` copies the compiled
DLL and its `.deps.json` file into `src/Skanyxx.Host/modules/`. Running build again overwrites
these files — there is no history. You cannot selectively revert a single module's DLL without
checking out the source at an older git commit and rebuilding.

**How to undo:** Check out the desired commit and rebuild.

```bash
git checkout <commit-hash>
dotnet build Skanyxx.sln --configuration Release
```

### 3. Configuration files created from template

**When:** First time `install.sh` / `install.ps1` runs (only if the file does not exist).

**What:** Copies `appsettings.template.json` to `appsettings.json`.
Once you add API keys and edit settings, those values exist only in your local file.
There is no automatic backup.

**How to undo:** Delete the file — the installer will recreate it from the template on next run.
Your API keys will be lost.

---

## Troubleshooting

### "No module DLLs found in modules/"

The module DLLs are created by the build. Run:
```bash
dotnet build Skanyxx.sln --configuration Release
```

### "Build failed"

Ensure .NET SDK 8 is installed (`dotnet --version`).
Check that `global.json` allows your installed SDK:
```json
{ "sdk": { "rollForward": "latestMajor", "allowPrerelease": true } }
```

### App starts but API calls fail

1. Check `http://localhost:5282/health` — all modules should be `Healthy`.
2. Verify `Anthropic.ApiKey` is set in `src/Skanyxx.Host/appsettings.json`.
3. Verify KAgent is running and `KAgent.BaseUrl` / `KAgent.Port` are correct.

### Port already in use

Use a different port:
```bash
./run.sh --port 8080
```

### Database errors on startup

Delete `skanyxx.db` and restart — it will be recreated automatically.
```bash
rm src/Skanyxx.Host/skanyxx.db
./run.sh
```
