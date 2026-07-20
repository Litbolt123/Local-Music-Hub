# Publishing a Windows release (Local Music Hub)

## Version source

Edit **`Directory.Build.props`** at the repo root:

```xml
<Version>0.13.0</Version>
```

Tag must be **`v` + Version** (e.g. `v0.13.0`).

## Before you tag

1. Bump version in `Directory.Build.props`.
2. Edit **`docs/RELEASE_BODY.md`** with user-facing bullets (CI requires ≥80 chars).
3. Commit and push to `main`.
4. Tag and push:

```powershell
git tag v0.11.0
git push origin v0.11.0
```

## What CI does

Workflow: **`.github/workflows/release-windows.yml`**

On tag push:

1. Publishes self-contained app
2. Compiles Inno → `LocalMusicHub-Setup-<version>.exe`
3. Creates GitHub Release with `docs/RELEASE_BODY.md` as description

Manual **workflow_dispatch** builds an artifact only (no Release).

## Local build

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

Output: `installer\Output\LocalMusicHub-Setup-<version>.exe`

## Installer asset name

Auto-update looks for assets named **`LocalMusicHub-Setup-*.exe`**.

Repository: `Litbolt123/Local-Music-Hub`

## First-time GitHub setup

If the repo does not exist yet:

```powershell
git init
git remote add origin https://github.com/Litbolt123/Local-Music-Hub.git
git add .
git commit -m "Initial commit with distribution workflow"
git push -u origin main
git tag v0.11.0
git push origin v0.11.0
```
