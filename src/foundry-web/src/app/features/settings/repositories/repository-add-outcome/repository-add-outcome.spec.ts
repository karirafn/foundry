import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RepositoryAddOutcomeComponent, isAccessViolation } from './repository-add-outcome';
import {
  BranchRulesOutcome,
  CreateRepositoryResponse,
  RepositoryEligibility,
  RepositorySummary,
  WebhookRegistrationOutcome,
} from '../repository.model';

const SETTINGS_URL = 'https://github.com/my-org/my-repo/settings/rules';

const ELIGIBLE: RepositoryEligibility = { status: 'eligible', violations: [], reason: null };
const INELIGIBLE_RULES: RepositoryEligibility = {
  status: 'ineligible',
  violations: [{ rule: 'branch-protection:allow-direct-pushes', description: 'Allows direct pushes to the protected branch.' }],
  reason: null,
};

function repository(eligibility: RepositoryEligibility | null, providerType: string): RepositorySummary {
  return {
    id: 'r1',
    slug: 'my-org/my-repo',
    accountId: 'a1',
    accountName: 'acc',
    providerType,
    position: 0,
    pollIntervalSeconds: null,
    effectivePollIntervalSeconds: 300,
    pollIntervalIsDefault: true,
    isActive: true,
    maxConcurrentWorkers: 1,
    rebaseAfterRun: true,
    mergeWhenChecksPass: false,
    reviewPullRequests: true,
    lastPolledAt: null,
    eligibility,
  };
}

interface WebhookSetup {
  webhook?: WebhookRegistrationOutcome | null;
  providerType?: string;
  repositoryLink?: boolean;
}

function setup(
  branchRules: BranchRulesOutcome,
  eligibility: RepositoryEligibility | null = INELIGIBLE_RULES,
  { webhook, providerType = 'github', repositoryLink }: WebhookSetup = {},
) {
  TestBed.configureTestingModule({ imports: [RepositoryAddOutcomeComponent], providers: [provideRouter([])] });
  const fixture = TestBed.createComponent(RepositoryAddOutcomeComponent);
  const response: CreateRepositoryResponse = { repository: repository(eligibility, providerType), branchRules };
  if (webhook !== undefined) {
    response.webhook = webhook;
  }
  fixture.componentRef.setInput('response', response);
  if (repositoryLink !== undefined) {
    fixture.componentRef.setInput('repositoryLink', repositoryLink);
  }
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const row = (key: string) => el.querySelector(`[data-row="${key}"]`) as HTMLElement | null;
  return { fixture, el, row };
}

describe('RepositoryAddOutcomeComponent', () => {
  it('should render the access, branch rules and eligibility rows in that order', () => {
    // Arrange / Act
    const { el } = setup({ kind: 'applied' }, ELIGIBLE);

    // Assert
    const keys = Array.from(el.querySelectorAll('.repository-add-outcome__row')).map(r => r.getAttribute('data-row'));
    expect(keys).toEqual(['access', 'branch-rules', 'eligibility']);
  });

  it('should show applied as a passing row with no settings link, rules list or explanation', () => {
    // Arrange / Act
    const { el, row } = setup({ kind: 'applied', reason: null, settingsUrl: null, detail: null }, ELIGIBLE);

    // Assert
    expect(row('branch-rules')?.classList).toContain('repository-add-outcome__row--pass');
    expect(row('branch-rules')?.textContent).toContain('Applied');
    expect(el.querySelector('.repository-add-outcome__settings-link')).toBeFalsy();
    expect(el.querySelector('.repository-add-outcome__rules')).toBeFalsy();
    expect(el.querySelector('.repository-add-outcome__reason')).toBeFalsy();
  });

  it('should ignore a settingsUrl on applied', () => {
    // Arrange / Act
    const { el } = setup({ kind: 'applied', settingsUrl: SETTINGS_URL }, ELIGIBLE);

    // Assert
    expect(el.querySelector('.repository-add-outcome__settings-link')).toBeFalsy();
  });

  const nonApplied: { name: string; outcome: BranchRulesOutcome; status: string; tone: string; reasonText: string }[] = [
    { name: 'already-present', outcome: { kind: 'already-present', settingsUrl: SETTINGS_URL }, status: 'Already in place', tone: 'pass', reasonText: 'already in place' },
    { name: 'not-requested', outcome: { kind: 'not-requested', settingsUrl: SETTINGS_URL }, status: 'Not requested', tone: 'neutral', reasonText: 'turned off' },
    { name: 'skipped / access-failure', outcome: { kind: 'skipped', reason: 'access-failure', settingsUrl: SETTINGS_URL }, status: 'Skipped', tone: 'neutral', reasonText: "account token can't confirm the access" },
    { name: 'skipped / unverifiable', outcome: { kind: 'skipped', reason: 'unverifiable', settingsUrl: SETTINGS_URL }, status: 'Skipped', tone: 'neutral', reasonText: "couldn't be verified" },
    { name: 'failed / insufficient-permission', outcome: { kind: 'failed', reason: 'insufficient-permission', settingsUrl: SETTINGS_URL, detail: 'Must have admin rights' }, status: 'Failed', tone: 'fail', reasonText: "account token isn't allowed" },
    { name: 'failed / provider-error', outcome: { kind: 'failed', reason: 'provider-error', settingsUrl: SETTINGS_URL, detail: 'rules written but not effective' }, status: 'Failed', tone: 'fail', reasonText: 'provider rejected' },
  ];

  for (const c of nonApplied) {
    it(`should render ${c.name} with its status, plain-language reason, the three rules and the settings link`, () => {
      // Arrange / Act
      const { el, row } = setup(c.outcome);

      // Assert
      const branchRow = row('branch-rules')!;
      expect(branchRow.classList).toContain(`repository-add-outcome__row--${c.tone}`);
      expect(branchRow.querySelector('.repository-add-outcome__status')?.textContent?.trim()).toBe(c.status);
      expect(branchRow.querySelector('.repository-add-outcome__reason')?.textContent).toContain(c.reasonText);

      const rules = Array.from(el.querySelectorAll('.repository-add-outcome__rules li')).map(li => li.textContent?.trim());
      expect(rules).toEqual([
        'Reject direct pushes (require a pull request)',
        'Block force pushes',
        'Block deletion of the default branch',
      ]);

      const link = el.querySelector('a.repository-add-outcome__settings-link') as HTMLAnchorElement;
      expect(link).toBeTruthy();
      expect(link.getAttribute('href')).toBe(SETTINGS_URL);
      expect(link.getAttribute('target')).toBe('_blank');
      expect(link.getAttribute('rel')).toBe('noopener');
    });
  }

  it('should show the detail for a failed outcome', () => {
    // Arrange / Act
    const { el } = setup({ kind: 'failed', reason: 'provider-error', settingsUrl: SETTINGS_URL, detail: 'rules written but not effective' });

    // Assert
    expect(el.querySelector('.repository-add-outcome__detail')?.textContent).toContain('rules written but not effective');
  });

  it('should not show a detail for a non-failed outcome even if one is present', () => {
    // Arrange / Act
    const { el } = setup({ kind: 'skipped', reason: 'unverifiable', settingsUrl: SETTINGS_URL, detail: 'stray' });

    // Assert
    expect(el.querySelector('.repository-add-outcome__detail')).toBeFalsy();
  });

  it('should omit the settings link when the server sent no settingsUrl', () => {
    // Arrange / Act
    const { el } = setup({ kind: 'skipped', reason: 'unverifiable', settingsUrl: null });

    // Assert
    expect(el.querySelector('.repository-add-outcome__settings-link')).toBeFalsy();
    expect(el.querySelector('.repository-add-outcome__rules')).toBeTruthy();
  });

  describe('isAccessViolation', () => {
    it('should treat credential-invalid as an access violation', () => {
      // Act
      const result = isAccessViolation({ rule: 'credential-invalid', description: 'rejected' });

      // Assert
      expect(result).toBe(true);
    });
  });

  describe('access row', () => {
    it.each([
      ['cannot-push:my-org/my-repo', 'Token cannot push to my-org/my-repo.'],
      ['no-credential:my-org', 'No credential for namespace my-org.'],
      ['credential-unreadable', "The account token can't be decrypted."],
      ['credential-invalid', 'The provider rejected the account token. Enter a new token on the account to resume.'],
    ])('should fail when eligibility has the %s access violation and list its description', (rule, description) => {
      // Arrange / Act
      const { row } = setup(
        { kind: 'skipped', reason: 'access-failure', settingsUrl: SETTINGS_URL },
        { status: 'ineligible', violations: [{ rule, description }], reason: null },
      );

      // Assert
      expect(row('access')?.classList).toContain('repository-add-outcome__row--fail');
      expect(row('access')?.textContent).toContain(description);
    });

    it('should read "Missing permissions" when the token lacks a permission on the repository', () => {
      // Arrange / Act
      const { row } = setup(
        { kind: 'skipped', reason: 'access-failure', settingsUrl: SETTINGS_URL },
        {
          status: 'ineligible',
          violations: [{ rule: 'cannot-push:my-org/my-repo', description: 'Token is missing Administration (read and write) for my-org/my-repo.' }],
          reason: null,
        },
      );

      // Assert
      expect(row('access')?.textContent).toContain('Missing permissions');
      expect(row('access')?.textContent).not.toContain('No push access');
    });

    it.each(['no-credential:my-org', 'credential-unreadable', 'credential-invalid'])(
      'should read "No push access" for the %s access violation',
      (rule) => {
        // Arrange / Act
        const { row } = setup(
          { kind: 'skipped', reason: 'access-failure', settingsUrl: SETTINGS_URL },
          { status: 'ineligible', violations: [{ rule, description: 'Some access problem.' }], reason: null },
        );

        // Assert
        expect(row('access')?.textContent).toContain('No push access');
      },
    );

    it('should pass when the only violations are branch-protection rules', () => {
      // Arrange / Act
      const { row } = setup({ kind: 'failed', reason: 'provider-error', settingsUrl: SETTINGS_URL }, INELIGIBLE_RULES);

      // Assert
      expect(row('access')?.classList).toContain('repository-add-outcome__row--pass');
      expect(row('access')?.textContent).toContain('Confirmed');
    });

    it('should be neutral (unverified) when eligibility is unreachable', () => {
      // Arrange / Act
      const { row } = setup(
        { kind: 'skipped', reason: 'unverifiable', settingsUrl: SETTINGS_URL },
        { status: 'unreachable', violations: [], reason: 'rate-limited' },
      );

      // Assert
      expect(row('access')?.classList).toContain('repository-add-outcome__row--neutral');
      expect(row('access')?.textContent).toContain('Unverified');
    });
  });

  describe('eligibility row', () => {
    it.each([
      [ELIGIBLE, 'pass', 'Eligible'],
      [INELIGIBLE_RULES, 'fail', 'Ineligible'],
      [{ status: 'unreachable', violations: [], reason: 'never-probed' } as RepositoryEligibility, 'neutral', 'Unverified'],
    ])('should reflect eligibility status %#', (eligibility, tone, label) => {
      // Arrange / Act
      const { row } = setup({ kind: 'not-requested', settingsUrl: SETTINGS_URL }, eligibility);

      // Assert
      expect(row('eligibility')?.classList).toContain(`repository-add-outcome__row--${tone}`);
      expect(row('eligibility')?.textContent).toContain(label);
    });

    it('should be neutral "Not checked" when eligibility is null', () => {
      // Arrange / Act
      const { row } = setup({ kind: 'not-requested', settingsUrl: SETTINGS_URL }, null);

      // Assert
      expect(row('eligibility')?.classList).toContain('repository-add-outcome__row--neutral');
      expect(row('eligibility')?.textContent).toContain('Not checked');
    });
  });

  describe('webhook row', () => {
    const webhookRow = (el: HTMLElement) => el.querySelector('[data-row="webhook"]') as HTMLElement | null;

    it('should render the webhook row after the eligibility row', () => {
      // Arrange / Act
      const { el } = setup({ kind: 'applied' }, ELIGIBLE, { webhook: { kind: 'created' } });

      // Assert
      const keys = Array.from(el.querySelectorAll('.repository-add-outcome__row')).map(r => r.getAttribute('data-row'));
      expect(keys).toEqual(['access', 'branch-rules', 'eligibility', 'webhook']);
    });

    it.each([null, undefined])('should render no webhook row when webhook is %s', (webhook) => {
      // Arrange / Act
      const { el } = setup({ kind: 'applied' }, ELIGIBLE, { webhook });

      // Assert
      expect(webhookRow(el)).toBeFalsy();
    });

    it.each(['created', 'updated'] as const)('should show %s as a passing "Registered" row with no reason or link', (kind) => {
      // Arrange / Act
      const { el } = setup({ kind: 'applied' }, ELIGIBLE, { webhook: { kind } });

      // Assert
      const row = webhookRow(el)!;
      expect(row.classList).toContain('repository-add-outcome__row--pass');
      expect(row.querySelector('.repository-add-outcome__label')?.textContent?.trim()).toBe('Webhook');
      expect(row.querySelector('.repository-add-outcome__status')?.textContent?.trim()).toBe('Registered');
      expect(row.querySelector('.repository-add-outcome__reason')).toBeFalsy();
      expect(row.querySelector('.repository-add-outcome__repository-link')).toBeFalsy();
    });

    const notRegistered: { name: string; outcome: WebhookRegistrationOutcome; providerType: string; tone: string; reason: string }[] = [
      {
        name: 'skipped / not-configured',
        outcome: { kind: 'skipped', reason: 'not-configured' },
        providerType: 'github',
        tone: 'neutral',
        reason: 'Skipped: Foundry has no webhook URL yet. Set it in Dispatch settings; until then this repository polls at its normal interval.',
      },
      {
        name: 'skipped / token-unreadable',
        outcome: { kind: 'skipped', reason: 'token-unreadable' },
        providerType: 'github',
        tone: 'neutral',
        reason: "Skipped: the account token can't be read. Re-enter it on the account, or add the webhook by hand.",
      },
      {
        name: 'skipped / unknown reason',
        outcome: { kind: 'skipped', reason: 'something-new' as WebhookRegistrationOutcome['reason'] },
        providerType: 'github',
        tone: 'neutral',
        reason: 'Skipped: Foundry did not register the webhook.',
      },
      {
        name: 'failed / insufficient-permission on GitHub',
        outcome: { kind: 'failed', reason: 'insufficient-permission' },
        providerType: 'github',
        tone: 'fail',
        reason: "Failed: the account token isn't allowed to manage webhooks on this repository. It needs the Webhooks (read and write) permission.",
      },
      {
        name: 'failed / insufficient-permission on GitLab',
        outcome: { kind: 'failed', reason: 'insufficient-permission' },
        providerType: 'GitLab',
        tone: 'fail',
        reason: "Failed: the account token isn't allowed to manage webhooks on this repository. It needs the Maintainer role.",
      },
      {
        name: 'failed / provider-error on GitHub',
        outcome: { kind: 'failed', reason: 'provider-error' },
        providerType: 'github',
        tone: 'fail',
        reason: 'Failed: GitHub rejected the registration.',
      },
      {
        name: 'failed / no reason on GitLab',
        outcome: { kind: 'failed', reason: null },
        providerType: 'gitlab',
        tone: 'fail',
        reason: 'Failed: GitLab rejected the registration.',
      },
    ];

    for (const c of notRegistered) {
      it(`should show ${c.name} as "Not registered" with its reason`, () => {
        // Arrange / Act
        const { el } = setup({ kind: 'applied' }, ELIGIBLE, { webhook: c.outcome, providerType: c.providerType });

        // Assert
        const row = webhookRow(el)!;
        expect(row.classList).toContain(`repository-add-outcome__row--${c.tone}`);
        expect(row.querySelector('.repository-add-outcome__status')?.textContent?.trim()).toBe('Not registered');
        expect(row.querySelector('.repository-add-outcome__reason')?.textContent?.trim()).toBe(c.reason);
      });
    }

    it('should show the provider detail of a failed registration', () => {
      // Arrange / Act
      const { el } = setup({ kind: 'applied' }, ELIGIBLE, {
        webhook: { kind: 'failed', reason: 'provider-error', detail: 'Validation Failed: hook already exists' },
      });

      // Assert
      expect(webhookRow(el)!.querySelector('.repository-add-outcome__detail')?.textContent?.trim())
        .toBe('Validation Failed: hook already exists');
    });

    it('should not show a detail on a skipped registration', () => {
      // Arrange / Act
      const { el } = setup({ kind: 'applied' }, ELIGIBLE, {
        webhook: { kind: 'skipped', reason: 'not-configured', detail: 'stray' },
      });

      // Assert
      expect(webhookRow(el)!.querySelector('.repository-add-outcome__detail')).toBeFalsy();
    });

    it.each([
      ['skipped', { kind: 'skipped', reason: 'not-configured' }],
      ['failed', { kind: 'failed', reason: 'provider-error', detail: 'boom' }],
    ] as [string, WebhookRegistrationOutcome][])(
      'should end a %s row with a same-tab link to the repository page',
      (_, webhook) => {
        // Arrange / Act
        const { el } = setup({ kind: 'applied' }, ELIGIBLE, { webhook });

        // Assert
        const details = webhookRow(el)!.querySelector('.repository-add-outcome__details')!;
        const link = details.lastElementChild as HTMLAnchorElement;
        expect(link.classList).toContain('repository-add-outcome__repository-link');
        expect(link.textContent?.trim()).toBe('View webhook details');
        expect(link.getAttribute('href')).toBe('/settings/repositories/r1');
        expect(link.getAttribute('target')).toBeNull();
      },
    );

    it('should render no repository link when repositoryLink is false', () => {
      // Arrange / Act
      const { el } = setup({ kind: 'applied' }, ELIGIBLE, {
        webhook: { kind: 'skipped', reason: 'not-configured' },
        repositoryLink: false,
      });

      // Assert
      const row = webhookRow(el)!;
      expect(row.querySelector('.repository-add-outcome__repository-link')).toBeFalsy();
      expect(row.querySelector('.repository-add-outcome__reason')?.textContent).toContain('Dispatch settings');
    });
  });
});
