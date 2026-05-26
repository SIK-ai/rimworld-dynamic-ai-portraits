using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;

namespace AIPortraits
{
    // ─────────────────────────────────────────────────────────────────────────────
    // DATA MODEL
    // ─────────────────────────────────────────────────────────────────────────────
    public class PawnState
    {
        // ── Identity ────────────────────────────────────────────────────────────
        public string pawnId;
        public string name;
        public string gender;
        public int    bioAge;
        public string bodyType;         // Thin / Hulk / Fat / Female / Male / etc.
        public string headShape;        // from headTypeDef label (Narrow, Average, Wide, etc.)
        public string framing;          // portrait / bodyshot / special

        // ── Appearance ──────────────────────────────────────────────────────────
        public string hairStyle;
        public string hairColor;
        public string skinColor;
        public string beardStyle;       // facial hair / beard defName label
        public string tattooDef;        // tattoo def label (Ideology DLC)
        public string furColor;         // for fur-bearing xenotypes

        // ── Xenotype / Genes ─────────────────────────────────────────────────────
        public string       xenotype;               // base xenotype label
        public string       xenotypeName;           // custom xenotype name if player-named
        public List<string> cosmeticGenes  = new List<string>(); // visual genes (horns, tail, ears, skin, etc.)
        public List<string> abilityGenes   = new List<string>(); // deathrest, hemogen, regen — surfaced to LLM prompt only
        public bool         isHemogenic;            // sanguophage-class bloodfeeder
        public bool         hasTail;
        public bool         hasHorns;
        public bool         hasFur;

        // ── Equipment ────────────────────────────────────────────────────────────
        public string       primaryWeapon;          // weapon label
        public string       weaponType;             // Melee / Ranged / Unarmed
        public List<string> apparel = new List<string>(); // all worn apparel labels
        public string       apparelStyle;           // dominant visual style (armored, tribal, etc.)

        // ── Health & Body ────────────────────────────────────────────────────────
        public List<string> headInjuries   = new List<string>();
        public List<string> bodyInjuries   = new List<string>();   // visible on torso/arm area
        public List<string> missingParts   = new List<string>();
        public List<string> implants       = new List<string>();   // bionics, archotech
        public bool         isSick;
        public bool         isInPain;
        public float        painLevel;              // 0-1
        public bool         isSleeping;
        public bool         isBloodloss;
        public bool         isBurning;

        // ── Mind / Mood ───────────────────────────────────────────────────────────
        public string mentalState;
        public float  moodLevel;               // 0-1
        public bool   isPsychicSensitive;
        public bool   isCombatDrafted;
        public bool   isFleeing;

        // ── Traits ────────────────────────────────────────────────────────────────
        public List<string> traits = new List<string>(); // e.g. "Psychopath", "Iron-willed", etc.

        // ── Skills ────────────────────────────────────────────────────────────────
        // Top 2 skills (highest level, fire_passion = Major) — affects portrait flavor
        public string topSkill1;
        public string topSkill2;
        public bool   isViolentIncapable;       // pacifist

        // ── Ideology / Role ────────────────────────────────────────────────────────
        public string ideologyName;             // e.g. "Seekers of the Golden Path"
        public string ideologyRole;             // e.g. "Archon", "Tender", "Headliner"
        public string favoriteColor;            // ideoligion color → tint hint

        // ── Backstory ────────────────────────────────────────────────────────────
        public string childhoodTitle;           // e.g. "Tribal child"
        public string adulthoodTitle;           // e.g. "Ex-soldier"

        // ── EXTENDED (added 2026) ────────────────────────────────────────────────
        // Drugs (separated from isSick so the AI can render addict-specific signs)
        public List<string> addictions      = new List<string>(); // "yayo", "smokeleaf", "alcohol"

        // Body condition (bucketed in hash to avoid cache thrash)
        public bool isExhausted;            // sleep-deprivation or rest < 28%
        public bool isMalnourished;         // Malnutrition hediff or food < 25%

        // Chronic + cosmetic permanent conditions
        public List<string> chronicConditions = new List<string>(); // "cataract", "frail", "dementia", "bad back"
        public List<string> permanentScars    = new List<string>(); // "blackened fingertips from frostbite", "burn scar on cheek"

        // Pregnancy (Biotech)
        public int pregnancyTrimester;      // 0 = none, 1/2/3

        // Royalty
        public string royalTitle;           // "Knight", "Count", null if none
        public int    psylinkLevel;         // 0–6

        // Faction context
        public string factionName;          // "Outlander Union", "Pirate Confederacy"
        public string pawnKind;             // "Mercenary", "Imperial trooper", "Pirate raider"

        // Romance
        public bool hasSpouse;
        public bool hasLover;               // includes fiancé

        // Captivity
        public bool isPrisoner;
        public bool isSlave;

        // Anomaly
        public bool isInhumanized;
        public bool isGhoul;

        // NOTE: GetStateHash() was removed. The mod no longer auto-regenerates portraits on
        // state change — once a pawn has a portrait it persists until manually refreshed via
        // the Pawn Gallery's "Create New Portrait" button. Cache keys are now plain pawn IDs
        // (in-memory: pawn.ThingID; on-disk: AIPortraitsManager.GetActiveKey = worldId_pawnId).
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // EXTRACTOR
    // ─────────────────────────────────────────────────────────────────────────────
    public static class PawnStateExtractor
    {
        public static PawnState ExtractState(Pawn pawn)
        {
            if (pawn == null) return null;

            PawnState s = new PawnState();
            s.pawnId = pawn.ThingID;

            string frameVal;
            if (AIPortraitsMod.settings != null && AIPortraitsMod.settings.pawnFraming != null &&
                AIPortraitsMod.settings.pawnFraming.TryGetValue(pawn.ThingID, out frameVal))
            {
                s.framing = frameVal;
            }
            else
            {
                s.framing = "portrait";
            }

            // ── IDENTITY ─────────────────────────────────────────────────────────
            s.name   = pawn.Name != null ? pawn.Name.ToStringShort : "Unknown";
            s.gender = pawn.gender.ToString().ToLower();
            s.bioAge = pawn.ageTracker != null ? pawn.ageTracker.AgeBiologicalYears : 20;

            // Body type
            if (pawn.story != null && pawn.story.bodyType != null)
                s.bodyType = pawn.story.bodyType.label ?? pawn.story.bodyType.defName;

            // Head shape
            if (pawn.story != null && pawn.story.headType != null)
                s.headShape = pawn.story.headType.label ?? "";

            // ── APPEARANCE ────────────────────────────────────────────────────────
            if (pawn.story != null)
            {
                // Hair
                if (pawn.story.hairDef != null)
                {
                    s.hairStyle = pawn.story.hairDef.label;
                    s.hairColor = GetColorDescription(pawn.story.HairColor);
                }
                else
                {
                    s.hairStyle = "bald";
                    s.hairColor = "";
                }

                // Skin
                s.skinColor = GetSkinColorDescription(pawn.story.SkinColor);

                // Beard — only set if it's a real beard (not "No beard", null, etc.)
                if (pawn.style != null && pawn.style.beardDef != null)
                {
                    string beardLabel = pawn.style.beardDef.label ?? "";
                    string beardLower = beardLabel.ToLower();
                    if (beardLabel.Length > 0 &&
                        beardLower != "no beard" &&
                        beardLower != "clean shaven" &&
                        !beardLower.Contains("nobeard"))
                        s.beardStyle = beardLabel;
                }

                // Tattoo (Ideology DLC) — pawn.style?.FaceTattoo, pawn.style?.BodyTattoo
                if (pawn.style != null)
                {
                    List<string> tattoos = new List<string>();
                    if (pawn.style.FaceTattoo != null && pawn.style.FaceTattoo.defName != "NoTattoo_Face")
                        tattoos.Add(pawn.style.FaceTattoo.label);
                    if (pawn.style.BodyTattoo != null && pawn.style.BodyTattoo.defName != "NoTattoo_Body")
                        tattoos.Add(pawn.style.BodyTattoo.label);
                    if (tattoos.Count > 0)
                        s.tattooDef = string.Join(", ", tattoos.ToArray());
                }

                // Backstory titles
                if (pawn.story.Childhood != null)
                    s.childhoodTitle = pawn.story.Childhood.title;
                if (pawn.story.Adulthood != null)
                    s.adulthoodTitle = pawn.story.Adulthood.title;
            }

            // ── XENOTYPE & GENES ──────────────────────────────────────────────────
            if (pawn.genes != null)
            {
                // Xenotype
                if (pawn.genes.Xenotype != null)
                    s.xenotype = pawn.genes.Xenotype.label;
                else
                    s.xenotype = "human";

                // Custom xenotype name (player-renamed)
                if (!string.IsNullOrEmpty(pawn.genes.xenotypeName))
                    s.xenotypeName = pawn.genes.xenotypeName;

                // Fur color from genes
                if (pawn.genes.UniqueXenotype || pawn.genes.Xenotype != null)
                {
                    // Check for fur/skin color gene
                    Color furColorValue = pawn.story != null ? pawn.story.SkinColor : Color.white;
                    s.furColor = GetColorDescription(furColorValue);
                }

                // Parse all active genes for cosmetic/functional tags
                foreach (Gene gene in pawn.genes.GenesListForReading)
                {
                    if (gene == null || gene.def == null || !gene.Active) continue;

                    string geneName  = gene.def.label ?? gene.def.defName;
                    string geneDefLc = gene.def.defName.ToLower();

                    // Detect visual/cosmetic genes
                    bool isCosmetic = IsCosmeticGene(geneDefLc, geneName);
                    if (isCosmetic)
                    {
                        s.cosmeticGenes.Add(geneName);

                        // Flag specific visual features
                        if (geneDefLc.Contains("tail"))                    s.hasTail  = true;
                        if (geneDefLc.Contains("horn"))                    s.hasHorns = true;
                        if (geneDefLc.Contains("fur"))                     s.hasFur   = true;
                    }

                    // Detect hemogen / bloodfeeder genes
                    if (geneDefLc.Contains("hemogen") || geneDefLc.Contains("deathrest") ||
                        geneDefLc.Contains("sanguophage") || geneDefLc.Contains("bloodfeeder"))
                    {
                        s.isHemogenic = true;
                        s.abilityGenes.Add(geneName);
                    }
                    else if (IsAbilityGene(geneDefLc))
                    {
                        s.abilityGenes.Add(geneName);
                    }
                }
            }
            else
            {
                s.xenotype = "human";
            }

            // ── EQUIPMENT ─────────────────────────────────────────────────────────
            // Build a rich weapon descriptor mirroring apparel: quality + stuff + color + label + damage
            if (pawn.equipment != null && pawn.equipment.Primary != null && pawn.equipment.Primary.def != null)
            {
                ThingWithComps wpn = pawn.equipment.Primary;
                string wpnLabel = wpn.def.label;

                // Quality
                string wpnQuality = "";
                QualityCategory wq;
                if (wpn.TryGetQuality(out wq))
                {
                    if      (wq == QualityCategory.Awful)      wpnQuality = "battered ";
                    else if (wq == QualityCategory.Poor)       wpnQuality = "rough ";
                    else if (wq == QualityCategory.Good)       wpnQuality = "well-maintained ";
                    else if (wq == QualityCategory.Excellent)  wpnQuality = "finely crafted ";
                    else if (wq == QualityCategory.Masterwork) wpnQuality = "masterwork ornate ";
                    else if (wq == QualityCategory.Legendary)  wpnQuality = "legendary gleaming ";
                }

                // Stuff (material) — real material, not name-guessed
                string wpnStuff = "";
                if (wpn.Stuff != null)
                {
                    string sl = wpn.Stuff.defName.ToLower();
                    if      (sl.Contains("uranium"))     wpnStuff = "dense uranium ";
                    else if (sl.Contains("plasteel"))    wpnStuff = "high-tech plasteel ";
                    else if (sl.Contains("gold"))        wpnStuff = "gilded gold ";
                    else if (sl.Contains("silver"))      wpnStuff = "polished silver ";
                    else if (sl.Contains("jade"))        wpnStuff = "carved jade ";
                    else if (sl.Contains("steel"))       wpnStuff = "steel ";
                    else if (sl.Contains("wood"))        wpnStuff = "wooden ";
                    else if (sl.Contains("bone"))        wpnStuff = "bone ";
                    else if (sl.Contains("stone") || sl.Contains("granite") || sl.Contains("slate"))
                                                          wpnStuff = "rough stone ";
                }
                else
                {
                    // No-stuff weapons (most guns) — derive material vibe from name
                    string wnLc = wpnLabel.ToLower();
                    if      (wnLc.Contains("charge") || wnLc.Contains("plasma") ||
                             wnLc.Contains("laser")  || wnLc.Contains("pulse"))   wpnStuff = "glowing-energy ";
                    else if (wnLc.Contains("sniper") || wnLc.Contains("assault") ||
                             wnLc.Contains("rifle"))                              wpnStuff = "military-grade ";
                }

                // Color (only if it's notable — pure white/grey defaults skipped to avoid noise)
                string wpnColor = "";
                Color dc = wpn.DrawColor;
                float maxC = Mathf.Max(dc.r, Mathf.Max(dc.g, dc.b));
                float minC = Mathf.Min(dc.r, Mathf.Min(dc.g, dc.b));
                if ((maxC - minC) > 0.15f) // saturated → worth describing
                    wpnColor = GetColorDescription(dc) + " ";

                // Damage
                string wpnDamage = "";
                if (wpn.MaxHitPoints > 0)
                {
                    float hpFrac = (float)wpn.HitPoints / wpn.MaxHitPoints;
                    if      (hpFrac < 0.35f) wpnDamage = ", chipped and notched from use";
                    else if (hpFrac < 0.60f) wpnDamage = ", well-worn";
                }

                s.primaryWeapon = (wpnQuality + wpnStuff + wpnColor + wpnLabel + wpnDamage).Trim();
                s.weaponType    = wpn.def.IsMeleeWeapon ? "melee" : "ranged";
            }
            else
            {
                s.primaryWeapon = "";
                s.weaponType    = "unarmed";
            }

            // Apparel — collect all, detect style
            if (pawn.apparel != null && pawn.apparel.WornApparel != null)
            {
                int armorCount   = 0;
                int tribalCount  = 0;
                int royalCount   = 0;

                foreach (Apparel item in pawn.apparel.WornApparel)
                {
                    if (item.def == null) continue;

                    string labelLc = (item.def.label ?? "").ToLower();
                    string defLc = item.def.defName.ToLower();

                    // Skip lower-body/utility apparel that would force full-body generation
                    if (labelLc.Contains("pants") || labelLc.Contains("trousers") || labelLc.Contains("jeans") ||
                        labelLc.Contains("leggings") || labelLc.Contains("skirt") || labelLc.Contains("boots") ||
                        labelLc.Contains("shoes") || labelLc.Contains("socks") || labelLc.Contains("footwear") ||
                        labelLc.Contains("belt") || labelLc.Contains("holster"))
                    {
                        continue;
                    }

                    // Build a rich apparel string: "<quality> <special-material> <color> <label> <damage>"
                    // e.g. "legendary devilstrand dark crimson duster, tattered"
                    string itemLabel = item.def.label;
                    string colorDesc = GetColorDescription(item.DrawColor);

                    // Quality (Awful/Poor → ragged; Excellent/Masterwork/Legendary → ornate)
                    string qualityPrefix = "";
                    QualityCategory q;
                    if (item.TryGetQuality(out q))
                    {
                        if      (q == QualityCategory.Awful)      qualityPrefix = "ragged ";
                        else if (q == QualityCategory.Poor)       qualityPrefix = "worn ";
                        else if (q == QualityCategory.Good)       qualityPrefix = "well-made ";
                        else if (q == QualityCategory.Excellent)  qualityPrefix = "well-crafted ";
                        else if (q == QualityCategory.Masterwork) qualityPrefix = "masterwork ornate ";
                        else if (q == QualityCategory.Legendary)  qualityPrefix = "legendary gilt-trimmed ";
                    }

                    // Stuff (only flag visually distinctive materials)
                    string stuffPrefix = "";
                    if (item.Stuff != null)
                    {
                        string sl = item.Stuff.defName.ToLower();
                        if      (sl.Contains("devilstrand"))  stuffPrefix = "iridescent devilstrand ";
                        else if (sl.Contains("hyperweave"))   stuffPrefix = "sleek hyperweave ";
                        else if (sl.Contains("thrumbofur"))   stuffPrefix = "luxurious thrumbofur ";
                        else if (sl.Contains("humanleather")) stuffPrefix = "unsettling human-leather ";
                        else if (sl.Contains("plasteel"))     stuffPrefix = "plasteel-reinforced ";
                        else if (sl.Contains("uranium"))      stuffPrefix = "dense uranium ";
                    }

                    // Damage
                    string damageSuffix = "";
                    if (item.MaxHitPoints > 0)
                    {
                        float hpFrac = (float)item.HitPoints / item.MaxHitPoints;
                        if      (hpFrac < 0.35f) damageSuffix = ", torn and bloodied";
                        else if (hpFrac < 0.60f) damageSuffix = ", showing wear and tears";
                    }

                    string apparelDesc = (qualityPrefix + stuffPrefix +
                                          (string.IsNullOrEmpty(colorDesc) ? "" : colorDesc + " ") +
                                          itemLabel + damageSuffix).Trim();

                    s.apparel.Add(apparelDesc);

                    if (defLc.Contains("armor") || defLc.Contains("plate") || defLc.Contains("marine"))
                        armorCount++;
                    if (defLc.Contains("tribal") || defLc.Contains("pelt") || defLc.Contains("cloth"))
                        tribalCount++;
                    if (defLc.Contains("royal") || defLc.Contains("noble") || defLc.Contains("prestige"))
                        royalCount++;
                }

                if      (armorCount  >= 2) s.apparelStyle = "heavily armored";
                else if (royalCount  >= 1) s.apparelStyle = "noble robes";
                else if (tribalCount >= 2) s.apparelStyle = "tribal";
                else if (s.apparel.Count == 0) s.apparelStyle = "nude";
                else                       s.apparelStyle = "civilian";
            }

            // ── HEALTH ────────────────────────────────────────────────────────────
            s.isSleeping = !pawn.Awake();
            s.isCombatDrafted = pawn.Drafted;

            if (pawn.health != null && pawn.health.hediffSet != null)
            {
                s.painLevel  = pawn.health.hediffSet.PainTotal;
                s.isInPain   = s.painLevel > 0.25f;

                // ── PASS 1 — find replaced body parts so we can suppress redundant
                //              "missing femur/tibia/foot/toe×N" entries that are just
                //              the internal accounting under a replaced leg/arm.
                HashSet<BodyPartRecord> replacedParts = new HashSet<BodyPartRecord>();
                foreach (Hediff h in pawn.health.hediffSet.hediffs)
                {
                    if (h.def == null || h.Part == null) continue;
                    if (h is Hediff_AddedPart || h is Hediff_Implant ||
                        h.def.defName.ToLower().Contains("peg"))
                    {
                        replacedParts.Add(h.Part);
                    }
                }

                // ── PASS 2 — process. Group by type with counts so we don't list the
                //              same thing 10 times in the UI / prompt.
                Dictionary<string, int> implantCounts     = new Dictionary<string, int>();
                Dictionary<string, int> missingCounts     = new Dictionary<string, int>();
                Dictionary<string, int> headInjuryCounts  = new Dictionary<string, int>();
                Dictionary<string, int> bodyInjuryCounts  = new Dictionary<string, int>();
                Dictionary<string, int> scarCounts        = new Dictionary<string, int>();

                foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
                {
                    if (hediff.def == null) continue;

                    string defLc = hediff.def.defName.ToLower();

                    // ── ADDICTIONS (separated from sick — has its own visual signs) ──
                    if (hediff is Hediff_Addiction)
                    {
                        if      (defLc.Contains("yayo"))      s.addictions.Add("yayo");
                        else if (defLc.Contains("flake"))     s.addictions.Add("flake");
                        else if (defLc.Contains("gojuice"))   s.addictions.Add("go-juice");
                        else if (defLc.Contains("wakeup"))    s.addictions.Add("wake-up");
                        else if (defLc.Contains("smokeleaf")) s.addictions.Add("smokeleaf");
                        else if (defLc.Contains("alcohol"))   s.addictions.Add("alcohol");
                        else if (defLc.Contains("psychite"))  s.addictions.Add("psychite tea");
                        else                                   s.addictions.Add(hediff.def.label);
                        continue; // don't double-count as sick
                    }

                    // ── BODY CONDITION ──
                    if (defLc.Contains("sleepdeprivation") || defLc.Contains("tired"))
                    {
                        s.isExhausted = true;
                        continue;
                    }
                    if (defLc.Contains("malnutrition") || defLc.Contains("hunger"))
                    {
                        s.isMalnourished = true;
                        continue;
                    }

                    // ── PREGNANCY (Biotech) ──
                    if (defLc.Contains("pregnant"))
                    {
                        // hediff.Severity ranges 0–1 across the pregnancy
                        if      (hediff.Severity < 0.34f) s.pregnancyTrimester = 1;
                        else if (hediff.Severity < 0.67f) s.pregnancyTrimester = 2;
                        else                              s.pregnancyTrimester = 3;
                        continue;
                    }

                    // ── ANOMALY: inhumanized / void-touched ──
                    if (defLc.Contains("inhumanized") || defLc.Contains("voidtouched"))
                    {
                        s.isInhumanized = true;
                        continue;
                    }

                    // ── CHRONIC COSMETIC CONDITIONS (currently lost in isSick) ──
                    if (defLc == "cataract" || defLc.Contains("cataract"))
                        { s.chronicConditions.Add("milky cataract over eye"); continue; }
                    if (defLc == "badback" || defLc.Contains("badback"))
                        { s.chronicConditions.Add("stooped from a bad back"); continue; }
                    if (defLc == "frail" || defLc.Contains("frailty"))
                        { s.chronicConditions.Add("frail and thin-limbed"); continue; }
                    if (defLc == "dementia" || defLc.Contains("dementia") || defLc.Contains("alzheimer"))
                        { s.chronicConditions.Add("vacant dementia stare"); continue; }
                    if (defLc.Contains("hearingloss") || defLc.Contains("deaf"))
                        { s.chronicConditions.Add("cocked head listening"); continue; }
                    if (defLc.Contains("asthma"))
                        { s.chronicConditions.Add("labored asthmatic breathing"); continue; }
                    if (defLc.Contains("carcinoma") || defLc.Contains("cancer"))
                        { s.chronicConditions.Add("hollow-cheeked, terminal pallor"); continue; }

                    // Sickness (general bad/disease hediffs that don't match anything above)
                    if (hediff.def.makesSickThought || hediff.def.isBad)
                        s.isSick = true;

                    // Blood loss
                    if (defLc.Contains("bloodloss"))
                        s.isBloodloss = true;

                    // Fire
                    if (defLc.Contains("burn") || defLc.Contains("fire"))
                        s.isBurning = true;

                    BodyPartRecord part = hediff.Part;
                    if (part == null) continue;

                    bool isHead = IsHeadPart(part);
                    string partLabel = part.def != null ? part.def.label : "body part";

                    // Permanent frostbite/burn scars — cosmetic, distinct from active injuries
                    if (hediff.IsPermanent() && defLc.Contains("frostbite"))
                    {
                        Bump(scarCounts, "blackened " + partLabel + " from old frostbite");
                        continue;
                    }
                    if (hediff.IsPermanent() && defLc.Contains("burn"))
                    {
                        Bump(scarCounts, "faded burn scar on " + partLabel);
                        continue;
                    }

                    if (hediff is Hediff_MissingPart)
                    {
                        // Skip if any ancestor part was replaced (peg leg, bionic arm, etc.)
                        // — that explains why this child part is "missing" and listing it
                        // is just noise.
                        if (IsDescendantOfAny(part, replacedParts)) continue;
                        Bump(missingCounts, "missing " + partLabel);
                    }
                    else if (hediff is Hediff_AddedPart || hediff is Hediff_Implant ||
                             defLc.Contains("bionic") || defLc.Contains("archotech") ||
                             defLc.Contains("mechtech") || defLc.Contains("prosthetic") ||
                             defLc.Contains("peg") || defLc.Contains("wooden"))
                    {
                        // Use the hediff's actual label, NOT a generic "cybernetic <part>".
                        // PegLeg.label = "peg leg", BionicArm.label = "bionic arm", etc.
                        string implantLabel = hediff.def.label;
                        if (string.IsNullOrEmpty(implantLabel)) implantLabel = "prosthetic " + partLabel;
                        Bump(implantCounts, implantLabel);
                    }
                    else if (hediff is Hediff_Injury)
                    {
                        string injLabel = hediff.IsPermanent()
                            ? "scar on " + partLabel
                            : "wounded " + partLabel;

                        if (isHead) Bump(headInjuryCounts, injLabel);
                        else        Bump(bodyInjuryCounts, injLabel);
                    }
                }

                // Materialise count-dictionaries into the public lists, with
                // pluralization so "peg leg" + "peg leg" becomes "two peg legs".
                foreach (KeyValuePair<string, int> kv in implantCounts)
                    s.implants.Add(FormatCount(kv.Key, kv.Value));
                foreach (KeyValuePair<string, int> kv in missingCounts)
                    s.missingParts.Add(FormatCount(kv.Key, kv.Value));
                foreach (KeyValuePair<string, int> kv in headInjuryCounts)
                    s.headInjuries.Add(FormatCount(kv.Key, kv.Value));
                foreach (KeyValuePair<string, int> kv in bodyInjuryCounts)
                    s.bodyInjuries.Add(FormatCount(kv.Key, kv.Value));
                foreach (KeyValuePair<string, int> kv in scarCounts)
                    s.permanentScars.Add(FormatCount(kv.Key, kv.Value));

                // Backup: sleep/food NEED-based detection if hediffs didn't catch it
                if (!s.isExhausted && pawn.needs != null && pawn.needs.rest != null &&
                    pawn.needs.rest.CurLevelPercentage < 0.28f)
                    s.isExhausted = true;
                if (!s.isMalnourished && pawn.needs != null && pawn.needs.food != null &&
                    pawn.needs.food.CurLevelPercentage < 0.25f)
                    s.isMalnourished = true;
            }

            // ── MOOD / MENTAL STATE ───────────────────────────────────────────────
            if (pawn.needs != null && pawn.needs.mood != null)
                s.moodLevel = pawn.needs.mood.CurLevel;
            else
                s.moodLevel = 0.5f;

            if (pawn.MentalStateDef != null)
                s.mentalState = pawn.MentalStateDef.label;

            // Fleeing
            if (pawn.jobs != null && pawn.jobs.curDriver != null)
            {
                string jobName = pawn.jobs.curDriver.GetType().Name.ToLower();
                if (jobName.Contains("flee") || jobName.Contains("escape"))
                    s.isFleeing = true;
            }

            // Psychic sensitivity
            s.isPsychicSensitive = pawn.GetStatValue(StatDefOf.PsychicSensitivity, true) > 1.0f;

            // ── TRAITS ────────────────────────────────────────────────────────────
            if (pawn.story != null && pawn.story.traits != null)
            {
                foreach (Trait trait in pawn.story.traits.allTraits)
                {
                    if (trait.def != null)
                        s.traits.Add(trait.LabelCap);
                }
            }

            // Violence incapable — check WorkTags
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                s.isViolentIncapable = true;

            // ── TOP SKILLS ────────────────────────────────────────────────────────
            if (pawn.skills != null)
            {
                int best1Level = 0, best2Level = 0;
                string best1Name = null, best2Name = null;

                foreach (SkillRecord skill in pawn.skills.skills)
                {
                    if (skill.passion == Passion.Major || skill.passion == Passion.Minor)
                    {
                        int effective = skill.Level + (skill.passion == Passion.Major ? 3 : 1);
                        if (effective > best1Level)
                        {
                            best2Level = best1Level; best2Name = best1Name;
                            best1Level = effective;  best1Name = skill.def.label;
                        }
                        else if (effective > best2Level)
                        {
                            best2Level = effective; best2Name = skill.def.label;
                        }
                    }
                }
                s.topSkill1 = best1Name;
                s.topSkill2 = best2Name;
            }

            // ── IDEOLOGY ─────────────────────────────────────────────────────────
            if (pawn.ideo != null && pawn.ideo.Ideo != null)
            {
                Ideo ideo = pawn.ideo.Ideo;
                s.ideologyName = ideo.name;
                s.favoriteColor = GetColorDescription(ideo.Color);

                try
                {
                    Precept_Role role = ideo.GetRole(pawn);
                    if (role != null)
                        s.ideologyRole = role.LabelCap;
                }
                catch (Exception ex)
                {
                    Log.Warning("[Dynamic AI Portraits] Could not read ideology role: " + ex.Message);
                }
            }

            // ── ROYALTY DLC — title + psylink ────────────────────────────────────
            if (pawn.royalty != null)
            {
                try
                {
                    RoyalTitleDef mainTitleDef = pawn.royalty.MainTitle();
                    if (mainTitleDef != null)
                        s.royalTitle = mainTitleDef.GetLabelFor(pawn) ?? mainTitleDef.label;
                }
                catch (Exception ex)
                {
                    Log.Warning("[Dynamic AI Portraits] Could not read royal title: " + ex.Message);
                }

                try
                {
                    // GetPsylinkLevel exists as an extension; fall back to entropy if missing
                    Hediff psylinkHediff = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.PsychicAmplifier);
                    if (psylinkHediff != null)
                        s.psylinkLevel = (int)psylinkHediff.Severity;
                }
                catch (Exception)
                {
                    // PsychicAmplifier may not exist on this version — silently skip
                }
            }

            // ── FACTION + PAWN KIND ──────────────────────────────────────────────
            try
            {
                if (pawn.Faction != null && pawn.Faction.def != null)
                    s.factionName = pawn.Faction.def.label;
                if (pawn.kindDef != null)
                    s.pawnKind = pawn.kindDef.label;
            }
            catch (Exception) { }

            // ── ROMANCE ──────────────────────────────────────────────────────────
            if (pawn.relations != null)
            {
                try
                {
                    foreach (DirectPawnRelation dpr in pawn.relations.DirectRelations)
                    {
                        if (dpr == null || dpr.def == null) continue;
                        string r = dpr.def.defName.ToLower();
                        if (r == "spouse")      s.hasSpouse = true;
                        else if (r == "lover" || r == "fiance" || r == "fiancee") s.hasLover = true;
                    }
                }
                catch (Exception) { }
            }

            // ── CAPTIVITY (Prisoner / Slave) ─────────────────────────────────────
            try
            {
                s.isPrisoner = pawn.IsPrisoner;
                s.isSlave    = pawn.IsSlave;
            }
            catch (Exception) { }

            // ── ANOMALY: ghoul / mutant ──────────────────────────────────────────
            try
            {
                if (pawn.kindDef != null && pawn.kindDef.defName != null &&
                    pawn.kindDef.defName.ToLower().Contains("ghoul"))
                    s.isGhoul = true;
            }
            catch (Exception) { }

            return s;
        }

        // ── GENE CLASSIFICATION ───────────────────────────────────────────────────

        private static bool IsCosmeticGene(string defLc, string label)
        {
            // Visual/cosmetic gene patterns
            return defLc.Contains("skin_")     || defLc.Contains("hair_")    ||
                   defLc.Contains("fur_")      || defLc.Contains("horn")     ||
                   defLc.Contains("tail")      || defLc.Contains("ear_")     ||
                   defLc.Contains("eye_")      || defLc.Contains("jaw_")     ||
                   defLc.Contains("body_")     || defLc.Contains("beard_")   ||
                   defLc.Contains("cosmetic")  || defLc.Contains("node_")    ||
                   defLc.Contains("scale")     || defLc.Contains("carapace") ||
                   defLc.Contains("tentacle")  || defLc.Contains("beak")     ||
                   defLc.Contains("snout")     || defLc.Contains("crest")    ||
                   defLc.Contains("feather")   || defLc.Contains("wing");
        }

        private static bool IsAbilityGene(string defLc)
        {
            return defLc.Contains("psych")     || defLc.Contains("ability")  ||
                   defLc.Contains("deathless") || defLc.Contains("ageless")  ||
                   defLc.Contains("healing")   || defLc.Contains("regen")    ||
                   defLc.Contains("resurrect") || defLc.Contains("warcall");
        }

        // ── BODY PART CHECKS ─────────────────────────────────────────────────────

        private static bool IsHeadPart(BodyPartRecord part)
        {
            if (part == null || part.def == null) return false;
            string name = part.def.defName.ToLower();
            return name.Contains("head") || name.Contains("eye")  || name.Contains("ear") ||
                   name.Contains("nose") || name.Contains("jaw")  || name.Contains("cheek") ||
                   name.Contains("skull") || name.Contains("brain") || name.Contains("neck");
        }

        // Walk parent chain — true if any ancestor body part is in the replaced set.
        // Used to skip redundant "missing femur/tibia/foot/toe" entries that are children
        // of a replaced leg (etc.).
        private static bool IsDescendantOfAny(BodyPartRecord part, HashSet<BodyPartRecord> replaced)
        {
            if (replaced == null || replaced.Count == 0) return false;
            BodyPartRecord cur = part.parent;
            while (cur != null)
            {
                if (replaced.Contains(cur)) return true;
                cur = cur.parent;
            }
            return false;
        }

        // ── COUNT/AGGREGATION HELPERS ───────────────────────────────────────────

        private static void Bump(Dictionary<string, int> counts, string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            int n;
            counts.TryGetValue(key, out n);
            counts[key] = n + 1;
        }

        // Turn a (label, count) pair into a readable phrase:
        //   ("peg leg", 1) → "peg leg"
        //   ("peg leg", 2) → "two peg legs"
        //   ("missing toe", 5) → "several missing toes"
        private static string FormatCount(string label, int count)
        {
            if (count <= 1) return label;
            string plural = Pluralize(label);
            if (count == 2) return "two "     + plural;
            if (count <= 4) return count + " " + plural;
            return "several " + plural;
        }

        // Crude English pluralization — good enough for body parts and prosthetics.
        private static string Pluralize(string label)
        {
            if (string.IsNullOrEmpty(label)) return label;
            string l = label.ToLower();
            if (l.EndsWith("foot"))   return label.Substring(0, label.Length - 4) + "feet";
            if (l.EndsWith("tooth"))  return label.Substring(0, label.Length - 5) + "teeth";
            if (l.EndsWith("s") || l.EndsWith("x") || l.EndsWith("ch")) return label + "es";
            if (l.EndsWith("y") && label.Length > 1 && !"aeiou".Contains(label[label.Length - 2].ToString())) return label.Substring(0, label.Length - 1) + "ies";
            return label + "s";
        }

        // ── COLOR HELPERS ─────────────────────────────────────────────────────────

        private static string GetColorDescription(Color c)
        {
            float r = c.r, g = c.g, b = c.b;
            float bright = (r + g + b) / 3f;
            float maxC   = Mathf.Max(r, Mathf.Max(g, b));
            float sat    = maxC < 0.001f ? 0f : (maxC - Mathf.Min(r, Mathf.Min(g, b))) / maxC;

            // Greyscale
            if (sat < 0.12f)
            {
                if (bright < 0.15f) return "jet black";
                if (bright < 0.35f) return "dark grey";
                if (bright < 0.6f)  return "grey";
                if (bright < 0.85f) return "light grey";
                return "white";
            }

            // Hue-based
            float hue;
            float satVal, valVal;
            Color.RGBToHSV(c, out hue, out satVal, out valVal);
            hue *= 360f;

            // Black fallback for extremely dark colors
            if (valVal < 0.12f) return "jet black";

            // Brown/Auburn/Blonde refinements
            if (hue >= 10f && hue < 50f)
            {
                if (valVal < 0.3f) return "dark brown";
                if (valVal < 0.65f) return "brown";
            }
            else if ((hue >= 0f && hue < 10f) || (hue >= 350f))
            {
                if (valVal < 0.45f) return "auburn"; // dark red
            }

            string prefix = bright < 0.25f ? "dark " : (bright > 0.75f ? "light " : "");

            if      (hue < 15f  || hue >= 345f) return prefix + "red";
            else if (hue < 45f)                  return prefix + "orange";
            else if (hue < 70f)                  return prefix + "golden blonde";
            else if (hue < 100f)                 return prefix + (bright > 0.55f ? "blonde" : "olive");
            else if (hue < 150f)                 return prefix + "green";
            else if (hue < 195f)                 return prefix + "teal";
            else if (hue < 255f)                 return prefix + "blue";
            else if (hue < 285f)                 return prefix + "indigo";
            else if (hue < 315f)                 return prefix + "purple";
            else                                  return prefix + "magenta";
        }

        private static string GetSkinColorDescription(Color c)
        {
            float r = c.r, g = c.g, b = c.b;
            float maxC = Mathf.Max(r, Mathf.Max(g, b));
            float sat  = maxC < 0.001f ? 0f : (maxC - Mathf.Min(r, Mathf.Min(g, b))) / maxC;
            float bright = (r + g + b) / 3f;

            // Clearly alien/xenotype skin colors
            if (sat > 0.3f)
            {
                float hue;
                float d2, d3;
                Color.RGBToHSV(c, out hue, out d2, out d3);
                hue *= 360f;
                if      (hue < 30f  || hue >= 330f) return "reddish skin";
                else if (hue < 80f)                  return "sallow yellowish skin";
                else if (hue < 160f)                 return "greenish skin";
                else if (hue < 250f)                 return "bluish skin";
                else if (hue < 300f)                 return "purple-tinted skin";
                else                                  return "pinkish skin";
            }

            // Human range (low saturation)
            if (bright > 0.82f) return "extremely pale skin, almost albino";
            if (bright > 0.70f) return "fair pale skin";
            if (bright > 0.58f) return "light skin";
            if (bright > 0.45f) return "medium olive skin";
            if (bright > 0.30f) return "tan brown skin";
            if (bright > 0.18f) return "dark brown skin";
            return "very dark skin";
        }
    }
}
