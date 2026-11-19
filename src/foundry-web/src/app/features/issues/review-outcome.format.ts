import { getFailureCategoryDisplay } from '../../shared/utils/failure-category';

// Human-readable labels for a review that failed open (reviewOutcome / reviewSkippedReason, ADR 0113).
// The reason is the review run's failure category token, so it reads like that category's badge.
// Unknown values fall back to the raw wire text so a newer backend never renders blank.

export function formatReviewSkippedReason(reason: string | null | undefined): string | null {
  if (reason == null || reason === '') {
    return null;
  }
  return getFailureCategoryDisplay(reason)?.label.toLowerCase() ?? reason;
}

export interface ReviewOutcomeBadge {
  readonly label: string;
  readonly cssClass: string;
}

/** Card badge: a skipped review is the only review outcome a summary carries. */
export function reviewOutcomeBadge(
  outcome: string | null | undefined,
  skippedReason?: string | null,
): ReviewOutcomeBadge | null {
  if (outcome !== 'skipped') {
    return null;
  }
  const reason = formatReviewSkippedReason(skippedReason);
  return {
    label: reason !== null ? `Review skipped: ${reason}` : 'Review skipped',
    cssClass: 'issue-card__checks-badge--warning',
  };
}
