// Human-readable labels for Foundry's own merge after checks pass (mergeOutcome / mergeSkippedReason, ADR 0105).
// Unknown values fall back to the raw wire text so a newer backend never renders blank.

const MERGE_SKIPPED_REASON_LABELS: Record<string, string> = {
  not_mergeable: 'not mergeable',
  unauthorized: 'no merge permission',
  head_moved: 'branch moved after checks',
  provider_error: 'provider error',
};

export function formatMergeSkippedReason(reason: string | null | undefined): string | null {
  if (reason == null || reason === '') {
    return null;
  }
  return MERGE_SKIPPED_REASON_LABELS[reason] ?? reason;
}

export interface MergeOutcomeBadge {
  readonly label: string;
  readonly cssClass: string;
}

/** Card badge — only a skipped merge is worth an operator's notice; merged and disabled show nothing. */
export function mergeOutcomeBadge(
  outcome: string | null | undefined,
  skippedReason?: string | null,
): MergeOutcomeBadge | null {
  if (outcome !== 'skipped') {
    return null;
  }
  const reason = formatMergeSkippedReason(skippedReason);
  return {
    label: reason !== null ? `Merge skipped: ${reason}` : 'Merge skipped',
    cssClass: 'issue-card__checks-badge--warning',
  };
}
