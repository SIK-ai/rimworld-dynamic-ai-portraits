## 2025-02-14 - Removed unused `using` statement

**Action:** Removed unused `using System.Collections.Generic;` from `Source/U2NetRemover.cs`.
**Learning:** Verified with `mcs --parse` that removing this unused directive does not break compilation.
