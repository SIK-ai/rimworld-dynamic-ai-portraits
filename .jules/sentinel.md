## 2024-05-24 - [API Key Masking in Mod Settings]
**Vulnerability:** API keys (Google AI Studio, HuggingFace) were displayed in plain text in the Mod Settings UI.
**Learning:** RimWorld's `listing.TextEntry` displays text plainly. This presents a risk of credential leakage for users who may be screen sharing or streaming their gameplay/settings.
**Prevention:** For sensitive credentials in RimWorld mod settings, allocate a `Rect` using `listing.GetRect(height)` and use `UnityEngine.GUI.PasswordField(rect, value, '*')` to mask the input, followed by `listing.Gap(listing.verticalSpacing)` to maintain correct layout spacing.
