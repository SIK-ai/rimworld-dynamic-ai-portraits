## 2024-05-17 - [Dictionary serialization]
**Learning:** Scribe_Collections.Look for Dictionaries should always use working lists (keys and values) to avoid unintended behavior or null refs when serializing.
**Action:** Ensure ExposeData has working lists defined for Dictionaries and that collection null checks are performed safely within the `LoadSaveMode.PostLoadInit` phase.
