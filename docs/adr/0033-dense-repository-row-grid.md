# Dense Repository Row: Shared CSS Grid Template

## Context

The repository settings list previously used a two-row card layout per item (a slug row above a metadata row). As the number of metadata columns grew, vertical space was wasted and column values did not align across rows, making it hard to scan the list at a glance.

## Decision

Replace the two-row card with a single shared CSS Grid row per item, using one `--repo-row-columns` custom property on `.repository-list` and `grid-template-columns: var(--repo-row-columns)` on `.repository-list__item`. The eight columns are: reorder group (auto), identity/slug (1fr), account name, poll interval, status, last-polled, eligibility toggle, and Edit/Delete actions.

The four metadata cells are wrapped in a `__metadata` div with `display:contents` so they participate as direct grid children without adding a box to the layout. This allows the cells to align column-to-column across all rows while remaining semantically grouped in markup.

A `56rem` content-derived breakpoint (wider than the pixel-based predecessor `640px`) switches to a two-tier flex layout on narrow viewports: tier 1 keeps identity and pinned controls; tier 2 renders the metadata cells as a `flex-wrap` inline cluster with CSS `::before` dot separators, so the cluster wraps as a block rather than one value per line.

## Considered Options

- **Two-row card (status quo)** — simple to understand but wastes vertical space and misaligns columns across rows.
- **Table element** — aligns columns naturally but imposes semantic table roles inappropriate for draggable list items with CDK.
- **CSS Grid with `display:contents` metadata wrapper (chosen)** — aligns columns via a single shared template while preserving DOM structure for the drag-and-drop CDK and accessibility tree.
