# 0071. Bound the assembled worker system prompt under MAX_ARG_STRLEN

**Date:** 2026-09-14
**Status:** Accepted

## Context

`SYSTEM_PROMPT` is delivered to the worker container as a single env var and a single `--append-system-prompt` argv. Linux caps any single argv or env string at `MAX_ARG_STRLEN` = 131,071 bytes, so an oversized prompt fails at `execve` with an opaque `E2BIG` before `claude` starts. Nothing bounded the assembled prompt: each contributor was capped independently (`MaxComments` = 50, `MaxCommentBodyLength` = 4000) and their sum was unchecked. Caps were counted in characters while the ceiling is in bytes, and XML-encoding (`&` → `&amp;`) expands the byte count up to 5x, so a fully-loaded revision dispatch overflowed.

## Decision

Enforce a single UTF-8 byte budget over the fully assembled `SYSTEM_PROMPT` string inside `SystemPromptBuilder.Build`, measured after XML encoding and newline normalisation on exactly the bytes delivered. The design ceiling is 120,000 bytes, leaving headroom under the 131,071-byte kernel limit. Fixed parts (safety preamble, template, section scaffolding) are assembled first; only the variable revision-comment list consumes the remaining quota, dropped oldest-first with the size-omission count stated in the prompt. A single comment larger than the whole budget is truncated on a UTF-8 rune boundary and marked rather than dropped. When the fixed floor alone exceeds the budget, `Build` fails the dispatch explicitly rather than truncating safety rules. `entrypoint.sh` guards defensively at 131,071 bytes as a last line of defence against drift or a builder bypass. Per-comment body truncation stays in the HTTP clients as defense-in-depth. Delivering the prompt via a file or stdin was rejected: it sidesteps the ceiling but changes the worker contract and the entrypoint's `claude` invocation.

## Consequences

- The worker can never fail at `execve` on prompt size in normal operation; oversized input degrades to dropped/truncated feedback or an explicit dispatch failure.
- The budget is measured once on the assembled string, so no per-contributor cap can recreate the overflow.
- The C# design ceiling (120,000) and the shell kernel guard (131,071) are a coupled pair recorded in `DOMAIN.md` and must move together.
- `SystemPromptBuilder.Build` now returns `Result<string>`, rippling through its caller and test fixtures.
- Reviewer intent can be lost when feedback exceeds budget; the prompt states the omission count so the worker knows feedback was withheld.
