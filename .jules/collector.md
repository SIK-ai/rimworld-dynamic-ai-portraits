## 2024-05-24 - String Allocations in UI Render Loop

**Learning:** String concatenation and LINQ/foreach loops over arrays instantiated in hot loops (like `OnGUI` and `Tick`) generate huge GC pressure due to constant allocation. In `GetPortraitTexture`, recreating a new string array `new[] { "portrait", "bodyshot", "special" }`, iterating via `foreach`, and building key strings via `pawn.ThingID + "_" + framing` multiple times every frame per pawn causes micro-stutters.

**Action:**
- Cache arrays in `static readonly` fields.
- Replace `foreach` with `for` loops.
- Use dictionaries mapping deterministic inputs to strings to act as string allocation caches (`pawnKeyCache`, `lockedPawnKeyCache`). Invalidate caches on appropriate events (like world changes).