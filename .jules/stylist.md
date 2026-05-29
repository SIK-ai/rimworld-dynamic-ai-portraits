## 2024-05-18 - [Button UI improvement]
**Action:** Implemented proper hover states, sound effects, and Text.Anchor restorations for custom buttons in UI_AIPortraitCard.cs.
**Learning:** `Widgets.ButtonInvisible` does not trigger standard hover highlighting or sounds, so `Widgets.DrawHighlightIfMouseover` and `SoundDefOf.Click.PlayOneShotOnCamera(null)` are needed for custom buttons. Also, changing `Text.Anchor` and `Text.Font` should be restored via a `finally` block to prevent leaving the GUI state modified.
