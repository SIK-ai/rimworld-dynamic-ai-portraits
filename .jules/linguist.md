## 2026-05-28 - UI_StoryBook Localization
**Action:** Moved hardcoded strings to Keyed XML and implemented string caching to avoid GC allocations in OnGUI.
**Learning:** Cached formatted missing panel string in ParseStoryContent instead of recalculating during layout loop.
