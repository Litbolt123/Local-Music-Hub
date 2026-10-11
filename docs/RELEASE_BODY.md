## Local Music Hub 0.13.55

### Playback and seek bar
- Game and loop-heavy FLACs (e.g. Harvest Moon rips with “play loop twice” in the file) show the **full** track length on the seek bar, not half the real duration.
- Position and seeking stay aligned when the FLAC decoder reports a shorter length than the library file.

### Volume
- In-app volume and Windows Volume Mixer stay independent: skipping tracks, play/pause, and the Hub volume slider no longer reset session volume to 100%.

### Still included from 0.13.52
- Reliable click-to-seek (including near the end of a song), accented title fixes, settings persistence, and now-playing bar polish.

Install **`LocalMusicHub-Setup-0.13.55.exe`** from this release. Your library under `%LocalAppData%\LocalMusicHub\` is kept.
