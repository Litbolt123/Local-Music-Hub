# Music stack setup (Local Music Hub + YouTube Downloader)

Give this guide to a friend who just wants **download → library → play** on Windows 10/11.

## What you need

| App | Installer | Purpose |
|-----|-----------|---------|
| **YouTube Downloader** | `YouTubeToMp3-Setup-*.exe` | Download from YouTube / YouTube Music |
| **Local Music Hub** | `LocalMusicHub-Setup-*.exe` | Scan, organize, and play your files |

Both are **self-contained** — no .NET or Python install.

**YouTube Downloader 1.9.8+** includes **yt-dlp** and **ffmpeg** inside the app. No winget required for basic downloads.

## Step-by-step

### 1. Install both apps

Run each setup exe. Defaults install to:

- `%LocalAppData%\Programs\YouTubeToMp3\`
- `%LocalAppData%\Programs\LocalMusicHub\`

Start Menu shortcuts are created automatically.

### 2. First launch — YouTube Downloader

1. Open **YouTube Downloader** from the Start Menu.
2. Pick a **music folder** (e.g. `Music\YouTube` under your profile).
3. Paste a YouTube URL and click download — it should work immediately.

**Optional:** Settings → enable **Start with Windows** if you use Music Hub’s sidebar to queue downloads (the downloader must be running).

**Optional (YouTube Music):** if album downloads fail, install Deno:

```powershell
winget install DenoLand.Deno -e
```

Restart YouTube Downloader after installing Deno.

### 3. First launch — Local Music Hub

1. Open **Local Music Hub**.
2. **Settings → Library:** confirm your music folder is listed (Music Hub can auto-detect the downloader’s folder).
3. Click **Scan library** on the main window.
4. Play tracks from the library view.

### 4. Link the two apps (recommended)

In **Local Music Hub → Settings:**

- **Integrate YouTube Downloader** — on
- **Show downloader sidebar** — on

In **YouTube Downloader → Settings:**

- Enable **Music Hub integration** / extension API (if not already on).

You can paste URLs in Music Hub’s sidebar; downloads run in YouTube Downloader and new files appear in your library after scan (or auto-watch).

### 5. Browser extension (optional)

In YouTube Downloader → Settings, follow the link to load the unpacked extension from:

`%LocalAppData%\Programs\YouTubeToMp3\browser-extension`

Use it to send videos from Chrome/Edge to the downloader.

## Updates

Both apps check **GitHub Releases** on startup (can disable in Settings → Updates).

When a new version appears, download the new setup exe and run it — **your settings and library database are kept** in `%LocalAppData%`.

## Where your data lives

| What | Path |
|------|------|
| Music Hub settings + library DB | `%LocalAppData%\LocalMusicHub\` |
| Downloader settings + history | `%LocalAppData%\YouTubeToMp3\` |
| Your music files | Whatever folder you chose (default: under `Music\`) |

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Download says tools missing | Reinstall YouTube Downloader 1.9.8+ from Releases |
| Music Hub doesn’t see new files | Run **Scan library** or enable folder watch in Settings |
| Sidebar download does nothing | Start YouTube Downloader; check integration in both apps’ Settings |
| 403 / low quality from YouTube | Update to latest downloader release; optional Deno install |

## For you (maintainer)

Release both apps with matching tags when distribution changes affect the stack. See `docs/releasing.md` in each repo.
