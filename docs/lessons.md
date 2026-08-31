# Lessons

What worked and what did not. **Read this before repeating a fix.** After a fix or a failed approach, append a class (not a chat transcript): Symptom · Failed · Worked · Verify · Do not regress.

Vault index: `C:\Users\Augus\OneDrive\Documents\Obsidian Vault\Atlas\Lessons.md`


Also read `GitHub projects\docs\app-development-pipeline.md` section 10.

## 2026-08-24 — pairs with Downloader, not streaming

**Failed:** Treating Hub as a streaming client.

**Worked:** Local library. Data `%LocalAppData%\LocalMusicHub`. Version in `Directory.Build.props`.

## 2026-08-30 — WPF now-playing cover flash (♪ placeholder)

**Symptom:** Music-note placeholder flashes during track/album change; cover crossfade feels sharp; album click waits ~1s before Jump back in updates.

**Failed:** Faster crossfade only (fade-to-zero still exposed ♪); instant preview only at `BeginQueueAndPlay` (album path still blocked on `GetTracksForAlbum` first).

**Worked:** Hide placeholder when `Image.Source` set; keep previous cover until async decode; skip crossfade when upgrading from album `CoverThumbnail` hint (`_coverHintActive`); optimistic `ApplyAlbumPlayChrome` + carousel bump before DB; load album tracks on background thread.

**Verify:** Click album ▶ on Home — cover/title update immediately, album jumps to Jump back in without waiting for audio; skip tracks — no ♪ flash, softer dissolve.

**Do not regress:** Do not fade cover to opacity 0 with visible placeholder underneath; do not call `GetTracksForAlbum` synchronously on album play click path.

## 2026-08-30 — WPF now-playing bar layout jump

**Symptom:** Bottom panel grows/shifts after click when title, stats, and stars populate in a second pass.

**Failed:** Two-phase album chrome (album name first, track title second); stats/rating only in `RefreshNowPlaying` after playback starts.

**Worked:** Fixed `Height` on title/artist/stats rows + reserved star row; `ApplyNowPlayingChrome` sets all fields once; `RefreshNowPlaying` skips title/cover when track id unchanged; removed interim album-only text.

**Verify:** Click album ▶ — bar height stays constant; one content update with full track line + stats + stars.

**Do not regress:** Do not show album-level title/artist as a separate preview state before track metadata loads.

## 2026-08-30 — SQLite partial SELECT + ReadTrack optional columns

**Symptom:** Playback stops working after optimizing album load with column subset (no cover blobs).

**Failed:** Assuming metadata-only SELECT is enough if `ReadTrack` still calls `GetOrdinal` on columns not in query.

**Worked:** Add all non-blob columns used by `ReadTrack` to lite SELECT; wrap `ReadNullableInt/Double/Float` in try/catch; show faulted task message in UI.

**Verify:** Album ▶ and track double-click start playback; no silent background failure.

**Do not regress:** Any new `ReadTrack` field must be in `TrackColumnsNoCover` or use safe optional reader.

## 2026-08-30 — Now-playing cover cache by id, not blob

**Symptom:** Skip / album change still waits ~1s for bottom-left title/cover even after instant chrome.

**Failed:** Caching only by cover-blob bytes — lite queue loads have `CoverArt = null`, so skip still decoded late.

**Worked:** Key decoded bitmaps by track id and album; remember album-grid thumbnails into the same cache; prefetch next/previous while current song plays; paint album thumbnail on click before first-track metadata returns.

**Verify:** Skip next/prev and click another album — title/cover should swap immediately after the first play of that album.

**Do not regress:** Do not require `LibraryTrack.CoverArt` to be populated for cache hits.

## 2026-08-30 — Background prefetch + no sync DB on skip refresh

**Symptom:** Small delay remains on skip/album despite cover cache.

**Failed:** Only warming cover bitmaps — stats refresh still hit DB synchronously on UI thread after every skip.

**Worked:** `NowPlayingPrefetchService` preloads first tracks + cover bytes for visible albums and queue neighbors; `WarmSync` for in-memory bytes; async stats-only DB refresh.

**Verify:** Browse Home briefly, then skip — chrome should update immediately; first album play after Home browse should show track title without pause.

**Do not regress:** Do not call `GetTrackById` synchronously from `RefreshNowPlayingSecondary`.

## 2026-08-30 — Jump back in hard bump + progress bar lag

**Symptom:** Jump back in / recently played album moves with a slight delay and hard snap; progress bar and time labels feel sluggish on skip.

**Failed:** Full `ShowHome()` on `TrackStarted` (second hard carousel reload ~1s later); `ItemsSource = null` rebind for Jump back in; `Math.Max` on slider fraction (stuck near end of previous track after skip).

**Worked:** `ObservableCollection<LibraryAlbum>` with `Move` to front; eased scroll animation; optimistic bump on album click with skip of duplicate bump when track starts; `ResetPositionChromeForTrack` on track id change; per-frame `CompositionTarget.Rendering` with `allowBackward` on reset.

**Verify:** Click album ▶ on Home — album slides to front once, no second jump when audio starts; skip — slider snaps to 0 and advances smoothly every frame.

**Playtest:** 2026-08-30 — August confirmed v0.13.34 is **exactly** the target feel. This is the gold standard for future Hub now-playing work.

**Do not regress:** Do not call `ShowHome()` from `Playback_OnTrackStarted` for carousel refresh; do not null/rebind Jump back in ItemsSource on optimistic bump.

## 2026-08-30 — Crossfade seek bar jump (regression)

**Symptom:** With crossfade on, progress bar jumps backward near track end or after click; worked in 0.12.x.

**Failed:** `TrackPreview` on crossfade start called `ApplyNowPlayingChrome` for next track → `ResetPositionChromeForTrack` zeroed bar while outgoing song still playing; per-frame render used wrong duration.

**Worked:** Skip TrackPreview chrome during `IsInCrossfadeTransition`; monotonic slider from `Playback.Position` on outgoing stream until handoff; on track change resume at actual position when playback already on that track.

**Verify:** Crossfade enabled → play through end of song (bar stays forward); click seek bar multiple times mid-song; handoff lands at correct offset on next track.

**Do not regress:** Do not reset position chrome from TrackPreview during crossfade overlap.

## 2026-08-30 — Tray Quit NullReferenceException

**Symptom:** "Object reference not set to an instance of an object" when choosing Quit from system tray.

**Failed:** `Application.Shutdown()` synchronously inside the tray context-menu click handler — teardown disposed `NotifyIcon` while the menu was still closing.

**Worked:** `Dispatcher.BeginInvoke` to quit after the menu click returns; detach `ContextMenuStrip` before dispose; guard `CleanupOnRealClose` against double-run.

**Verify:** Tray → Quit — app exits with no error dialog.

**Do not regress:** Do not call `Shutdown()` inline from WinForms tray menu handlers.

## 2026-08-30 — Seek bar repeat click snaps back (regression)

**Symptom:** First click-to-seek works; later clicks on same song jump back or don't seek; worse with crossfade.

**Failed:** Clearing `_positionScrubbing` before deferred `CommitPositionSeek`; generation counter invalidating commits; `_positionSeekCommitted` left true across scrubs.

**Worked:** v0.11.0 pattern — scrubbing stays true until after `Seek()` + pin slider/labels; `_positionSeekCommitted` reset in `BeginPositionScrub`; defer commit one `Loaded` frame for `IsMoveToPointEnabled`.

**Verify:** Click bar 5+ times at different positions (crossfade on/off); drag thumb; no snap-back between release and seek.

**Do not regress:** Never set `_positionScrubbing = false` before `CommitPositionSeek` completes.

## 2026-08-30 — Seek bar UI moves but audio doesn't follow

**Symptom:** Click/drag seek bar — thumb and time labels jump to the right spot but the song keeps playing from the old position; forward seeks worse than backward; repeat clicks unreliable.

**Failed:** Pinning UI before `Seek()` only; `RaiseState()` during seek fighting the bar; seeking `ActiveReader` during crossfade (incoming track at 0); `Math.Abs` drift resync snapping bar backward after forward seek; clearing `_positionScrubbing` before deferred commit.

**Worked:** `CancelCrossfadeForSeek()` then seek `_gapless.CurrentReader ?? _reader`; `FlushPlaybackBuffer()` (WASAPI `Stop`+`Play`) so buffered audio doesn't keep old position; `_speedProvider.ResetInterpolation()`; `PositionChanged` → `PinPositionChrome` after seek; 2s seek grace skipping drift reconcile; generation counter on commit so rapid clicks supersede stale deferred commits.

**Verify:** Click bar 5+ times forward and back on same song (crossfade on/off); drag thumb; audio and bar stay matched; bar advances smoothly while playing.

**Do not regress:** Do not skip `FlushPlaybackBuffer` after seek; do not resync drift backward during seek grace; do not commit seek without generation check when deferring one frame.

## 2026-08-30 — Seek fails near end of song (intermittent)

**Symptom:** Seek works mid-song but sometimes fails in the last seconds — bar moves but audio skips to next track or stays behind; worse with crossfade on.

**Failed:** `FlushPlaybackBuffer` Stop+Play without guarding `PlaybackStopped` (near EOF `ShouldResumeAfterSpuriousStop` returned false → auto-advance); cancelling crossfade without stopping output (audio thread race); not re-preloading `_crossfadeNextReader` after seek cancel.

**Worked:** Seek guard suppresses `PlaybackStopped` for 500ms; stop output before cancel/seek/play; `PreloadNextTrack()` after seek; 500ms crossfade auto-begin suppress (UI + audio thread); `ShouldResumeAfterSpuriousStop` uses cue-relative `Position`/`Duration`.

**Verify:** With crossfade on, seek to last 5s, last 1s, and back to middle repeatedly; drag thumb in final crossfade window; audio stays on current track at clicked position.

**Do not regress:** Never let `PlaybackStopped` advance the queue during an active seek flush.

## 2026-08-30 — Seek bar stuck at end while audio plays elsewhere

**Symptom:** Click near end of song — bar jumps to end and stays there while audio keeps playing from earlier position; pause shows real time (e.g. 0:39 not 1:41).

**Failed:** `if (Math.Abs(actual - target) > 1500) actual = target` pinned UI to click when seek didn't land; `PositionChanged` fired requested position not `Playback.Position`; drift reconcile ignored UI-ahead-of-decode; crossfade monotonic `Math.Max` locked slider at 1.0.

**Worked:** Pin to `Playback.Position` after seek (only trust click if decode lags ≤400ms forward); `PositionChanged` reports actual decode position; resync when `errorMs < -750`; per-frame clamp when interpolated > actual+300ms; allow monotonic backward on large desync.

**Verify:** Click last 10s of a track — bar and audio stay matched while playing; no stuck-at-end until pause.

**Do not regress:** Never set UI anchor to click target when decode is more than ~400ms behind.

## 2026-08-30 — Rapid seek clicks snap bar back

**Symptom:** Single seek OK; clicking around the bar many times causes snap-back; worse near end of song.

**Failed:** One commit per mouse-up with immediate Stop/Play each time; per-frame render clamped to lagging `Playback.Position` even during 2s seek grace between commits.

**Worked:** 75ms debounce — burst clicks coalesce to one seek after pause; `IsPositionSeekProtected()` skips render clamp during scrub + grace; block `PositionChanged` chrome updates during grace.

**Verify:** Click bar 10+ times at random positions quickly; bar stays at last click, audio follows, no snap-back between clicks.

**Do not regress:** Do not clamp interpolated position to decode while seek grace is active.

## 2026-08-30 — One forward seek snaps bar back for whole song

**Symptom:** Single forward click OK briefly, then bar snaps back and stays wrong for rest of track; pause shows real position.

**Failed:** Reading `Playback.Position` immediately after Stop/Play seek (stale decode); `errorMs < -750` backward drift resync after 2s grace; global render clamp whenever UI ahead of decode.

**Worked:** Pin to click target on commit; `ApplicationIdle` late-bind to decode only if within 1.5s; remove mid-song backward drift resync; clamp only in last second near EOF.

**Verify:** Click forward once mid-song — bar stays at new position and advances; repeat near end; no permanent snap-back.

**Do not regress:** Do not run backward drift resync on negative errorMs during normal mid-song playback.

## 2026-08-30 — Repeat forward seek moves UI only (not audio)

**Symptom:** First forward click works; later clicks move bar but audio stays put.

**Failed:** `PlaybackStopped` handler called `Play()` while seek guard active — restarted output at old decode position before `reader.CurrentTime` updated; UI pinned to click regardless of seek landing; 75ms debounce dropped seeks.

**Worked:** Ignore `PlaybackStopped` while `_seekGuardDepth > 0`; `Play()` only after guard exits; `shouldResume = !_isPaused`; verify/retry seek on `ApplicationIdle`; immediate per-click commit with generation dedupe.

**Verify:** Click forward 5+ times at different positions — audio and bar both jump each time.

**Do not regress:** Never call `Play()` from `PlaybackStopped` during an active `Seek()`.

## 2026-08-30 — Song title U+FFFD (Oxygène → Oxygne)

**Symptom:** Now-playing shows diamond replacement char in title (e.g. Oxygène); feels like regression but unrelated to seek bar.

**Failed:** `TagTextHelper.Clean` only at library scan — bad titles already in SQLite kept showing; `DisplayTitle` returned raw `Title` column.

**Worked:** Run `TagTextHelper.Clean` on `DisplayTitle`/`DisplayArtist`/`DisplayAlbum` at display time; when title contains U+FFFD, fall back to audio filename (e.g. `12. Oxygène.flac`).

**Verify:** Play Oxygène — title shows è, not; if filename also lacks accent, rescan after fixing file/tags.

**Do not regress:** Do not show raw DB title without `TagTextHelper.Clean` on display properties.

## 2026-08-30 — Seek bar layered patches cause stutter/UI-only seeks

**Symptom:** After multiple seek fixes, audio stutters in one spot, bar glitches, repeat forward seeks move UI only; user frustrated.

**Failed:** Stop/Play on every seek; ApplicationIdle seek retry; 2s grace + render clamp + backward drift resync stacked; `BeginPositionScrub` incremented generation twice per click.

**Worked:** v0.11-style seek — `reader.CurrentTime` + `ResetInterpolation` only; commit pins `Playback.Position`; remove retry/grace/clamp; guard `BeginPositionScrub` with `if (_positionScrubbing) return`.

**Verify:** Play through song without seeking (smooth bar); click forward 5× (audio + bar); no stutter loop at one timestamp.

**Do not regress:** Do not add ApplicationIdle seek retry; do not Stop/Play on every seek unless a measured buffer issue returns.

## 2026-08-30 — YouTube FLAC titles stored as U+FFFD (Oxygène / Équinoxe)

**Symptom:** Now-playing shows diamond in title (Oxyg�ne, �quinoxe). Filename fallback failed because the files on disk also contain U+FFFD.

**Failed:** Display-time Clean + filename fallback — both tag TITLE and NTFS name already have U+FFFD; original bytes are gone from those fields.

**Worked:** FLAC DESCRIPTION still has UTF-8 ("Oxygène", "Équinoxe"). Match corrupted TITLE to a DESCRIPTION line (`U+FFFD` → `\p{L}`). Repair SQLite on library open. Prefer existing library title over incoming FFFD on upsert.

**Verify:** Play Oxygène and Équinoxe — titles show accents, no diamond.

**Do not regress:** Do not treat a TITLE with U+FFFD as authoritative when DESCRIPTION or an existing good library title is available.

## 2026-08-30 — Always rebuild before rerun (Hub)

**Symptom:** August playtests and none of the agent’s fixes are in the running app.

**Failed:** Launching `%LocalAppData%\Programs\LocalMusicHub\LocalMusicHub.exe` or a shortcut without `scripts/build-release.ps1` first.

**Worked:** Stop Hub → `scripts/build-release.ps1` (Release build + sync) → then Start-Process. Every rerun, no exceptions.

**Verify:** After rebuild, Help/about or file version matches `Directory.Build.props`.

**Do not regress:** Never rerun Hub without a successful rebuild in the same turn.

## 2026-08-30 — Seek fails in last quarter of the bar

**Symptom:** Forward seek works in the first ~75% of the song; clicking the last quarter of the bar does not move audio (or jumps to next track).

**Failed:** Seeking to tag `Duration` past real PCM (EOF → next track); auto-crossfade starting as soon as remaining ≤ CrossfadeSeconds after a seek; RepeatButton LargeChange=1 on a 0–1 slider (click ahead → 1.0).

**Worked:** Duration = min(tag, reader.TotalTime); clamp seek 80ms before EOF; `_suppressCrossfadeForCurrentTrack` after user Seek until the track changes; pin UI to click target; `LargeChange=0.01` so RepeatButton clicks do not jump to 1.0.

**Verify:** Click last quarter of a song with crossfade on — audio stays on this track at that time and continues; bar holds.

**Do not regress:** Do not start a next-track crossfade immediately after a user seek into the fade window.
