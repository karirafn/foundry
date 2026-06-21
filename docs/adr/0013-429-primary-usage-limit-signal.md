# 429 Status as Primary Usage-Limit Signal

## Context

`ContainerOutputParser` classified a worker result as usage-limited only when `terminal_reason` matched a two-value allowlist (`blocking_limit`, `rapid_refill_breaker`).
Issue #15's real output carried `api_error_status: 429` with `terminal_reason: "completed"` and `subtype: "success"`, so the limit went undetected and the issue was stranded as "Non-zero exit code: 1" with no dispatch pause.

## Decision

Classify a worker result as `UsageLimited` when `api_error_status == 429` (primary signal) OR `terminal_reason` is in the existing allowlist (secondary signal).
The union of two signals means a future non-limit 429 would be misclassified, but no such case is known and the conservative bias (pause dispatch) is safe.

## Considered Options

- **`terminal_reason`-only detection** (status quo) — rejected: misses the real 429 output shape that caused the bug.
- **429-only detection** — rejected: would break detection of older output shapes that report via `terminal_reason` without an `api_error_status` field.
- **Detect via the `result` text phrase "You've hit your limit"** — rejected: the string is fragile and potentially localizable; used only for reset-time extraction, not classification.
