## 2024-05-19 - Use structs as Dictionary keys to prevent per-frame string allocation
**Learning:** String concatenation for cache keys (like `pawn.ThingID + "|" + framing`) inside per-frame UI updates (like `OnGUI` overlay rendering via `GetActiveKeyForFraming`) creates massive garbage collection pressure.
**Action:** Define a lightweight struct implementing `IEquatable<T>` (e.g. `PawnFramingKey` with `int pawnId` and `string framing`) and use it as the `Dictionary` key to completely eliminate allocations on cache hits.

## $(date +%Y-%m-%d) - Cached string allocation on per-frame texture lookups
**Learning:** Concatenating strings dynamically in per-frame rendering code like UI_AIPortraitCard.cs (e.g. `pawn.ThingID + "_" + framing`) caused continuous string allocations and high GC pressure.
**Action:** Used the `PawnFramingKey` struct to cache the results of these string concatenations into new dictionaries `pawnKeyCache` and `lockedKeyCache`, eliminating the per-frame allocations during texture lookups.
