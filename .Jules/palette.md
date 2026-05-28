## 2024-05-18 - [Loading States on Custom IMGUI Buttons]
**Learning:** RimWorld/Unity IMGUI does not automatically disable interactive clicks or provide default loading states for custom primitives like `Widgets.ButtonInvisible`.
**Action:** When creating asynchronous actions triggered by `ButtonInvisible`, always fetch the generation status (`AIPortraitsManager.GetStatus`) and explicitly return early (bypass the click handler) while updating the label/tooltip/color to indicate the "loading" or "disabled" state.
