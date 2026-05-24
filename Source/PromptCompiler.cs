using System;
using System.Collections.Generic;
using System.Text;

namespace AIPortraits
{
    public static class PromptCompiler
    {
        // ──────────────────────────────────────────────────────────────────────────
        // IMAGEN SYSTEM PROMPTS (one per art style, Imagen-only)
        // ──────────────────────────────────────────────────────────────────────────

        private const string KoreanImagenSystemPrompt =
@"Generate a semi-realistic anime character portrait for a game UI overlay.
Style: Korean manhwa webtoon art (Limbus Company JRPG aesthetic). Painterly brushwork with sharp clean line art. Rich warm/cool shadow contrast. Vibrant but controlled colors. Soft detailed skin shading. Hair with visible strand definition.
Composition: Bust or half-body shot. Single character. Subject centered.
Background: Fully transparent PNG (alpha channel). No background elements, fills, gradients, or environment behind the character whatsoever.
Content: Render the character's appearance, expression, gear, and any special effects exactly as described in the user prompt. Do not add, remove, or override any described details.";

        private const string WesternImagenSystemPrompt =
@"Generate a realistic dark fantasy character portrait for a game UI overlay.
Style: Western RPG oil painting art (Path of Exile, Pillars of Eternity aesthetic). Dramatic chiaroscuro lighting. Rich deep saturated colors. Detailed intricate rendering of costume and armor. Painterly brushwork with precise edges.
Composition: Bust or half-body shot. Single character. Subject centered.
Background: Fully transparent PNG (alpha channel). No background elements, fills, gradients, or environment behind the character whatsoever.
Content: Render the character's appearance, expression, gear, and any special effects exactly as described in the user prompt. Do not add, remove, or override any described details.";

        private const string PixelImagenSystemPrompt =
@"Generate a pixel-art character portrait for a game UI overlay.
Style: 16-bit tactical JRPG sprite (Tactics Ogre, Final Fantasy Tactics aesthetic). Strict pixel grid — every pixel sharp and deliberate. No anti-aliasing, smooth gradients, or naturalistic textures. Thin dark pixel outlines on all boundaries. Cel-shading in 2-3 flat color bands per surface. Limited palette (32-48 colors), muted but rich.
Composition: Bust or half-body shot. Single character. Subject centered.
Background: Fully transparent PNG (alpha channel). No background elements, fills, gradients, or environment behind the character whatsoever.
Content: Render the character's appearance, expression, gear, and any special effects exactly as described in the user prompt. Do not add, remove, or override any described details.";

        // ──────────────────────────────────────────────────────────────────────────
        // MAIN ENTRY
        // ──────────────────────────────────────────────────────────────────────────

        public static string CompilePositivePrompt(PawnState state, AIPortraitsSettings settings, string continuityStyleToken = null)
        {
            if (state == null) return "";

            StringBuilder p = new StringBuilder();

            switch (settings.portraitStyle)
            {
                case PortraitStyle.Realistic_Korean:  AppendKoreanRealisticHeader(p, continuityStyleToken); break;
                case PortraitStyle.Realistic_Western: AppendWesternRealisticHeader(p, continuityStyleToken); break;
                case PortraitStyle.DotPixel:          AppendPixelHeader(p, continuityStyleToken); break;
                default:                              AppendKoreanRealisticHeader(p, continuityStyleToken); break;
            }

            AppendRaceClass(p, state);
            AppendPhysical(p, state);
            AppendGear(p, state);
            p.Append(GetExpression(state) + ", ");
            AppendHealthModifiers(p, state);
            p.Append(GetBackground() + ", ");
            AppendLoreFlavor(p, state);

            if (!string.IsNullOrEmpty(settings.baseStylePrompt))
                p.Append(settings.baseStylePrompt);

            return p.ToString().TrimEnd(',', ' ');
        }

        // Returns the Imagen system prompt for the given style.
        // Only used by AsyncAIClient for the Imagen backend — other backends use the style headers in the positive prompt.
        public static string CompileImagenSystemPrompt(PortraitStyle style)
        {
            switch (style)
            {
                case PortraitStyle.Realistic_Korean:  return KoreanImagenSystemPrompt;
                case PortraitStyle.Realistic_Western: return WesternImagenSystemPrompt;
                case PortraitStyle.DotPixel:          return PixelImagenSystemPrompt;
                default:                              return PixelImagenSystemPrompt;
            }
        }

        // ──────────────────────────────────────────────────────────────────────────
        // ART STYLE HEADERS (positive prompt preamble for all backends)
        // ──────────────────────────────────────────────────────────────────────────

        private static void AppendKoreanRealisticHeader(StringBuilder p, string continuityToken)
        {
            p.Append("semi-realistic anime character portrait, Korean manhwa webtoon style, Limbus Company JRPG art aesthetic, painterly brushwork, sharp clean line art, rich warm and cool shadow contrast, vibrant saturated color palette, bust-up framing, transparent PNG background, alpha channel transparency, no background elements, ");
            if (!string.IsNullOrEmpty(continuityToken))
                p.Append(continuityToken + ", ");
        }

        private static void AppendWesternRealisticHeader(StringBuilder p, string continuityToken)
        {
            p.Append("realistic dark fantasy character portrait, Western RPG oil painting style, Path of Exile character art aesthetic, dramatic chiaroscuro lighting, rich deep colors, detailed intricate armor and costume rendering, bust-up framing, transparent PNG background, alpha channel transparency, no background elements, ");
            if (!string.IsNullOrEmpty(continuityToken))
                p.Append(continuityToken + ", ");
        }

        private static void AppendPixelHeader(StringBuilder p, string continuityToken)
        {
            p.Append("detailed 16-bit pixel-art character portrait, classic JRPG sprite style, strict pixel grid, thin dark pixel outlines, flat cel-shading color bands, limited color palette 32-48 colors, bust-up framing, transparent PNG background, alpha channel transparency, no background elements, ");
            if (!string.IsNullOrEmpty(continuityToken))
                p.Append(continuityToken + ", ");
        }

        // ──────────────────────────────────────────────────────────────────────────
        // RACE / CLASS  — the core identity sentence
        // ──────────────────────────────────────────────────────────────────────────

        private static void AppendRaceClass(StringBuilder p, PawnState state)
        {
            string genderNoun = state.gender == "female" ? "woman" : "man";
            string race = BuildRaceDescriptor(state);
            string role = BuildRoleDescriptor(state);

            p.Append("portrait of a " + state.bioAge + " year old " + race + " " + genderNoun);
            if (!string.IsNullOrEmpty(role))
                p.Append(", " + role);
            p.Append(", ");
        }

        private static string BuildRaceDescriptor(PawnState state)
        {
            string xt = (state.xenotype ?? "human").ToLower();

            if (xt == "sanguophage")    return "vampiric pale-skinned sanguophage";
            if (xt == "yttakin")        return "feline cat-eared yttakin";
            if (xt == "impid")          return "imp-blooded impid with reddish skin";
            if (xt == "dirtmole")       return "gaunt ashen-skinned dirtmole";
            if (xt == "neanderthal")    return "heavy-browed neanderthal";
            if (xt == "highmate")       return "elven sharp-featured highmate";
            if (xt == "genie")          return "slender long-limbed genie";
            if (xt == "waster")         return "radiation-scarred waster";
            if (xt == "hussar")         return "tall disciplined hussar";

            if (!string.IsNullOrEmpty(state.xenotypeName))
                return state.xenotypeName;

            List<string> raceWords = new List<string>();
            if (state.hasHorns) raceWords.Add("horned");
            if (state.hasTail)  raceWords.Add("tailed");
            if (state.hasFur)   raceWords.Add("fur-covered");

            if (raceWords.Count > 0)
                return string.Join(", ", raceWords.ToArray()) + " humanoid";

            return "human";
        }

        private static string BuildRoleDescriptor(PawnState state)
        {
            List<string> roleParts = new List<string>();

            if (!string.IsNullOrEmpty(state.ideologyRole))
                roleParts.Add(state.ideologyRole);

            if (!string.IsNullOrEmpty(state.adulthoodTitle))
                roleParts.Add(state.adulthoodTitle.ToLower());

            string skillRole = GetSkillRole(state.topSkill1, state.topSkill2);
            if (!string.IsNullOrEmpty(skillRole))
                roleParts.Add(skillRole);

            if (roleParts.Count == 0) return "";

            int max = Math.Min(roleParts.Count, 2);
            return string.Join(", ", roleParts.GetRange(0, max).ToArray());
        }

        private static string GetSkillRole(string skill1, string skill2)
        {
            string s = (skill1 ?? "").ToLower();
            if (s.Contains("shooting") || s.Contains("combat"))    return "marksman";
            if (s.Contains("melee"))                                return "brawler warrior";
            if (s.Contains("medicine"))                             return "field medic";
            if (s.Contains("crafting") || s.Contains("construct")) return "artificer";
            if (s.Contains("research"))                             return "scholar";
            if (s.Contains("art"))                                  return "artist";
            if (s.Contains("plant") || s.Contains("animal"))       return "ranger";
            if (s.Contains("social"))                               return "negotiator";
            if (s.Contains("mining"))                               return "miner";
            return "";
        }

        // ──────────────────────────────────────────────────────────────────────────
        // PHYSICAL APPEARANCE
        // ──────────────────────────────────────────────────────────────────────────

        private static void AppendPhysical(StringBuilder p, PawnState state)
        {
            if (!string.IsNullOrEmpty(state.skinColor))
                p.Append(state.skinColor + ", ");

            string buildDesc = GetBodyBuildDesc(state.bodyType);
            if (!string.IsNullOrEmpty(buildDesc))
                p.Append(buildDesc + " build, ");

            if (!string.IsNullOrEmpty(state.headShape) && state.headShape != "Average")
                p.Append(state.headShape + " face shape, ");

            string eyeDesc = GetEyeDescription(state);
            if (!string.IsNullOrEmpty(eyeDesc))
                p.Append(eyeDesc + ", ");

            if (!string.IsNullOrEmpty(state.hairStyle) && state.hairStyle != "bald")
                p.Append(state.hairColor + " " + state.hairStyle + " hair, ");
            else if (state.hairStyle == "bald")
                p.Append("shaved bald head, ");

            if (!string.IsNullOrEmpty(state.beardStyle) &&
                state.beardStyle.ToLower() != "clean shaven" &&
                state.beardStyle.ToLower() != "no beard")
                p.Append(state.beardStyle + " beard, ");

            if (!string.IsNullOrEmpty(state.tattooDef))
                p.Append("visible tattoo: " + state.tattooDef + ", ");

            foreach (string gene in state.cosmeticGenes)
            {
                string gl = gene.ToLower();
                if (gl.Contains("hair") || gl.Contains("skin") || gl.Contains("fur"))
                    continue;
                if (gene.Length < 40)
                    p.Append(gene + " (gene trait), ");
            }

            if (state.isHemogenic)
                p.Append("pale aristocratic vampire-like appearance, faint red iris glow, sharp fangs, ");

            // Pregnancy — visible bump
            if (state.pregnancyTrimester == 2)
                p.Append("visibly pregnant, mid-pregnancy showing, hand resting gently on belly, ");
            else if (state.pregnancyTrimester == 3)
                p.Append("heavily pregnant third trimester, prominent belly, glowing maternal skin, ");
        }

        private static string GetBodyBuildDesc(string bodyType)
        {
            if (string.IsNullOrEmpty(bodyType)) return "";
            string bt = bodyType.ToLower();
            if (bt.Contains("thin") || bt.Contains("slim"))  return "lean slender";
            if (bt.Contains("fat") || bt.Contains("heavy"))  return "heavyset stocky";
            if (bt.Contains("hulk") || bt.Contains("huge"))  return "massive muscular";
            if (bt.Contains("female"))                        return "graceful";
            return "";
        }

        private static string GetEyeDescription(PawnState state)
        {
            string xt = (state.xenotype ?? "human").ToLower();
            if (xt == "sanguophage") return "blood-red glowing eyes";
            if (xt == "yttakin")     return "amber slit-pupil cat eyes";
            if (xt == "impid")       return "glowing crimson demon eyes";
            if (xt == "highmate")    return "large luminous almond-shaped eyes";
            if (state.isHemogenic)   return "faintly glowing red irises";

            foreach (string gene in state.cosmeticGenes)
            {
                string gl = gene.ToLower();
                if (gl.Contains("eye") && gl.Contains("glow")) return "glowing eyes";
                if (gl.Contains("eye") && gl.Contains("dark")) return "dark irises";
            }
            return "";
        }

        // ──────────────────────────────────────────────────────────────────────────
        // GEAR — armor material + weapon
        // ──────────────────────────────────────────────────────────────────────────

        private static void AppendGear(StringBuilder p, PawnState state)
        {
            if (!string.IsNullOrEmpty(state.primaryWeapon))
            {
                string weapMaterial = GuessWeaponMaterial(state.primaryWeapon);
                string weapStyle = state.weaponType == "melee"
                    ? "holding a " + weapMaterial + state.primaryWeapon + " in hand, "
                    : "armed with a " + weapMaterial + state.primaryWeapon + " slung on back, ";
                p.Append(weapStyle);
            }
            else if (state.isViolentIncapable)
            {
                p.Append("unarmed, peaceful stance, ");
            }

            if (state.apparel.Count > 0)
            {
                string gearLine = BuildGearLine(state);
                if (!string.IsNullOrEmpty(gearLine))
                    p.Append(gearLine + ", ");
            }
        }

        private static string GuessWeaponMaterial(string weaponLabel)
        {
            string wl = weaponLabel.ToLower();
            if (wl.Contains("uranium"))                                               return "dense uranium ";
            if (wl.Contains("plasteel"))                                              return "high-tech plasteel ";
            if (wl.Contains("steel") || wl.Contains("blade"))                         return "steel ";
            if (wl.Contains("wood") || wl.Contains("tribal"))                         return "crude wooden ";
            if (wl.Contains("charge") || wl.Contains("plasma") ||
                wl.Contains("laser") || wl.Contains("pulse"))                         return "glowing energy ";
            if (wl.Contains("sniper") || wl.Contains("rifle"))                        return "military-grade ";
            if (wl.Contains("bone"))                                                  return "bone ";
            return "";
        }

        private static string BuildGearLine(PawnState state)
        {
            if (state.apparelStyle == "heavily armored")
            {
                List<string> armorPieces = new List<string>();
                for (int i = 0; i < Math.Min(state.apparel.Count, 2); i++)
                    armorPieces.Add(GuessArmorMaterial(state.apparel[i]) + state.apparel[i]);
                return "wearing heavy armor: " + string.Join(", ", armorPieces.ToArray());
            }
            if (state.apparelStyle == "noble robes")
                return "wearing ornate noble robes and regalia";
            if (state.apparelStyle == "tribal")
                return "wearing rough tribal clothing and pelts";
            if (state.apparel.Count > 0)
            {
                int max = Math.Min(state.apparel.Count, 3);
                string[] pieces = new string[max];
                for (int i = 0; i < max; i++)
                    pieces[i] = GuessArmorMaterial(state.apparel[i]) + state.apparel[i];
                return "wearing " + string.Join(", ", pieces);
            }
            return "";
        }

        private static string GuessArmorMaterial(string apparelLabel)
        {
            string al = apparelLabel.ToLower();
            if (al.Contains("plate") || al.Contains("cataphract")) return "heavy steel-plate ";
            if (al.Contains("marine") || al.Contains("power"))     return "powered composite ";
            if (al.Contains("flak"))                                return "military-grade flak ";
            if (al.Contains("duster") || al.Contains("jacket"))    return "worn leather ";
            if (al.Contains("robe") || al.Contains("noble"))       return "silk embroidered ";
            if (al.Contains("pelt") || al.Contains("tribal"))      return "rough hide ";
            if (al.Contains("bone"))                                return "carved bone ";
            if (al.Contains("uranium"))                             return "dense uranium-plate ";
            if (al.Contains("plasteel"))                            return "lightweight plasteel ";
            if (al.Contains("prestige") || al.Contains("royal"))   return "gold-trimmed ";
            return "";
        }

        // ──────────────────────────────────────────────────────────────────────────
        // EXPRESSION / MOOD
        // ──────────────────────────────────────────────────────────────────────────

        private static string GetExpression(PawnState state)
        {
            if (state.isSleeping)
                return "eyes peacefully closed, serene sleeping expression";

            if (!string.IsNullOrEmpty(state.mentalState))
            {
                string s = state.mentalState.ToLower();
                if (s.Contains("berserk") || s.Contains("rage") || s.Contains("kill"))
                    return "screaming with berserker rage, bloodshot eyes wide, murderous expression, teeth bared, veins on temples";
                if (s.Contains("wander") || s.Contains("break") || s.Contains("sob"))
                    return "weeping openly, tears streaming, shattered hollow expression";
                if (s.Contains("pyro") || s.Contains("fire"))
                    return "manic gleeful grin, wild wide eyes reflecting flames, unhinged pyromaniac expression";
                if (s.Contains("flee") || s.Contains("panic"))
                    return "terrified wide eyes, mouth open in silent scream, panicked flight expression";
                if (s.Contains("binge") || s.Contains("food"))
                    return "glazed satisfied eyes, crumbs on lips, content overeating expression";
                return "hollow haunted eyes, thousand-yard stare, vacant manic expression";
            }

            foreach (string trait in state.traits)
            {
                string tl = trait.ToLower();
                if (tl.Contains("psychopath"))                          return "cold flat emotionless gaze, slight disturbing smile";
                if (tl.Contains("abrasive"))                            return "sneering aggressive expression, challenging stare";
                if (tl.Contains("kind"))                                return "warm gentle eyes, soft empathetic smile";
                if (tl.Contains("iron-willed") || tl.Contains("tough")) return "resolute stoic expression, iron-set jaw, determined unflinching gaze";
                if (tl.Contains("neurotic"))                            return "anxious tense expression, nervous darting eyes";
                if (tl.Contains("brawler"))                             return "cocky confident smirk, battle-ready stance";
                if (tl.Contains("nimble") || tl.Contains("quick"))     return "alert focused sharp-eyed expression";
            }

            if (state.moodLevel > 0.85f) return "warm confident smile, bright proud eyes, relaxed victorious expression";
            if (state.moodLevel > 0.65f) return "calm composed expression, steady determined eyes";
            if (state.moodLevel < 0.20f) return "hollow sunken eyes, deep frown, utterly defeated exhausted expression";
            if (state.moodLevel < 0.40f) return "tired world-weary expression, heavy-lidded eyes, furrowed brow";
            return "stoic neutral expression, focused distant gaze";
        }

        // ──────────────────────────────────────────────────────────────────────────
        // HEALTH MODIFIERS
        // ──────────────────────────────────────────────────────────────────────────

        private static void AppendHealthModifiers(StringBuilder p, PawnState state)
        {
            if (state.isBloodloss)
                p.Append("pallid bloodless complexion, blood stains on clothing, blood trickling from wounds, ");
            else if (state.isSick)
                p.Append("pale sickly complexion, dark under-eyes, feverish sheen on skin, ");

            if (state.isInPain && !state.isSleeping)
            {
                if (state.painLevel > 0.7f)
                    p.Append("grimacing in severe agony, clutching wounds, desperate pained expression, ");
                else
                    p.Append("grimacing in pain, tense jaw, ");
            }

            if (state.isBurning || state.isOnFire)
                p.Append("embers and smoke rising from singed clothing, scorched skin, ");

            if (state.headInjuries.Count > 0)
            {
                if (state.headInjuries.Count > 1)
                    p.Append("multiple facial scars and head wounds, dried blood on face, ");
                else
                    p.Append(state.headInjuries[0] + ", ");
            }

            if (state.bodyInjuries.Count > 0)
                p.Append("visible body wounds and battle scars on torso, ");

            if (state.missingParts.Count > 0)
                p.Append(string.Join(", ", state.missingParts.GetRange(0, Math.Min(state.missingParts.Count, 2)).ToArray()) + ", ");

            if (state.implants.Count > 0)
            {
                bool hasArcho = false;
                foreach (string imp in state.implants)
                    if (imp.ToLower().Contains("archotech")) { hasArcho = true; break; }

                if (hasArcho)
                    p.Append("gleaming gold-white archotech implants with faint energy glow, ");
                else
                    p.Append("sleek bionic cybernetic implants with blue LED glow lines, ");
            }

            // Body condition (bucketed — only shows when actually bad)
            if (state.isExhausted)
                p.Append("sleep-deprived, dark sunken circles under eyes, weary slack posture, ");
            if (state.isMalnourished)
                p.Append("malnourished, gaunt hollow cheeks, sharp collarbones, ");

            // Chronic cosmetic conditions
            foreach (string cc in state.chronicConditions)
                p.Append(cc + ", ");

            // Permanent visible scars (frostbite, burn) — separate from active injuries
            foreach (string ps in state.permanentScars)
                p.Append(ps + ", ");

            // Drug addictions — each has distinctive visual signs
            foreach (string addict in state.addictions)
            {
                string al = addict.ToLower();
                if      (al.Contains("yayo"))      p.Append("hollow-eyed yayo addict, faint nasal redness, twitchy expression, ");
                else if (al.Contains("flake"))     p.Append("flake addict, sunken cheeks, dilated pupils, ");
                else if (al.Contains("go-juice"))  p.Append("go-juice user, wired tense gaze, faint veining on temples, ");
                else if (al.Contains("wake-up"))   p.Append("wake-up addict, jittery alert stare, ");
                else if (al.Contains("smokeleaf")) p.Append("smokeleaf user, yellowed fingertips, hazy laid-back eyes, ");
                else if (al.Contains("alcohol"))   p.Append("alcoholic, ruddy nose with broken capillaries, bloodshot eyes, ");
                else if (al.Contains("psychite"))  p.Append("psychite tea drinker, jittery focus, ");
            }
        }

        // ──────────────────────────────────────────────────────────────────────────
        // BACKGROUND
        // ──────────────────────────────────────────────────────────────────────────

        private static string GetBackground()
        {
            return "isolated on a fully transparent background, alpha channel transparency, no background elements, pure transparent PNG";
        }

        // ──────────────────────────────────────────────────────────────────────────
        // LORE FLAVOR (traits, ideology accent, skill aura)
        // ──────────────────────────────────────────────────────────────────────────

        private static void AppendLoreFlavor(StringBuilder p, PawnState state)
        {
            if (state.isPsychicSensitive)
                p.Append("faint psychic energy aura radiating from the character, subtle shimmering halo, ");

            if (state.isViolentIncapable)
                p.Append("gentle peaceful aura, soft warm light around the figure, ");

            if (!string.IsNullOrEmpty(state.ideologyRole))
            {
                string role = state.ideologyRole.ToLower();
                if (role.Contains("archon") || role.Contains("high"))
                    p.Append("regal authoritative bearing, faint divine golden light, ");
                else if (role.Contains("headliner") || role.Contains("performer"))
                    p.Append("theatrical dramatic pose, spotlight-like lighting, ");
                else if (role.Contains("tender") || role.Contains("healer"))
                    p.Append("gentle healer aura, soft green-white light, ");
                else if (role.Contains("executor") || role.Contains("justicar"))
                    p.Append("grim executioner bearing, cold harsh light, chains and authority symbols, ");
            }

            // Rim light from ideology color — applies to all styles since it's on the character, not the background
            if (!string.IsNullOrEmpty(state.favoriteColor))
                p.Append("subtle " + state.favoriteColor + " rim light on the character, ");

            // ── EXTENDED FLAVOR ────────────────────────────────────────────────────

            // Royal title (Royalty DLC) — regal bearing scales with rank
            if (!string.IsNullOrEmpty(state.royalTitle))
            {
                string rt = state.royalTitle.ToLower();
                if      (rt.Contains("stellarch") || rt.Contains("consul"))
                    p.Append("supreme imperial " + state.royalTitle + " bearing, magnificent regalia, faint divine aura, ");
                else if (rt.Contains("duke") || rt.Contains("count"))
                    p.Append("high noble " + state.royalTitle + " bearing, ornate imperial regalia, gilded sigil at collar, ");
                else if (rt.Contains("baron") || rt.Contains("praetor"))
                    p.Append("noble " + state.royalTitle + " bearing, refined imperial garb, ");
                else if (rt.Contains("knight") || rt.Contains("freeholder") || rt.Contains("yeoman"))
                    p.Append(state.royalTitle + " of the Empire, dignified bearing, subtle imperial sigil, ");
                else
                    p.Append(state.royalTitle + " of the Empire, ");
            }

            // Psylink — visible neural enhancement at high levels
            if (state.psylinkLevel >= 4)
                p.Append("powerful psycaster level " + state.psylinkLevel + ", glowing blue psychic energy at temples, visible neural lace, otherworldly presence, ");
            else if (state.psylinkLevel >= 2)
                p.Append("psycaster, faint blue glow at temples, subtle neural enhancement, ");
            else if (state.psylinkLevel == 1)
                p.Append("psylink novice, faint psychic shimmer, ");

            // Faction of origin / pawnKind — heavy aesthetic cue
            if (!string.IsNullOrEmpty(state.pawnKind))
            {
                string pk = state.pawnKind.ToLower();
                if      (pk.Contains("pirate") || pk.Contains("raider"))
                    p.Append("former pirate raider, hard-bitten weathered face, scars and crude tattoos, ");
                else if (pk.Contains("imperial") || pk.Contains("trooper") || pk.Contains("janissary"))
                    p.Append("imperial trooper bearing, disciplined military stance, ");
                else if (pk.Contains("tribal") || pk.Contains("warrior"))
                    p.Append("tribal warrior heritage, ritual face markings, ");
                else if (pk.Contains("mercenary") || pk.Contains("merc"))
                    p.Append("hardened mercenary look, professional combat-worn, ");
                else if (pk.Contains("noble") || pk.Contains("yeoman"))
                    p.Append("noble-born bearing, refined manners, ");
            }

            // Romance — wedding band / matching token
            if (state.hasSpouse)
                p.Append("wedding band visible on ring finger, softness in expression, ");
            else if (state.hasLover)
                p.Append("subtle love-token or matching pendant, ");

            // Captivity — slave collar is a strong visual; prisoner posture
            if (state.isSlave)
                p.Append("metal slave collar around neck, weary downcast eyes, ");
            else if (state.isPrisoner)
                p.Append("prisoner bearing, dirty clothes, downcast guarded eyes, ");

            // Anomaly horrors
            if (state.isInhumanized)
                p.Append("inhumanized — unsettlingly calm void-touched gaze, faintly black sclera, disturbing serene smile, ");
            if (state.isGhoul)
                p.Append("ghoul — pallid corpse-grey skin, sunken hollow eyes, claw-like hands, slack jaw, ");
        }

        // ──────────────────────────────────────────────────────────────────────────
        // NEGATIVE PROMPT (SD backends — HuggingFace, Pollinations)
        // ──────────────────────────────────────────────────────────────────────────

        public static string CompileNegativePrompt(AIPortraitsSettings settings)
        {
            string baseNeg = settings.baseNegativePrompt;
            return baseNeg + ", photorealistic photograph, blurry, 3d render, anti-aliasing, multiple characters in one frame, solid background, gradient background, textured background, complex background, any non-transparent background behind the character, deformed anatomy, bad anatomy, extra limbs, watermark, text, signature";
        }

        // ──────────────────────────────────────────────────────────────────────────
        // CONTINUITY TOKEN
        // ──────────────────────────────────────────────────────────────────────────

        public static string GetContinuityToken(PortraitStyle style)
        {
            switch (style)
            {
                case PortraitStyle.Realistic_Korean:
                    return "consistent semi-realistic anime style, same painterly brushwork, same warm and cool color contrast, same sharp line art, same facial proportions";
                case PortraitStyle.Realistic_Western:
                    return "consistent dark fantasy oil painting style, same chiaroscuro lighting, same rich color depth, same level of armor detail and rendering quality";
                case PortraitStyle.DotPixel:
                    return "consistent 16-bit pixel-art style, same pixel grid density, same thin dark outlines, same cel-shading band style, same limited color palette";
                default:
                    return "";
            }
        }
    }
}
