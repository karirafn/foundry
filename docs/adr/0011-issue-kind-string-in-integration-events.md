# IssueKind as String in Integration Events

## Context

The IssueKind value object classifies issues by work type (Feature, Bug, Refactor, Documentation). The Monitoring module detects labels and must communicate the classification to the Issues module via the IssueDetected integration event. The Monitoring module should not reference Issues domain types.

## Decision

The IssueDetected integration event carries IssueKindLabel as a string. The Monitoring module's LabelClassifier produces the string, and the Issues module's handler parses it into the domain IssueKind value object via IssueKind.FromLabel(). This keeps domain type ownership in the Issues module and avoids placing domain logic in the Contracts project.
