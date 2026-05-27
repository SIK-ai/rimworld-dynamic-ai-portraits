using UnityEngine;
using Verse;
using HarmonyLib;

namespace AIPortraits
{
    public class AIPortraitsMod : Mod
    {
        public static AIPortraitsSettings settings;
        public static AIPortraitsMod Instance;

        public AIPortraitsMod(ModContentPack content) : base(content)
        {
            Instance = this;
            settings = GetSettings<AIPortraitsSettings>();
            DebugLog.Begin("mod init");

            // Execute Harmony patches. Guard so a single failing patch can't abort the whole
            // mod constructor — that would unregister settings and present as "no config".
            var harmony = new Harmony("antigravity.aiportraits");
            try
            {
                harmony.PatchAll();
                Log.Message("[Dynamic AI Portraits] Initialized successfully. Harmony patches applied.");
            }
            catch (System.Exception e)
            {
                Log.Error("[Dynamic AI Portraits] Harmony PatchAll failed (mod still loads): " + e);
            }
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
