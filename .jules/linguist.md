
## YYYY-MM-DD - Extracted strings from HarmonyPatches.cs
**Action:** Replaced 13 hardcoded strings in `Source/HarmonyPatches.cs` with `.Translate()` calls mapped to keys in `DynamicAIPortraits_Keys.xml`.
**Learning:** Hardcoded strings in custom portrait cards (`Widgets.Label` and tooltips) can be localized successfully by extracting them to XML language dictionaries and using `.Translate()` with fallback to positional arguments for concatenated strings.
