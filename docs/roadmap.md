# Roadmap — Local Music Hub

Based on `advanced-music-player-features.md` (feature catalogue). Phases are ordered for a **local library controller** with **YouTube Downloader** integration — no store, no streaming service.

## Phase 0 — Foundation (v0.1, **done**)

- WPF + .NET 8 shell, dark/light theme
- SQLite library index, folder scan
- Tag read (TagLib#), browse tracks/albums/artists, search
- NAudio playback, queue, transport controls
- Read YouTube Downloader `settings.json`, watch music output folder

## Phase 1 — Library core (v0.2, **done**)

- Monitored folders with incremental scan (FileSystemWatcher + debounce)
- Play count, last played, ratings (DB columns + UI)
- Manual playlists + play queue UI (separate from library browse)
- Album grid with artwork
- Track detail / tag editor (single track)
- Remove dead entries, rescan single folder

## Phase 2 — Downloader integration (v0.3, **done**)

- HTTP client to YouTube Downloader API (`/health`, `/check`, `/download`) — same token as browser extension
- **Download from player:** paste URL or “Paste & download” in sidebar
- Auto-import on download complete (folder watch + `history.json` poll)
- In-app status: “Downloading via YouTube Downloader…” / waiting / added

## Phase 3 — Organization & power library (v0.4–0.5, **done**)

**v0.4:** shuffle/repeat, batch tags, smart playlists, UX polish

**v0.5:** path migration (`UpdateFilePath`), library statistics dashboard, duplicate detection (tag + duration), template auto-organize

## Phase 4 — Playback quality (v0.6–0.7, **done**)

- ISampleProvider pipeline with EQ + ReplayGain apply
- WASAPI output + device selection
- ReplayGain library scan (analyze + DB cache)
- Gapless preload + crossfade mixing

**v0.7.1 polish:** Home (Jump back in / Recently added), smarter playlist rule builder (more fields, AND/OR, live match count)

## Phase 5 — Long tail (v0.7.2+, in progress)

**v0.7.2 shipped:**
- Lyrics pane (local `.txt`/`.lrc` sidecar, or LRCLIB online)
- MusicBrainz lookup / auto-tag from context menu
- Smart playlist library pickers (artists, albums, genres, formats)

**v0.7.3 shipped:**
- Smart playlist picker fix (selected artist/album actually filters tracks)
- Discord Rich Presence (Settings → Presence & scrobbling)
- Last.fm scrobbling (connect with API key + account; now playing + scrobble)

**v0.7.4 shipped:**
- Smart playlist AND/OR guidance (conflicting Album Is rules → Switch to Any)
- MusicBrainz release picker + Cover Art Archive front cover fetch

**v0.7.5 shipped:**
- MusicBrainz cover preview before Apply (auto on release select + Preview cover)
- Batch album cover lookup (context menu / toolbar → apply to all tracks)

**v0.7.6 shipped:**
- Hardened Cover Art Archive fetch (JSON + thumbs, longer timeout, clearer timeout message)
- Adjust cover → **MusicBrainz…** loads CAA cover into crop/preview before Save

**v0.7.7 shipped:**
- Apply MusicBrainz cover while preview is still downloading (shared fetch)
- Script hooks (Settings → Scripts folder: on-track-started / changed / import / scan-complete)

**v0.7.8 shipped:**
- Cover art via Apple Music + Deezer (+ Last.fm if API key set) — no Cover Art Archive / archive.org
- Cover download progress bar + per-URL timeouts

**v0.7.9 shipped:**
- Unified **Edit album** (metadata + cover in one window)
- Richer cover tools: paste, drag-drop, rotate, zoom, output size/quality, fetch online, remove

**v0.8.0 shipped:**
- M3U playlist import/export (playlist context menu)
- Play next + Show in Explorer
- Transport volume / mute + sleep timer (fade then pause)
- Tray play/pause/next/prev + optional track-change balloon
- Global media keys when unfocused
- Mini player window
- Synced LRC lyrics highlighting
- Statistics CSV export

**v0.8.1 shipped:**
- Lyrics Close button fix (non-modal window needed explicit Click)
- Background lyrics prefetch from **LRCLIB** on import / scan / play; cache sidecar + app cache
- Settings toggle: Auto-download lyrics
- CoverAdjust stale archive.org copy cleaned up

**v0.8.2 shipped:**
- Larger default mini player
- Settings → **Download lyrics for all tracks…** (progress status)
- Per-album **Download lyrics** (album header + context menu)

**v0.8.3 shipped:**
- **Genres** browse view (open / play genre)
- Sleep timer: **end of track** / **end of queue**
- Smart playlist rules: Unrated, Bitrate, Duration
- Folder art fallback (`cover.jpg` / `folder.jpg`, etc.)
- Custom **10-band EQ** editor (Settings → Customize EQ…)

**v0.8.4 polish:**
- Artists/Genres search actually filters; empty states; Open button only when useful
- Artist/Genre drill-down with ← back (same header pattern as albums)
- Custom EQ drafts until Settings Save (Cancel no longer mutates live settings)
- Stale “Adjust cover” / archive.org user copy cleaned up
- Mini player: close button + cover refresh with now-playing

**v0.8.5 shipped:**
- Quick filter chips (Never played / Unrated / ★★★★+ / FLAC / Added 30d)
- Duplicates **Keep best** (per group + all groups) with quality scoring
- Optional **embed lyrics in tags** (Settings); read embedded lyrics first
- Search includes genre; empty hint on All tracks; purge queue after duplicate deletes

**v0.8.6 shipped:**
- Spotify-inspired UI restyle: near-black layers, green accent, pill buttons, softer selection
- Player bar layout: track left / transport+seek center / volume+utils right
- More rounding and breathing room on sidebars, album cards, and chrome

**v0.8.7 shipped:**
- Accent picker in Settings: **Purple (classic)** or **Spotify green** (dark/light still separate)
- Spotify-style sliders: thin track, soft fill, small circular thumb

**v0.8.8 shipped:**
- Clean vector transport icons (play/pause, prev/next, shuffle, repeat/repeat-one) — no emoji
- Mini player uses the same play/prev/next icons

**v0.8.9 shipped:**
- Volume slider fill matches thin track (no thick “D” thumb disconnect); circle still hover-only
- Home album rows: hidden scrollbars + Spotify-style circle chevron arrows on hover

**v0.8.10 shipped:**
- Genre split/dedupe on browse + ingest (fixes repeated `Soundtrack;Soundtrack;…`)

**v0.8.11 shipped:**
- Album/artist/genre list text contrast (`HubTextPrimaryBrush`)

**v0.8.12 shipped:**
- Lyrics bulk v2: persist LRCLIB not-found + resume interrupted jobs (`lyrics-not-found.json`, `lyrics-job.json`)
- Auto/bulk sweeps skip cached + not-found; manual album/track/playlist forces re-download; Settings **Retry failed lyrics**

**v0.8.13 shipped:**
- Hi-res cover decode (album header ~720px, now-playing ~288px; grid stays 150px)
- Playlist mosaic thumbnails (sidebar + playlist header)

**v0.8.14 shipped:**
- Playback speed 0.5×–2.0× (resampling; pitch shifts with tempo) — Settings + transport combo

**v0.9.0 shipped:**
- Playlist folders (`playlist_folders` + `playlists.folder_id`); nested TreeView sidebar; create/rename/delete/move

**v0.9.1 shipped:**
- CUE sheet albums: parse `.cue`, virtual tracks with seek start + auto-advance at cue end

**v0.9.2 shipped:**
- Right panel **Queue | Artist** toggle; local stats + Last.fm bio when API key configured

**v0.9.3 shipped:**
- Library inbox (`review_status`); Settings **Mark new imports as inbox**; sidebar Inbox browse; context **Mark inbox done**

**v0.9.4 shipped:**
- AcoustID fingerprint lookup (API key + `fpcalc`) → MusicBrainz apply; context **Identify with AcoustID…**

**v0.9.5 shipped:**
- Startup crash fix: Queue | Artist toggles no longer use Button-only toolbar style; playlist TreeView template fix
- Startup errors log to `%LocalAppData%\LocalMusicHub\startup-crash.log`

**v0.9.6 shipped:**
- Polish: `HubToolbarToggleButton` style; CUE path fixes (`AudioFilePath` for filesystem ops); stale whole-file row removed when `.cue` present
- Lyrics: per-cue cache keys, embedded read from audio file; LRCLIB User-Agent updated
- Context **Mark inbox done** only when selection has inbox tracks

**v0.9.7 shipped:**
- Dark playlist selection; themed speed combo; resizable/hideable side panels
- Library tools hub; sidebar nav icons; hover 5-star rating

**v0.9.8 shipped:**
- Play button white icon on accent; smoother star hover; volume no longer restarts track

**Still open:**
- DLNA / Cast (optional)

---

**Principle:** Each phase should ship a usable app. The catalogue is the ceiling, not the v1 scope.
