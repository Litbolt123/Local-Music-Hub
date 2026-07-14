# Context summary

## Agent environment

- **`move_agent_to_root` does not work** here — never call it. Stay on home workspace and use absolute paths under `GitHub projects\` for Local Music Hub / YouTube Downloader.
- **Release builds** auto-sync to `%LocalAppData%\Programs\LocalMusicHub\` and refresh Start Menu / Desktop shortcuts (`scripts\update-windows-shortcuts.ps1`).

## 2026-07-14 — Roadmap batch v0.8.12 → v0.9.4 (minus DLNA)

Shipped full plan except DLNA/Cast:

| Version | Highlights |
|---------|------------|
| **0.8.12** | Lyrics not-found persistence, job resume, manual-only force re-download, Retry failed in Settings |
| **0.8.13** | Hi-res covers (detail/now-playing), playlist 2×2 mosaic thumbnails |
| **0.8.14** | Playback speed (Settings + transport); pitch shifts with tempo |
| **0.9.0** | Playlist folders + nested TreeView sidebar |
| **0.9.1** | CUE virtual tracks, cue seek/advance |
| **0.9.2** | Queue \| Artist panel + Last.fm bio |
| **0.9.3** | Inbox review status + browse + mark done |
| **0.9.4** | AcoustID + fpcalc → MusicBrainz apply |

**Current version:** **0.10.0** (Local Music Hub) / **1.9.8** (YouTube Downloader)

## 2026-07-14 — Distribution (friend-ready installers)

### YouTube Downloader 1.9.8
- `scripts\fetch-bundled-tools.ps1` — downloads yt-dlp + ffmpeg into `installer\prereq\tools\win-x64\`
- Publish copies `tools\` into app; Inno installs to `{app}\tools\`
- `ToolDependencyService` checks bundled `{AppContext.BaseDirectory}\tools\` before PATH
- GitHub Actions `release-windows.yml`; `docs/releasing.md`, `docs/RELEASE_BODY.md`, `README.md`
- First-run notice: no winget required for yt-dlp/ffmpeg

### Local Music Hub 0.10.0
- `UpdateCheckService` → `Litbolt123/Local-Music-Hub`, prefix `LocalMusicHub-Setup-`
- Settings → Updates UI; startup update card on main window
- GitHub Actions `release-windows.yml`; `docs/releasing.md`, `docs/RELEASE_BODY.md`
- `docs/music-stack-setup.md` — friend walkthrough for both apps
- `git init` done (no commit — user must push to GitHub)
- README updated from stale v0.1.0

### Friend flow (after first GitHub release)
1. Install both setup exes from Releases
2. Download works without winget (YT 1.9.8+)
3. Enable integration in both Settings
4. Auto-update checks when releases exist

## 2026-07-14 — v0.9.9 speed combo closed text

- `HubComboBox`: closed/selected display text now uses `TextElement.Foreground` bound to parent `ComboBox` on the toggle pill border (dark mode showed black "1×" on dark pill)
- **Version:** **0.9.9**

## 2026-07-14 — v0.9.8 play/stars/volume fixes

- Play button: accent background + white icon (no black-on-black)
- Star rating: smoother hover via MouseMove; keeps accent on existing stars while previewing change; SyncRating skips updates during hover
- Volume: live gain change (no track reload/restart); speed/EQ reload preserves position
- **Version:** **0.9.8**

## 2026-07-14 — v0.9.7 UI polish

- Dark playlist selection (custom TreeViewItem; no white highlight)
- Themed compact playback-speed combo
- Resizable left/right sidebars (GridSplitter) + hide/show chrome; widths persisted
- Library tools window (stats / duplicates / organize / ReplayGain / clean / scan)
- Sidebar nav icons (Home, tracks, albums, artists, genres, inbox, queue)
- Hover/click 5-star rating control on now-playing bar
- **Version:** **0.9.7**

## 2026-07-14 — v0.9.6 polish pass

- `HubToolbarToggleButton` style (dark/light) for Queue | Artist panel
- CUE fixes: `AudioFilePath` for Explorer, queue purge, cover clear, tag writes; scan removes stale whole-file row when `.cue` exists
- Lyrics: embedded/sidecar from audio path, app cache keyed by virtual path for cue tracks; job resume checks audio file exists
- Context **Mark inbox done** hidden unless selected tracks are inbox
- Duplicate playlist tree template removed from window resources
- **Version:** **0.9.6** — Release build + shortcut sync OK

## 2026-07-14 — v0.9.5 startup crash fix

- **0.9.4 crash:** Queue | Artist `ToggleButton`s used `HubToolbarButton` (Button-only style) → WPF startup exception
- Fixed ToggleButton styling; playlist TreeView template moved into `TreeView.Resources`
- Startup errors now log to `%LocalAppData%\LocalMusicHub\startup-crash.log` and show a dialog
- Release rebuild + shortcut sync completed

## 2026-07-14 — v0.8.11 album card text contrast

- Home/album grid titles use `HubTextPrimaryBrush` (white on dark) instead of default black
- Album/artist/genre list rows matched for consistent contrast
- **Version:** **0.8.11**

## 2026-07-14 — v0.8.10 genre split/dedupe fix

- Genres browse splits `;` `/` `|` `,` and dedupes (fixes `Soundtrack;Soundtrack;…` rows)
- Genre drill-down + smart playlist genre rules match split segments
- New scans normalize genre tags on ingest; plural labels (“1 track · 1 album”)
- **Version:** **0.8.10**

## 2026-07-14 — v0.8.9 sleek volume + carousel arrows

- Volume fill height matches track; thumb overlays cleanly on hover/drag
- Home “Jump back in” / “Recently added”: no horizontal scrollbar; circle chevron buttons on hover
- Carousel polish: wheel-scroll over albums, smaller arrows centered on cover art
- **Version:** **0.8.9**

## 2026-07-14 — v0.8.8 clean transport icons

- Main player bar: vector Path icons for play/pause, prev/next, shuffle, repeat (+ badge for repeat-one)
- Active shuffle/repeat tint with accent color; mini player matched for play/prev/next
- Shuffle path fixed (was malformed / looked broken)
- **Version:** **0.8.8**

## 2026-07-14 — v0.8.7 accent picker + Spotify sliders

- Settings → Appearance → Accent: Purple (classic) or Spotify green
- Custom thin sliders (seek/volume/settings) with soft fill + round thumb
- Default accent restored to purple; green remains optional
- **Version:** **0.8.7**

## 2026-07-14 — v0.8.6 Spotify-like UI restyle

- Dark theme: `#121212` / `#181818` layers, Spotify green accent `#1DB954`, pill buttons (radius 20), soft gray selection (not accent fill)
- Light theme matched with green accent + rounding
- Main window: breathing room between columns, larger album cards, sans header
- Player bar reorganized like Spotify (track | transport+seek | volume/utils)
- **Version:** **0.8.6**

## 2026-07-14 — v0.8.5 filters, duplicates keep-best, embed lyrics

- **Quick filters:** Never played, Unrated, ★★★★+, FLAC, Added 30d (AND); Clear
- **Duplicates:** Keep best in group / all groups (bitrate, format, rating, plays, cover, lyrics)
- **Lyrics:** Settings → embed plain lyrics in tags; read embedded before sidecar/cache
- Polish: genre in search, empty All tracks hint, purge missing queue after duplicate delete
- **Version:** **0.8.5**

## 2026-07-14 — v0.8.4 polish pass

- Fixed Artists/Genres live search + empty hints; Open button visibility
- Artist/Genre drill-down with back navigation (reuses album header)
- EQ customize drafts until Settings Save
- Cleaned Adjust cover / archive.org-facing strings
- Mini player close + now-playing cover sync
- **Version:** **0.8.4**

## 2026-07-14 — v0.8.3 genres, sleep modes, EQ, folder art

- **Genres** sidebar browse + drill-down
- **Sleep:** end of track / end of queue (+ existing minute timers)
- **Smart playlists:** Unrated, Bitrate (kbps), Duration (seconds)
- **Folder art:** scan falls back to cover.jpg / folder.jpg when tags lack art
- **Custom EQ:** 10-band editor; preset tag `custom` + CustomEqBands in settings
- Skipped DLNA/Cast again
- **Version:** **0.8.3**

## 2026-07-14 — v0.8.2 mini player + bulk lyrics download

- **Mini player:** larger default size (460×168, bigger cover/controls)
- **Settings:** **Download lyrics for all tracks…** queues LRCLIB for missing tracks with live status
- **Per album:** album header **Download lyrics** + context **Download lyrics for album…**
- Progress shows in main view title while a job runs
- **Version:** **0.8.2**

## 2026-07-14 — v0.8.1 lyrics close + background LRCLIB cache

- **Close button:** Lyrics window uses `Show()` (non-modal); `IsCancel` alone does not close — added explicit `Close_OnClick`
- **Source:** [LRCLIB](https://lrclib.net) only for online lyrics (never archive.org). Also local `.lrc`/`.txt` sidecars
- **Cache:** successful fetches write sidecar next to audio when writable, plus `%LocalAppData%\LocalMusicHub\LyricsCache\`
- **Prefetch:** background queue on import, after scan, and on track start (Settings → Auto-download lyrics, default on; ~1 req/sec)
- **Version:** **0.8.1**

## 2026-07-14 — v0.8.0 playback & playlist polish

- **Playlists:** M3U import/export (right-click playlist); **Play next**; **Show in Explorer**
- **Transport:** volume slider + mute; **Sleep** timer (fade last 15s then pause); **Mini** player
- **System:** tray Play/Pause/Next/Prev; optional track-change balloon (Settings); global media keys when unfocused
- **Lyrics:** synced LRC highlighting (local `.lrc` or LRCLIB synced)
- **Stats:** Export CSV from Library statistics
- **Skipped this sprint:** DLNA / Cast
- **Version:** **0.8.0**

## 2026-07-14 — v0.7.9 unified album editor

- **Edit album** consolidates Adjust cover / Edit album / Album cover into one window
- Cover tools: choose, paste, drag-drop, fetch online (Apple/Deezer), rotate, zoom, shift, size/quality, reset, remove
- **Version:** **0.7.9**

## 2026-07-14 — v0.7.8 covers without archive.org

- **Cover sources:** Apple iTunes Search + Deezer (+ Last.fm if API key configured); skip Cover Art Archive / archive.org entirely
- **Progress:** progress bar + step text while searching/downloading; Apply still waits on shared fetch
- **Version:** **0.7.8**

## 2026-07-14 — v0.7.7 early cover apply + script hooks

- **Cover apply:** shared in-flight CAA download — Apply waits on the same fetch as Preview (no need to wait for preview UI)
- **Script hooks:** Settings toggle + Scripts folder; events on-track-started / on-track-changed / on-import / on-scan-complete
- **Version:** **0.7.7**

## 2026-07-14 — v0.7.6 CAA harden + Adjust Cover MusicBrainz

- **Cover Art Archive:** retries via metadata JSON + 250/500/front URLs; 45s cover client; friendly timeout copy when archive.org is unreachable
- **Adjust cover:** **MusicBrainz…** button opens album cover picker and loads into crop preview
- **Version:** **0.7.6**

## 2026-07-14 — v0.7.5 MusicBrainz cover preview + album batch

- **Cover preview:** MusicBrainz lookup shows preview pane; auto-fetches on release change; Preview cover button; Apply reuses previewed bytes
- **Album cover:** context **MusicBrainz album cover…** + toolbar **Album cover…** — search releases, preview, apply to all tracks in album
- **Version:** **0.7.5**

## 2026-07-14 — v0.7.4 smart playlist OR hint + MusicBrainz covers

- **Smart playlists:** Detect Album/Artist Is A + Is B under All (AND); show clear message + **Switch to Any (OR)** button
- **MusicBrainz:** release picker combo; optional Cover Art Archive front cover on Apply
- **Version:** **0.7.4**

## 2026-07-14 — v0.7.3 smart playlist fix + Discord / Last.fm

- **Smart playlists:** Fixed library pickers — editable ComboBox now remembers the selected artist/album/genre so rules save correctly and playlists populate
- **Matcher:** Artist/album rules use trimmed case-insensitive compares (featured artists / Unknown Album handled)
- **Discord Rich Presence:** Settings → Presence & scrobbling; shows now playing while playing
- **Last.fm:** API key/secret + Connect; now-playing updates and scrobbles after 30s or half the track
- **Version:** **0.7.3**

## 2026-07-14 — v0.7.2 smart playlist pickers + Phase 5 start

- **Smart playlists:** Artist / Album / Genre / Format values are editable dropdowns filled from your library (type to filter or pick)
- **Lyrics:** transport **Lyrics** button — local sidecar `.txt`/`.lrc`, else LRCLIB
- **MusicBrainz:** context menu **MusicBrainz lookup…** searches recordings and applies tags
- **Version:** **0.7.2**

## 2026-07-14 — v0.7.1 Home + smarter playlists

- **Home:** Jump back in (recently played albums) + Recently added horizontal rows; default browse view
- **Smart playlists:** richer rule builder — more fields (album, title, year, format, last played), AND/OR match mode, live match count, clearer operators/placeholders, “Played recently” preset
- Library stats layout padding fix (earlier in session)
- **Version:** **0.7.1**

## 2026-07-14 — v0.7.0 Phase 3 + Phase 4 complete

**Phase 3 (v0.5):**
- **Path identity:** `UpdateFilePath` / `MigrateFilePath`; watcher rename pairing + suppress during organize
- **Library stats:** `StatisticsWindow` with SQL aggregates
- **Duplicates:** tag + duration grouping; `DuplicatesWindow` with delete/open folder
- **Auto-organize:** `FileOrganizerService` + preview/apply; template + root in settings

**Phase 4 (v0.6–0.7):**
- **Pipeline:** `PlaybackPipeline` — EQ → ReplayGain → volume sample chain
- **WASAPI:** output backend + device list in Settings
- **ReplayGain:** tag read (ID3 TXXX), DB columns, apply modes, library scan
- **EQ presets:** Flat, Bass boost, Vocal, Treble
- **Gapless + crossfade:** `GaplessSampleProvider`, `CrossfadeSampleProvider`

- **Version:** **0.7.0**

## 2026-07-14 — v0.4.0 polish + gaps

- **Shuffle / repeat:** Transport bar 🔀 / 🔁; `PlaybackService` honors modes; persisted in `AppSettings`.
- **Batch tags:** Multi-select tracks → **Edit tags (N selected)…**; `BatchTagEditorWindow` with per-field Apply checkboxes; background writes + progress.
- **Smart playlists:** DB columns `is_smart`, `rules_json`; `SmartPlaylistEvaluator`; editor with presets (Highly rated, Last 30 days, Never played); ⚡ icon in sidebar.
- **UX polish:** Space play/pause, Ctrl+F search, media keys; 200ms search debounce; virtualized track/queue lists; empty-state hint for smart playlists with no matches.
- **Version:** **0.4.0**

## 2026-07-14 — v0.3.6 single-instance app

- Only one **Local Music Hub** process can run; a second launch brings the existing window forward.
- Second-instance `--import` args are forwarded to the running copy.
- **Version:** **0.3.6**

## 2026-07-14 — v0.3.5 auto-import file lock retry

- **Folder watch import:** waits/retries when yt-dlp still has the file open (no more sidebar errors mid-download).
- **Debounce:** duplicate Created/Changed events for the same path are coalesced.
- **Version:** **0.3.5**

## 2026-07-14 — v0.3.4 album folder import

- **`LibraryImportRequestService`:** supports `importFolder` in `import-request.json`.
- **`ImportAudioFolder`:** imports all supported audio files from an album folder.
- **CLI:** `--import-folder` with `--import` for album paths from Downloader.
- **Version:** **0.3.4**

## 2026-07-14 — v0.3.2 import from Downloader

- Handles `import-request.json` and `--import` path from YouTube Downloader.
- **Version:** **0.3.2**

## 2026-07-13 — v0.3.1 sidebar downloader panel

- **Collapse:** ▾/▸ button on sidebar YouTube Downloader header minimizes URL/download controls (state saved).
- **Hide completely:** Settings → **Show YouTube Downloader panel in sidebar** (integration/folder watch unchanged).
- **Version:** **0.3.1**

## 2026-07-13 — v0.3.0 downloader integration

- **API client:** `YouTubeDownloaderApiClient` — `/health`, `/check`, `/download` with `X-Extension-Token`.
- **Sidebar UI:** URL box, Download, Paste & download; duplicate prompt via `/check`.
- **Status:** Linked / offline / queued / waiting / added / error in sidebar.
- **Auto-import:** folder watch (primary) + `history.json` poll every 2s (backup, 15 min max).
- **Credentials:** port/token synced from downloader `settings.json` on bridge apply + settings save.
- **Version:** **0.3.0**

## 2026-07-13 — v0.2.3 layout fixes

- **Album track list width:** TrackList was sizing to content because `AlbumGridScroller` was the DockPanel last-child fill target. Both now share a fill `Grid` so the song list spans the center column.
- **Play queue header:** Title + buttons in separate columns so "Play queue" is no longer covered.
- **Version:** **0.2.3**

## 2026-07-13 — v0.2.2 album page polish

- **Album Play ↔ Pause only:** The album header **Play album** button becomes **Pause** while that album is playing; click pauses/resumes. Toolbar **Play** also toggles for the current view context.
- **Other albums stay Play:** Only the actively playing album card shows ⏸; every other album card keeps ▶.
- **Spotify-like track rows:** Album track list uses full-width `# | Title/Artist | Duration`; ListBox items stretch (dark + light themes).
- **Version:** **0.2.2**
- Close `LocalMusicHub.exe` before rebuild if the exe is locked.
- **`move_agent_to_root` does not work** — never call it; use absolute paths.

## 2026-07-13 — Ready for Music Hub v0.2

- User confirmed YouTube Downloader album-artist + “One” title-cleaner fixes work.
- Next: Local Music Hub Phase 1 (v0.2 library core). Awaiting which slice to start.

## 2026-07-13 — v0.2 library core (all Phase 1 slices)

- **Queue UI:** Right panel — reorder ↑/↓, remove, clear, double-click to play; Add to queue from library.
- **Playlists:** Create / rename / delete; add tracks via context menu; browse + double-click play.
- **Album grid:** Albums view shows cover art tiles; click opens tracks, double-click plays album.
- **Tag editor:** Right-click → Edit tags… writes TagLib metadata + DB (title/artist/album artist/album/track/year/genre/rating).
- **Folder watching:** `LibraryFolderWatcher` debounced FileSystemWatcher when Settings “watch folders” is on.
- **Play stats:** play_count + last_played on track start; rating stars in now-playing + context menu.
- **Maintenance:** Clean dead files button; full scan still available.
- **Version:** **0.2.0**
- **Review pass fixes:** Album drill-down no longer wiped on refresh; deferred album click so double-click plays; queue refresh no longer recurses into full library refresh; SQLite `foreign_keys=ON` + playlist orphan cleanup on track delete; removing current queue item while paused no longer auto-starts playback.
- **Polish:** Album grid selection highlight + Play/Add-to-queue use selected album; “Remove from playlist” context item; album covers loaded one blob per album (not all track covers).
- **Cover centering:** Display center-crops rectangular art (fixes left-biased YouTube thumbs). **Adjust cover…** dialog — pan X/Y, choose new image, apply to one track or whole album; writes tags + DB.
- **v0.2 polish:** Cover/album batch writes run off UI thread with progress; queue double-click = play-from-here (drops earlier items); Albums remembers last opened album when leaving/returning; **Edit album** sets album artist + album on all tracks.
- **Album UX:** Spotify-style album header with **← All albums** back button; cover/tag writes release the playing file (Windows lock) then resume so the selected/playing track updates too.
- **v0.2.1:** Toolbar no longer overlaps (title + wrapping actions); album hover ▶ fixed (transparent hit-target + RelativeSource); double-click opens, ▶ plays.

## 2026-07-13 — Album grouping fix + v0.1.1 bump

- **Why albums split:** Tags have **Track Artist** (per song, often includes feats) vs **Album Artist** (whole release). We grouped by `(album_artist, album)` and fell back to track artist when album artist was missing — so *One* split into C418 / C418 feat. … rows.
- **Fix:** Group albums by **album title** only; display artist = most common album artist on the release, else most common track artist. Tag reader no longer copies track performer into album artist. Rescan optional to refresh stored tags.

## 2026-07-13 — Albums browse crash fix

- **Symptom:** Clicking **Albums** shortly after opening the app caused a ~1s pause then crash.
- **Cause:** The track list still used the track `ItemTemplate` (`DisplayTitle`, `DurationLabel`) when bound to `LibraryAlbum` objects — WPF binding failures can crash or fault the UI thread.
- **Fix:** Separate `DataTemplate`s for tracks/albums/artists; `ApplyListTemplate()` switches template before setting `ItemsSource`. SQLite access serialized with a lock; album SQL uses `COALESCE` for empty artist/album; library scan runs on a background task with browse disabled during scan.
- **Rebuild:** Kill any running `LocalMusicHub.exe` before `dotnet build` (file lock).

## 2026-07-13 — Settings expanded (mirror YouTube Downloader patterns)

- **Before:** Only dark theme, watch folders, integrate checkbox, multiline library folders.
- **Now — Library:** Primary folder + Browse, extra folders, watch, rescan on save.
- **YouTube Downloader:** Link toggle, detected music folder, open folder / open downloader data, status text.
- **Playback:** Default volume slider.
- **Appearance:** Dark theme, minimize to tray, start with Windows (+ tray icon service).
- **Backup:** Export / import settings JSON.
- **About:** Version + data paths.
- **Not ported (downloader-only):** format/quality, embed thumbnail, log panel, parallel downloads, extension token UI, GitHub updates.


- **Crash fix:** App died on startup — `BrowseList` selection fired during XAML load before controls existed. Guarded with `IsLoaded` / null checks. That is why elevated `dotnet run` appeared to do nothing.
- **Icon:** Purple music-note `app.ico` via `scripts\make-app-icon.ps1`; wired into csproj + Inno Setup.
- **Build mirror of YouTube Downloader:** `scripts\build-installer.ps1`, `publish-installer.ps1`, `bump-version.ps1`, `get-version.ps1`, `write-version-inc.ps1`, `install-build-prerequisites.ps1`, `run.ps1`.
- **Installer:** `installer\LocalMusicHub.iss` → `LocalMusicHub-Setup-0.1.0.exe` (~60 MB self-contained).
- **How to run/dev:** prefer `.\scripts\run.ps1` (normal PowerShell, not elevated CMD).

## 2026-07-13 — Local Music Hub v0.1 scaffold

- **New project:** `Local Music Hub` — WPF + .NET 8, sibling to YouTube Downloader under `GitHub projects/`.
- **v0.1 scope:** Library scan (SQLite), browse tracks/albums/artists, search, NAudio playback, now playing bar.
- **Downloader link:** Reads `YouTubeToMp3/settings.json`, watches music output folder for new files.
- **Roadmap:** `docs/roadmap.md` phases feature catalogue into library → integration → power tools → audiophile playback.
- **User direction:** Local-only library controller (no store); goal is iTunes-class depth over multiple phases; v1 priority = library + playback core.
