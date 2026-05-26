using UnityEngine;
using Verse;
using HarmonyLib;

namespace AIPortraits
{
    public class AIPortraitsMod : Mod
    {
        public static AIPortraitsSettings settings;
        public static AIPortraitsMod instance;

        public AIPortraitsMod(ModContentPack content) : base(content)
        {
            instance = this;
            settings = GetSettings<AIPortraitsSettings>();
            
            // Execute Harmony patches
            var harmony = new Harmony("antigravity.aiportraits");
            harmony.PatchAll();
            
            Log.Message("[Dynamic AI Portraits] Initialized successfully. Harmony patches applied.");
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            settings.DoSettingsWindowContents(inRect);
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "Dynamic AI Portraits";
        }
    }
}
