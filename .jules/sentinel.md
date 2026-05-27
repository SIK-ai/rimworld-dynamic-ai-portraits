## 2024-05-27 - [API Key Log Leakage]
**Vulnerability:** The `videoApiKey` and `cfBgRemovalKey` from user settings were missing from the log redaction filter (`SanitizeLog` method in `AsyncAIClient.cs`), causing them to potentially be written to plain-text log files during API errors.
**Learning:** Adding new sensitive settings fields to `ModSettings.cs` must be coupled with corresponding redaction implementations in logging/error-handling pipelines.
**Prevention:** Establish a checklist/policy requiring developers to audit logging methods (like `SanitizeLog`) whenever new user-provided credentials are added.
