import { formatMergeSkippedReason, mergeOutcomeBadge } from './merge-outcome.format';

describe('formatMergeSkippedReason', () => {
  it.each([
    ['not_mergeable', 'not mergeable'],
    ['unauthorized', 'no merge permission'],
    ['head_moved', 'branch moved after checks'],
    ['provider_error', 'provider error'],
  ])('labels %s as %s', (reason, label) => {
    expect(formatMergeSkippedReason(reason)).toBe(label);
  });

  it('falls back to the raw token for an unknown reason', () => {
    expect(formatMergeSkippedReason('from_the_future')).toBe('from_the_future');
  });

  it.each([null, undefined, ''])('is null for %o', (reason) => {
    expect(formatMergeSkippedReason(reason)).toBeNull();
  });
});

describe('mergeOutcomeBadge', () => {
  it('shows a skipped merge with its short reason as a warning', () => {
    expect(mergeOutcomeBadge('skipped', 'unauthorized')).toEqual({
      label: 'Merge skipped: no merge permission',
      cssClass: 'issue-card__checks-badge--warning',
    });
  });

  it('shows a skipped merge without a reason', () => {
    expect(mergeOutcomeBadge('skipped', null)?.label).toBe('Merge skipped');
  });

  it.each(['merged', 'disabled', null, undefined])('shows nothing for %o', (outcome) => {
    expect(mergeOutcomeBadge(outcome, null)).toBeNull();
  });
});
