#!/usr/bin/env bash
# check-ci-path-filters.sh — Fail when a dorny/paths-filter pattern matches no tracked file.
#
# Run from the repository root:
#   bash scripts/check-ci-path-filters.sh
#
# A pattern that matches nothing makes its filter output false on every pull
# request, so the guarded job skips all its steps and passes while empty.
#
# Patterns are checked with git's ":(glob)" pathspec. paths-filter uses
# picomatch; the two agree for the forms the workflows use (literal paths,
# dir/**, dir/**/*.ext and root *.ext). Negated ("!") patterns only narrow a
# filter, so they are skipped.

set -euo pipefail

shopt -s nullglob
workflows=(.github/workflows/*.yml)
shopt -u nullglob

# Emits one "<workflow>\t<filter>\t<pattern>" line per list item inside a
# `filters: |` block scalar. The block ends at the first non-blank line indented
# no deeper than the `filters:` key.
list_patterns() {
  awk '
    function indent_of(line) { match(line, /^ */); return RLENGTH }
    {
      if (in_block) {
        if ($0 ~ /^[[:space:]]*$/) { next }
        if (indent_of($0) <= block_indent) { in_block = 0 }
      }
      if (!in_block) {
        if ($0 ~ /^[[:space:]]*filters:[[:space:]]*\|[[:space:]]*$/) {
          in_block = 1
          block_indent = indent_of($0)
          filter = ""
        }
        next
      }
      line = $0
      if (line ~ /^[[:space:]]*-[[:space:]]/) {
        sub(/^[[:space:]]*-[[:space:]]+/, "", line)
        sub(/[[:space:]]+#.*$/, "", line)
        sub(/[[:space:]]+$/, "", line)
        if (line ~ /^\047.*\047$/ || line ~ /^".*"$/) {
          line = substr(line, 2, length(line) - 2)
        }
        printf "%s\t%s\t%s\n", FILENAME, filter, line
      } else if (line ~ /^[[:space:]]*[^[:space:]#][^:]*:[[:space:]]*(#.*)?$/) {
        sub(/^[[:space:]]+/, "", line)
        sub(/:.*$/, "", line)
        filter = line
      }
    }
  ' "$@"
}

if [ "${#workflows[@]}" -eq 0 ]; then
  exit 0
fi

failures=0
while IFS=$'\t' read -r workflow filter pattern; do
  if [[ "${pattern}" == '!'* ]]; then
    continue
  fi
  if [ -z "$(git ls-files -- ":(glob)${pattern}")" ]; then
    echo "ERROR: ${workflow}: filter '${filter}': pattern '${pattern}' matches no tracked file"
    failures=$((failures + 1))
  fi
done < <(list_patterns "${workflows[@]}")

if [ "${failures}" -gt 0 ]; then
  echo "${failures} path-filter pattern(s) match no tracked file."
  exit 1
fi
