## Local Music Hub 0.13.17

This release replaces the broken **v0.13.0** GitHub asset (that tag shipped `LocalMusicHub-Setup-0.11.6.exe`). Install **`LocalMusicHub-Setup-0.13.17.exe`** from this release.

### Updates (What Am I Doing–style)
- **Download and run installer** from Settings or the main update card: downloads the Setup EXE, launches it, and **fully quits** (not tray).
- Update checks use the version in `LocalMusicHub-Setup-*.exe` when a release tag and asset disagree.
- Tray balloon opens the in-app update card.

### Playback & formats
- **FLAC** playback via BunLabs.NAudio.Flac (not Media Foundation); files with an ID3 prefix or wrong extension are handled more reliably.
- **OGG/Vorbis** (and other MF-unsupported types) play via dedicated decoders; unplayable queue items are skipped.
- Playlist **Play** starts the playlist correctly; `SetQueueAndPlay` starts audio in one step.
- Output device picker uses WASAPI and can hot-swap without restarting the track.

### Library & tags
- FLAC/OGG tag writes use Vorbis comments only (no ID3-on-FLAC), so Windows Explorer Details stay populated; Library tools → **Fix Windows Details**.
- Folder watching detects new Music files again (Settings no longer leaves the watcher muted).
- Album editor: refresh album from disk, artist-for-all-tracks, better Save layout.
- Spotify-style **Add to playlist…** picker and **Add songs** panel for manual playlists.

### CLI / Harbor
- `--minimized`, `--playlist "Name"`, and `--volume` (0–1) for launch integrations; second-instance playlist/volume forwarding.

### Also since 0.13.0
- Playlist mosaics, theme crash fixes, playlists vs albums UX, and scan/UI polish from 0.13.1–0.13.16.
