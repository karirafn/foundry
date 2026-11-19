import { formatReviewSkippedReason, reviewOutcomeBadge } from './review-outcome.format';

describe('formatReviewSkippedReason', () => {
  it.each([
    ['verdict_invalid', 'invalid verdict'],
    ['model_unavailable', 'model unavailable'],
    ['transient_api_error', 'claude api error'],
    ['timed_out', 'timed out'],
  ])('labels %s as %s', (reason, label) => {
    expect(formatReviewSkippedReason(reason)).toBe(label);
  });

  it('falls back to the raw token for an unknown reason', () => {
    expect(formatReviewSkippedReason('from_the_future')).toBe('from_the_future');
  });

  it.each([null, undefined, ''])('is null for %o', (reason) => {
    expect(formatReviewSkippedReason(reason)).toBeNull();
  });
});

describe('reviewOutcomeBadge', () => {
  it('shows a skipped review with its short reason as a warning', () => {
    expect(reviewOutcomeBadge('skipped', 'verdict_invalid')).toEqual({
      label: 'Review skipped: invalid verdict',
      cssClass: 'issue-card__checks-badge--warning',
    });
  });

  it('shows a skipped review without a reason', () => {
    expect(reviewOutcomeBadge('skipped', null)?.label).toBe('Review skipped');
  });

  it.each([null, undefined, 'approved'])('shows nothing for %o', (outcome) => {
    expect(reviewOutcomeBadge(outcome, null)).toBeNull();
  });
});
