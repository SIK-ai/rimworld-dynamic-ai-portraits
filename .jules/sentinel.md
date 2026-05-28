
## 2024-05-24 - API Key Leakage in UnityWebRequest Errors
**Vulnerability:** API keys passed as parameters to coroutines were leaked in UnityWebRequest error messages (e.g., request.error and request.downloadHandler.text).
**Learning:** Centralized logging sanitizers (like SanitizeLog) don't protect isolated coroutines that bypass them and return errors directly via callbacks. API keys embedded in URLs will leak into request.error on HTTP failures.
**Prevention:** Always inline-scrub sensitive parameters (like apiKey) from error messages before calling error callbacks in isolated methods.
