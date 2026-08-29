#!/usr/bin/env bats
# Tests for scripts/check-adr-numbering.sh.
#
# Each test builds an isolated fixture tree under a per-test temp directory,
# then invokes the script with ADR_DIR and DOMAIN_FILE pointing into that tree.
# The real docs/adr/ and DOMAIN.md are never touched.

SCRIPT="$(cd "$(dirname "$BATS_TEST_FILENAME")/.." && pwd)/check-adr-numbering.sh"

setup() {
  FIXTURE_ROOT="$(mktemp -d)"
  FIXTURE_ADR="${FIXTURE_ROOT}/docs/adr"
  mkdir -p "${FIXTURE_ADR}"
  # Minimal valid ADR file used as a baseline across tests.
  cat > "${FIXTURE_ADR}/0001-first-decision.md" <<'EOF'
# First Decision

## Context

Some context.

## Decision

Some decision.
EOF
  cat > "${FIXTURE_ADR}/0002-second-decision.md" <<'EOF'
# Second Decision

## Context

References [ADR 0001](0001-first-decision.md).

## Decision

Some decision.
EOF
  # Minimal DOMAIN.md that does not cite any ADR.
  echo "# Domain" > "${FIXTURE_ROOT}/DOMAIN.md"

  export FIXTURE_ROOT FIXTURE_ADR
}

teardown() {
  rm -rf "${FIXTURE_ROOT}"
}

run_check() {
  ADR_DIR="${FIXTURE_ADR}" \
  DOMAIN_FILE="${FIXTURE_ROOT}/DOMAIN.md" \
  REPO_ROOT="${FIXTURE_ROOT}" \
    bash "${SCRIPT}" "$@"
}

# ── 1. Clean tree exits 0 ─────────────────────────────────────────────────────

@test "clean fixture tree exits 0" {
  run run_check
  [ "$status" -eq 0 ]
}

# ── 2. Duplicate prefix ───────────────────────────────────────────────────────

@test "duplicate prefix exits non-zero and names the file" {
  cat > "${FIXTURE_ADR}/0001-duplicate-slug.md" <<'EOF'
# Duplicate Decision

## Decision

Some decision.
EOF
  run run_check
  [ "$status" -ne 0 ]
  [[ "$output" == *"0001"* ]]
}

# ── 3. File with no H1 ───────────────────────────────────────────────────────

@test "plain file with no H1 exits non-zero and names the file" {
  cat > "${FIXTURE_ADR}/0003-no-title.md" <<'EOF'
## Context

Missing the top-level heading.
EOF
  run run_check
  [ "$status" -ne 0 ]
  [[ "$output" == *"0003-no-title.md"* ]]
}

@test "front matter present but no H1 exits non-zero and names the file" {
  cat > "${FIXTURE_ADR}/0004-fm-no-title.md" <<'EOF'
---
status: accepted
---

## Context

Front matter is present but the H1 is missing.
EOF
  run run_check
  [ "$status" -ne 0 ]
  [[ "$output" == *"0004-fm-no-title.md"* ]]
}

# ── 4. Citation resolving to no file ─────────────────────────────────────────

@test "bare citation to non-existent ADR exits non-zero and names the citing file" {
  # 0003 cites 9999, which has no matching file.
  cat > "${FIXTURE_ADR}/0003-dangling-citation.md" <<'EOF'
# Dangling Citation

## Decision

See [ADR 9999](9999-nonexistent.md) for rationale.
EOF
  run run_check
  [ "$status" -ne 0 ]
  [[ "$output" == *"0003-dangling-citation.md"* ]]
}

# ── 5. Link label number disagrees with target prefix ────────────────────────

@test "link label number mismatching target prefix exits non-zero and names the file" {
  # Add a valid 0013 ADR so the citation-resolution check (a) passes for 0013,
  # isolating the label-mismatch check (b) as the sole source of failure.
  cat > "${FIXTURE_ADR}/0013-valid-decision.md" <<'EOF'
# Valid Decision

## Decision

Some decision with no citations.
EOF
  cat > "${FIXTURE_ADR}/0003-mismatched-link.md" <<'EOF'
# Mismatched Link

## Decision

See [ADR 0013](0001-first-decision.md) — label says 0013 but target prefix is 0001.
EOF
  run run_check
  [ "$status" -ne 0 ]
  [[ "$output" == *"0003-mismatched-link.md"* ]]
  # Confirm the label-mismatch message fired (not the resolution error).
  [[ "$output" == *"label number"* ]] || [[ "$output" == *"does not match target prefix"* ]]
}

# ── 6. supersedes: pointer to a missing file ─────────────────────────────────

@test "supersedes pointer to missing file exits non-zero and names the ADR" {
  cat > "${FIXTURE_ADR}/0003-bad-supersedes.md" <<'EOF'
---
status: accepted
supersedes: 9999-nonexistent.md
---

# Bad Supersedes

## Decision

Some decision.
EOF
  run run_check
  [ "$status" -ne 0 ]
  [[ "$output" == *"0003-bad-supersedes.md"* ]]
}

# ── 7. Four digits inside a slug are not a prefix ────────────────────────────

@test "four digits inside slug do not cause false duplicate failure" {
  # 0042-serialize-enums-as-strings-for-http pattern: digits in the middle.
  cat > "${FIXTURE_ADR}/0003-serialize-enums-1234-http.md" <<'EOF'
# Serialize Enums 1234 HTTP

## Decision

Some decision involving 1234 things.
EOF
  run run_check
  [ "$status" -eq 0 ]
}

# ── 8. Citation inside fenced code block is excluded ─────────────────────────

@test "citation inside backtick fenced code block is not checked for resolution" {
  cat > "${FIXTURE_ADR}/0003-with-code-block.md" <<'EOF'
# With Code Block

## Decision

Normal prose here.

```
See ADR 9999 in a backtick code fence — should be excluded from resolution check.
```

Back to prose.
EOF
  run run_check
  [ "$status" -eq 0 ]
}

@test "citation inside tilde fenced code block is not checked for resolution" {
  cat > "${FIXTURE_ADR}/0003-with-tilde-block.md" <<'EOF'
# With Tilde Block

## Decision

Normal prose here.

~~~
See ADR 9999 in a tilde code fence — should be excluded from resolution check.
~~~

Back to prose.
EOF
  run run_check
  [ "$status" -eq 0 ]
}

@test "link with anchor fragment resolves the file and does not false-error" {
  cat > "${FIXTURE_ADR}/0003-with-anchor.md" <<'EOF'
# With Anchor

## Decision

See [ADR 0001](0001-first-decision.md#context) for background.
EOF
  run run_check
  [ "$status" -eq 0 ]
}

# ── 9. C# hyphen form ADR-NNNN is accepted bare ──────────────────────────────

@test "ADR-NNNN in a .cs file resolves and is accepted without a link" {
  mkdir -p "${FIXTURE_ROOT}/tests/Unit"
  cat > "${FIXTURE_ROOT}/tests/Unit/SomeTest.cs" <<'EOF'
// This test verifies the behaviour described in ADR-0001.
public class SomeTest { }
EOF
  run env ADR_DIR="${FIXTURE_ADR}" DOMAIN_FILE="${FIXTURE_ROOT}/DOMAIN.md" REPO_ROOT="${FIXTURE_ROOT}" bash "${SCRIPT}"
  [ "$status" -eq 0 ]
}

@test "ADR-NNNN in a .cs file for non-existent ADR exits non-zero" {
  mkdir -p "${FIXTURE_ROOT}/tests/Unit"
  cat > "${FIXTURE_ROOT}/tests/Unit/SomeTest.cs" <<'EOF'
// Refers to ADR-9999 which does not exist.
public class SomeTest { }
EOF
  run env ADR_DIR="${FIXTURE_ADR}" DOMAIN_FILE="${FIXTURE_ROOT}/DOMAIN.md" REPO_ROOT="${FIXTURE_ROOT}" bash "${SCRIPT}"
  [ "$status" -ne 0 ]
  [[ "$output" == *"SomeTest.cs"* ]]
}

# ── 10. DOMAIN.md at root: links with docs/adr/ prefix ──────────────────────

@test "DOMAIN.md link with docs/adr/ prefix resolves correctly" {
  cat > "${FIXTURE_ROOT}/DOMAIN.md" <<'EOF'
# Domain

See [ADR 0001](docs/adr/0001-first-decision.md) for the first decision.
EOF
  run run_check
  [ "$status" -eq 0 ]
}

@test "DOMAIN.md link missing docs/adr/ prefix fails with the unresolvable target" {
  # DOMAIN.md sits at root; a bare filename without docs/adr/ cannot resolve.
  cat > "${FIXTURE_ROOT}/DOMAIN.md" <<'EOF'
# Domain

See [ADR 0001](0001-first-decision.md) — missing the docs/adr/ prefix.
EOF
  run run_check
  [ "$status" -ne 0 ]
  [[ "$output" == *"DOMAIN.md"* ]]
}

# ── 11. Bare-only citing file requires at least one link ─────────────────────

@test "file that cites ADR NNNN but has no link form exits non-zero" {
  cat > "${FIXTURE_ADR}/0003-bare-only.md" <<'EOF'
# Bare Only Citation

## Decision

See ADR 0001 for the first decision — no link form used.
EOF
  run run_check
  [ "$status" -ne 0 ]
  [[ "$output" == *"0003-bare-only.md"* ]]
}

# ── 12. superseded-by pointer to a missing file ──────────────────────────────

@test "superseded-by pointer to missing file exits non-zero and names the ADR" {
  cat > "${FIXTURE_ADR}/0003-bad-superseded-by.md" <<'EOF'
---
status: superseded
superseded-by: 9999-nonexistent.md
---

# Bad Superseded-By

## Decision

Some decision.
EOF
  run run_check
  [ "$status" -ne 0 ]
  [[ "$output" == *"0003-bad-superseded-by.md"* ]]
}
