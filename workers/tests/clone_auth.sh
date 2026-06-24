#!/bin/bash
# bash tests for make_clone_url and make_askpass_script in entrypoint.sh.
# Run with: bash workers/tests/clone_auth.sh

set -euo pipefail

ENTRYPOINT="$(cd "$(dirname "$0")/.." && pwd)/entrypoint.sh"

# ---------------------------------------------------------------------------
# Load only the pure functions from entrypoint.sh by sourcing a filtered copy
# ---------------------------------------------------------------------------

# Extract the two functions from the entrypoint into a temp file and source it.
# This avoids executing the top-level logic (env var checks, clone, etc.).
TMPFILE="$(mktemp)"
trap 'rm -f "$TMPFILE"' EXIT

# Extract make_clone_url and make_askpass_script function bodies
awk '
  /^make_clone_url\(\)|^make_askpass_script\(\)/ { printing=1; brace=0 }
  printing {
    print
    # Track brace depth to find end of function
    for (i=1; i<=length($0); i++) {
      c = substr($0, i, 1)
      if (c == "{") brace++
      else if (c == "}") {
        brace--
        if (brace == 0) { printing=0; break }
      }
    }
  }
' "$ENTRYPOINT" > "$TMPFILE"

. "$TMPFILE"

# ---------------------------------------------------------------------------
# Test helpers
# ---------------------------------------------------------------------------
PASS=0
FAIL=0

assert_eq() {
    local desc="$1" actual="$2" expected="$3"
    if [ "$actual" = "$expected" ]; then
        PASS=$((PASS + 1))
        printf "PASS: %s\n" "$desc"
    else
        FAIL=$((FAIL + 1))
        printf "FAIL: %s\n  expected: %s\n  actual:   %s\n" "$desc" "$expected" "$actual"
    fi
}

assert_not_contains() {
    local desc="$1" haystack="$2" needle="$3"
    case "$haystack" in
        *"$needle"*)
            FAIL=$((FAIL + 1))
            printf "FAIL: %s\n  '%s' must not appear in: %s\n" "$desc" "$needle" "$haystack"
            ;;
        *)
            PASS=$((PASS + 1))
            printf "PASS: %s\n" "$desc"
            ;;
    esac
}

# ---------------------------------------------------------------------------
# Tests for make_clone_url
# ---------------------------------------------------------------------------

# GitLab URL becomes https://oauth2@host/path.git
result="$(make_clone_url "https://gitlab.example.com/owner/repo")"
assert_eq \
    "GitLab URL: produces oauth2@host/path.git" \
    "$result" \
    "https://oauth2@gitlab.example.com/owner/repo.git"

# GitHub URL also becomes https://oauth2@host/path.git (single code path)
result="$(make_clone_url "https://github.com/owner/repo")"
assert_eq \
    "GitHub URL: produces oauth2@host/path.git" \
    "$result" \
    "https://oauth2@github.com/owner/repo.git"

# Already ending in .git — suffix stays idempotent
result="$(make_clone_url "https://gitlab.example.com/owner/repo.git")"
assert_eq \
    ".git suffix is idempotent" \
    "$result" \
    "https://oauth2@gitlab.example.com/owner/repo.git"

# No token in the returned clone URL
result="$(GIT_PAT="supersecret" make_clone_url "https://gitlab.example.com/owner/repo")"
assert_not_contains \
    "token never appears in clone URL" \
    "$result" \
    "supersecret"

# Non-https URL is rejected with exit 1
if make_clone_url "git@gitlab.example.com:owner/repo.git" 2>/dev/null; then
    FAIL=$((FAIL + 1))
    printf "FAIL: non-https URL should be rejected\n"
else
    PASS=$((PASS + 1))
    printf "PASS: non-https URL is rejected with non-zero exit\n"
fi

# ---------------------------------------------------------------------------
# Tests for make_askpass_script
# ---------------------------------------------------------------------------

# The askpass script prints GIT_PAT when executed
ASKPASS_SCRIPT="$(make_askpass_script)"
result="$(GIT_PAT="my-token-value" sh -c "$ASKPASS_SCRIPT")"
assert_eq \
    "askpass script prints \$GIT_PAT" \
    "$result" \
    "my-token-value"

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
printf "\n%d passed, %d failed\n" "$PASS" "$FAIL"
[ "$FAIL" -eq 0 ]
