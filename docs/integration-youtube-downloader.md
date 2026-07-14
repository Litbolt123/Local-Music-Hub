# YouTube Downloader ↔ Local Music Hub integration

## Current (v0.3)

1. **Settings import** — Hub reads `%LocalAppData%\YouTubeToMp3\settings.json` and uses `musicOutputFolder` (or default Music folder).
2. **Folder watch** — `FileSystemWatcher` on the music output tree; new audio files are indexed after a short debounce.
3. **Extension API** — Hub calls `GET /health`, `POST /check`, `POST /download` on `http://127.0.0.1:{port}` with `X-Extension-Token`.
4. **Download UI** — Sidebar URL box, **Download**, and **Paste & download** (single video, `contentKind: music`, MP3).
5. **Completion** — Primary: folder watch auto-import. Backup: poll `history.json` every 2s for up to 15 minutes.
6. **Status** — Sidebar shows linked/offline/queued/waiting/added/error states.

### Prerequisites

- YouTube Downloader desktop app **running**
- **Browser extension API** enabled in downloader settings
- Hub **Integrate YouTube Downloader** checkbox on (Settings)

### API (Hub → Downloader)

Reuse the existing extension HTTP API on YouTube Downloader:

- `POST /check` before download for duplicates
- `POST /download` with token, URL, `contentKind: music`, `format: mp3`, `scope: single`

Hub syncs `browserExtensionPort` + `browserExtensionToken` from downloader settings on apply/save.

YouTube Downloader **v1.9.2+** reports Music Hub link status in Settings and `/health`.

## Planned (future)

### A. Downloader → Hub (push)

| Method | Description |
|--------|-------------|
| Folder watch | Implemented |
| `history.json` poll | Implemented |
| Push API | Downloader POSTs to `http://127.0.0.1:{hubPort}/library/ingest` when a music job completes |

### B. Optional shared types

Small JSON contract for `LibraryIngestEvent`:

```json
{
  "path": "C:\\Music\\Artist\\Album\\01 - Title.mp3",
  "contentKind": "music",
  "sourceUrl": "https://youtube.com/...",
  "completedUtc": "2026-07-13T12:00:00Z"
}
```

## Implementation note

Downloader-side `/library/notify` can be a small follow-up PR if push ingest is preferred over polling.
