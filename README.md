# Dynamic AI Portraits — RimWorld 1.6 Mod

Generates AI portraits of your colonists that reflect their real-time state: apparel, weapons, health, injuries, mental breaks, xenotype, ideology role, mood, royal title, addictions, pregnancy, and more. Portrait floats above the inspect pane as a clean transparent overlay whenever a colonist is selected and no inspect tab is open.

---

## Features

- **6 image-generation backends** in one dropdown — free, local, cheap-paid, premium-paid
- **3 art styles** — Korean Webtoon (Solo Leveling), Rick & Morty Cartoon, 16-bit Pixel JRPG
- **Per-pawn framing** — portrait, full-body shot, or special themed scene; each cached independently
- **Optional Gemini Flash prompt engineering** — LLM rewrites pawn data into a creative image prompt
- **Refresh button on the overlay** — regenerate any portrait in one click
- **Pawn Gallery** — browse, set active, copy prompts, delete saved portraits
- **Prompt Preview tab** — see exactly what was sent to the image API, no API call required
- **Perceptual YCbCr background removal** — preserves arms, faces, and skin tones
- **Cross-save isolation** — portraits keyed per world save
- **No auto-regeneration** — once a pawn has a portrait, it persists until you click refresh

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

The mod's API Settings tab has a **Provider** dropdown. Pick one, optionally pick a **Model**, paste an **API key** if needed. The provider dropdown is the only choice that matters — model defaults sensibly per provider.

### 🆓 Pollinations — Free, no signup
- Truly free, no account or key required
- Model: `sana` (the only one Pollinations serves now)
- ~50s per portrait, outputs JPEG (background remover post-processes)
- Best for: trying the mod with zero friction

### ☁ Cloudflare Workers AI — Best value
- **10,000 free requests/day**, then ~$0.0005/image
- Models: FLUX.1 Schnell, SDXL, Dreamshaper, SDXL Lightning
- ~2–4s per portrait
- **Setup:** sign up at [dash.cloudflare.com](https://dash.cloudflare.com), grab your **Account ID** from the dashboard right sidebar, create an **API Token** with `Workers AI Read` permission, paste both into the mod's API Key field as `account_id:token` (single colon between them)
- Best for: most users — effectively free for any colony size

### 💎 Google Imagen 4 — Best quality
- $0.02 per image (Fast tier)
- ~3s per portrait, **true transparent PNG output** (the only backend that does)
- Models: `imagen-4.0-fast`, `imagen-4.0-generate`, `imagen-4.0-ultra`, `imagen-3.0-fast`
- **Setup:** get a free API key at [aistudio.google.com/app/apikey](https://aistudio.google.com/app/apikey), paste it
- Best for: best style adherence + clean cutouts, willing to pay

### ⚡ DeepInfra — Cheapest paid
- ~$0.0005 per image, no free tier
- Models: FLUX-1-schnell, FLUX-1-dev, sdxl-turbo
- ~2–5s per portrait
- **Setup:** sign up at [deepinfra.com](https://deepinfra.com) (GitHub OAuth, ~2 min), paste your API token
- Best for: bulk generation when Cloudflare's free tier doesn't fit

### 🤗 HuggingFace Inference — Limited free
- Small monthly free credit, then pay-as-you-go
- Models: FLUX.1-schnell, FLUX.1-dev, SDXL
- ~30s–2min on cold start, then fast
- **Setup:** create a token at [huggingface.co/settings/tokens](https://huggingface.co/settings/tokens)
- Best for: occasional generation when other free tiers are exhausted

### 🖥 Local GPU — Free forever
- Runs on your own machine, no API costs ever
- Works with **AUTOMATIC1111**, **Forge**, **SD.Next**, or **ComfyUI** (A1111-compat mode)
- ~2–5s per portrait on modern dGPU, slower on integrated graphics
- **Setup:**
  1. Install a server. Easiest: [Stability Matrix](https://lykos.ai/) → one-click installs Forge
  2. Download a model (e.g. **FLUX.1 Schnell fp8** ~8GB, or **Juggernaut XL** ~6GB)
  3. Launch with `--api` flag (edit `webui-user.bat`, add `--api` to `COMMANDLINE_ARGS`)
  4. Wait for `Running on local URL: http://127.0.0.1:7860`
  5. In the mod, select **Local GPU** — default URL is already set
- Best for: anyone with NVIDIA RTX 3060+ / Apple Silicon / wants total privacy

> **Tip:** if you have a desktop + laptop, run the server on the desktop with `--listen` flag, then point the laptop at the desktop's LAN IP (e.g. `http://192.168.1.42:7860`). Game stays light, generation stays fast.

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

In API Settings → **Prompt Generation** section, switch from `No — Compiled Template` to `Gemini Flash Lite`. The mod will send pawn metadata to Gemini, which rewrites it into a creative, optimized image prompt before sending to your image backend.

- Free Google AI Studio key works (same key as Imagen if you're already using it)
- Costs ~$0.0001 per call on Gemini Flash Lite (essentially free)
- Falls back to the built-in template if Gemini fails
- Visible in the **Prompt Preview** tab — shows the actual LLM output

---

## Prompt Preview Tab

Third tab in mod settings. Pick any colonist to see:

1. **Last actual prompt sent** (read from disk, no API call)
2. **Pawn data sheet** — every field the extractor captured
3. **Compiled prompt** — what the template would produce (or LLM fallback)
4. **LLM system instruction** — only shown when Gemini Flash is on

Use this to debug "why didn't the helmet show up?" — the Apparel line on the data sheet tells you whether extraction missed it or the image model deprioritized it.

---

## Pawn Gallery Tab

Browse all saved portraits per colonist with thumbnails. For each portrait you can:

- **Set Active** — pin it as that pawn's permanent portrait
- **Delete** — remove the file
- **Click image** — copy the prompt to clipboard
- **Open Folder** — reveal `Documents/RimWorld Portraits/<PawnName>/`
- **Create New Portrait** — same as the ↻ button, generates a fresh one

---

## How Generation Triggers

The mod **never auto-regenerates** on state change. Once a pawn has a portrait it persists until you explicitly refresh:

- **First selection** of a pawn → one-time generation
- **↻ button** on the overlay → regenerate + auto-pin as active
- **Create New Portrait** in Pawn Gallery → same as ↻
- State changes (gear, mood, addiction, drafted) → **no effect**, portrait stays

Generation is faction-gated: only colonists, prisoners, and slaves of the player faction trigger generation. Raiders, traders, animals, and mechs are skipped (no wasted API calls).

---

## Building from Source

Requirements: Windows, .NET Framework 4.x (`csc.exe` at the standard path), RimWorld 1.6.

1. Edit `build.bat` and set `RIMWORLD_MANAGED` to your `RimWorldWin64_Data\Managed` folder and `HARMONY_PATH` to your Harmony `0Harmony.dll`.
2. Run `build.bat`. Compiled `AIPortraits.dll` lands in `1.6\Assemblies\` and `Assemblies\`.

---

## Compatibility

- RimWorld **1.6** only
- Requires **Harmony**
- Compatible with **Biotech**, **Ideology**, **Royalty**, and **Anomaly** DLCs (xenotypes, genes, ideology roles, royal titles, psylink, ghoul/inhumanized states)
- Patches `MainTabWindow_Inspect.ExtraOnGUI` (postfix) only — minimal mod surface area
- Tested alongside RimTalk, Simple Sidearms, Character Editor (no known conflicts)

---

## License

MIT — see [LICENSE](LICENSE). Free to use, modify, and redistribute with attribution.
