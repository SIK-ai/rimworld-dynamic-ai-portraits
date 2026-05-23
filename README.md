# Dynamic AI Portraits — RimWorld 1.6 Mod

Generates AI portraits of your colonists that reflect their real-time state: apparel, health, injuries, mental breaks, xenotype, ideology role, and mood. Portraits update automatically as your pawns change. When you select a pawn with no inspect tab open, the portrait floats above the inspect pane as a clean transparent overlay.

---

## Features

- **Dynamic generation** — portrait regenerates when pawn state changes (gear, health, mood, mental state)
- **Three art styles** — Korean manhwa anime, Western dark fantasy oil painting, 16-bit pixel-art JRPG
- **Three AI backends** — Pollinations (free, no key), HuggingFace Inference API, Google Imagen 3
- **Portrait overlay** — transparent PNG displayed above the inspect pane when a pawn is selected and no tab is open; disappears when you open any tab
- **Lock portraits** — pin any generated portrait as a pawn's permanent portrait from the Pawn Gallery
- **Pawn Gallery** — browse, set active, copy prompt, and delete saved portraits from mod settings
- **Cross-save isolation** — portraits are keyed per world-save so colonists don't bleed between saves
- **Continuity token** — re-generations maintain consistent style for the same pawn

---

## Installation

### Manual (no Steam Workshop)

1. Go to the [Releases](https://github.com/SIK-ai/rimworld-dynamic-ai-portraits/releases) page and download the latest zip, **or** click **Code → Download ZIP** on the main page.
2. Extract the folder and rename it to `AIPortraits`.
3. Move `AIPortraits/` into your RimWorld `Mods` folder:
   - **Windows (Steam):** `C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\`
   - **Windows (GOG/manual):** next to your `RimWorldWin64.exe`
4. In RimWorld, enable **Harmony** first, then **Dynamic AI Portraits**, and restart if prompted.
5. Open **Options → Mod Settings → Dynamic AI Portraits** and configure your backend.

> **Harmony** is required. Get it from the [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077) or include the DLL from your existing Harmony install.

---

## Backend Setup

### Pollinations — Free, no key

- Select **Pollinations** as the backend.
- Default model: `sana` (the only model currently available on Pollinations). Leave the API URL as-is.
- No API key needed.
- Expect ~60 s per fresh generation; results are cached so repeat generations are instant.
- **Note:** Pollinations outputs JPEG, not PNG. Portraits will have an opaque background rather than true transparency. Quality is good for a free option but noticeably below Imagen.

### HuggingFace Inference API

- Select **HuggingFace** as the backend.
- Create an account at [huggingface.co](https://huggingface.co) and generate an API token under **Settings → Access Tokens**. An API token is required — requests without one are rejected.
- Paste the token into the **API Key** field.
- Set **Model** to any image generation model ID available on the Inference API, e.g.:
  - `stabilityai/stable-diffusion-xl-base-1.0`
  - `black-forest-labs/FLUX.1-dev`
- HuggingFace provides a small monthly free credit allocation. Models go cold between requests — the first generation may take up to 2 minutes while the model loads. Pro plan ($9/month) removes rate limits.

### Google Imagen 3

- Select **Google Imagen** as the backend.
- Get an API key from [Google AI Studio](https://aistudio.google.com/app/apikey) (free tier available).
- Paste the key into the **API Key** field.
- Default model: `imagen-3.0-fast-generate-001`. If you have access to Imagen 4 preview, you can try `imagen-4.0-generate-preview-05-20`.
- Google Imagen produces the highest quality portraits and best follows the style prompts.

---

## How It Works

1. When you select a pawn, the mod extracts their current state (xenotype, apparel, health, mood, traits, ideology role, weapon, etc.).
2. That state is hashed and checked against a local disk cache. If a matching portrait exists, it is shown immediately.
3. If no cache hit, a prompt is compiled from the pawn state and sent to your configured AI backend.
4. When the image returns it is saved to cache, shown as the pawn's overlay portrait, and also written to `Documents/RimWorld Portraits/<PawnName>/`.
5. The portrait regenerates automatically when the pawn's state hash changes (e.g. they equip new armor or enter a mental break).

---

## Building from Source

Requirements: Windows, .NET Framework 4.x (`csc.exe` at `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\`), RimWorld 1.6.

1. Edit `build.bat` and set `RIMWORLD_MANAGED` to your RimWorld `RimWorldWin64_Data\Managed` folder and `HARMONY_PATH` to your Harmony `0Harmony.dll`.
2. Run `build.bat`. The compiled `AIPortraits.dll` is written to `1.6\Assemblies\` and copied to `Assemblies\`.

---

## Compatibility

- RimWorld **1.6** only
- Requires **Harmony**
- Compatible with Biotech, Ideology, and Anomaly content (xenotypes, genes, ideology roles, anomaly mental states)
- Should be compatible with most other mods — the mod only patches `MainTabWindow_Inspect.ExtraOnGUI` (postfix) and `PortraitsCache.Get` (postfix)

---

## License

MIT — free to use, modify, and redistribute with attribution.
