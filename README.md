# Dynamic AI Portraits — RimWorld 1.5 / 1.6 Mod

Generates AI portraits and looping animated previews of your colonists that reflect their real-time state: apparel, weapons, health, injuries, mental breaks, xenotype, ideology role, mood, royal title, addictions, pregnancy, and more. The portrait floats above the inspect pane as a clean transparent overlay whenever a colonist is selected and no inspect tab is open.

---

## Features

- **6 image-generation backends** in one dropdown — free, local, cheap-paid, premium-paid.
- **3 art styles** — Korean Webtoon (Manhwa), Rick & Morty Cartoon, 16-bit Pixel JRPG.
- **Per-pawn framing** — portrait, full-body shot, or special themed scene; each cached independently.
- **Animated Looping Previews** — Powered by **Google Veo 3.1 Lite**, converting static portraits into 4-second looping videos.
- **Colony Storybook & Comic Generator** — Aggregates in-game play logs, battles, tales, and desktop `rimlog.txt` logs. Generates narrative novel chapters and comic book panel layouts via Gemini. Includes an in-game illustrated reader.
- **Optional Gemini Flash prompt engineering** — LLM rewrites pawn data into a creative image prompt.
- **ModSettings Controls** — Adjust horizontal offset, vertical offset, portrait scale, and toggle helmet exclusion directly from the settings panel.
- **Refresh button on the overlay** — Regenerate any portrait in one click.
- **Pawn Gallery** — Browse saved portraits and video loops, set active states, copy prompts, and delete saved files.
- **Prompt Preview tab** — Inspect exactly what is sent to the image generation and LLM APIs.
- **Local u2netp ONNX background removal** — offline neural cutout (u2netp via ONNX Runtime) that isolates the character on `portrait` / `bodyshot` framings; optional cloud **Cloudflare Bria RMBG 1.4** for flawless matting, with a legacy YCbCr flood-fill as the last-resort fallback. The `special` framing keeps its scenic background.
- **Organized Save Folders** — Saved files are grouped into subfolders keyed by the pawn's unique ID (`Documents/RimWorld Portraits/<PawnID>_<PawnName>/`) to prevent collisions.
- **No auto-regeneration** — Portraits remain pinned to their current state until you click the refresh button.

---

## Installation

1. Download the latest release zip from [Releases](https://github.com/SIK-ai/rimworld-dynamic-ai-portraits/releases) (or **Code → Download ZIP** on the main page).
2. Extract and rename the folder to `AIPortraits`.
3. Move `AIPortraits/` into your RimWorld `Mods` folder:
   - **Windows (Steam):** `C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\`
   - **Windows (GOG):** next to `RimWorldWin64.exe`
4. In RimWorld's mod list, enable **Harmony** first, then **Dynamic AI Portraits**.
5. Open **Options → Mod Settings → Dynamic AI Portraits** and pick a provider.

> **Harmony is required.** Get it from the [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077).

---

## Backend / Provider Selection

The mod's **API Settings** tab allows configuring your preferred provider and model parameters.

### 🆓 Pollinations — Free, no signup
- Truly free, no account or key required.
- Model: `sana`
- ~50s per portrait (background remover post-processes transparency).
- Best for: trying the mod with zero friction.

### ☁ Cloudflare Workers AI — Best value
- **10,000 free requests/day**, then ~$0.0005/image.
- Models: FLUX.1 Schnell, SDXL, Dreamshaper, SDXL Lightning.
- ~2–4s per portrait.
- **Setup:** sign up at [dash.cloudflare.com](https://dash.cloudflare.com), retrieve your **Account ID** from the dashboard, create an **API Token** with `Workers AI Read` permission, and paste both into the API Key field as `account_id:token` (separated by a single colon).
- Best for: most users.

### 💎 Google Imagen 4 & Gemini (Nano Banana) — Best quality
- $0.02 per image (Fast tier).
- ~3s per portrait, **true transparent PNG output**.
- Models: `imagen-4.0-fast`, `imagen-4.0-generate`, `imagen-4.0-ultra`, `imagen-3.0-fast`, `nanobanana-2` (Gemini 3.1 Flash Image), `nanobanana` (Gemini 2.5 Flash Image), `nanobanana-pro` (Gemini 3 Pro Image).
- **Setup:** get a free API key at [aistudio.google.com/app/apikey](https://aistudio.google.com/app/apikey), paste it.
- Best for: high-quality style adherence and native transparency.

### ⚡ DeepInfra — Cheapest paid
- ~$0.0005 per image, no free tier.
- Models: FLUX-1-schnell, FLUX-1-dev, sdxl-turbo.
- ~2–5s per portrait.
- **Setup:** sign up at [deepinfra.com](https://deepinfra.com), paste your API token.
- Best for: high-speed paid generation.

### 🤗 HuggingFace Inference — Limited free
- Small monthly free credit, then pay-as-you-go.
- Models: FLUX.1-schnell, FLUX.1-dev, SDXL.
- ~30s–2min on cold start, then fast.
- **Setup:** create a token at [huggingface.co/settings/tokens](https://huggingface.co/settings/tokens).

### 🖥 Local GPU — Free forever
- Runs on your own machine, no API costs ever.
- Works with **AUTOMATIC1111**, **Forge**, **SD.Next**, or **ComfyUI** (A1111-compat mode).
- ~2–5s per portrait on modern dGPU, slower on integrated graphics.
- **Setup:**
  1. Install a server. Easiest: [Stability Matrix](https://lykos.ai/) → one-click installs Forge.
  2. Download a model (e.g. **FLUX.1 Schnell fp8** ~8GB, or **Juggernaut XL** ~6GB).
  3. Launch with `--api` flag (edit `webui-user.bat`, add `--api` to `COMMANDLINE_ARGS`).
  4. Wait for `Running on local URL: http://127.0.0.1:7860`.
  5. In the mod, select **Local GPU** — default URL is already set.

---

## 🎥 Google Veo 3.1 Lite Video loops

You can animate static pawn portraits into 4-second looping videos using Google Veo 3.1 Lite.

### Setup & Usage
1. Configure your Google API key under the Google Imagen provider settings.
2. Under the Pawn Gallery or Inspect Overlay, toggle **Video Mode** on for the pawn.
3. If an `.mp4` video doesn't exist, the mod will send the static portrait and compilation prompt to Google Veo to generate the animation loop.
4. While generating, the overlay displays "Making alive..." and falls back to rendering the static portrait.
5. Once completed, the video loops smoothly in the inspect pane overlay and gallery viewer.

---

## 📖 Colony Storybook & Comic Generator

You can compile your colony's live event history (interactions, battle events, major tales, and desktop `rimlog.txt` logs) into narrative chapters and generated comic book panels.

### Setup & Usage
1. Open **Mod Settings** and check **Enable Story Engine**.
2. Configure your desired **Generate Story Every (Days)** interval (e.g. `15` days equals 1 Quadrum).
3. (Optional) Check **Generate Comic Panels** to also trigger image generations for comic book layouts.
4. Set your LLM model (e.g. `gemini-2.5-flash`) and API Key under the **Prompt Generation** settings.
5. In game, once the configured interval days have passed, the engine generates a story chapter.
6. Click the **Open Colony Storybook** button in Mod Settings to read your story chapters and view the generated comic panels inline in the game.

---

## Advanced UI Settings & Options

The mod features a toggle to access advanced options in Mod Settings:

- **Exclude Headgear / Helmet**: Suppresses helmet rendering on the native reference sheet and excludes headgear tokens from LLM and template prompts, ensuring hair and faces remain visible.
- **Scale and Offsets**: Fine-tune the horizontal position, vertical position, and scale of the inspect pane overlay to avoid overlapping custom UI components.
- **Reset to Defaults**: A button to instantly wipe custom templates and restore the mod's built-in optimized style, negative, and system prompts.

---

## Art Styles

| Style | Aesthetic |
|---|---|
| **🎨 Korean Webtoon** | Solo Leveling — sharp inked line art, dramatic chiaroscuro, saturated focal colors |
| **📺 Rick & Morty Cartoon** | Adult Swim cartoon — thick black outlines, flat 2D fills, bulging eyes, wonky proportions |
| **🟦 Pixel / Dot** | 16-bit JRPG sprite (Tactics Ogre, Final Fantasy Tactics) — strict pixel grid, cel-shading bands |

Switching styles affects new generations. Existing cached portraits keep their original style. Click ↻ to regenerate with the current style.

---

## Per-Pawn Framing

Three small **P / B / S** buttons appear next to the ↻ refresh button on each pawn's portrait overlay:

- **P — Portrait** (default): standard bust-up
- **B — Bodyshot**: full-length character illustration
- **S — Special**: dynamic themed scene

Switching framing triggers a new generation under a separate cache key, so each pawn can have three saved portraits (one per framing). Switching back to a previously-generated framing shows the cached version instantly.

---

## Optional: Gemini Flash Prompt Engineering

In API Settings → **Prompt Generation** section, switch from `No — Compiled Template` to `Gemini Flash Lite` (or a `Gemma 4` model — **26B** or **31B**). The mod will send pawn metadata to the model, which rewrites it into a creative, optimized image prompt before sending to your image backend.

- Free Google AI Studio key works (same key as Imagen if you're already using it)
- Costs ~$0.0001 per call on Gemini Flash Lite (essentially free)
- Falls back to the built-in template if Gemini fails
- Visible in the **Prompt Preview** tab — shows the actual LLM output

---

## Building from Source

Requirements: Windows, .NET Framework 4.x (`csc.exe` at the standard path), RimWorld 1.5/1.6.

To compile the mod locally without polluting your Git status with machine-specific file paths:

1. Create a local configuration file named [build_local.bat](file:///C:/Users/SIK/Documents/antigravity/mysterious-carson/build_local.bat) in the repository root (this file is already ignored by `.gitignore`).
2. Add your local game and dependency paths to `build_local.bat`. For example:
   ```bat
   @echo off
   set "RIMWORLD_MANAGED=C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed"
   set "HARMONY_PATH=C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\Harmony\Current\Assemblies\0Harmony.dll"
   set "RIMWORLD_MODS_DIR=C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods"
   ```
3. Run [build.bat](file:///C:/Users/SIK/Documents/antigravity/mysterious-carson/build.bat). The build script will automatically detect and load your settings from `build_local.bat`, compile `AIPortraits.dll`, output it to target folders, and optionally deploy it straight to your game's `Mods` folder if `RIMWORLD_MODS_DIR` is set.

For a deeper dive into the mod's internal systems, codebase structure, and algorithms, see the [Developer Architecture Guide](DEVELOPER.md).

---

## Compatibility

- RimWorld **1.5 / 1.6**
- Requires **Harmony**
- Compatible with **Biotech**, **Ideology**, **Royalty**, and **Anomaly** DLCs (xenotypes, genes, ideology roles, royal titles, psylink, ghoul/inhumanized states)
- Patches `MainTabWindow_Inspect.ExtraOnGUI` (postfix) only — minimal mod surface area
- Tested alongside RimTalk, Simple Sidearms, Character Editor (no known conflicts)

---

## License

MIT — see [LICENSE](file:///C:/Users/SIK/Documents/antigravity/mysterious-carson/LICENSE). Free to use, modify, and redistribute with attribution.
