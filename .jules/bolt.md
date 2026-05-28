## 2024-05-19 - Use structs as Dictionary keys to prevent per-frame string allocation
**Learning:** String concatenation for cache keys (like `pawn.ThingID + "|" + framing`) inside per-frame UI updates (like `OnGUI` overlay rendering via `GetActiveKeyForFraming`) creates massive garbage collection pressure.
**Action:** Define a lightweight struct implementing `IEquatable<T>` (e.g. `PawnFramingKey` with `int pawnId` and `string framing`) and use it as the `Dictionary` key to completely eliminate allocations on cache hits.

## 2024-05-24 - Optimizing String Returns with Cache
**Learning:** Returning strings for configuration key lookups (`ImgKey`) that were generated using string concatenation per-frame creates high allocation overhead.
**Action:** Use the aforementioned `PawnFramingKey` to map to pre-built strings in a cache `Dictionary<PawnFramingKey, string>`, retrieving the allocated string cheaply without needing concatenation.
