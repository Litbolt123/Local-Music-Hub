## Local Music Hub 0.13.24

### Audio clicks / zzzt (Bluetooth-friendly)
- Fixed random mid-song **clicks / zzzt** sounds — especially on wireless headphones (e.g. AirPods).
- WASAPI now uses **event-driven** refill with a larger default buffer (**300 ms**).
- Settings → **Audio buffer** includes **Bluetooth (500 ms)** if you still hear glitches on wireless.
- Short decoder reads no longer underrun the output; flat EQ no longer runs unnecessary filters.

Install **`LocalMusicHub-Setup-0.13.24.exe`** from this release. Your library under `%LocalAppData%\LocalMusicHub\` is kept.
