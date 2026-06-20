# Scope-Aware Label Classification

## Context

`LabelClassifier` matched issue labels by exact case-insensitive equality (`feature`, `bug`, `refactor`, `documentation`). GitLab supports scoped labels with `::` syntax (e.g., `type::feature`), which are semantically equivalent but would not match the existing classifier.

## Decision

Extend `LabelClassifier` to strip the scope prefix before matching. Both `feature` and `type::feature` now classify as Feature. The classifier remains provider-agnostic — one implementation serves both GitHub and GitLab.

This is a minor behavioural change for GitHub: if a GitHub repo happened to use scoped labels (e.g., `type::bug`), they would now classify correctly where before they were ignored.
