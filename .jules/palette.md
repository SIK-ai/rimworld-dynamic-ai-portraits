## 2024-05-26 - Missing Hover States on Custom UI Elements
**Learning:** RimWorld's `Widgets.ButtonInvisible` is excellent for making custom UI areas interactive (like image thumbnails), but it provides zero visual feedback by default, leaving users unaware they can click.
**Action:** Whenever using `Widgets.ButtonInvisible`, always pair it with a manual hover check using `Mouse.IsOver(rect)` and a visual change, such as `Widgets.DrawHighlight(rect)` or a background color adjustment, to ensure discoverability.

## 2026-05-29 - Add Click Sound to Invisible Buttons
**Learning:** Found an interaction improvement opportunity. `Widgets.ButtonInvisible` is used extensively in RimWorld's UI framework, but it doesn't provide automatic click audio feedback unlike `Widgets.ButtonText`. This lack of auditory feedback can make interactive elements feel dead or unresponsive.
**Action:** When using `Widgets.ButtonInvisible` (or similar custom UI interactions), always manually trigger `SoundDefOf.Click.PlayOneShotOnCamera(null);` on click for better interactive feedback.
