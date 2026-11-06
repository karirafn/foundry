# Usage Report First, with a Probe-Turn Fallback

## Context

A Usage Poll needs two kinds of figures. One is the subscription windows (session, weekly, per-model weekly). The other is the extra-usage credit spend. The Claude Code CLI exposes them in two ways:

- `claude -p "/usage" --output-format stream-json --verbose` answers with a synthetic assistant message. Its wrapper-level `usage_report` sibling carries `rate_limits.limits[]` (Anthropic's rows, 0–100 `percent`, ISO-8601 `resets_at`, `scope.model.display_name`, Anthropic's own `severity`, `is_active`) and `rate_limits.extra_usage` (`is_enabled`, `monthly_limit`, `used_credits`, `utilization`, `currency`, with amounts in minor units). It uses no API turn. The CLI documents the shape as experimental.
- A minimal real turn (`claude -p "Reply with: ok" --max-turns 1 ...`) emits a `rate_limit_event`. Its `rate_limit_info.unifiedWindows` carries the five-hour and seven-day windows. It has no credit amounts and no per-model limits, and it costs a real (cheap) API turn.

The `/usage` print mode has been unstable across CLI versions. 2.1.187 emitted no `usage_report`. 2.1.302 and 2.1.314 emitted `rate_limits: null`. 2.1.316 emits populated rows and `extra_usage`. Dependabot bumps the CLI weekly, and workers CI has no OAuth session, so a regression would merge unnoticed. Foundry had moved to the probe turn alone, which lost the credits and the per-model limits.

## Decision

A Usage Poll runs `/usage` first and falls back to the probe turn only when the report carries no rate limits.

- **Primary**: the usage container runs `claude -p "/usage" --output-format stream-json --verbose`. `UsageReportParser` maps every row verbatim, with Anthropic's severity kept as a string, and maps `extra_usage` into `UsageExtraCredits`. The snapshot gets `Source = UsageReport`.
- **Fallback trigger**: a `usage_report` whose `rate_limits`, or whose `rate_limits.limits`, is null or absent parses to `UsageParseResult.RateLimitsMissing(ExtraCredits?)`. `UsagePollCoordinator` then runs the probe-turn container (`ProbeTurnUsageParser`) inside the same single-flight poll, after `/usage` finishes and never concurrently ([ADR 0081](0081-usage-polling-single-flight-joins.md)). A successful probe stores its session and weekly windows, together with any credits that `/usage` still reported, as `Source = ProbeTurn`. Each fallback logs a warning naming the CLI version from the stream-json `init` line (`claude_code_version`), so a regressed CLI shows up in the logs.
- **No fallback otherwise**: when `/usage` fails outright (no JSON line, broken fields, a Docker error, or a `timeout` kill), the poll fails, keeping the last good snapshot and stamping `LastRefreshFailedAt`. When `/usage` emits no `usage_report` at all (API-key mode, not reportable), the snapshot is cleared. When the fallback probe itself fails or reports no windows, the poll fails, because `/usage` already showed the account is reportable.
- **Credit percent and severity belong to the value object**: `UsageExtraCredits.Percent` is the server's `utilization` when present, otherwise `UsedCredits / MonthlyLimit * 100`. It is null when `MonthlyLimit` is null or 0 (amount-only). `Severity` is `critical` at >= 100, `warning` at >= 80, otherwise `normal`, and null when `Percent` is null. The client renders the server's decision.
- **The branching lives in code** ([ADR 0094](0094-code-decides-claude-judges-only-what-code-cannot.md)): `UsageReportSpec.Command` picks the argv for each container. No shell script chains the two commands.

## Considered Options

- **Call `api.anthropic.com/api/oauth/usage` directly with the volume token**: rejected, because raw-HTTP use of the OAuth token is what [ADR 0024](0024-delegate-claude-credential-lifecycle-to-claude-code.md) forbids, and the endpoint is undocumented.
- **`/usage` as the only source**: rejected, because a null-`rate_limits` regression would leave the meters failed until an operator reverted the CLI.
- **`/usage` for credits plus a probe turn for windows on every poll**: rejected, because it means two containers per poll, two parsers always live, and windows drawn from mixed sources.
- **One container running both commands via a shell script**: rejected, because the branching would be untestable in the coordinator.
- **Keep the probe turn only, showing overage flags without amounts**: rejected, because `rate_limit_event` carries no amounts, so no credits bar is possible.
- **Compute the credit percent in the parser or the client**: rejected, because the logic belongs on `UsageExtraCredits`.

## Consequences

- The snapshot gains a `Source` (`usageReport` | `probeTurn`) on the wire. A fallback snapshot has only `session` and `weekly_all` rows, and their severity is Foundry's own (>= 1.0 critical, >= 0.8 warning).
- The wire contract gains `extraUsage.percent` and `extraUsage.severity`. `extraUsage.monthlyLimit`, `extraUsage.utilization` and `limits[].resetsAt` become nullable, matching the CLI schema.
- In fallback mode every 5-minute poll spends a real API turn, as the probe-only design did. In normal operation no turn is spent.
- `/usage` calls an upstream endpoint that is rate-limited jointly with the operator's interactive sessions. A 429 surfaces as a failed poll, and the 5-minute cadence and single-flight bound the rate.
