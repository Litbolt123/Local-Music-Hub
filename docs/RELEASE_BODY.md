## Local Music Hub 0.13.55

### Playback and seek bar
- Some loop-heavy or multi-segment FLAC files now show the **full** track length on the seek bar when the decoder reports only part of the file.
- Position and seeking stay aligned when library duration and decoder length disagree.

### Volume
- In-app volume and Windows Volume Mixer stay independent: skipping tracks, play/pause, and the Hub volume slider no longer reset session volume to 100%.

### Still included from 0.13.52
- Reliable click-to-seek (including near the end of a song), accented title fixes, settings persistence, and now-playing bar polish.

Install **`LocalMusicHub-Setup-0.13.55.exe`** from this release. Your library under `%LocalAppData%\LocalMusicHub\` is kept.
