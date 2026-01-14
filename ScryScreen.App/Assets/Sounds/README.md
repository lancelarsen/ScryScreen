# ScryScreen Effect Sounds

Drop `.wav` or `.mp3` files into this folder to enable sound effects for overlays.

Expected filenames (you can rename in code later if desired):
- `rain_loop.wav` or `rain_loop.mp3` (loop)
- `sand_wind_loop.wav` or `sand_wind_loop.mp3` (loop)
- `fire_crackle_loop.wav` or `fire_crackle_loop.mp3` (loop)
- `thunder_clap.wav` or `thunder_clap.mp3` (one-shot)
- `quake_hit.wav` or `quake_hit.mp3` (one-shot)

Hourglass app filenames:
- `hourglass_sand_loop.wav` or `hourglass_sand_loop.mp3` (loop; plays while timer is running)
- `hourglass_gong.wav` or `hourglass_gong.mp3` (one-shot; plays when time hits zero)
- `hourglass_reset.wav` or `hourglass_reset.mp3` (one-shot; plays when Reset is pressed)

Recommended sources (license-friendly):
- OpenGameArt (filter for CC0 / Public Domain): https://opengameart.org/
- Pixabay Sound Effects (check each asset license): https://pixabay.com/sound-effects/
- Mixkit Sound Effects (check each asset license): https://mixkit.co/free-sound-effects/
- Freesound (license varies per asset; prefer CC0): https://freesound.org/

Search terms that tend to work well:
- Rain: "rain ambience loop", "steady rain loop"
- Sandstorm: "wind storm loop", "desert wind loop", "sandstorm wind"
- Fire: "fire crackle loop", "campfire crackling"
- Quake: "earthquake hit", "rumble hit", "impact rumble"
- Lightning: "thunder clap", "thunder strike"

Notes:
- WAV is recommended for simplest, reliable playback (MP3 is also supported).
- If a file is missing, ScryScreen will just skip that sound.
