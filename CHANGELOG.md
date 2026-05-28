# Changelog

All notable changes to **Dynamic AI Portraits** are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project aspires to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- **RimWorld 1.5 support** — multi-version build (1.5 + 1.6). `About.xml` and `LoadFolders.xml` declare both; `build.bat` refactored to compile and deploy per-version (`1.5\Assemblies/`, `1.6\Assemblies/`, `Assemblies/`).
- **Local u2netp ONNX background removal** (image + video) — offline neural cutout via ONNX Runtime, now the primary local remover for `portrait`/`bodyshot` framings; the YCbCr flood-fill remains as a fallback. `special` framing keeps its background.
- **Korean usage guide** ([사용방법.md](사용방법.md)) for sharing.
- **Opt-in Debug Mode** — structured per-session logging that bookends game start / clean-shutdown, for backtracking issues ([DebugLog.cs](Source/DebugLog.cs)).
- **Per-image settings + combined reference image** — generation settings can be tuned per image, and matched gear sprites are stitched into a single reference image for the backend.

### Changed
- **Flat-2D art direction** — style prompts overhauled to enforce strictly flat 2D illustration (bold ink lines, cel-shaded fills) and strip photographic/realism cues (lens, depth of field).
- Docs synced to current backends/models (automatic free-Pollinations fallback, Gemini 3.1 Flash Lite + Gemma 4 26B/31B) and de-leaked (relative links, no local paths/username).

### Fixed
- Matted video playback — transparent background + reliable mute.
- Guard against portrait/video double-generation.
- Veo video: dropped unsupported `generateAudio`, steer audio via prompt.
- Matte directory-deletion race that could corrupt in-progress mattes.
- **Log leak**: `videoApiKey` and `cfBgRemovalKey` are now scrubbed by `SanitizeLog` (the Veo key was previously not sanitized).
- Background-removal mask dilation (`DilateMask`) — grows the cutout so arms/clothing aren't clipped at the edges.
- Per-frame video matting hardened for portrait/bodyshot reliability.

## [0.2.0] — 2026-05-26

### Added
- **Google Veo 3.1 Lite Video Integration**: Added support for Google Veo 3.1 Lite video generation. Automatically animates static pawn portraits into 4-second looping previews. Integrates with the gallery player and cache mapping. See [AsyncAIClient.cs](Source/AsyncAIClient.cs#L1365-L1499).
- **ModSettings Scale and Offset Controls**: Surface manual controls in [ModSettings.cs](Source/ModSettings.cs) to adjust horizontal offset, vertical offset, and portrait scale.
- **Exclude Helmets/Headgear**: Added a toggle option to exclude headgear from both the generated prompt text and native render layers.
- **Privacy & Security (Sentinel)**: 
  - Integrated an automated [SanitizeLog](Source/AsyncAIClient.cs) method to scrub API keys, tokens, and account IDs from logs, URLs, and exception messages before logging to disk.
  - Masked all sensitive key text fields in the Mod Options menu ([ModSettings.cs](Source/ModSettings.cs)) with standard password field hides.
  - Created a portable local build layout via [build_local.bat](build_local.bat) to keep local compilation paths and Windows usernames private.
- **Reset to Defaults**: Added a "Reset to Default Prompts & Settings" button in Mod Options to restore original prompt templates.
- **Organized Save Folders**: Portraits are now saved in subfolders keyed to the pawn's unique ID (`Documents/RimWorld Portraits/<PawnID>_<PawnName>/`) to prevent collisions between pawns sharing identical names. Managed by [CacheManager.cs](Source/CacheManager.cs).
- **LLM Prompt Instruction Enhancements**: Enhanced rules 4 and 8 in [PromptCompiler.cs](Source/PromptCompiler.cs#L1085) to instruct Gemini Flash/LLM to write richer visual descriptions (e.g., hair flows, volumetric highlights, texture detail, color accents) for higher-aesthetic portrait outcomes.
- **Scheduled Agents Personas**: Added and expanded [SCHEDULED_AGENTS.md](SCHEDULED_AGENTS.md) containing 50 custom RimWorld agent personas.

### Fixed & Optimized (Bolt)
- **Garbage Collection Reduction**: Replaced string concatenation for cache lookup with a lightweight [PawnFramingKey](Source/UI_AIPortraitCard.cs#L23) struct in the UI render loop, removing substantial per-frame GC pressure.
- **Memoized Cache Lookups**: Memoized `GetActiveKey` lookups and implemented a `knownMissingFiles` cache inside [UI_AIPortraitCard.cs](Source/UI_AIPortraitCard.cs) to avoid redundant, expensive disk I/O checks for missing assets.
- **Reference Sheet Optimization**: Optimized `BuildReferenceSheet` texture stitching performance in [AsyncAIClient.cs](Source/AsyncAIClient.cs).
- **UX & UI Refinement (Palette)**:
  - Added button hover highlight states to all settings buttons and Pawn Gallery thumbnails.
  - Added "API key required" status warning indicators when key fields are empty.
  - Added tooltips on generation buttons explaining in-flight states.
- **Cleanup Passes**: 
  - Ran five code hygiene and codebase health passes, removing unused fields and cleaning up redundant video playback routines in [UI_AIPortraitCard.cs](Source/UI_AIPortraitCard.cs#L954).
  - Fixed dangling `Texture2D` instances in the locked-cache dictionary, preventing memory leaks on portrait refreshes.
  - Fixed C# 5 compatibility issues in compiled outputs.

## [0.1.0] — 2026-05-23

First tagged release. The mod is feature-complete for v0.1 across six image backends, three art styles, three framings, and an optional LLM prompt-engineering path. Full pawn-state extraction + perceptual background removal + manual-only regeneration.

### Added — Backends
- **Cloudflare Workers AI** backend — free 10k req/day, then ~$0.0005/img. FLUX.1 Schnell + SDXL + Dreamshaper. Single `account_id:token` API-key field.
- **DeepInfra** backend — ~$0.0005/img, GitHub OAuth signup, OpenAI-compatible API. FLUX & SDXL.
- **Google Imagen 4 Fast** backend ($0.02/img) — true transparent PNG output.
- **Local A1111 / Forge / SD.Next / ComfyUI** backend — free, runs on your own GPU. Works with any A1111-compatible server.
- **Provider + Model dropdowns** in API Settings — pick a provider, the model dropdown auto-filters. Single API-key field with provider-specific format hints.

### Added — Pawn data extraction (high-impact fields, all rendered into prompts)
- Apparel quality (Awful → Legendary) + Stuff material (Devilstrand / Hyperweave / Thrumbofur / etc.) + damage state.
- Weapon quality + material + condition.
- Drug addictions (yayo / flake / go-juice / wake-up / smokeleaf / alcohol / psychite tea) — separate from sick state.
- Chronic conditions (cataract, frail, dementia, bad back, hearing loss, asthma, carcinoma).
- Permanent frostbite + burn scars.
- Sleep deprivation, malnutrition.
- Pregnancy trimester (Biotech).
- Royal title (Royalty DLC).
- Psylink level (Royalty DLC).
- Faction of origin + kindDef (`Pirate Raider`, `Imperial Trooper`, `Tribal Warrior`).
- Romantic relations (spouse / lover wedding ring).
- Prisoner / Slave status (slave collar visible).
- Anomaly inhumanized + ghoul detection.
- Fur color (for hasFur xenotypes).
- isFleeing → panicked expression.
- ideologyName → "devout follower of X".
- topSkill2 → secondary skill role.

### Added — UI & UX
- **Refresh button** (↻) on every portrait overlay — regenerates and auto-pins.
- **Framing buttons** (P / B / S) — per-pawn portrait, bodyshot, special scene; each cached independently.
- **Prompt Preview tab** — third settings tab. Shows last-actual-sent prompt (no API call), pawn data sheet, compiled template, LLM system instruction.
- **Pawn Gallery tab** — browse, set active, copy prompts, delete saved portraits.
- **Mod icon** appears in mod list and settings.
- Fallback-framing texture during generation (previously-generated framing shown as placeholder).

### Added — Art styles
- **Korean Webtoon** (Solo Leveling) — sharp inked line art, dramatic chiaroscuro, saturated focal colors.
- **Rick & Morty Cartoon** (Adult Swim) — thick black outlines, flat 2D fills, bulging eyes, wonky proportions.
- **16-bit Pixel** (Tactics Ogre / FF Tactics) — strict pixel grid, cel-shading bands, limited palette.

### Added — LLM prompt engineering
- **Gemini 3.1 Flash Lite** optional prompt rewriter — receives structured pawn data sheet, returns optimized creative image prompt.
- Per-style LLM system prompt ([PromptCompiler.cs](Source/PromptCompiler.cs#L1085)).
- Fallback to compiled template on LLM failure.
- "Prompt Generation" two-button toggle in settings (No / Gemini Flash Lite).

### Added — Engine
- **Perceptual YCbCr background remover** — multi-peak edge color profiling, human skin tone protection, vibrant color preservation, core zone face guard, depth-limited halo cleanup. Replaces RGB-Euclidean flood fill. See [BackgroundRemover.cs](Source/BackgroundRemover.cs).
- Faction gating — only humanlikes of player faction (colonists / prisoners / slaves) trigger generation. Raiders / animals / mechs skipped.
- Per-pawn-per-save cache key (`worldId_pawnId_framing`) — cross-save isolation.
- Auto-pin on refresh — newly generated portrait becomes active automatically.
- Background remover automatically runs on every backend's output, re-encodes to PNG.

### Changed
- Default model for Pollinations: `flux` → `sana` (Pollinations consolidated their model lineup).
- Imagen default model: `imagen-3.0-fast-generate-001` → `imagen-4.0-fast-generate-001`.
- Default behaviour: **no auto-regeneration on state change**. Pawns keep their portrait until you explicitly refresh.
- LLM system prompt: removed hardcoded helmet/prop examples; gave Gemini general creative freedom over pose/composition/lighting.

### Fixed
- Closed-eye default — explicit "eyes open" anchor in expressions + system prompt rule.
- Arm-erased-by-background-remover — capped halo BFS depth to 3 pixels.
- Duplicate "missing femur / tibia / foot / 5x toe" health summary when limb is replaced with peg leg — descendant body parts are now suppressed under an AddedPart ancestor.
- Peg legs mislabelled "cybernetic leg" — implant labels now use the hediff's actual def.label.
- Identical implant entries deduplicated and pluralized (`two peg legs` instead of `cybernetic leg, cybernetic leg`).
- Vowel-aware `a/an` article in weapon prefix (`an autopistol`, not `a autopistol`).
- Duplicate transparency clause removed from prompt (style header already declares it).
- Overlay portrait was overlapping inspect-pane tab strip — lifted 36px above.
- Overlay portrait was distorting colonist-bar mini-portraits — `PortraitsCache.Get` patch removed.
- Settings tab strip overlapping window title bar — 32px top padding added.
- Empty `nudistCount` dead-code path cleaned; nude pawns now properly detected.

### Removed
- Dead `GetStateHash()` method.
- Dead `dominantSkills` local variable.
- Old `splitOutputSideBySide` setting and rendering path.
- Hard-coded helmet/prop suggestions from LLM system prompt.

## [0.0.0] — initial commit

Pre-release prototype. Core HarmonyPatches + AsyncAIClient + PromptCompiler.

[Unreleased]: https://github.com/SIK-ai/rimworld-dynamic-ai-portraits/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/SIK-ai/rimworld-dynamic-ai-portraits/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/SIK-ai/rimworld-dynamic-ai-portraits/releases/tag/v0.1.0
