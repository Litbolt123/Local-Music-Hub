# Context summary

## Agent environment

- **`move_agent_to_root` does not work** here — never call it. Stay on home workspace and use absolute paths under `GitHub projects\` for Local Music Hub / YouTube Downloader.
- **Release builds** auto-sync to `%LocalAppData%\Programs\LocalMusicHub\` and refresh Start Menu / Desktop shortcuts (`scripts\update-windows-shortcuts.ps1`).

## 2026-07-21 — Music Hub WAID-style installer upgrade (v0.13.16)

- **User:** Want Local Music Hub to use WAID update flow (download + run installer) instead of manual browser download.
- **Was:** Settings could download/run but didn’t fully quit; main update card only opened the browser.
- **Fix:** `InstallerUpgrade` + `App.ExitForInstallerUpgrade`; update card **Download and install**; Settings confirms then quits; tray balloon focuses update card.

## 2026-07-21 — WAID update flow shipped for Litbolt App Folder

- **Done:** v0.1.2 — GitHub update check (WAID-style) in App Folder + Local Video Hub; repos created.
- **Repos:** https://github.com/Litbolt123/Litbolt-App-Folder , https://github.com/Litbolt123/Local-Video-Hub

## 2026-07-20 — Litbolt App Folder: WAID update flow tomorrow

- **User:** Add the same update flow as WAID to Litbolt's App Folder — deferred to tomorrow.
- **Also:** Video product renamed to **Local Video Hub** (`LocalVideoHub.exe`).
- **Status:** Completed 2026-07-21 (v0.1.2).

## 2026-07-20 — Litbolt's App Folder born (v0.1.0)

- **Name:** Litbolt's App Folder (user rejected “Maple Bear Hub”).
- **Shipped:** Parent shell with Home / Music / Video / Downloader; **Pop tab out** + **Open standalone**; Video embedded + `LitboltVideo` standalone EXE.
- **Music & Downloader:** launcher modules → existing `%LocalAppData%\Programs\…` EXEs (stay standalone).
- **Install:** Desktop + Start Menu shortcuts synced on Release build.
- **Path:** `GitHub projects\Litbolt App Folder`

## 2026-07-20 — Synapse-style parent hub naming

- **User:** Wants Synapse-like parent — **not** named “Maple Bear Hub”.
- **Resolved:** Named **Litbolt's App Folder**.

## 2026-07-20 — Synapse-style parent hub + video player (ask)

- **User:** Wants a Razer Synapse–like parent app with a home and tabs for different apps; also wants a video player.
- **Status:** Clarifying scope before build (apps to include, single-process modules vs launcher, where video lives).
- **Deferred from earlier:** Video library/player noted after FLAC work.

## 2026-07-20 — Windows Details blank though Hub shows tags (v0.13.15)

- **User:** After edit/save, Explorer Properties → Details still empty; Hub shows metadata. Stardew FLACs.
- **Evidence:** `02 - Cloud Country.flac` starts with `ID3…`; TagLib has both Xiph (full tags) and Id3v2. Windows shell ignores ID3-on-FLAC.
- **Cause:** `AudioTagWriter` forced ID3v2.3 globally and `GetTag(Id3v2, true)` created ID3 on FLAC (date released).
- **Fix:** Xiph-only writes for FLAC/OGG; strip ID3 on save/cover; Library tools “Fix Windows Details”; 0.13.15.

## 2026-07-20 — FLAC “fLaC Sync not found” (v0.13.14)

- **User:** Title showed `Invalid Flac-File. "fLaC" Sync not found.` after 0.13.13.
- **Cause:** BunLabs `FlacReader` requires `fLaC` at stream start; many tagged FLACs (and some mislabeled files) begin with ID3v2.
- **Fix:** Seek past ID3, scan for sync, MF + format sniff fallbacks; 0.13.14.

## 2026-07-20 — FLAC playback + format clarity (v0.13.13)

- **User:** Many tracks are FLAC; expected broad format support. Video player/library noted for later.
- **Clarified:** Scan/tags support mp3/m4a/aac/flac/wav/ogg/opus/wma/webm; playback historically used Media Foundation for most types (unreliable for FLAC/OGG).
- **Fix:** Dedicated FLAC decoder (`BunLabs.NAudio.Flac`) in `HubAudioReader`; version 0.13.13.
- **Deferred:** Video library / player.
- **Follow-up Q:** Were tag/Properties write struggles from the same MF gap? **Mostly no** — tags use TagLib (separate from decode). Overlap only when a playing file is locked so Save can’t write; blank Details / ID3 were write-format and DB-merge issues (0.13.8).

## 2026-07-20 — MF 0xC00D36C4 on playlist play (v0.13.12)

- **User:** Title bar showed `The byte stream type of the given URL is unsupported. (0xC00D36C4)` (Stardew playlist).
- **Cause:** `AudioFileReader` uses Media Foundation, which often can’t decode OGG/Vorbis game OSTs (still indexed by TagLib).
- **Fix:** `HubAudioReader` opens OGG via NAudio.Vorbis, MP3 ACM fallback, auto-skip unplayable queue items.
- **Build:** 0.13.12 Release synced; EXE FileVersion 0.13.12.0.

## 2026-07-20 — Playlist play still silent on 0.13.10 (v0.13.11)

- **User:** v0.13.10 installed; manual playlist Play fills queue (12 tracks, now #1) but audio stays stopped (Play icon, 0:00).
- **Fixes:** `SetQueueAndPlay` loads with `play:true`; Space ignored when focus is a Button (start+pause race); surface `Playback.LastError` in title bar; transport Play uses Pause/Play explicitly.
- **Build:** 0.13.11 Release built and synced (EXE FileVersion 0.13.11.0 verified).

## 2026-07-20 — Manual playlists won’t play (v0.13.10)

- **User:** Normal playlists don’t play on Play; smart playlists work.
- **Cause:** `AlbumPlayPause_OnClick` / toolbar Play used `TogglePlayPause` whenever `CurrentTrack` was *in* the playlist, even when not playing — so it never `SetQueue`’d the playlist (common when the last song is also on a manual playlist).
- **Fix:** Only Pause when that context is actively playing; otherwise always `PlayCurrentSelection`. Explicit playlist branch in `ResolveTracksForPlayback`.
- **Build:** 0.13.10 Release built and synced.

## 2026-07-20 — Wonky playlist mosaic (v0.13.9)

- **User:** Smart playlist cover showed two thin vertical strips with empty space (Minecraft playlist screenshot).
- **Cause:** 2-tile mosaic drew half-width × full-height cells, squashing square art; DPI quirks could worsen it.
- **Fix:** Always compose a 2×2 grid (duplicate tiles when &lt;4); normalize tile DPI; display mosaic without center-crop; UniformToFill on detail cover.
- **Build:** 0.13.9 Release built and synced.

## 2026-07-20 — Refresh flash, mosaics, blank Properties (v0.13.8)

- **User:** Refresh turns left panes white briefly; playlist mosaics missing; Windows file Properties Details blank.
- **Fixes:** Stop disabling BrowseList/PlaylistNavTree during scan; deferred UI-thread mosaic load in `RefreshPlaylistNav`; album save preserves empty optional fields; ID3v2.3 on write; UpsertTrack merges blank disk tags with existing DB rows.
- **Build:** 0.13.8 Release built and synced.

## 2026-07-20 — Album editor Save clipped + refresh album (v0.13.7)

- **User:** Save album not visible unless window expanded; how to refresh an album; keep new UI reachable without resizing.
- **Fix:** Album editor DockPanel footer; scrollable details/cover; lower MinHeight. Album **Refresh** scans album folders. WrapPanel for detail actions; smart playlist footer docked; PlaylistAddPanel MaxHeight 240.
- **Build:** 0.13.7 Release built and synced.

## 2026-07-20 — HubTextPrimaryBrush crash (v0.13.6)

- **User:** Error dialog `'HubTextPrimaryBrush' resource not found` (likely opening Add to playlist / playlist add panel).
- **Cause:** `HubTheme.Apply` / `Ensure` tore down merged theme dictionaries before inserting the next; deferred DynamicResource lookups raced and threw.
- **Fix:** Insert-before-remove theme swap; skip full re-apply in `Ensure` when brushes already exist; TryFindResource for shuffle/repeat chrome.
- **Build:** 0.13.6 Release built and synced.

## 2026-07-20 — New Music folder files not detected (v0.13.5)

- **User:** Added a missing song into Music / album folder; Hub did not show it.
- **Cause:** `ApplyFolderWatcher()` set `SuppressEvents = true` and never cleared it after Settings save — live folder watch stayed muted. Tag reads also used a 150 ms sleep and swallowed failures (copy-in-progress).
- **Fix:** Restore suppress only while UI not ready; watcher uses `ReadTrackWhenReadyAsync`, larger buffer, directory bulk queue, limited retries.
- **Workaround:** toolbar **Scan library** finds anything the watcher missed.
- **Build:** 0.13.5 Release built and synced.

## 2026-07-20 — Spotify-style add to playlist (v0.13.4)

- **User ask:** Make add-to-playlist more like Spotify (search/suggestions/+), not just enterable text. Screenshot of Spotify’s Add to playlist sidebar.
- **Shipped:** (1) `AddToPlaylistPickerWindow` for context-menu add — searchable manual playlists + New playlist. (2) In-playlist **Add songs** panel with search, filter chips, track rows with cover/title/artist/**+**. Empty playlists auto-open the panel. `LibraryRepository.SuggestTracksForPlaylist` excludes tracks already on the playlist.
- **Build:** 0.13.4 Release built and synced to `%LocalAppData%\Programs\LocalMusicHub\`.

## 2026-07-20 — Album-wide artist (v0.13.3)

- **User ask:** Manually set artist for a whole album.
- **Change:** Album editor adds Artist (all tracks) + Apply ↓ + Same as album artist; checkbox applies on Save (default on). Writes per-track Artist tags, not only AlbumArtist.

## 2026-07-20 — Playlists ≠ albums (v0.13.2)

- **User:** “Playlists should not be albums” + find related issues.
- **Fixes:** Back from playlist no longer opens Albums; playlist Pause works; Edit album hidden on playlist; lyrics download uses playlist tracks; GetPlaylist instead of GetPlaylists(); empty-state copy.

## 2026-07-20 — Output device switching (v0.13.1)

- **Symptom:** Changing output device in Settings had no effect.
- **Cause:** Default backend was WaveOut, which ignores WASAPI device IDs; reload also rebuilt the entire decode pipeline unnecessarily.
- **Fix:** WASAPI default + auto-WASAPI when a specific device is chosen; `RecreateOutput()` hot-swaps endpoint on Save while keeping playback position.

## 2026-07-19 — v0.13.0 release + push ingest + player polish

- **Release:** `v0.13.0` — folds 0.11.x–0.12.x playback/theme fixes plus new features below. `RELEASE_BODY.md`, README, roadmap updated.
- **Push ingest:** Hub `LibraryIngestHost` on `http://127.0.0.1:47385/library/ingest` (token in `settings.json`). YouTube Downloader **2.1.1** calls push on music download complete (`LocalMusicHubPushIngest.cs`). Folder watch remains fallback.
- **Player polish:** Mini player seek + volume; tray menu accent tint; removed orphan `CoverAdjustWindow` / `AlbumEditWindow`.
- **Build:** Hub 0.13.0 synced locally. YT 2.1.1 rebuilt + synced from Release output (2026-07-20).

## 2026-07-19 — Seek bar hop at track end (v0.12.2)

- **Symptom:** Near end of a song, seek bar hops back ~1–2 s (crossfade overlap).
- **Cause:** `PositionReader` reported incoming decode position while UI still showed outgoing track; timer path allowed backward slider smoothing.
- **Fix:** `ActiveReader` stays on outgoing stream until crossfade handoff; UI skips drift reconcile and keeps rendering during crossfade.

## 2026-07-19 — Crossfade garbled audio (v0.12.1)

- **Symptom:** After several songs, audio garbled, popped, then stopped.
- **Cause:** Outgoing EOF fired `PlaybackStopped` during/after crossfade while incoming track still playing; handler advanced queue and called `LoadCurrent` on top of live playback. Also stale buffer samples when outgoing read returned 0 mid-fade.
- **Fix:** `ShouldResumeAfterSpuriousStop()` guard; clear `_crossfadeTransitionActive` synchronously on handoff; reset `SpeedSampleProvider` on track change; safer gapless/crossfade cleanup.

## 2026-07-19 — Accent themes + custom color (v0.12.0)

- **User ask:** Polish pass + more themes + color-picker theme after seek-bar/crossfade fixes confirmed working (0.11.6).
- **Shipped:** Eight accent presets (purple, Spotify, ocean, teal, rose, crimson, amber, sunset) plus **Custom color…** with hex field and WinForms color picker in Settings → Appearance.
- **Live preview** while Settings is open; **Cancel** or close without save reverts via `HubTheme.ApplyFromSettings()`.
- **Contrast:** `HubPrimaryForegroundBrush` on primary buttons and checked toolbar toggles (amber/sunset use dark text).
- **Refresh:** `StarRatingControl` listens to `HubTheme.ThemeChanged`; shuffle/repeat chrome updates after Settings closes; `LibraryToolsWindow` calls `HubTheme.Ensure`.
- **Build:** 0.12.0 synced to `%LocalAppData%\Programs\LocalMusicHub\`.

## 2026-07-19 — Maple Bear platform checklist + master prompt

- Cross-app platform docs created from Hub + YouTube Downloader + What Am I Doing patterns.
- Shared files: `GitHub projects/docs/maple-bear-app-platform-checklist.md`, `GitHub projects/docs/maple-bear-app-platform-master-prompt.md`.
- Hub contributed: single-instance + `activate.signal`, HubTheme light/dark, tray/autostart, JSON settings, GitHub updater, Inno + release workflow.

## 2026-07-14 — Tray context menu theming (v0.11.0)

- **Tray menu** (`TrayIconService` + `TrayMenuTheme`) now uses a custom WinForms `ProfessionalColorTable` aligned with `HubThemeDark` / `HubThemeLight` (`#181818` card bg, white text, `#2A2A2A` hover in dark mode).
- Follows **Settings → Use dark theme**; refreshes when settings are saved (`_tray.ApplyTheme()` after `HubTheme.ApplyFromSettings()`).
- **Volume slider:** at 100% the track now paints fully filled (no gray cap at the end).

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

## 2026-07-15 — Album cover download crash fix

- **Symptom:** Hub crashed when changing album cover via Edit album → Fetch online (Apple/Deezer download).
- **Likely causes:** Full-resolution cover decoded into WPF memory; UI progress updates off dispatcher; tag write racing downloader/playback file lock on FLAC.
- **Fixes:** `CoverArtHelper.NormalizeDownloadedCover` + bounded `DecodePixelWidth`; dispatcher-marshalled progress in `MusicBrainzAlbumCoverWindow`; retrying tag/cover writes via `AudioFileAccess`; top-level try/catch in `EditAlbum_OnClick`; runtime errors log to `%LocalAppData%\LocalMusicHub\crash.log` instead of hard shutdown.

## 2026-07-15 — Playback UI fixes (volume static, seek bar, special chars)

- **Volume static:** Replaced NAudio `VolumeSampleProvider` with `SmoothVolumeSampleProvider` — ramps gain per audio buffer (~20–40 ms) to avoid zipper noise when dragging the volume slider during playback.
- **Seek bar cuts back:** `PositionSlider` now previews time labels while dragging; actual `Seek()` runs only on mouse-up or lost capture (`FinishSeekFromSlider`), so position timer updates no longer fight mid-drag seeks.
- **Special characters (e.g. Équinoxe):** `TagTextHelper.Clean` fixes Latin-1 mojibake and falls back to the on-disk filename when tag title contains U+FFFD. `AudioTagReader` uses it for artist/album/title. Existing library may need **Rescan library** to refresh SQLite titles.
- **Built & synced:** v0.11.0 → `%LocalAppData%\Programs\LocalMusicHub\`.

## 2026-07-15 — Seek bar scrubbing fix (v0.11.0)

- **Symptom:** Track position slider jumped back while dragging during playback (e.g. 0:05 → 1:05), even after the first seek-on-release attempt.
- **Root causes:**
  - `_seeking` was cleared **before** `Seek()` and `RefreshNowPlaying()`, so the 250 ms position timer and `StateChanged` handlers could overwrite the slider with the old playback position between release and commit.
  - `RefreshNowPlaying()` always updated elapsed/remaining labels from `_playback.Position` during scrubbing, fighting the drag preview.
  - `PreviewMouseDown`/`PreviewMouseUp` alone are unreliable for thumb drags; no `_suppressPositionSlider` guard on programmatic slider updates (unlike volume).
  - Mini player has **no** position slider — bug was main-window only.
- **Fix (`MainWindow.xaml.cs`):**
  - `_positionScrubbing` + `_suppressPositionSlider` + `_positionSeekCommitted` (prevents double seek on `DragCompleted` + `MouseUp`).
  - Thumb `DragStarted`/`DragCompleted` hooked in `MainWindow_OnLoaded`; track click-to-jump still uses `PreviewMouseDown`/`Up` + `LostMouseCapture`.
  - While scrubbing: position timer skipped; slider and time labels driven only by user drag (preview).
  - On release: single `CommitPositionSeek()` — `Seek()`, then pin slider fraction and labels to target before clearing scrub state.
  - `RefreshNowPlaying()` skips slider/label updates while `_positionScrubbing`.
- **`PlaybackService.Seek`:** raises `PositionChanged` with the clamped relative target (not a re-read that could lag).
- **Scrubbing mode:** **Seek-on-release only** — audio does not follow the thumb mid-drag; labels preview the target time.
- **Built & synced:** v0.11.0 → `%LocalAppData%\Programs\LocalMusicHub\`.

## 2026-07-15 — Volume slider ramp fix (v0.11.0)

- **Symptom:** Volume still crackly/static when dragging during playback; ramp down felt sluggish.
- **Root cause:** `SmoothVolumeSampleProvider` advanced gain once per audio buffer (`step = delta * 0.08`) and applied a single flat multiplier to every sample in that buffer — step discontinuities between buffers caused zipper noise; exponential decay needed many buffers (~1s+) for large decreases.
- **Fix:** Per-frame linear ramp over **5 ms** (~220 frames @ 44.1 kHz); retargets from current gain when slider moves mid-ramp; snaps tiny deltas. Volume slider wiring (`_suppressVolumeSlider`, linear 0–1) unchanged — not the issue.
- **Built & synced:** v0.11.0 → `%LocalAppData%\Programs\LocalMusicHub\`.

## 2026-07-15 — Volume slider fill at max

- **Symptom:** Volume bar left a gray cap on the right at max volume.
- **Cause:** Fill border extended only 6 px past thumb center; `Value == 1` DataTrigger missed float drift (e.g. 0.999…).
- **Fix:** `HubSliderDecrease` right margin `-6` → `-12`; `SliderNearMaxConverter` (≥ 0.995) drives full-track fill on `HubSliderHorizontalAutoThumb` in both themes.

## 2026-07-15 — Seek bar click-to-jump fix

- **Symptom:** Clicking the position slider (`IsMoveToPointEnabled`) moved the thumb briefly then snapped back; no seek.
- **Cause:** Built-in move-to-point marks mouse events handled, so `PreviewMouseUp` never committed; value could update before scrub state started.
- **Fix:** `PreviewMouseLeftButtonUp` with `handledEventsToo`; start scrub on `ValueChanged` when mouse is down; defer `CommitPositionSeek` one dispatcher frame so slider value is final before read.

## 2026-07-15 — Gapless/crossfade now-playing sync

- **Symptom:** Near track end, remaining time jumped up; next track played but now-playing bar still showed previous track.
- **Cause:** `GaplessSampleProvider` swapped audio readers without advancing queue index or firing `TrackChanged`; position came from the new reader while duration still reflected the old track. Crossfade also started at track **beginning** instead of the last N seconds.
- **Fix:** `TrackAdvanced` event on gapless swap → advance queue, sync `_reader`, `TrackChanged`, preload next. Duration prefers library metadata; position clamped to duration. Crossfade begins only when remaining ≤ crossfade window (`UpdateTransitionState` on position timer).

## 2026-07-15 — Base feature polish pass (v0.11.0)

- **Audit:** Library/playback/queue/downloader integration solid; no automated tests; DLNA not shipped.
- **Quick fixes applied:** transport speed persists via `App.SaveSettings()`; library/ReplayGain scan error dialogs; `LoadCurrent` try/catch; album card + mini player vector icons; README/releasing.md → 0.11.0.
- **Deferred:** remove orphan `CoverAdjustWindow`/`AlbumEditWindow`; mini player seek/volume; rescan for mojibake titles; crossfade-only edge cases without gapless.

## 2026-07-19 — Album editor: per-track metadata

- **Ask:** Make more song details editable from the album edit dialog (like Windows file properties).
- **Change:** `AlbumEditorWindow` now has album-level **Year** and **Genre** (applied to all tracks), plus a **Tracks** list to edit **#**, **Title**, and **Artist** per song. CUE virtual tracks show read-only. Save writes tags + updates library DB.
- **Files:** `AlbumEditorWindow.xaml`, `AlbumEditorWindow.xaml.cs`, `MainWindow.xaml.cs` (`EditAlbum_OnClick`).
- **Built & synced:** v0.11.0 → `%LocalAppData%\Programs\LocalMusicHub\`.

## 2026-07-19 — Album editor: comments, date released, rating

- **Ask:** Add Comments, Date released, and Rating to album editor.
- **Album-level:** **Date released** (year or full date) and **Comments** (multiline) apply to all tracks; written to file tags (`RELEASEDATE`/`TDRL`, `COMMENT`) and SQLite.
- **Per-track:** **Rating** column (0–5 stars) in track grid; library DB only (same as tag editor).
- **Plumbing:** `LibraryTrack.Comment`, `LibraryTrack.DateReleased`; `AudioTagReader`/`AudioTagWriter`; DB columns `comment`, `date_released`.
- **Built & synced:** v0.11.0 → `%LocalAppData%\Programs\LocalMusicHub\`.

## 2026-07-19 — Startup performance fix

- **Symptom:** App took a long time to open.
- **Root causes:** UI thread blocked on startup loading playlist tree (cover mosaics per playlist; smart playlists ran full `SELECT *`) and home carousels (N+1 album metadata + cover blob queries).
- **Fixes:** Defer library load to background; playlist nav skips covers on startup; batch album cover/metadata queries; lightweight smart-playlist cover query; SQLite WAL mode.
- **Built & synced:** v0.11.0 → `%LocalAppData%\Programs\LocalMusicHub\`.

## 2026-07-19 — Startup performance pass 2

- **Still slow after pass 1:** SQLite opened in `MainWindow` field initializer (before window paint); cover JPEG decode on UI thread when home grid bound; 15× `PRAGMA table_info` per launch; folder watcher/downloader init on UI thread in `Loaded`.
- **Fixes:** Lazy `LibraryDataServices` (DB opens on background thread during library load); defer downloader/discord/folder-watcher/media-keys to `DispatcherPriority.Background`; pre-decode album thumbnails off UI (`CoverThumbnail`); schema version cache in `app_meta` skips repeat migrations.
- **Built & synced:** v0.11.0 → `%LocalAppData%\Programs\LocalMusicHub\` (shortcuts updated).

## 2026-07-15 — Album grid crash fix

- **Symptom:** Crash opening Albums/home grid (`Cannot find resource named 'IconPlayGeo'`).
- **Cause:** Polish pass moved album play button to vector `Path` using `{StaticResource IconPlayGeo}` before geometry was defined in `Window.Resources`.
- **Fix:** Define `IconPlayGeo` / `IconPauseGeo` at top of resource dictionary (before `AlbumGridItemTemplate`).

## 2026-07-19 — Smart playlist editor dark theme + playlist mosaic covers

- **Ask:** Smart playlist editor ComboBoxes bright white in dark mode; playlist thumbnail shows 4× same cover when playlist has 2 albums (e.g. Minecraft Alpha + Beta OR rules).
- **ComboBox fix:** Code-created rule dropdowns now use `HubComboBox` / new `HubEditableComboBox` theme styles (`SmartPlaylistEditorWindow.xaml.cs`, `HubThemeDark.xaml`, `HubThemeLight.xaml`).
- **Mosaic fix:** `LoadPlaylistCoverTiles` picks one cover per distinct album (`ROW_NUMBER` partition by album), then dedupes identical cover blobs before mosaic (`LibraryRepository.cs`). Two albums → side-by-side split in `BuildMosaic`.
- **Built:** Release compile OK; sync to `%LocalAppData%\Programs\LocalMusicHub\` may need app closed if running (robocopy exit 11).

## 2026-07-19 — Startup timing log

- **Ask:** Startup still feels slow after perf passes; add a log showing how long each open phase takes.
- **Change:** New `StartupProfiler` writes append-only `startup-timing.log` under `%LocalAppData%\LocalMusicHub\` with per-phase ms deltas + “slowest phases” summary. Instrumented: `App.OnStartup`, `MainWindow` ctor/`InitializeComponent`, first paint (`ContentRendered`), deferred init, library DB open, track count, playlist tree, home album queries, thumbnail warm, UI bind.
- **Setting:** `LogStartupTiming` (default `true`) in `AppSettings`.
- **Next:** User opens app once, shares log — use slowest lines to target real bottleneck.

## 2026-07-19 — Startup lazy service init

- **Log finding:** ~48–56 s gap before `mainwindow.ctor.enter`; library load only ~500 ms. Bottleneck was eager field init of NAudio/Discord/downloader services on `MainWindow` construction.
- **Change:** `MainWindow.LazyServices.cs` — lazy properties for `Playback`, `DownloaderBridge`, `Discord`, `Tray`, etc. Slim ctor (theme + XAML only). Playback/downloader wired on first use; `EnsurePlaybackConfigured` deferred to `DispatcherPriority.Background` after window loads.
- **Built & synced:** v0.11.0 → `%LocalAppData%\Programs\LocalMusicHub\`.

## 2026-07-19 — Startup UI freeze after lazy init

- **Symptom:** Fast startup but blank/non-interactive UI stuck on "Loading library…".
- **Cause (log):** Library data ready at ~814 ms but UI update queued behind `DeferSecondaryInitialization` blocking UI thread ~39 s (Discord/downloader/folder watcher).
- **Fix:** Heavy defer work moved to `Task.Run`; library load applies via `Dispatcher.BeginInvoke(Normal)` independent of defer. `ApplyInitialLibraryLoad` extracted.

## 2026-07-19 — Startup UI freeze pass 2

- **Symptom:** Window opens fast but home unusable ~40 s; log showed `library.bg_task.done` at ~900 ms but `library.ui.show_home` at ~41 s.
- **Cause:** `defer.secondary` UI callback (lyrics prefetch init + startup import) ran synchronously on UI thread ~40 s, blocking library UI queue.
- **Fix:** All heavy defer work stays on background thread; no UI-thread `ContinueWith`. Library UI posts at `Loaded` priority immediately. Folder watcher suppressed until home ready; `BeginInvoke` instead of `Invoke` for refresh handlers.

## 2026-07-19 — Scan specific folders

- **Ask:** Rescan specific folders instead of whole library only.
- **Change:** `Scan folders…` button (header + Library tools). `ScanFoldersWindow` picks library folders or browses to add one. `LibraryScanner.ScanFoldersAsync` + `RemoveMissingPathsUnderRoots` — only adds/updates/removes tracks under selected folders; rest of library untouched. Full **Scan library** unchanged.

## 2026-07-19 — Settings hang on open

- **Symptom:** After fast startup, clicking **Settings** froze the app.
- **Cause:** `Settings_OnClick` accessed `LyricsPrefetch` lazy property, which created `LyricsPrefetchService` on the UI thread. Constructor called `TryResumeSavedJob()`, looping thousands of pending paths with `File.Exists` + disk snapshot writes per track.
- **Fix:** Settings opens without creating lyrics service (`() => _lyricsPrefetchService`). Lyrics only created on background when user clicks download buttons (`Task.Run(EnsureLyricsPrefetch)`). `TryResumeSavedJob` moved to worker thread; batched job snapshots during resume (`_resumingJob`). Other `LyricsPrefetch` usages switched to `_lyricsPrefetchService?.Enqueue` or background `EnsureLyricsPrefetch`.

## 2026-07-19 — Global theme consistency

- **Ask:** All UI in Settings and everywhere else should use the current theme (dark/light).
- **Cause:** Many controls (ComboBox, PasswordBox, CheckBox, ListView, separators, context menus) had no explicit Hub style and fell back to WPF defaults (white backgrounds in dark mode).
- **Fix:** Added implicit default styles in `HubThemeDark.xaml` and `HubThemeLight.xaml` for ComboBox, PasswordBox, TextBox, CheckBox, Separator, ListView/ListViewItem, GridViewColumnHeader, ContextMenu, MenuItem, ToolTip. Fixed `HubBodyText` to use `HubTextPrimaryBrush` instead of hardcoded colors. Built & synced v0.11.0.

## 2026-07-19 — Crossfade + gapless double-start

- **Symptom:** With both crossfade and gapless on, next track starts, cuts off, then starts again (even at 1 s crossfade).
- **Cause:** Gapless auto-switched to the preloaded next track at EOF while crossfade was already playing the next track from a separate reader — two competing handoffs.
- **Fix:** When crossfade is enabled, gapless `AutoAdvance` is off and gapless does not preload next; crossfade owns the transition, hands off the positioned reader on `CrossfadeCompleted`, and continues intro audio if the outro ends first. Settings hint added.

## 2026-07-19 — Crossfade audible cut between tracks

- **Symptom:** No double-start, but a noticeable cut/gap during crossfade transitions.
- **Causes:** (1) Crossfade start only polled every 250 ms — could miss the overlap window. (2) Fade length ignored channel count (stereo fades were half as long as configured, handing off before the outro finished). (3) Linear crossfade dips volume mid-blend. (4) Handoff/UI work on the audio thread could glitch output.
- **Fix:** Sample-accurate crossfade start inside `CrossfadeSampleProvider.Read`, correct fade sample count (`rate × channels`), equal-power sin/cos blend curve, cached fade sample provider, defer preload/UI after handoff to thread pool. Built & synced v0.11.0.

## 2026-07-19 — Crossfade post-blend cut + home covers vanish

- **Crossfade cut:** Handoff created a new `ToSampleProvider()` on the incoming reader, causing a tiny discontinuity after the blend. Now passes the live sample provider through `AdoptCurrent`; intro-only phase applies proper gain ramp.
- **Home covers:** Playing an album called `ShowHome()` to refresh Jump Back In, but re-fetched albums without `WarmAlbumThumbnails`. Covers bound to null `CoverThumbnail`. Fixed by warming thumbnails in `ShowHome()` every time.

## 2026-07-19 — Crossfade cut: pipeline order

- **Symptom:** Small gap/cut still audible after crossfade blend, especially at handoff.
- **Cause:** Crossfade was **after** EQ/volume/speed — outgoing track was processed, incoming track mixed as raw PCM. At handoff, incoming suddenly went through the full effects chain → discontinuity.
- **Fix:** Moved crossfade immediately after gapless (before EQ/volume/speed). Both tracks mixed dry, then shared processing. On handoff: adopt same sample provider, apply new track ReplayGain (smooth volume ramp), reset speed interpolator.

## 2026-07-19 — Tray icon visible while window open

- **Ask:** App open should show both main window and system tray icon; closing window should still honor “stay in tray” setting.
- **Cause:** `OnTrayPreferenceChanged` called `HideTrayIcon()` whenever the window was visible and minimize-to-tray was off — tray only appeared after closing/hiding the window.
- **Fix:** Always `ShowTrayIcon()` on load and when preferences change. Close-to-tray behavior unchanged (`MainWindow_OnClosing`). Settings label clarified: tray stays visible; checkbox only controls close behavior.

## 2026-07-19 — Crossfade cut at track-change handoff

- **Symptom:** Crossfade blend sounds smooth, but a very short cut/gap occurs exactly when the track metadata changes; playback then continues at the correct crossfade position.
- **Likely causes:** (1) ReplayGain jumped to the new track only at handoff while the blend still used the old track’s gain. (2) Handoff ran mid-buffer on the audio thread (dispose + adopt). (3) Last fade frame not at full incoming gain. (4) Wasapi could spuriously stop when outgoing EOF hit while crossfade still had incoming audio.
- **Fix:** Ramp ReplayGain to the next track over the crossfade duration when fade starts (`CrossfadeStarted` + `SmoothVolumeSampleProvider.RampTo`). Defer handoff event to the start of the next audio buffer (`_pendingHandoff`). Snap incoming/outgoing gains on the last frame. Dispose outgoing readers on a background thread. Resume playback if Wasapi stops during an active crossfade.

## 2026-07-19 — Local dev versioning + crossfade attempt 2 (v0.11.1)

- **Ask:** Bump local version on each fix attempt; accumulate changelog for a future release (no publish).
- **Added:** `docs/UNRELEASED.md` for in-progress notes; `Directory.Build.props` → **0.11.1**.
- **Crossfade cut (attempt 2):** Removed intro-only read path (single mixed path always). Gapless pads outgoing EOF with silence during reserved incoming (`ReserveIncoming` at fade start, `CommitIncoming` at handoff) so Wasapi never gets zero-length reads mid-transition.

## 2026-07-19 — Settings lost on rebuild (v0.11.2)

- **Symptom:** Gapless, crossfade, Discord client ID (and possibly other settings) reset after dev rebuild/sync.
- **Likely cause:** Build script force-killed the app while `settings.json` was being written (startup used to save on every launch; kill mid-write → corrupt file → load fell back to defaults).
- **Fix:** Atomic settings save + `.bak` recovery; graceful shutdown via `shutdown.request` before force-kill; removed unconditional startup save; `App.OnExit` flushes settings.

## 2026-07-19 — Playback bar stuck at ~3s (v0.11.3)

- **Symptom:** After crossfade improvements, audio plays but elapsed/seek bar freezes (~3s into a song).
- **Cause:** Position read from the outgoing `AudioFileReader` while crossfade audio came from a separate `ToSampleProvider()` wrapper on the incoming reader — `CurrentTime` stopped advancing after handoff.
- **Fix:** Read/decode via `AudioFileReader` directly as `ISampleProvider`; `GaplessSampleProvider.PositionReader` switches to incoming stream once outgoing hits EOF during overlap.

## 2026-07-19 — Smooth seek bar (v0.11.4)

- **Ask:** Seek bar feels clunky on short tracks (e.g. 3 s); user happy with crossfade quality.
- **Fix:** While playing, slider moves every display frame with interpolated position between 250 ms audio samples; heavy tick work unchanged. Sub-30 s tracks round elapsed labels to 100 ms to avoid digit flicker.

## 2026-07-19 — Seek bar bounce (v0.11.5)

- **Symptom:** Seek bar drifted forward smoothly then snapped back slightly ~every second.
- **Cause:** 250 ms hard re-sync to `Playback.Position` while frame interpolation ran slightly ahead of decode position.
- **Fix:** Gentle clock steering for drift; anchor only resets on play/seek/track change; slider never moves backward during normal playback.

## 2026-07-19 — Seek bar micro-bounce on short tracks (v0.11.6)

- **Symptom:** Barely noticeable snap-back remained on ~3 s tracks.
- **Fix:** Removed backward drift steering; slider strictly monotonic while playing; skip drift reconcile on tracks under 20 s.

## 2026-07-19 — Theming architecture exploration

- **Report:** Two-layer theming — `HubThemeDark.xaml` / `HubThemeLight.xaml` (base palette + shared control styles) swapped at runtime; accent colors overridden at app level via `HubTheme.ApplyAccent` (`purple`, `spotify` only).
- **Settings:** Appearance section in `SettingsWindow.xaml` — dark checkbox + accent ComboBox; applies on Save (`MainWindow` calls `HubTheme.ApplyFromSettings` + `Tray.ApplyTheme`).
- **Gaps / quick wins:** `HubPrimaryButton` hardcodes `White` (breaks Spotify dark-on-green); shuffle/repeat icons not refreshed after theme save; `LibraryToolsWindow` missing `HubTheme.Ensure`; `StarRatingControl` caches accent brush on Loaded only; tray menu ignores accent.
- **Extension path:** Preset catalog in `HubTheme.ResolveAccent` + ComboBox items; custom accent via `AccentTheme=custom` + `CustomAccentColor` hex + contrast-based `HubPrimaryForegroundColor`.

## 2026-07-20 — CLI `--minimized --playlist` investigation (report only)

- **Ask:** Does music autoplay on start/`--minimized`? How are playlists started? IPC for `--playlist`? Minimal plan.
- **Findings:** No autoplay on startup or `--minimized` (tray hide only). Playlists via UI → `PlayCurrentSelection` / double-click → `Playback.SetQueueAndPlay`. Second-instance already forwards `--import` via `import-request.json` + `activate.signal`; extend same pattern for `--playlist`.
- **Decision:** Report only (~60–80 line change); parent decides implementation.

## 2026-07-24 — Harbor volume + playlist discovery (report only)

- **Ask:** Where is DefaultVolume stored; how playlists are stored; existing `--volume`/volume IPC; smallest Harbor path for (a) list playlist names read-only, (b) start LMH at a volume.
- **Volume:** `%LocalAppData%\LocalMusicHub\settings.json` property `DefaultVolume` (PascalCase), **0–1** double (default `0.85`). Live slider also writes `App.Settings.DefaultVolume` via `Playback.SetVolume`. **No `--volume` and no volume IPC.**
- **Playlists:** SQLite `%LocalAppData%\LocalMusicHub\library.db`, table `playlists` (`name`, plus `is_smart`, `rules_json`, `folder_id`). List: `SELECT name FROM playlists ORDER BY name COLLATE NOCASE`. WAL mode — concurrent read OK.
- **Existing CLI/IPC:** `--minimized`/`--tray`, `--playlist`/`--playlist=`, `--import`/`--import-folder`; second-instance → `playlist-request.json` (`PlaylistName`, `RequestedUtc`). Harbor already launches `--minimized --playlist "…"`.
- **Recommended:** Harbor read-only SQLite for playlist names (already has `Microsoft.Data.Sqlite`). Extend LMH with `--volume` (0–1) + optional `Volume` on `playlist-request.json` (and forward in `SingleInstanceService`); Harbor `TryLaunch` appends `--volume`. No code changes this session.

## 2026-07-24 — LMH update flow fixed to match WAID (v0.13.17)

- **User:** Fix Local Music Hub update stuff to match What Am I Doing.
- **Was:** Installed app still 0.11.0; WAID-style `InstallerUpgrade` in source but not synced. GitHub `v0.13.0` advertised as 0.13.0 while asset was `LocalMusicHub-Setup-0.11.6.exe`. Settings/quit paths not fully WAID-aligned.
- **Fix:** Update check prefers Setup EXE filename version when tag/asset disagree; Settings Updates UI matches WAID (panel + Check now / Releases page); `InstallerUpgrade` + full quit (close owned windows, Bypass Closing); tray balloon still focuses update card. Built/synced **0.13.17** to `%LocalAppData%\Programs\LocalMusicHub\`.
- **Open:** Publish a real GitHub Release with `LocalMusicHub-Setup-0.13.17.exe` so other machines can update via GitHub.

## 2026-07-24 — Publishing v0.13.17 GitHub Release

- **User:** Publish a real release.
- **First CI attempt:** Tag/notes OK but asset was still `LocalMusicHub-Setup-0.11.6.exe` — committed `installer/version.inc` (`#define AppVersion "0.11.6"`) was included before `/DAppVersion`, so Inno ignored CI’s version.
- **Fix:** `.iss` only includes `version.inc` when `AppVersion` is undefined; CI regenerates `version.inc` and asserts `Output\LocalMusicHub-Setup-<ver>.exe`. Deleted broken release/tag and republished.
- **Shipped:** https://github.com/Litbolt123/Local-Music-Hub/releases/tag/v0.13.17 — asset **`LocalMusicHub-Setup-0.13.17.exe`** (~65 MB), marked latest.

