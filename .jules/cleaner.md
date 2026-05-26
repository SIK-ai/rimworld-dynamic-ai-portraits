## 2024-05-24 - [Code hygiene]
**Action:** Removed unused `using System.Text` in `Source/PawnStateExtractor.cs`.
**Action:** Renamed PascalCase field `Instance` to `instance` in `Source/ModController.cs` and propagated the change across all usages to align with Ludeon camelCase field conventions.
**Action:** Renamed PascalCase fields `Y`, `Cb`, `Cr` to `y`, `cb`, `cr` in `YCbCrColor` struct (`Source/BackgroundRemover.cs`) and updated their references.
