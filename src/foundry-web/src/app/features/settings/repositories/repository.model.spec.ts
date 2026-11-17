import {
  EligibilityStatus,
  RepositoryEligibility,
  RepositorySummary,
  WebhookStatus,
  canApplyBranchRules,
  formatPollInterval,
  repositorySettingTags,
  repositoryStatusTag,
  repositoryUpdatesLine,
  splitRepositorySlug,
} from './repository.model';

describe('canApplyBranchRules', () => {
  const branchProtection = (rule: string) => ({ rule: `branch-protection:${rule}`, description: rule });

  it('should be true when every violation is a branch-protection violation', () => {
    // Arrange
    const eligibility: RepositoryEligibility = {
      status: 'ineligible',
      violations: [branchProtection('direct-pushes'), branchProtection('deletion')],
      reason: null,
    };

    // Act / Assert
    expect(canApplyBranchRules(eligibility)).toBe(true);
  });

  it('should be false when any violation is not a branch-protection violation', () => {
    // Arrange
    const eligibility: RepositoryEligibility = {
      status: 'ineligible',
      violations: [branchProtection('direct-pushes'), { rule: 'cannot-push:o/r', description: 'no push' }],
      reason: null,
    };

    // Act / Assert
    expect(canApplyBranchRules(eligibility)).toBe(false);
  });

  it('should be false for eligible, unreachable, null, or violation-less eligibility', () => {
    expect(canApplyBranchRules(null)).toBe(false);
    expect(canApplyBranchRules({ status: 'eligible', violations: [], reason: null })).toBe(false);
    expect(canApplyBranchRules({ status: 'unreachable', violations: [], reason: 'never-probed' })).toBe(false);
    expect(canApplyBranchRules({ status: 'ineligible', violations: [], reason: null })).toBe(false);
  });
});

function eligibilityOf(status: EligibilityStatus | null): RepositoryEligibility | null {
  if (status === null) {
    return null;
  }
  return { status, violations: [], reason: status === 'unreachable' ? 'never-probed' : null };
}

describe('repositoryStatusTag', () => {
  it.each<[EligibilityStatus | null, boolean, WebhookStatus | undefined, string | null]>([
    ['ineligible', true, 'silent', 'Ineligible'],
    ['ineligible', false, undefined, 'Ineligible'],
    ['unreachable', true, 'silent', 'Unverified'],
    ['eligible', true, 'silent', 'Webhook silent'],
    ['eligible', true, 'pending', 'Webhook pending'],
    ['eligible', true, 'not-registered', 'No webhook'],
    ['eligible', true, 'not-configured', null],
    ['eligible', true, 'active', null],
    ['eligible', true, undefined, null],
    ['eligible', false, 'silent', null],
    [null, true, 'pending', 'Webhook pending'],
  ])('eligibility %s, active %s, webhook %s shows %s', (eligibility, isActive, webhookStatus, expected) => {
    // Act
    const tag = repositoryStatusTag({ eligibility: eligibilityOf(eligibility), isActive, webhookStatus });

    // Assert
    expect(tag?.label ?? null).toBe(expected);
  });

  it.each<[EligibilityStatus, WebhookStatus, string]>([
    ['ineligible', 'active', 'ineligible'],
    ['unreachable', 'active', 'warning'],
    ['eligible', 'silent', 'warning'],
    ['eligible', 'pending', 'neutral'],
    ['eligible', 'not-registered', 'neutral'],
  ])('eligibility %s with webhook %s has tone %s', (eligibility, webhookStatus, tone) => {
    // Act
    const tag = repositoryStatusTag({ eligibility: eligibilityOf(eligibility), isActive: true, webhookStatus });

    // Assert
    expect(tag?.tone).toBe(tone);
  });
});

describe('splitRepositorySlug', () => {
  it('splits the namespace, trailing slash included, from the name', () => {
    expect(splitRepositorySlug('acme/platform/billing-api')).toEqual({ namespace: 'acme/platform/', name: 'billing-api' });
  });

  it('returns an empty namespace for a slug without a slash', () => {
    expect(splitRepositorySlug('billing-api')).toEqual({ namespace: '', name: 'billing-api' });
  });
});

describe('formatPollInterval', () => {
  it.each([
    [30, '30s'],
    [300, '5m'],
    [90, '2m'],
  ])('formats %i seconds as %s', (seconds, expected) => {
    expect(formatPollInterval(seconds)).toBe(expected);
  });
});

const NOW = Date.parse('2026-06-14T12:00:00Z');

function repo(overrides: Partial<RepositorySummary> = {}): RepositorySummary {
  return {
    id: 'repo-1',
    slug: 'acme/web',
    accountId: 'account-1',
    accountName: 'acme',
    providerType: 'github',
    position: 0,
    pollIntervalSeconds: null,
    effectivePollIntervalSeconds: 300,
    pollIntervalIsDefault: true,
    isActive: true,
    maxConcurrentWorkers: 1,
    rebaseAfterRun: true,
    mergeWhenChecksPass: false,
    lastPolledAt: null,
    eligibility: { status: 'eligible', violations: [], reason: null },
    webhookStatus: 'not-configured',
    ...overrides,
  };
}

function minutesBefore(minutes: number): string {
  return new Date(NOW - minutes * 60_000).toISOString();
}

describe('repositoryUpdatesLine', () => {
  it('reads "Updates by webhook" for an active webhook, with no time', () => {
    expect(repositoryUpdatesLine(repo({ webhookStatus: 'active', lastPolledAt: minutesBefore(8) }), NOW)).toBe('Updates by webhook');
  });

  it('reads "Updates by webhook" for an active webhook that never polled', () => {
    expect(repositoryUpdatesLine(repo({ webhookStatus: 'active', lastPolledAt: null }), NOW)).toBe('Updates by webhook');
  });

  it('reads the poll interval and last poll for a polling repository', () => {
    expect(repositoryUpdatesLine(repo({ lastPolledAt: minutesBefore(8) }), NOW)).toBe('Polls every 5m, last polled 8m ago');
  });

  it('reads "just now" for a poll under a minute ago', () => {
    const line = repositoryUpdatesLine(
      repo({ effectivePollIntervalSeconds: 30, lastPolledAt: new Date(NOW - 20_000).toISOString() }),
      NOW,
    );
    expect(line).toBe('Polls every 30s, last polled just now');
  });

  it('reads hours for a poll hours ago', () => {
    expect(repositoryUpdatesLine(repo({ lastPolledAt: minutesBefore(120) }), NOW)).toBe('Polls every 5m, last polled 2h ago');
  });

  it('reads days for a poll days ago', () => {
    expect(repositoryUpdatesLine(repo({ lastPolledAt: minutesBefore(3 * 24 * 60) }), NOW)).toBe('Polls every 5m, last polled 3d ago');
  });

  it('reads "never polled" when the repository was never polled', () => {
    expect(repositoryUpdatesLine(repo({ lastPolledAt: null }), NOW)).toBe('Polls every 5m, never polled');
  });

  it('reads "Paused" for a paused repository, even with an active webhook', () => {
    expect(repositoryUpdatesLine(repo({ isActive: false, webhookStatus: 'active', lastPolledAt: minutesBefore(8) }), NOW))
      .toBe('Paused, last polled 8m ago');
  });

  it('reads "Paused, never polled" for a paused repository never polled', () => {
    expect(repositoryUpdatesLine(repo({ isActive: false, lastPolledAt: null }), NOW)).toBe('Paused, never polled');
  });

  it('uses the polling line when an older server sends no webhook status', () => {
    expect(repositoryUpdatesLine(repo({ webhookStatus: undefined, lastPolledAt: minutesBefore(8) }), NOW))
      .toBe('Polls every 5m, last polled 8m ago');
  });
});

describe('repositorySettingTags', () => {
  it('returns no tags at the defaults', () => {
    expect(repositorySettingTags(repo())).toEqual([]);
  });

  it('tags a non-default worker count', () => {
    expect(repositorySettingTags(repo({ maxConcurrentWorkers: 3 }))).toEqual(['3 workers']);
  });

  it('tags merge on pass', () => {
    expect(repositorySettingTags(repo({ mergeWhenChecksPass: true }))).toEqual(['Merges on pass']);
  });

  it('tags rebase off', () => {
    expect(repositorySettingTags(repo({ rebaseAfterRun: false }))).toEqual(['No rebase']);
  });

  it('returns every non-default tag in order', () => {
    expect(repositorySettingTags(repo({ maxConcurrentWorkers: 3, mergeWhenChecksPass: true, rebaseAfterRun: false })))
      .toEqual(['3 workers', 'Merges on pass', 'No rebase']);
  });
});
