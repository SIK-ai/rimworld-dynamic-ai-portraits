## 2024-05-24 - Extracted strings in UI HarmonyPatches
**Action:** Extracted hardcoded UI strings from `Source/HarmonyPatches.cs` to `Languages/English/Keyed/DynamicAIPortraits_Keys.xml` and cached `.Translate()` calls.
**Learning:** Using `static string` caching is essential when translating strings inside hot GUI paths to avoid excessive allocations.
