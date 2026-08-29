#!/usr/bin/env bash
# check-adr-numbering.sh — Enforce the ADR numbering contract.
#
# Run from the repository root:
#   bash scripts/check-adr-numbering.sh
#
# Scanned file sets:
#   Citation resolution  : docs/adr/*.md  +  DOMAIN.md  +  tests/**/*.cs  src/**/*.cs
#   Link / at-least-one  : docs/adr/*.md  +  DOMAIN.md  (markdown only)
#   Front-matter pointers: docs/adr/*.md
#
# Environment variables (all optional):
#   ADR_DIR     — path to the ADR directory      (default: docs/adr)
#   DOMAIN_FILE — path to the domain glossary    (default: DOMAIN.md)
#   REPO_ROOT   — root used to locate src/ and tests/ for C# scanning
#                 (default: two levels above ADR_DIR, i.e. the repo root when
#                 ADR_DIR is the standard docs/adr)
#
# Exit codes:
#   0  — all checks passed
#   1  — one or more violations found (violations printed to stdout)

set -euo pipefail

ADR_DIR="${ADR_DIR:-docs/adr}"
DOMAIN_FILE="${DOMAIN_FILE:-DOMAIN.md}"

# Resolve REPO_ROOT: default to the grandparent of ADR_DIR so that a standard
# docs/adr layout maps to the repository root.  Tests set this to their fixture
# root to avoid scanning the real workspace's src/ and tests/.
if [[ -z "${REPO_ROOT:-}" ]]; then
  REPO_ROOT="$(cd "$(dirname "$ADR_DIR")" && cd .. && pwd)"
fi

errors=0

# Temp directory for stripped markdown content files (one per scanned file).
# Cleaned up on any exit.
TMPDIR_WORK="$(mktemp -d)"
trap 'rm -rf "${TMPDIR_WORK}"' EXIT

# ── helpers ──────────────────────────────────────────────────────────────────

fail() {
  echo "ERROR: $*"
  errors=$((errors + 1))
}

# Strip fenced code blocks from stdin (``` ... ```) and print remaining lines.
# Uses a simple state machine: inside a fence block, lines are suppressed.
strip_fences() {
  awk '
    /^```/ {
      in_fence = !in_fence
      next
    }
    !in_fence { print }
  '
}

# Return the four-digit prefix of a filename, or empty string if it does not
# match the expected NNNN-slug.md pattern.
prefix_of() {
  basename "$1" | command grep -oP '^\d{4}(?=-)'
}

# Write the fence-stripped content of $1 to a temp file and return the path.
make_stripped() {
  local src="$1"
  local dest
  dest="${TMPDIR_WORK}/$(basename "$src").stripped"
  strip_fences < "$src" > "$dest"
  printf '%s' "$dest"
}

# ── 1. Filename format ────────────────────────────────────────────────────────

while IFS= read -r -d '' adr; do
  name=$(basename "$adr")
  if ! printf '%s\n' "$name" | command grep -qP '^[0-9]{4}-[a-z0-9]+(-[a-z0-9]+)*\.md$'; then
    fail "Filename does not match NNNN-kebab-slug.md: $adr"
  fi
done < <(find "$ADR_DIR" -maxdepth 1 -name '*.md' -print0 | sort -z)

# ── 2. Unique prefixes ────────────────────────────────────────────────────────

# Collect all prefixes; flag duplicates.
declare -A seen_prefix
while IFS= read -r -d '' adr; do
  p=$(prefix_of "$adr")
  [[ -z "$p" ]] && continue
  if [[ -n "${seen_prefix[$p]+x}" ]]; then
    fail "Duplicate four-digit prefix $p: ${seen_prefix[$p]} and $adr"
  else
    seen_prefix[$p]="$adr"
  fi
done < <(find "$ADR_DIR" -maxdepth 1 -name '*.md' -print0 | sort -z)

# ── 3. H1 after optional front matter ────────────────────────────────────────

while IFS= read -r -d '' adr; do
  # Walk the file: skip optional front-matter block, then find first non-blank line.
  found_h1=0
  in_fm=0
  closed_fm=0
  line_num=0
  while IFS= read -r line; do
    line_num=$((line_num + 1))
    if [[ $line_num -eq 1 && "$line" == "---" ]]; then
      in_fm=1
      continue
    fi
    if [[ $in_fm -eq 1 && $closed_fm -eq 0 && "$line" == "---" ]]; then
      closed_fm=1
      continue
    fi
    if [[ $in_fm -eq 1 && $closed_fm -eq 0 ]]; then
      # Still inside front matter — skip.
      continue
    fi
    # Past front matter (or no front matter). Find first non-blank line.
    if [[ -n "${line// /}" ]]; then
      if [[ "$line" == "# "* ]]; then
        found_h1=1
      fi
      break
    fi
  done < "$adr"
  if [[ $found_h1 -eq 0 ]]; then
    fail "No H1 title found after optional front matter: $adr"
  fi
done < <(find "$ADR_DIR" -maxdepth 1 -name '*.md' -print0 | sort -z)

# ── 4. Front-matter pointers resolve ─────────────────────────────────────────

while IFS= read -r -d '' adr; do
  # Extract supersedes: and superseded-by: values from front matter only.
  in_fm=0
  line_num=0
  while IFS= read -r line; do
    line_num=$((line_num + 1))
    if [[ $line_num -eq 1 ]]; then
      if [[ "$line" == "---" ]]; then
        in_fm=1
        continue
      else
        break  # No front matter.
      fi
    fi
    if [[ $in_fm -eq 1 && "$line" == "---" ]]; then
      break  # End of front matter.
    fi
    if [[ $in_fm -eq 1 ]]; then
      if [[ "$line" =~ ^(supersedes|superseded-by):\ (.+)$ ]]; then
        target="${BASH_REMATCH[2]}"
        target_path="${ADR_DIR}/${target}"
        if [[ ! -f "$target_path" ]]; then
          fail "Front-matter pointer '${target}' in $adr does not resolve to an existing file"
        fi
      fi
    fi
  done < "$adr"
done < <(find "$ADR_DIR" -maxdepth 1 -name '*.md' -print0 | sort -z)

# ── 5 & 6. Citation resolution and link checks ───────────────────────────────
# Scan markdown files for ADR NNNN citations (excluding fenced code blocks).
# Also scan .cs files for bare ADR NNNN / ADR-NNNN citations.
# Rules applied to markdown files (docs/adr/*.md + DOMAIN.md):
#   a. Every cited NNNN must resolve to docs/adr/NNNN-*.md.
#   b. Every [ADR NNNN](path): path must resolve relative to the citing file's
#      directory, and the label number must equal the target's prefix.
#   c. Every markdown file that cites ADR NNNN must contain at least one link
#      to that number.
# Rules applied to .cs files:
#   a. Every cited NNNN (bare ADR NNNN or ADR-NNNN) must resolve.
#      No link required.

# Collect existing ADR prefixes for resolution lookups.
declare -A adr_exists
while IFS= read -r -d '' adr; do
  p=$(prefix_of "$adr")
  [[ -n "$p" ]] && adr_exists[$p]=1
done < <(find "$ADR_DIR" -maxdepth 1 -name '*.md' -print0)

check_markdown_file() {
  local file="$1"
  local file_dir
  file_dir=$(dirname "$file")

  # Write fence-stripped content to a temp file for reliable repeated grep access.
  local stripped
  stripped=$(make_stripped "$file")

  # Collect all cited numbers (from "ADR NNNN" pattern).
  local cited_nums
  cited_nums=$(command grep -oP '(?<=ADR )\d{4}' "$stripped" | sort -u || true)

  for num in $cited_nums; do
    # a. Resolution check.
    if [[ -z "${adr_exists[$num]+x}" ]]; then
      fail "Citation 'ADR $num' in $file does not resolve to any docs/adr/$num-*.md"
    fi

    # c. At-least-one-link check: file must contain [ADR NNNN](...) for this number.
    if ! command grep -qP "\[ADR $num\]" "$stripped"; then
      fail "File $file cites ADR $num but contains no link to it"
    fi
  done

  # b. Link label vs target prefix check.
  local link
  while IFS= read -r link; do
    [[ -z "$link" ]] && continue
    local label_num target_path target_basename target_prefix resolved
    label_num=$(printf '%s\n' "$link" | command grep -oP '(?<=\[ADR )\d{4}')
    target_path=$(printf '%s\n' "$link" | command grep -oP '(?<=\()[^)]+(?=\))')
    # Resolve path relative to the citing file's directory.
    resolved="${file_dir}/${target_path}"
    if [[ ! -f "$resolved" ]]; then
      fail "Link target '$target_path' in $file does not resolve to an existing file (looked for $resolved)"
    fi
    target_basename=$(basename "$target_path")
    target_prefix=$(printf '%s\n' "$target_basename" | command grep -oP '^\d{4}')
    if [[ "$label_num" != "$target_prefix" ]]; then
      fail "Link [ADR $label_num]($target_path) in $file: label number $label_num does not match target prefix $target_prefix"
    fi
  done < <(command grep -oP '\[ADR \d{4}\]\([^)]+\)' "$stripped" || true)
}

check_cs_file() {
  local file="$1"
  # Extract ADR NNNN and ADR-NNNN citations from .cs files.
  local cited_nums
  cited_nums=$(command grep -oP 'ADR[ -]\K\d{4}' "$file" | sort -u || true)
  for num in $cited_nums; do
    if [[ -z "${adr_exists[$num]+x}" ]]; then
      fail "Citation 'ADR $num' in $file does not resolve to any docs/adr/$num-*.md"
    fi
  done
}

# Process all markdown files.
while IFS= read -r -d '' md; do
  check_markdown_file "$md"
done < <(
  find "$ADR_DIR" -maxdepth 1 -name '*.md' -print0
  [[ -f "$DOMAIN_FILE" ]] && printf '%s\0' "$DOMAIN_FILE"
)

# Process all C# files in ${REPO_ROOT}/src/ and ${REPO_ROOT}/tests/.
while IFS= read -r -d '' cs; do
  check_cs_file "$cs"
done < <(
  find "${REPO_ROOT}/src" "${REPO_ROOT}/tests" -name '*.cs' -print0 2>/dev/null || true
)

# ── Result ────────────────────────────────────────────────────────────────────

if [[ $errors -gt 0 ]]; then
  echo "Found $errors violation(s)."
  exit 1
fi

echo "ADR numbering contract: OK"
exit 0
