#!/usr/bin/env bats
# Tests for scripts/check-ci-path-filters.sh.
#
# Each test builds a throwaway git repository under a per-test temp directory
# and runs the script from its root. The real repository is never touched.

SCRIPT="$(cd "$(dirname "$BATS_TEST_FILENAME")/.." && pwd)/check-ci-path-filters.sh"

setup() {
  FIXTURE_ROOT="$(mktemp -d)"
  git -C "${FIXTURE_ROOT}" init -q
  mkdir -p "${FIXTURE_ROOT}/.github/workflows"
  export FIXTURE_ROOT
}

teardown() {
  rm -rf "${FIXTURE_ROOT}"
}

# Creates and stages each named file in the fixture repository.
track() {
  local path
  for path in "$@"; do
    mkdir -p "${FIXTURE_ROOT}/$(dirname "${path}")"
    touch "${FIXTURE_ROOT}/${path}"
    git -C "${FIXTURE_ROOT}" add -- "${path}"
  done
}

# Writes a workflow with one filters block named `api` holding the given patterns.
workflow_with_patterns() {
  local file="${FIXTURE_ROOT}/.github/workflows/ci.yml"
  {
    echo "jobs:"
    echo "  api:"
    echo "    steps:"
    echo "      - uses: dorny/paths-filter@v4"
    echo "        with:"
    echo "          filters: |"
    echo "            api:"
    local pattern
    for pattern in "$@"; do
      echo "              - ${pattern}"
    done
    echo ""
    echo "      - name: Build"
    echo "        run: dotnet build"
  } > "${file}"
  git -C "${FIXTURE_ROOT}" add -- .github/workflows/ci.yml
}

run_check() {
  cd "${FIXTURE_ROOT}" && bash "${SCRIPT}"
}

@test "a literal file path that exists passes" {
  track Directory.Build.props
  workflow_with_patterns "'Directory.Build.props'"
  run run_check
  [ "$status" -eq 0 ]
}

@test "a dir/** pattern with files under dir passes" {
  track workers/Dockerfile.base
  workflow_with_patterns "'workers/**'"
  run run_check
  [ "$status" -eq 0 ]
}

@test "a src/**/*.cs pattern with a .cs file two levels deep passes" {
  track src/Module/Feature/Thing.cs
  workflow_with_patterns "'src/**/*.cs'"
  run run_check
  [ "$status" -eq 0 ]
}

@test "a root *.slnx pattern passes when the file is at the root" {
  track Foundry.slnx
  workflow_with_patterns "'*.slnx'"
  run run_check
  [ "$status" -eq 0 ]
}

@test "a root *.sln pattern fails when only Foundry.slnx exists" {
  track Foundry.slnx
  workflow_with_patterns "'*.sln'"
  run run_check
  [ "$status" -ne 0 ]
  [[ "$output" == *"*.sln"* ]]
}

@test "a pattern naming a removed directory fails and names the pattern, workflow and filter" {
  track src/Modules/Credentials/Login/Endpoint.cs
  workflow_with_patterns "'src/Modules/Credentials/**'" "'src/Modules/Workers/Login/**'"
  run run_check
  [ "$status" -ne 0 ]
  [[ "$output" == *"src/Modules/Workers/Login/**"* ]]
  [[ "$output" == *"ci.yml"* ]]
  [[ "$output" == *"api"* ]]
  [[ "$output" != *"src/Modules/Credentials/**"* ]]
}

@test "a pattern followed by a comment is matched without the comment" {
  track workers/Dockerfile.base
  workflow_with_patterns "'workers/**'  # the smoke test builds from workers/"
  run run_check
  [ "$status" -eq 0 ]
}

@test "every filters block in every workflow file is checked" {
  track a/one.txt b/two.txt c/three.txt
  cat > "${FIXTURE_ROOT}/.github/workflows/first.yml" <<'EOF'
jobs:
  one:
    steps:
      - uses: dorny/paths-filter@v4
        with:
          filters: |
            alpha:
              - 'a/**'
      - name: Next
        run: true
  two:
    steps:
      - uses: dorny/paths-filter@v4
        with:
          filters: |
            beta:
              - 'missing-beta/**'
EOF
  cat > "${FIXTURE_ROOT}/.github/workflows/second.yml" <<'EOF'
jobs:
  three:
    steps:
      - uses: dorny/paths-filter@v4
        with:
          filters: |
            gamma:
              - 'missing-gamma/**'
              - 'b/**'
  four:
    steps:
      - uses: dorny/paths-filter@v4
        with:
          filters: |
            delta:
              - 'c/**'
            epsilon:
              - 'missing-epsilon/**'
EOF
  git -C "${FIXTURE_ROOT}" add -- .github/workflows
  run run_check
  [ "$status" -ne 0 ]
  [[ "$output" == *"first.yml"*"beta"*"missing-beta/**"* ]]
  [[ "$output" == *"second.yml"*"gamma"*"missing-gamma/**"* ]]
  [[ "$output" == *"second.yml"*"epsilon"*"missing-epsilon/**"* ]]
  [[ "$output" != *"'a/**'"* ]]
  [[ "$output" != *"alpha"* ]]
}

@test "a pattern matching only an untracked file fails" {
  mkdir -p "${FIXTURE_ROOT}/scratch"
  touch "${FIXTURE_ROOT}/scratch/notes.txt"
  workflow_with_patterns "'scratch/**'"
  run run_check
  [ "$status" -ne 0 ]
  [[ "$output" == *"scratch/**"* ]]
}

@test "a negated pattern is skipped" {
  track src/App.cs
  workflow_with_patterns "'src/**'" "'!src/never-existed/**'"
  run run_check
  [ "$status" -eq 0 ]
}

@test "a workflow with no filters block is skipped without error" {
  cat > "${FIXTURE_ROOT}/.github/workflows/release.yml" <<'EOF'
jobs:
  release:
    steps:
      - name: Release
        run: echo release
EOF
  git -C "${FIXTURE_ROOT}" add -- .github/workflows/release.yml
  run run_check
  [ "$status" -eq 0 ]
}
