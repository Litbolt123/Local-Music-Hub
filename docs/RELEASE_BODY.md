## Local Music Hub 0.13.0

### Playback & crossfade
- Crossfade transitions are stable across long listening sessions (no garbled audio or sudden stops).
- Seek bar stays smooth at track end during crossfade (no backward hop on the outgoing song).

### Themes
- Eight accent presets plus **Custom color…** with hex entry and a color picker.
- Live preview in Settings; contrast-aware text on light accents (amber, sunset).

### Downloader integration
- **Push ingest:** YouTube Downloader can notify Hub instantly when a music download finishes (`POST /library/ingest` on port 47385). Folder watch remains as fallback.
- Requires **YouTube Downloader 2.1.1+** with Hub integration enabled in both apps.

### Player polish
- Mini player: seek bar, volume slider, accent-colored play button.
- Tray menu hover/selection tints follow your accent color.

### Also since 0.11.0
- Settings persist reliably across rebuilds; atomic `settings.json` writes.
- Smoother seek bar motion; fixes for short tracks and crossfade position tracking.

### Updates
If you are on 0.12.x or earlier, install **LocalMusicHub-Setup-0.13.0.exe** from this release, or use Settings → Updates after 0.11.0.
