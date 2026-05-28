## 2026-05-28 - Wrap debug logs and clean up empty catch blocks
**Action:** Wrapped all unconditional `Log.Message` calls in `if (Prefs.DevMode)` and replaced empty `catch` blocks with DevMode-wrapped `Log.Warning`s.
**Learning:** Found several empty catch blocks hiding potential errors. A spammed log hides the real errors, but silent exceptions are just as bad. DevMode allows us to see these silent failures when we want to.
