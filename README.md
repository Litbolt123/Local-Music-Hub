# Local Music Hub

A local-first music library and player for Windows — built to pair with **YouTube Downloader**, not replace streaming services.

**License:** [Apache License 2.0](LICENSE)

**Current version: 0.13.0**

## Install (friends)

1. Download **`LocalMusicHub-Setup-0.13.0.exe`** from [GitHub Releases](https://github.com/Litbolt123/Local-Music-Hub/releases) (once published).
2. Run the installer — no .NET install required (self-contained).
3. Optional but recommended: install **YouTube Downloader** from [its releases](https://github.com/Litbolt123/YouTube-to-MP3/releases) for downloading music into your library.

Full two-app setup: see **`docs/music-stack-setup.md`**.

## Features

- Scan folders into a SQLite library (MP3, M4A, FLAC, WAV, OGG, Opus, WMA)
- Browse **tracks**, **albums**, and **artists**; playlists; smart filters
- Playback with queue, gapless/crossfade, EQ, ReplayGain, speed control
- **YouTube Downloader integration:** sidebar download queue, auto-import, shared music folder
- Library tools: duplicates, organize, ReplayGain scan, lyrics
- Dark/light themes; resizable side panels
- **Auto-update check** via GitHub Releases (Settings → Updates)

## Run (dev)

From a **normal** (non-admin) PowerShell in the repo root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

## Build installer

```powershell
# First time only (if needed):
powershell -ExecutionPolicy Bypass -File .\scripts\install-build-prerequisites.ps1

# Publish + Inno Setup:
powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

Installer output: `installer\Output\LocalMusicHub-Setup-<version>.exe`

## Publish a release

See `docs/releasing.md`. Short version: bump `Directory.Build.props`, edit `docs/RELEASE_BODY.md`, commit, then:

```powershell
git tag v0.13.0
git push origin v0.13.0
```

## Data locations

| App | Path |
|-----|------|
| Local Music Hub | `%LocalAppData%\LocalMusicHub\` |
| YouTube Downloader | `%LocalAppData%\YouTubeToMp3\` |

## Roadmap

See `docs\roadmap.md`.

## Related project

**YouTube Downloader** (`YouTube to MP3` repo) — downloads into your music folders; Local Music Hub indexes and plays them.
