using System;
using System.Collections.Generic;
using System.Text;

namespace AIPortraits
{
    public static class PromptCompiler
    {
        // Picks special-shot concepts AT RANDOM (random per generation, not role-mapped).
        private static readonly System.Random SpecialRng = new System.Random();

        // ──────────────────────────────────────────────────────────────────────────
        // IMAGEN SYSTEM PROMPTS (one per art style, Imagen-only)
        // ──────────────────────────────────────────────────────────────────────────



        // ──────────────────────────────────────────────────────────────────────────
        // MAIN ENTRY
        // ──────────────────────────────────────────────────────────────────────────

        public static string CompilePositivePrompt(PawnState state, AIPortraitsSettings settings, string continuityStyleToken = null)
        {
            if (state == null) return "";

            StringBuilder p = new StringBuilder();

            switch (settings.portraitStyle)
            {
                case PortraitStyle.Realistic_Korean:  AppendKoreanRealisticHeader(p, continuityStyleToken, state, settings); break;
                case PortraitStyle.Realistic_Western: AppendWesternRealisticHeader(p, continuityStyleToken, state, settings); break;
                case PortraitStyle.DotPixel:          AppendPixelHeader(p, continuityStyleToken, state, settings); break;
                default:                              AppendKoreanRealisticHeader(p, continuityStyleToken, state, settings); break;
            }

            // Hard "solo" enforcement. Cloudflare/FLUX does NOT accept a negative prompt, so the
            // "multiple characters" negative is ignored — and FLUX loves to add a second person to
            // full-body shots. State solo explicitly and early in the positive prompt.
            p.Append("solo, single character, only one person, alone in frame, no other people, no second person, ");

            AppendRaceClass(p, state, settings);
            AppendPhysical(p, state);
            AppendGear(p, state);
            p.Append(GetExpression(state, settings) + ", ");
            // Held cute/skill props (lollipop, beer mug, smokeleaf joint, mining ore, etc.)
            // are intentionally DISABLED: they cluttered the portrait and animated badly in
            // video (the pawn waving "a random stone", smoke wisps, etc.). Health modifiers
            // now always render their own subtle facial visuals (no prop substitution).
            AppendHealthModifiers(p, state, false);
            // (Transparency is already declared once at the top of the prompt by the style
            // header — no need to repeat it via GetBackground() and create a duplicate clause.)
            AppendLoreFlavor(p, state, settings);

            if (!string.IsNullOrEmpty(settings.baseStylePrompt))
                p.Append(settings.baseStylePrompt);

            return p.ToString().TrimEnd(',', ' ');
        }

        // Returns the Imagen system prompt for the given style.
        // Only used by AsyncAIClient for the Imagen backend — other backends use the style headers in the positive prompt.
        public static string CompileImagenSystemPrompt(PortraitStyle style, AIPortraitsSettings settings, string framing)
        {
            bool isSpecial = framing == "special";
            string bg = isSpecial 
                ? "Background: A detailed thematic background reflecting their role, skill, or environment (no white background, no transparent background)."
                : "Background: Isolated on a solid white background, flat clean background, no environment, no gradients, no shadows.";
            
            string compKorean = isSpecial
                ? "Composition: Dynamic scene framing and camera angle (like a dramatic backshot, wide-angle selfie, or low-angle action shot) reflecting their role or environment."
                : "Composition: Bust or half-body shot. Single character. Subject centered. Dramatic light source from one side.";

            string compWestern = isSpecial
                ? "Composition: Dynamic scene framing and camera angle (like a dramatic backshot, wide-angle selfie, or low-angle action shot) reflecting their role or environment."
                : "Composition: Bust or half-body shot. Single character. Subject centered.";

            string compPixel = isSpecial
                ? "Composition: Dynamic scene framing and camera angle (like a dramatic backshot, wide-angle selfie, or low-angle action shot) reflecting their role or environment."
                : "Composition: Bust or half-body shot. Single character centered.";

            switch (style)
            {
                case PortraitStyle.Realistic_Korean:
                    return @"Generate a Korean webtoon manhwa character portrait for a game UI overlay.
Style: " + settings.manhwaStylePrompt + @"
" + compKorean + @"
" + bg + @"
Content: Render the character's appearance, expression, gear, and any special effects exactly as described in the user prompt. Do not add, remove, or override any described details.
Eyes: The character's eyes must be OPEN and clearly visible by default. Only render closed eyes if the prompt explicitly says the character is sleeping or dead. Do not default to closed-eye expressions even when the art style traditionally uses them.";

                case PortraitStyle.Realistic_Western:
                    return @"Generate an Adult Swim cartoon character portrait for a game UI overlay.
Style: " + settings.cartoonStylePrompt + @"
" + compWestern + @"
" + bg + @"
Content: Render the character's appearance, expression, gear, and any special effects exactly as described in the user prompt. Do not add, remove, or override any described details.
Eyes: The character's eyes must be OPEN and clearly visible by default — bulging cartoon eyes with small dot pupils. Only render closed eyes if the prompt explicitly says the character is sleeping or dead.";

                case PortraitStyle.DotPixel:
                default:
                    return @"Generate a high-quality 16-bit JRPG retro pixel-art character portrait for a game UI overlay.
Style: " + settings.pixelStylePrompt + @"
" + compPixel + @"
" + bg + @"
Content: Render the character's appearance, clean expression, and gear exactly as described in the user prompt. Keep the portrait looking clean, professional, and aesthetically pleasing. Never render closed eyes, screaming faces, or distorted facial features unless explicitly requested.";
            }
        }

        // ──────────────────────────────────────────────────────────────────────────
        // ART STYLE HEADERS (positive prompt preamble for all backends)
        // ──────────────────────────────────────────────────────────────────────────

        private static void AppendKoreanRealisticHeader(StringBuilder p, string continuityToken, PawnState state, AIPortraitsSettings settings)
        {
            string frameText = GetFramingHeaderPart(state);
            string bgText = GetBackgroundText(state);
            string subjectType = "character portrait";
            if (state.framing == "bodyshot") subjectType = "character full body illustration";
            else if (state.framing == "special") subjectType = "character dynamic scene illustration";

            p.Append(settings.manhwaStylePrompt + ", " + subjectType + ", " + frameText + ", " + bgText + ", ");
            p.Append("flat 2D hand-drawn illustration, bold clean ink linework, flat cel-shaded color fills, hard-edged anime shadows, bright rim highlights, strictly flat 2D art style, no lens blur, no depth of field, ");
            if (!string.IsNullOrEmpty(continuityToken))
                p.Append(continuityToken + ", ");
        }

        private static void AppendWesternRealisticHeader(StringBuilder p, string continuityToken, PawnState state, AIPortraitsSettings settings)
        {
            string frameText = GetFramingHeaderPart(state);
            string bgText = GetBackgroundText(state);
            string subjectType = "character portrait";
            if (state.framing == "bodyshot") subjectType = "character full body illustration";
            else if (state.framing == "special") subjectType = "character dynamic scene illustration";

            p.Append(settings.cartoonStylePrompt + ", " + subjectType + ", " + frameText + ", " + bgText + ", ");
            p.Append("flat 2D cartoon illustration, thick bold black outlines, flat color fills, minimal flat cel shadows, strictly flat 2D art style, no lens blur, no depth of field, ");
            if (!string.IsNullOrEmpty(continuityToken))
                p.Append(continuityToken + ", ");
        }

        private static void AppendPixelHeader(StringBuilder p, string continuityToken, PawnState state, AIPortraitsSettings settings)
        {
            string frameText = GetFramingHeaderPart(state);
            string bgText = GetBackgroundText(state);
            string subjectType = "character portrait";
            if (state.framing == "bodyshot") subjectType = "character full body sprite";
            else if (state.framing == "special") subjectType = "character dynamic scene illustration";

            p.Append(settings.pixelStylePrompt + ", " + subjectType + ", " + frameText + ", " + bgText + ", ");
            p.Append("crisp pixel-art grid, sharp pixel alignment, simple flat JRPG cel-shaded lighting, limited color palette, strictly flat 2D art style, ");
            if (!string.IsNullOrEmpty(continuityToken))
                p.Append(continuityToken + ", ");
        }

        private static string GetFramingHeaderPart(PawnState state)
        {
            string framing = state.framing;
            if (framing == "bodyshot") return "full length full body shot, head-to-toe framing showing the entire body including legs and boots, standing pose, no cropping at the knees or ankles, complete outfit visible";
            if (framing == "special")
            {
                // Special = a random, FUNNY, trait-aware comedy scene (compiled/video path).
                return GetFunnySpecialScene(state);
            }
            return "bust-up framing";
        }

        private static string GetBackgroundText(PawnState state)
        {
            if (state.framing != "special")
                return "isolated on a solid white background, flat clean background, no environment, no gradients, no shadows, crisp sharp edges with the background";

            // Special: the funny comedy scene (GetFunnySpecialScene) already describes its own
            // setting, so don't append a second, conflicting background here.
            return "";
        }

        // A random, FUNNY, trait-aware comedy scene for the "special" framing. ~50% of the time
        // (when the pawn has a relevant trait/mood/skill) it picks a gag matched to them; otherwise
        // a universal meme gag. Picked fresh each generation (SpecialRng) so re-rolls vary. PG only.
        private static string GetFunnySpecialScene(PawnState state)
        {
            List<string> memes = new List<string>();
            memes.Add("a 'this is fine' meme moment: calmly sipping a hot drink with a serene little smile while the base quietly burns down behind them");
            memes.Add("an over-confident dating-app SELFIE held at arm's length, ring-light glow and a cheesy smolder");
            memes.Add("a derpy candid snapshot caught mid-sneeze, comically surprised face with motion lines");
            memes.Add("a heroic JRPG cover-art splash treating an ordinary everyday tool like a legendary weapon, wind-swept with sparkles and lens flare");
            memes.Add("a smug gym-mirror flex selfie with a ridiculous over-the-top muscle pose");
            memes.Add("an enthusiastic double thumbs-up straight at the viewer with a goofy grin while confetti rains down");
            memes.Add("brooding way too dramatically in heavy rain with a single cinematic tear and excessive anime sparkles");
            memes.Add("a stiff, awkward school-picture-day pose in front of a cheesy painted studio backdrop");
            memes.Add("frozen mid-air doing a completely unnecessary triumphant backflip with comic impact stars");
            memes.Add("proudly presenting a 'World's Best Colonist' mug to the camera like a grand championship trophy");
            memes.Add("a fabulous fashion-runway model pose in mismatched, tattered survival gear with a sassy diva expression");
            memes.Add("posing victoriously with one boot resting on a sprung deadfall trap they obviously just stepped in");

            List<string> matched = new List<string>();
            if (state.traits != null)
            {
                foreach (string t in state.traits)
                {
                    string tl = t.ToLower();
                    if (tl.Contains("pyroman")) matched.Add("cackling with a manic gleeful grin while holding a flaming torch aloft, little comedic fires and smoke puffs popping up everywhere");
                    else if (tl.Contains("cannibal")) matched.Add("smiling far too innocently while holding a suspicious wrapped 'mystery meat' sandwich, a guilty sweat-drop on the brow");
                    else if (tl.Contains("sloth") || tl.Contains("lazy")) matched.Add("lazily napping in a hammock with a little snore bubble, totally unbothered while comedic chaos unfolds");
                    else if (tl.Contains("brawler") || tl.Contains("tough")) matched.Add("throwing a cocky boxing pose with a big comic 'POW' impact effect and a confident smirk");
                    else if (tl.Contains("kind")) matched.Add("beaming warmly while joyfully swarmed by a pile of adoring cute animals");
                    else if (tl.Contains("nimble") || tl.Contains("quick")) matched.Add("frozen mid-spin in a flashy, totally needless action-hero pose with motion lines and sparkles");
                    else if (tl.Contains("transhuman")) matched.Add("proudly showing off a gleaming bionic limb to the camera like the hottest new gadget, smug techie grin");
                    else if (tl.Contains("jealous") || tl.Contains("abrasive")) matched.Add("photobombing the frame with a comically exaggerated death-glare and tightly crossed arms");
                    else if (tl.Contains("nudist")) matched.Add("a coy beach-vacation pose behind a strategically placed potted plant, cheeky wink, fully and modestly covered");
                }
            }
            if (state.moodLevel > 0.65f) matched.Add("throwing a tiny one-person party with a party hat, streamers and confetti and an ecstatic grin");
            else if (state.moodLevel < 0.35f) matched.Add("slumped under a personal little comedic rain-cloud, clutching a single wilted flower with over-dramatic gloom");
            if (!string.IsNullOrEmpty(state.mentalState)) matched.Add("an unhinged manic grin in the middle of cartoonish chaos, googly wide eyes and comic 'BONK' stars");

            string skill = (state.topSkill1 ?? "").ToLower();
            if (skill.Contains("cook")) matched.Add("buried up to the neck in a giant mountain of lavish meals, cheeks comically stuffed, blissful expression");
            else if (skill.Contains("min")) matched.Add("heroically hoisting a comically oversized glowing ore chunk overhead like a bodybuilding trophy");
            else if (skill.Contains("shoot") || skill.Contains("melee")) matched.Add("an absurdly over-dramatic action-movie battle pose with explosions and big 'BOOM' text behind them");
            else if (skill.Contains("research") || skill.Contains("intellect")) matched.Add("a bored-genius pose surrounded by floating nonsensical equations and a tiny smoking exploded beaker");
            else if (skill.Contains("animal")) matched.Add("getting affectionately mobbed and face-licked by their own bonded animals, flailing with a delighted laugh");
            else if (skill.Contains("plant")) matched.Add("proudly presenting a single, comically enormous prize vegetable that gleams with pride");
            else if (skill.Contains("art")) matched.Add("dramatically unveiling a lumpy, questionable self-sculpture with way too much confidence");
            else if (skill.Contains("social")) matched.Add("an over-the-top social-media influencer pose with sparkles and finger-hearts");
            else if (skill.Contains("construct") || skill.Contains("craft")) matched.Add("posing like a proud handyman beside a wonky, comically lopsided homemade contraption");

            List<string> pool = (matched.Count > 0 && SpecialRng.Next(2) == 0) ? matched : memes;
            return pool[SpecialRng.Next(pool.Count)];
        }

        // ──────────────────────────────────────────────────────────────────────────
        // RACE / CLASS  — the core identity sentence
        // ──────────────────────────────────────────────────────────────────────────

        private static void AppendRaceClass(StringBuilder p, PawnState state, AIPortraitsSettings settings)
        {
            string genderNoun = state.gender == "female" ? "woman" : "man";
            string race = BuildRaceDescriptor(state);
            string role = BuildRoleDescriptor(state, settings);

            string introText = "portrait of a ";
            if (state.framing == "bodyshot") introText = "full length body shot of a ";
            else if (state.framing == "special") introText = "fun dynamic scene of a ";

            p.Append(introText + state.bioAge + " year old " + race + " " + genderNoun);
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

        private static string BuildRoleDescriptor(PawnState state, AIPortraitsSettings settings)
        {
            // Priority: skill role (drives prop visuals) > adulthood title > ideology role.
            // Ideology is last because it is separately described in AppendLoreFlavor.
            List<string> roleParts = new List<string>();

            string skillRole = GetSkillRole(state.topSkill1, state.topSkill2);
            if (!string.IsNullOrEmpty(skillRole))
                roleParts.Add(skillRole);

            if (!string.IsNullOrEmpty(state.adulthoodTitle))
                roleParts.Add(state.adulthoodTitle.ToLower());

            if (settings.includeIdeology && !string.IsNullOrEmpty(state.ideologyRole))
                roleParts.Add(state.ideologyRole);

            if (roleParts.Count == 0) return "";

            int max = Math.Min(roleParts.Count, 2);
            return string.Join(", ", roleParts.GetRange(0, max).ToArray());
        }

        private static string GetSkillRole(string skill1, string skill2)
        {
            string primary   = SkillToRole(skill1);
            string secondary = SkillToRole(skill2);

            if (string.IsNullOrEmpty(primary))   return secondary ?? "";
            if (string.IsNullOrEmpty(secondary)) return primary;
            if (primary == secondary)            return primary;
            return primary + " and " + secondary;
        }

        private static string SkillToRole(string skill)
        {
            if (string.IsNullOrEmpty(skill)) return "";
            string s = skill.ToLower();
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
            {
                string tempHelmet;
                BuildGearLine(state, out tempHelmet);
                if (!IsFaceCoverLabel(tempHelmet))
                {
                    p.Append(state.beardStyle + " beard, ");
                }
            }

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

            // Fur (for fur-bearing xenotypes like Yttakin, custom furred genelines)
            if (state.hasFur && !string.IsNullOrEmpty(state.furColor))
                p.Append("thick " + state.furColor + " fur covering body, visible at neck and arms, ");

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

        private static int GetPawnSeed(PawnState state)
        {
            if (string.IsNullOrEmpty(state.pawnId)) return 0;
            int hash = 0;
            foreach (char c in state.pawnId)
            {
                hash = (hash * 31) + c;
            }
            return Math.Abs(hash);
        }

        private static bool IsFaceCoverLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return false;
            string l = label.ToLower();
            return l.Contains("mask") || l.Contains("veil") || l.Contains("goggles") || l.Contains("visor");
        }

        private static void AppendGear(StringBuilder p, PawnState state)
        {
            string helmet;
            string apparelLine = BuildGearLine(state, out helmet);

            if (!string.IsNullOrEmpty(helmet))
            {
                if (IsFaceCoverLabel(helmet))
                {
                    // Face covers are always worn on the face/eyes to avoid weird rendering on the neck
                    if (helmet.ToLower().Contains("goggles") || helmet.ToLower().Contains("visor"))
                    {
                        p.Append("wearing their " + helmet + " over their eyes, ");
                    }
                    else
                    {
                        p.Append("wearing their " + helmet + " on their face, ");
                    }

                    // Standard weapon carriage when wearing face cover
                    if (!string.IsNullOrEmpty(state.primaryWeapon))
                    {
                        string weapStyle = state.weaponType == "melee"
                            ? "holding " + Article(state.primaryWeapon) + " " + state.primaryWeapon + " in hand, "
                            : "armed with " + Article(state.primaryWeapon) + " " + state.primaryWeapon + " slung on back, ";
                        p.Append(weapStyle);
                    }
                    else if (state.isViolentIncapable)
                    {
                        p.Append("unarmed, peaceful stance, ");
                    }
                }
                else
                {
                    // Always wear the helmet/hat ON THE HEAD. The previous code rotated through
                    // "held under arm / hanging from shoulder / off the weapon" poses, which read
                    // as "hat off" — especially bad in video. Force the worn-on-head pose.
                    bool hasRanged = !string.IsNullOrEmpty(state.primaryWeapon) && state.weaponType == "ranged";
                    int poseChoice = 0;

                    if (poseChoice == 0)
                    {
                        p.Append("wearing their " + helmet + " on their head, ");
                        if (!string.IsNullOrEmpty(state.primaryWeapon))
                        {
                            string weapStyle = state.weaponType == "melee"
                                ? "holding " + Article(state.primaryWeapon) + " " + state.primaryWeapon + " in hand, "
                                : "armed with " + Article(state.primaryWeapon) + " " + state.primaryWeapon + " slung on back, ";
                            p.Append(weapStyle);
                        }
                        else if (state.isViolentIncapable)
                        {
                            p.Append("unarmed, peaceful stance, ");
                        }
                    }
                    else if (poseChoice == 1)
                    {
                        p.Append("holding their " + helmet + " under one arm, ");
                        if (!string.IsNullOrEmpty(state.primaryWeapon))
                        {
                            p.Append("with their " + state.primaryWeapon + " slung on their back, ");
                        }
                    }
                    else if (poseChoice == 2)
                    {
                        p.Append("with their " + helmet + " hanging from their shoulder strap, ");
                        if (!string.IsNullOrEmpty(state.primaryWeapon))
                        {
                            string weapStyle = state.weaponType == "melee"
                                ? "holding " + Article(state.primaryWeapon) + " " + state.primaryWeapon + " in hand, "
                                : "armed with " + Article(state.primaryWeapon) + " " + state.primaryWeapon + " slung on back, ";
                            p.Append(weapStyle);
                        }
                        else if (state.isViolentIncapable)
                        {
                            p.Append("unarmed, peaceful stance, ");
                        }
                    }
                    else if (poseChoice == 3)
                    {
                        p.Append("with their " + helmet + " slung around their neck by its strap, ");
                        if (!string.IsNullOrEmpty(state.primaryWeapon))
                        {
                            string weapStyle = state.weaponType == "melee"
                                ? "holding " + Article(state.primaryWeapon) + " " + state.primaryWeapon + " in hand, "
                                : "armed with " + Article(state.primaryWeapon) + " " + state.primaryWeapon + " slung on back, ";
                            p.Append(weapStyle);
                        }
                        else if (state.isViolentIncapable)
                        {
                            p.Append("unarmed, peaceful stance, ");
                        }
                    }
                    else if (poseChoice == 4 && hasRanged)
                    {
                        p.Append("resting one hand on their " + state.primaryWeapon + " with their " + helmet + " hanging off the weapon barrel, ");
                    }
                }
            }
            else
            {
                // No helmet/hat
                if (!string.IsNullOrEmpty(state.primaryWeapon))
                {
                    string weapStyle = state.weaponType == "melee"
                        ? "holding " + Article(state.primaryWeapon) + " " + state.primaryWeapon + " in hand, "
                        : "armed with " + Article(state.primaryWeapon) + " " + state.primaryWeapon + " slung on back, ";
                    p.Append(weapStyle);
                }
                else if (state.isViolentIncapable)
                {
                    p.Append("unarmed, peaceful stance, ");
                }
            }

            if (!string.IsNullOrEmpty(apparelLine))
            {
                p.Append(apparelLine + ", ");
            }
        }

        private static string BuildGearLine(PawnState state, out string helmet)
        {
            helmet = null;
            List<string> remainingApparel = new List<string>();

            if (state.apparel != null)
            {
                foreach (var app in state.apparel)
                {
                    if (IsHeadgearLabel(app))
                    {
                        if (state.excludeHelmet)   // per-image setting
                        {
                            // Exclude headgear entirely
                        }
                        else
                        {
                            helmet = app;
                        }
                    }
                    else
                    {
                        remainingApparel.Add(app);
                    }
                }
            }

            string apparelDesc = string.Join(", ", remainingApparel.ToArray());
            if (string.IsNullOrEmpty(apparelDesc))
            {
                // Pawn has no body clothing (naked, or only headgear).
                // Describe them in modest clothing to prevent the AI from generating nudity and triggering NSFW filters.
                return "wearing simple modest fabric wraps";
            }

            if (state.apparelStyle == "heavily armored")
            {
                return "wearing heavy armor: " + apparelDesc;
            }
            if (state.apparelStyle == "noble robes")
            {
                return "wearing ornate noble regalia: " + apparelDesc;
            }
            if (state.apparelStyle == "tribal")
            {
                return "wearing tribal garb: " + apparelDesc;
            }
            return "wearing " + apparelDesc;
        }

        public static bool IsHeadgear(RimWorld.Apparel item)
        {
            if (item == null || item.def == null || item.def.apparel == null) return false;
            var groups = item.def.apparel.bodyPartGroups;
            if (groups != null)
            {
                foreach (var g in groups)
                {
                    if (g == RimWorld.BodyPartGroupDefOf.FullHead || g == RimWorld.BodyPartGroupDefOf.UpperHead)
                    {
                        return true;
                    }
                }
            }
            return IsHeadgearLabel(item.def.label);
        }

        public static bool IsHeadgearLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return false;
            string l = label.ToLower();
            return l.Contains("helmet") || l.Contains("hat") || l.Contains("hood") || 
                   l.Contains("cap") || l.Contains("cowl") || l.Contains("mask") || 
                   l.Contains("tuque") || l.Contains("crown") || l.Contains("visor") || 
                   l.Contains("goggles") || l.Contains("veil");
        }

        // isAddictionProp is set true when the prop is driven by an addiction (vs. a trait).
        // The caller passes this into AppendHealthModifiers to suppress the redundant
        // addiction-visual descriptor (e.g. skip "bloodshot eyes" when we already show a beer mug).
        private static bool AppendCuteProps(StringBuilder p, PawnState state, out bool isAddictionProp)
        {
            isAddictionProp = false;

            bool hasAlcohol   = false;
            bool hasSmokeleaf = false;

            if (state.addictions != null)
            {
                foreach (string addict in state.addictions)
                {
                    string al = addict.ToLower();
                    if (al.Contains("alcohol"))   hasAlcohol   = true;
                    if (al.Contains("smokeleaf")) hasSmokeleaf = true;
                }
            }

            bool isKind = false;
            foreach (string trait in state.traits)
                if (trait.ToLower().Contains("kind")) { isKind = true; break; }

            if (hasSmokeleaf)
            {
                p.Append("holding and smoking a stylized joint with a gentle wisp of pixelated smoke, ");
                isAddictionProp = true;
                return true;
            }
            if (hasAlcohol)
            {
                p.Append("holding a frosty foaming glass mug of beer, ");
                isAddictionProp = true;
                return true;
            }
            if (isKind)
            {
                p.Append("holding a cute colorful round lollipop, ");
                return true;  // trait-based, isAddictionProp stays false
            }
            return false;
        }

        private static void AppendSkillProps(StringBuilder p, PawnState state, bool alreadyHoldingProp)
        {
            if (alreadyHoldingProp) return;

            string skill = (state.topSkill1 ?? "").ToLower();
            if (string.IsNullOrEmpty(skill)) return;

            if (skill.Contains("plant"))
            {
                p.Append("holding a freshly harvested healroot sprig with distinctive blue-green leaves, ");
            }
            else if (skill.Contains("medicine"))
            {
                p.Append("holding a glowing blue glitterworld medicine injector, ");
            }
            else if (skill.Contains("shooting"))
            {
                p.Append("looking alert in a cool tactical marksman ready-stance, ");
            }
            else if (skill.Contains("melee"))
            {
                p.Append("standing in a cool combat-ready swordfighter stance, ");
            }
            else if (skill.Contains("mining"))
            {
                p.Append("holding a raw chunk of glowing blue plasteel ore, ");
            }
            else if (skill.Contains("cooking"))
            {
                p.Append("holding a wooden plate of lavish meal with roasted meat and wild berries, ");
            }
            else if (skill.Contains("animal"))
            {
                p.Append("with a cute loyal baby muffalo standing beside them, ");
            }
            else if (skill.Contains("art"))
            {
                p.Append("holding a carving chisel and a small polished green jade sculpture, ");
            }
            else if (skill.Contains("research") || skill.Contains("intellect"))
            {
                p.Append("holding a glowing blue techprint datashard with digital circuitry lines, ");
            }
            else if (skill.Contains("construct"))
            {
                p.Append("holding a high-tech constructor tool and a metallic component, ");
            }
            else if (skill.Contains("crafting"))
            {
                p.Append("holding tailors shears and a roll of brilliant red devilstrand fabric, ");
            }
            else if (skill.Contains("social"))
            {
                p.Append("holding a royal scroll of title with a golden crown seal, ");
            }
        }

        // ──────────────────────────────────────────────────────────────────────────
        // EXPRESSION / MOOD
        // ──────────────────────────────────────────────────────────────────────────

        private static string GetExpression(PawnState state, AIPortraitsSettings settings)
        {
            string prefix = "eyes open, ";

            // Focus on clean, composed, JRPG/anime-style expressions with open/desired eyes.
            foreach (string trait in state.traits)
            {
                string tl = trait.ToLower();
                if (tl.Contains("psychopath"))                          return prefix + "calm neutral expression, cool emotionless gaze";
                if (tl.Contains("abrasive"))                            return prefix + "steady confident expression, sharp intense stare";
                if (tl.Contains("kind"))                                return "eyes open, soft gentle smile, warm kind eyes";
                if (tl.Contains("iron-willed") || tl.Contains("tough")) return prefix + "resolute composed expression, determined unflinching gaze";
                if (tl.Contains("neurotic"))                            return prefix + "alert focused expression, steady gaze";
                if (tl.Contains("brawler"))                             return prefix + "confident smirk, battle-ready composed look";
                if (tl.Contains("nimble") || tl.Contains("quick"))     return prefix + "focused alert expression, sharp clear eyes";
            }

            if (state.moodLevel > 0.65f) return prefix + "calm gentle smile, warm confident eyes";
            if (state.moodLevel < 0.35f) return prefix + "serious stoic expression, calm focused gaze";
            return prefix + "composed neutral expression, calm steady gaze";
        }

        // ──────────────────────────────────────────────────────────────────────────
        // HEALTH MODIFIERS
        // ──────────────────────────────────────────────────────────────────────────

        // skipAddictionVisuals: when true, skip rendering addiction-specific face signs
        // because a cute prop for that addiction was already appended (avoids contradiction).
        private static void AppendHealthModifiers(StringBuilder p, PawnState state, bool skipAddictionVisuals = false)
        {
            // Optimize for a beautiful, clean, non-distorted portrait drawing.
            // Exclude bloodloss, sickness, severe grimacing, starvation, and fire/burning to keep the portrait looking clean and high-quality.

            if (state.headInjuries.Count > 0)
            {
                if (state.headInjuries.Count > 1)
                    p.Append("clean stylized battle scars on face, ");
                else
                    p.Append("stylized face scar, ");
            }

            if (state.bodyInjuries.Count > 0)
                p.Append("clean stylized battle scars on face or neck, ");

            if (state.missingParts.Count > 0)
                p.Append(string.Join(", ", state.missingParts.GetRange(0, Math.Min(state.missingParts.Count, 2)).ToArray()) + ", ");

            if (state.implants.Count > 0)
            {
                // Classify the implant set: peg/wooden, bionic, archotech, simple-prosthetic.
                bool hasPeg = false, hasWooden = false, hasBionic = false,
                     hasArcho = false, hasSimple = false;
                foreach (string imp in state.implants)
                {
                    string il = imp.ToLower();
                    if (il.Contains("peg"))                       hasPeg = true;
                    if (il.Contains("wooden") || il.Contains("denture")) hasWooden = true;
                    if (il.Contains("bionic"))                    hasBionic = true;
                    if (il.Contains("archotech"))                 hasArcho = true;
                    if (il.Contains("simple prosthetic") || il.Contains("prosthetic")) hasSimple = true;
                }

                // List the actual implants by name so the AI renders the right thing
                p.Append("with " + string.Join(" and ", state.implants.ToArray()));

                // Append a visual descriptor matching the most striking implant type
                if (hasArcho)
                    p.Append(", gleaming gold-white archotech glow at the joints");
                else if (hasBionic)
                    p.Append(", sleek metallic cybernetic finish, blue LED accent lines");
                else if (hasPeg || hasWooden)
                    p.Append(", worn wooden prosthetic with leather straps and brass buckles");
                else if (hasSimple)
                    p.Append(", simple metal prosthetic, riveted plates");
                p.Append(", ");
            }

            // Chronic cosmetic conditions
            foreach (string cc in state.chronicConditions)
                p.Append(cc + ", ");

            // Permanent visible scars (frostbite, burn) — separate from active injuries
            foreach (string ps in state.permanentScars)
            {
                string psl = ps.ToLower();
                if (psl.Contains("torso") || psl.Contains("chest") || psl.Contains("abdomen") || psl.Contains("ribs") || psl.Contains("heart") || psl.Contains("lung") || psl.Contains("stomach") || psl.Contains("liver") || psl.Contains("kidney"))
                {
                    p.Append("subtle scar on neck, ");
                }
                else
                {
                    p.Append(ps + ", ");
                }
            }

            // Drug addictions — each has distinctive visual signs.
            // Skip alcohol/smokeleaf if AppendCuteProps already rendered a prop for them
            // (avoids contradictory "holding a beer mug" + "bloodshot eyes" in same prompt).
            foreach (string addict in state.addictions)
            {
                string al = addict.ToLower();
                if      (al.Contains("yayo"))      p.Append("hollow-eyed yayo addict, faint nasal redness, twitchy expression, ");
                else if (al.Contains("flake"))     p.Append("flake addict, sunken cheeks, dilated pupils, ");
                else if (al.Contains("go-juice"))  p.Append("go-juice user, wired tense gaze, faint veining on temples, ");
                else if (al.Contains("wake-up"))   p.Append("wake-up addict, jittery alert stare, ");
                else if (al.Contains("smokeleaf") && !skipAddictionVisuals) p.Append("smokeleaf user, yellowed fingertips, hazy laid-back eyes, ");
                else if (al.Contains("alcohol")   && !skipAddictionVisuals) p.Append("alcoholic, ruddy nose with broken capillaries, bloodshot eyes, ");
                else if (al.Contains("psychite"))  p.Append("psychite tea drinker, jittery focus, ");
            }
        }

        // ──────────────────────────────────────────────────────────────────────────
        // LORE FLAVOR (traits, ideology accent, skill aura)
        // ──────────────────────────────────────────────────────────────────────────

        private static void AppendLoreFlavor(StringBuilder p, PawnState state, AIPortraitsSettings settings)
        {
            if (state.isPsychicSensitive)
                p.Append("faint psychic energy aura radiating from the character, subtle shimmering halo, ");

            if (state.isViolentIncapable)
                p.Append("gentle peaceful aura, soft warm light around the figure, ");

            if (settings.includeIdeology && !string.IsNullOrEmpty(state.ideologyRole))
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

            // Ideology name — subtle faith descriptor, only if it's a player-named ideo (not generic)
            if (settings.includeIdeology &&
                !string.IsNullOrEmpty(state.ideologyName) &&
                !state.ideologyName.ToLower().StartsWith("ideo_") &&  // skip default-generated names
                state.ideologyName.Length < 60)
            {
                p.Append("devout follower of " + state.ideologyName + ", subtle ideoligion iconography, ");
            }

            // Rim light from ideology color — applies to all styles since it's on the character, not the background
            if (settings.includeRimLighting && !string.IsNullOrEmpty(state.favoriteColor))
                p.Append("soft " + state.favoriteColor + " rim lighting, ");

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

        public static string CompileNegativePrompt(AIPortraitsSettings settings, string framing = "portrait")
        {
            string baseNeg = settings.baseNegativePrompt;
            string suffix;
            if (framing == "special")
            {
                suffix = "photorealistic photograph, blurry, 3d render, anti-aliasing, multiple characters in one frame, solid background, flat background, plain background, white background, gradient background, deformed anatomy, bad anatomy, extra limbs, watermark, text, signature";
            }
            else
            {
                suffix = "photorealistic photograph, blurry, 3d render, anti-aliasing, multiple characters in one frame, gradient background, textured background, complex background, detailed background, environment, scenery, shadows, deformed anatomy, bad anatomy, extra limbs, watermark, text, signature";
            }
            return string.IsNullOrEmpty(baseNeg) ? suffix : baseNeg + ", " + suffix;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // CONTINUITY TOKEN
        // ──────────────────────────────────────────────────────────────────────────

        public static string GetContinuityToken(PortraitStyle style)
        {
            switch (style)
            {
                case PortraitStyle.Realistic_Korean:
                    return "consistent Solo Leveling webtoon manhwa style, same sharp inked line art, same dramatic chiaroscuro lighting, same saturated focal colors, same refined facial proportions";
                case PortraitStyle.Realistic_Western:
                    return "consistent Rick and Morty Adult Swim cartoon style, same thick black outlines, same flat 2D color fills, same bulging cartoon eyes with dot pupils, same wonky proportions";
                case PortraitStyle.DotPixel:
                    return "consistent 16-bit pixel-art style, same pixel grid density, same thin dark outlines, same cel-shading band style, same limited color palette";
                default:
                    return "";
            }
        }

        // Choose "a" or "an" based on the first phonetic letter of the next word.
        // Crude but covers the common cases (autopistol, assault rifle, ornate sword, etc.)
        private static string Article(string word)
        {
            if (string.IsNullOrEmpty(word)) return "a";
            char c = char.ToLower(word[0]);
            return (c == 'a' || c == 'e' || c == 'i' || c == 'o' || c == 'u') ? "an" : "a";
        }

        // ──────────────────────────────────────────────────────────────────────────
        // LLM PROMPT GENERATION — helpers used by AsyncAIClient when useLLMPrompt=true
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Serialises a PawnState into a concise plain-English data sheet that is
        /// sent to Gemini Flash as the user message. The LLM then writes the final
        /// image-generation prompt from this structured description.
        /// </summary>
        public static string CompilePawnStateDescription(PawnState state, AIPortraitsSettings settings)
        {
            if (state == null) return "";
            var sb = new StringBuilder();

            sb.AppendLine("Name: " + state.name);
            sb.AppendLine("Age: " + state.bioAge + ", Gender: " + state.gender);

            string race = state.xenotype ?? "human";
            if (!string.IsNullOrEmpty(state.xenotypeName)) race += " (" + state.xenotypeName + ")";
            sb.AppendLine("Race: " + race);

            if (!string.IsNullOrEmpty(state.skinColor))  sb.AppendLine("Skin: " + state.skinColor);
            if (!string.IsNullOrEmpty(state.hairStyle) && state.hairStyle != "bald")
                sb.AppendLine("Hair: " + state.hairColor + " " + state.hairStyle);
            else if (state.hairStyle == "bald")
                sb.AppendLine("Hair: bald/shaved head");
            if (!string.IsNullOrEmpty(state.beardStyle)) sb.AppendLine("Beard: " + state.beardStyle);
            if (!string.IsNullOrEmpty(state.bodyType))   sb.AppendLine("Build: " + state.bodyType);

            if (state.traits.Count > 0)
                sb.AppendLine("Traits: " + string.Join(", ", state.traits.ToArray()));

            float moodPct = state.moodLevel * 100f;
            string moodWord = state.moodLevel > 0.65f ? "happy" : state.moodLevel < 0.35f ? "unhappy" : "neutral";
            sb.AppendLine("Mood: " + moodPct.ToString("F0") + "% (" + moodWord + ")");
            if (!string.IsNullOrEmpty(state.mentalState))
                sb.AppendLine("Mental state: " + state.mentalState);

            if (!string.IsNullOrEmpty(state.topSkill1))  sb.AppendLine("Top skill: " + state.topSkill1);
            if (!string.IsNullOrEmpty(state.topSkill2))  sb.AppendLine("Second skill: " + state.topSkill2);
            if (state.isViolentIncapable)                sb.AppendLine("Is incapable of violence (pacifist)");

            if (!string.IsNullOrEmpty(state.primaryWeapon))
                sb.AppendLine("Weapon: " + state.primaryWeapon + " (" + state.weaponType + ")");
            // Separate body apparel and headgear to detect if body is bare
            List<string> bodyApparel = new List<string>();
            List<string> headgear = new List<string>();
            if (state.apparel != null)
            {
                foreach (var app in state.apparel)
                {
                    if (IsHeadgearLabel(app))
                    {
                        if (!state.excludeHelmet)   // per-image setting
                            headgear.Add(app);
                    }
                    else
                    {
                        bodyApparel.Add(app);
                    }
                }
            }

            List<string> printedApparel = new List<string>();
            printedApparel.AddRange(headgear);
            if (bodyApparel.Count > 0)
            {
                printedApparel.AddRange(bodyApparel);
            }
            else
            {
                printedApparel.Add("simple modest clothing");
            }
            sb.AppendLine("Apparel: " + string.Join(", ", printedApparel.ToArray()));

            if (!string.IsNullOrEmpty(state.childhoodTitle))
                sb.AppendLine("Childhood: " + state.childhoodTitle);
            if (!string.IsNullOrEmpty(state.adulthoodTitle))
                sb.AppendLine("Pawn Biography: " + state.adulthoodTitle);
            if (state.abilityGenes != null && state.abilityGenes.Count > 0)
                sb.AppendLine("Ability genes: " + string.Join(", ", state.abilityGenes.ToArray()));

            if (settings.includeIdeology)
            {
                if (!string.IsNullOrEmpty(state.ideologyName))  sb.AppendLine("Ideology: " + state.ideologyName);
                if (!string.IsNullOrEmpty(state.ideologyRole))  sb.AppendLine("Ideology role: " + state.ideologyRole);
                if (!string.IsNullOrEmpty(state.favoriteColor)) sb.AppendLine("Ideology color: " + state.favoriteColor);
            }

            if (state.implants.Count > 0)
                sb.AppendLine("Implants / prosthetics: " + string.Join(", ", state.implants.ToArray()));
            if (state.headInjuries.Count > 0)
                sb.AppendLine("Head injuries / scars: " + string.Join(", ", state.headInjuries.ToArray()));
            if (state.bodyInjuries.Count > 0)
                sb.AppendLine("Body injuries / scars: " + string.Join(", ", state.bodyInjuries.ToArray()));
            if (state.addictions.Count > 0)
                sb.AppendLine("Addictions: " + string.Join(", ", state.addictions.ToArray()));
            if (state.chronicConditions.Count > 0)
                sb.AppendLine("Chronic conditions: " + string.Join(", ", state.chronicConditions.ToArray()));
            if (state.cosmeticGenes.Count > 0)
                sb.AppendLine("Special appearance genes: " + string.Join(", ", state.cosmeticGenes.ToArray()));

            if (state.isHemogenic)       sb.AppendLine("Special: sanguophage vampire (blood-red eyes, pale fangs)");
            if (!string.IsNullOrEmpty(state.royalTitle)) sb.AppendLine("Royal title: " + state.royalTitle);
            if (state.psylinkLevel > 0)  sb.AppendLine("Psylink level: " + state.psylinkLevel + "/6");
            if (state.pregnancyTrimester > 0) sb.AppendLine("Pregnant (trimester " + state.pregnancyTrimester + ")");
            if (state.hasSpouse)         sb.AppendLine("Has a spouse");
            if (state.isSlave)           sb.AppendLine("Is a slave (slave collar visible)");
            if (state.isPrisoner)        sb.AppendLine("Is a prisoner");
            if (state.isGhoul)           sb.AppendLine("Is a ghoul (pallid, hollow, undead)");
            if (state.isInhumanized)     sb.AppendLine("Is void-touched / inhumanized (disturbing serene void gaze)");

            if (!string.IsNullOrEmpty(state.framing))
            {
                if (state.framing == "bodyshot")
                    sb.AppendLine("Framing: full length full body shot (head to toe framing showing the entire character including legs, feet, and boots, standing pose, absolutely no cropping)");
                else if (state.framing == "special")
                    sb.AppendLine("Framing: special (dynamic selfie shot or expressive custom illustration close-up)");
                else
                    sb.AppendLine("Framing: standard portrait (bust-up framing)");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Returns the Gemini Flash system prompt for the given art style.
        /// This tells the model exactly what kind of image-generation prompt to produce.
        /// </summary>
        public static string GetLLMSystemPrompt(PortraitStyle style, AIPortraitsSettings settings, string framing = "portrait", bool excludeHelmet = false)
        {
            string styleDesc;
            switch (style)
            {
                case PortraitStyle.Realistic_Korean:
                    styleDesc = "modern Korean webtoon manhwa style: " +
                                (settings != null ? settings.manhwaStylePrompt : "sharp inked line art, dramatic chiaroscuro with deep blacks and bright rim highlights, saturated focal colors, refined modern proportions") +
                                ", professional digital anime illustration, highly polished";
                    break;
                case PortraitStyle.Realistic_Western:
                    styleDesc = "Rick and Morty / Adult Swim 2D cartoon animation style: " +
                                (settings != null ? settings.cartoonStylePrompt : "clean bold outlines, flat vibrant color fills");
                    break;
                case PortraitStyle.DotPixel:
                    styleDesc = "high-quality JRPG pixel art style: " +
                                (settings != null ? settings.pixelStylePrompt : "sharp deliberate pixel grid, thin dark outlines, limited color palette");
                    break;
                default:
                    styleDesc = "flat 2D anime RPG character portrait, professional digital illustration, bold clean outlines, flat cel-shading";
                    break;
            }

            string framingTask = "write one optimized image generation prompt for a bust-up portrait.";
            string rule5 = "Always end with: isolated on a solid white background, flat clean background, no environment, no gradients, no shadows, crisp sharp edges with the background, bust-up framing.";
            string rule7 = "FACE COVERS & BEARDS: If a face-covering mask, respirator, veil, goggles, or visor is listed, describe it as worn on the face/eyes. If they also have a beard or facial hair, DO NOT describe the beard/facial hair (or state it is completely hidden inside the mask) to ensure the mask renders cleanly without clipping.";

            if (framing == "bodyshot")
            {
                framingTask = "write one optimized image generation prompt for a full-length body illustration showing the character from head to toe, including legs, pants, and boots/shoes.";
                rule5 = "Always end with: isolated on a solid white background, flat clean background, no environment, no gradients, no shadows, crisp sharp edges with the background, standing full body length framing showing the entire character from head to toe including boots and legs, no cropping.";
            }
            else if (framing == "special")
            {
                string[] specialConcepts = new string[] {
                    "a realistic modern smartphone SELFIE (arm's-length, candid expression, slight wide-angle lens look, casual everyday setting)",
                    "a POKEMON / TCG holographic TRADING CARD (full card layout: foil border, a name banner at the top, the character as dramatic hero art, a small role/'type' tag and a short flavour/stat line)",
                    "an IMITATION OF EDVARD MUNCH'S 'THE SCREAM' recreated with THIS character (iconic open-mouthed screaming pose, hands to cheeks, swirling blood-orange and blue painterly sky)",
                    "an IMITATION OF DA VINCI'S 'MONA LISA' recreated with THIS character (enigmatic half-smile, three-quarter pose, Renaissance oil-painting style)",
                    "an IMITATION OF GRANT WOOD'S 'AMERICAN GOTHIC' recreated with THIS character",
                    "a heroic BAROQUE / RENAISSANCE OIL PORTRAIT of THIS character",
                    "a dramatic ACTION MOVIE POSTER featuring THIS character with bold poster styling",
                    "a glossy MAGAZINE COVER featuring THIS character with cover text",
                    "a dramatic BACKSHOT looking over the shoulder at their environment",
                    "a low-angle dynamic ACTION pose in their environment",
                    "a 'THIS IS FINE' meme: the character sitting calmly with a serene little smile and a hot drink while the room behind them is on fire",
                    "a goofy over-confident DATING-APP SELFIE: arm's-length, ring-light glow, cheesy smolder and a wink",
                    "a smug GYM-MIRROR FLEX selfie with a ridiculous over-the-top muscle pose",
                    "the character proudly holding a 'WORLD'S BEST COLONIST' mug to camera like a grand championship trophy",
                    "a derpy CANDID snapshot caught mid-sneeze with a comically surprised face and motion lines",
                    "an over-the-top INFLUENCER pose bursting with sparkles, finger-hearts and confetti"
                };
                string specialPick = specialConcepts[SpecialRng.Next(specialConcepts.Length)];
                framingTask = "write one optimized image generation prompt for a SPECIAL creative key-art scene. Use EXACTLY this concept — it was chosen AT RANDOM for variety, so do NOT swap it for one that 'fits' the character better and do NOT pick a different one: " + specialPick + ". Commit to it fully and make it expressive and instantly readable as that concept.";
                rule5 = "Always end with the chosen concept's own framing plus a FILLED background that fits it (NEVER transparent or plain white) - e.g. the holographic card frame for a trading card, the swirling sky for The Scream, the casual setting for a selfie, the gallery/canvas for an oil painting, or a role-appropriate environment.";
                rule7 = "FACE COVERS & SPECIALS: If a face-covering mask is listed, describe it worn on the face, hiding any beard. Commit fully to the randomly-assigned special concept above; do NOT substitute a different concept based on the character's role or mood.";
            }

            string rule6 = "HEADGEAR/HELMET MANDATE: If a helmet, hat, hood, cap, cowl, or mask is in the apparel list, you ABSOLUTELY MUST include it in the prompt. Describe it prominently as worn on their head/face, or held under one arm.";
            if (excludeHelmet)
            {
                rule6 = "HEADGEAR/HELMET EXCLUSION: Do NOT include any helmet, hat, hood, cap, cowl, or mask in the prompt. Ensure the character's hair, hair style, face, and head are fully visible and not covered.";
            }

            return
                "You are an expert AI image prompt engineer for a RimWorld character portrait generator.\n" +
                "Given a character data sheet, " + framingTask + "\n\n" +
                "TARGET ART STYLE: " + styleDesc + "\n" +
                "CRITICAL: This is a FLAT 2D HAND-DRAWN ILLUSTRATION (webtoon / manhwa / cartoon / pixel art), NOT a photograph and NOT a 3D render. It must read as printed comic / webtoon artwork — bold clean ink lines, flat cel-shaded color fills, CMYK print-style colors. NEVER use photography or cinematography vocabulary (no 'photo', 'photograph', 'photorealistic', 'hyperrealistic', 'realistic skin', 'lens', 'mm', 'depth of field', 'bokeh', 'studio lighting', 'cinematic', 'volumetric', '3d render', 'octane render').\n\n" +
                "RULES:\n" +
                "1. Structure the prompt into clean, distinct lines separated by newlines, with each line focusing on a specific visual dimension:\n" +
                "   - Line 1: Core Subject & Pose (describe the character, hair flows, expression, pose, and active gestures)\n" +
                "   - Line 2: Clothing & Gear (describe the clothing, armor materials, textures, and held/slung weapons or props)\n" +
                "   - Line 3: Composition & Framing (describe the framing and viewing angle as a 2D drawing — e.g. close-up bust framing, three-quarter view, dynamic low angle. NEVER mention cameras, lenses, focal length, mm, depth of field, bokeh, or the word 'photo')\n" +
                "   - Line 4: Stylized Shading (describe the drawn shading technique and light direction — e.g. flat cel-shaded color blocks, hard-edged anime drop shadows, bold rim highlights. Keep it 2D and hand-drawn; NO photographic or cinematic studio lighting, NO volumetric light)\n" +
                "   - Line 5: Aesthetics & Color Scheme (specify color harmonies, accent hues, composition style, and mood)\n" +
                "   - Line 6: Style Medium & Quality Keywords (specify the art style, line art quality, shading technique, and high-end production terms)\n" +
                "   - Line 7: Background Setting (as defined in rule 5)\n" +
                "   Do NOT output the line labels (e.g., do not write 'Line 1:' or 'Core Subject & Pose:'), just write the visual descriptors for each section on a new line.\n" +
                "2. Include: art style keywords, physical appearance, expression, visible weapon/apparel, personality cues.\n" +
                "3. Resolve contradictions — when traits conflict, pick the more visually striking interpretation.\n" +
                "4. You have full creative freedom over pose, composition, camera angle, lighting, and overall styling. Aim for a premium, visually striking, highly detailed, and creative portrait that captures the character's identity. Use your judgment to make the most impactful artistic decisions to create a stunning key visual.\n" +
                "5. " + rule5 + "\n" +
                "6. " + rule6 + "\n" +
                "7. " + rule7 + "\n" +
                "8. HIGH QUALITY VISUALS: Elevate the aesthetic with crisp inked linework, bold cel-shaded shading, and clean flat color harmony tailored to the style. Avoid generic terms like 'hyperrealistic' or 'detailed'; use concrete drawn-art modifiers instead (e.g. 'glistening stylized hair flows', 'bold cel-shaded highlights', 'masterpiece digital illustration', 'crisp pixel alignment', 'flat iridescent fabric fills'). Do NOT use 'volumetric', 'cinematic', or any photographic lighting terms.\n" +
                "9. STRICT SAFETY: Do NOT use any sexually suggestive, nude, naked, topless, bare-chested, or NSFW-sensitive keywords. The output must be strictly PG-rated and safe for work. Ensure the character is always described as wearing clothing (e.g., if no specific apparel is listed, specify simple modest clothes like a plain tunic or fabric wraps).\n" +
                "10. Keep total output under 350 words.\n" +
                "11. Output ONLY the prompt — no explanations, no headers, no quotes.\n" +
                "12. 2D ARTWORK ONLY (MANDATORY): The result is a flat 2D graphic drawing/illustration. Your prompt must NOT contain any photography, 3D, or realism terms. Do not use words like photo, photograph, photorealistic, hyperrealistic, realistic, lens, focal length, mm, depth of field, bokeh, studio lighting, cinematic, volumetric, 3d render, octane. Describe everything as drawn, inked, cel-shaded, flat-colored illustration art.\n" +
                "13. SOLO SUBJECT (MANDATORY): Depict EXACTLY ONE single character, alone in the frame — never two or more people. Even for a pawn with a spouse, lover, or followers, show only this one character. Begin the prompt with 'solo, single character, one person alone' and never describe a companion, partner, couple, crowd, or any other person.";
        }
    }
}
