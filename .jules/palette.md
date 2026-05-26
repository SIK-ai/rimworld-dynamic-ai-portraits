## 2024-05-26 - Missing Hover States on Custom UI Elements
**Learning:** RimWorld's `Widgets.ButtonInvisible` is excellent for making custom UI areas interactive (like image thumbnails), but it provides zero visual feedback by default, leaving users unaware they can click.
**Action:** Whenever using `Widgets.ButtonInvisible`, always pair it with a manual hover check using `Mouse.IsOver(rect)` and a visual change, such as `Widgets.DrawHighlight(rect)` or a background color adjustment, to ensure discoverability.
