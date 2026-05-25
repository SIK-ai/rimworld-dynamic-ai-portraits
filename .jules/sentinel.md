## 2024-05-25 - Prevent API Key Leakage in Error Logs
**Vulnerability:** API keys and tokens could be leaked in plaintext if an API request fails, as raw API error responses (`request.downloadHandler.text`) and UnityWebRequest errors (`request.error`) were being logged and passed to callbacks without sanitization.
**Learning:** External API providers often echo back the request payload, headers, or URL (which may contain the API key as a query parameter) in their error responses, making raw error logging a security risk.
**Prevention:** Implement a central sanitization function (e.g., `SanitizeLog`) to mask known sensitive values before they are logged or surfaced to the user.
