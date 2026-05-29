## 2024-12-07 - [Localization update for UI and overlays]
**Action:** Replaced hardcoded UI strings in `HarmonyPatches.cs` and `UI_StoryBook.cs` with `.Translate()` calls and added them to `English.xml` Keyed language data.
**Learning:** `Widgets.Label` and UI strings across custom drawing overlays often contain status info ("Making alive...", "Painting...") that is very visible to players. Moving these to Keyed XML allows full localization.
