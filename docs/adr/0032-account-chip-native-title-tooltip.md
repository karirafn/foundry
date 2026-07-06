# account-chip uses native title attribute for tooltip

## Context

`fd-account-chip` displays a secondary detail tooltip (org name · subscription type, or "Re-login needed") when hovered. The codebase has no tooltip library or custom tooltip primitive.

## Decision

Use the native HTML `title` attribute for the tooltip. This satisfies the acceptance criterion with zero new dependencies and matches the zero-library convention established throughout the project. The trade-off is that `title` tooltips are not accessible on touch devices and have a browser-controlled display delay — acceptable here because the full tooltip content (org, subscription) is also visible in the Settings panel one click away, and the chip's `aria-label` already provides a complete accessible description for screen readers.

## Considered Options

- **Custom tooltip directive / overlay** — would require a new primitive, new dependency, or non-trivial implementation. Rejected: over-engineering for a single secondary-detail use case.
- **aria-describedby with visually-hidden span** — adds DOM complexity without improving the visual hover experience. Rejected: the `title` attribute is the established browser convention for hover tooltips on interactive elements.

## Consequences

Touch users do not see the org/subscription detail inline. A future tooltip primitive could replace `title` at the call site without changing any other component behavior.
