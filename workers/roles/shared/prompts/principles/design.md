Design:
- Invalid states are unrepresentable: domain concepts have their own types, and required data is required by the type.
- Errors surface as early as possible: compile time over runtime, runtime over never.
- Expected failures are explicit in the return type; a failure never looks like a valid value.
- Every blocked or failed state has an automatic exit; manual intervention is the fallback, not the only way out.
- Defaults are safe and valid; the correct path takes the least code.
- Input is rejected at the boundary with a clear message; the rule itself lives on the type that owns the data.
- Access is as restrictive as callers allow; members exist for production callers, not tests.
