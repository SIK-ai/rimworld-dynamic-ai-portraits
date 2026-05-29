
## 2024-05-24 - Explicit dictionary working lists & safe null checks
**Learning:** Scribe_Collections.Look for dictionaries requires explicit working list variables passed by ref. Checking for and initializing null collections must be done within the LoadSaveMode.PostLoadInit phase.
**Action:** Added working lists to AIPortraitsSettings and moved dictionary null-coalescing checks into PostLoadInit.
