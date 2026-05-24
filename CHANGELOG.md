# Changelog

All notable changes to **Dynamic AI Portraits** are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project aspires to [Semantic Versioning](https://semver.org/).

## [Unreleased]

_(nothing pending)_

## [0.1.0] — 2026-05-23

First tagged release. The mod is feature-complete for v0.1 across six image
backends, three art styles, three framings, and an optional LLM prompt-engineering
path. Full pawn-state extraction + perceptual background removal + manual-only
regeneration.

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
- Romantic relations (spouse / lover wedding band).
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
- Per-style LLM system prompt (`GetLLMSystemPrompt`).
- Fallback to compiled template on LLM failure.
- "Prompt Generation" two-button toggle in settings (No / Gemini Flash Lite).

### Added — Engine

- **Perceptual YCbCr background remover** — multi-peak edge color profiling, human skin tone protection, vibrant color preservation, core zone face guard, depth-limited halo cleanup. Replaces RGB-Euclidean flood fill.
- Faction gating — only humanlikes of player faction (colonists / prisoners / slaves) trigger generation. Raiders / animals / mechs skipped.
- Per-pawn-per-save cache key (`worldId_pawnId_framing`) — cross-save isolation.
- Auto-pin on refresh — newly generated portrait becomes active automatically.
- Background remover automatically runs on every backend's output, re-encodes to PNG.

### Changed

- Default model for Pollinations: `flux` → `sana` (Pollinations consolidated their model lineup).
- Imagen default model: `imagen-3.0-fast-generate-001` → `imagen-4.0-fast-generate-001`.
- Default behaviour: **no auto-regeneration on state change**. Pawns keep their portrait until you click ↻.
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

- Dead `GetStateHash()` method (40 lines of orphaned MD5).
- Dead `dominantSkills` local variable.
- Old `splitOutputSideBySide` setting and rendering path.
- Hard-coded helmet/prop suggestions from LLM system prompt.

## [0.0.0] — initial commit

Pre-release prototype. Core HarmonyPatches + AsyncAIClient + PromptCompiler.

[Unreleased]: https://github.com/SIK-ai/rimworld-dynamic-ai-portraits/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/SIK-ai/rimworld-dynamic-ai-portraits/releases/tag/v0.1.0
