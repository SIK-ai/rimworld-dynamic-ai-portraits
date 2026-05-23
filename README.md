# Dynamic AI Portraits — RimWorld 1.6 Mod

Dynamically generates high-quality AI portraits of your colonists that reflect their real-time state: apparel, health, injuries, mental breaks, xenotype, ideology role, mood, and more.

Portraits update automatically as your pawns change. Lock a favourite image per-pawn, browse and manage saved portraits from the mod settings gallery.

---

## Features

- **Dynamic generation** — portrait regenerates when pawn state changes (gear, health, mood, mental state)
- **Three art styles** — Korean manhwa anime, Western dark fantasy oil painting, 16-bit pixel-art JRPG
- **Three AI backends** — Pollinations (free, no key), HuggingFace Inference API, Google Imagen 3
- **Transparent PNG portraits** — overlaid on the inspect pane when a pawn is selected and no tab is open
- **Lock portraits** — pin any generated portrait as a pawn's permanent portrait
- **Pawn Gallery** — browse, set active, copy prompt, and delete saved portraits from the settings UI
- **Cross-save isolation** — portraits are keyed per world-save so colonists don't bleed between saves
- **Continuity token** — re-generations of the same pawn maintain consistent style

---

## Installation

1. Download and extract this repository into your RimWorld `Mods` folder:
   ```
   <RimWorld>/Mods/AIPortraits/
   ```
2. Enable **Harmony** (required dependency) and **Dynamic AI Portraits** in the mod list.
3. Open **Options → Mod Settings → Dynamic AI Portraits** and configure your backend.

---

## Backend Setup

### Pollinations (Free — no API key required)
- Select **Pollinations** in the backend dropdown.
- Default model: `flux`. Leave API URL as-is.
- No key needed. Rate-limited; expect ~10–30 s per generation.

### HuggingFace Inference API
- Select **HuggingFace**.
- Paste your HuggingFace API token into the **API Key** field.
- Set **Model** to any compatible SDXL/SD model ID, e.g. `stabilityai/stable-diffusion-xl-base-1.0`.

### Google Imagen 3
- Select **Google Imagen**.
- Paste your Google AI Studio API key into the **API Key** field.
- Default model: `imagen-3.0-fast-generate-001`.

---

## Building from Source

Requirements: .NET Framework 4.x (`csc.exe`), RimWorld 1.6, Harmony mod installed.

1. Edit `build.bat` and set `RIMWORLD_MANAGED` and `HARMONY_PATH` to match your RimWorld install location.
2. Run `build.bat`. The compiled `AIPortraits.dll` is placed in `1.6\Assemblies\` and `Assemblies\`.

---

## Compatibility

- RimWorld **1.6** only
- Requires **Harmony** (available on Steam Workshop)
- Compatible with Biotech, Ideology, Anomaly xenotypes and genes

---

## License

MIT — free to use, modify, and redistribute with attribution.
