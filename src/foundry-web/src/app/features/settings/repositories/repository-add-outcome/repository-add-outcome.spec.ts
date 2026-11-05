import { TestBed } from '@angular/core/testing';
import { RepositoryAddOutcomeComponent } from './repository-add-outcome';
import {
  BranchRulesOutcome,
  CreateRepositoryResponse,
  RepositoryEligibility,
  RepositorySummary,
} from '../repository.model';

const SETTINGS_URL = 'https://github.com/my-org/my-repo/settings/rules';

const ELIGIBLE: RepositoryEligibility = { status: 'eligible', violations: [], reason: null };
const INELIGIBLE_RULES: RepositoryEligibility = {
  status: 'ineligible',
  violations: [{ rule: 'branch-protection:allow-direct-pushes', description: 'Allows direct pushes to the protected branch.' }],
  reason: null,
};

function repository(eligibility: RepositoryEligibility | null): RepositorySummary {
  return {
    id: 'r1',
    slug: 'my-org/my-repo',
    accountId: 'a1',
    accountName: 'acc',
    providerType: 'github',
    position: 0,
    pollIntervalSeconds: null,
    effectivePollIntervalSeconds: 300,
    pollIntervalIsDefault: true,
    isActive: true,
    maxConcurrentWorkers: 1,
    rebaseAfterRun: true,
    lastPolledAt: null,
    eligibility,
  };
}

function setup(branchRules: BranchRulesOutcome, eligibility: RepositoryEligibility | null = INELIGIBLE_RULES) {
  TestBed.configureTestingModule({ imports: [RepositoryAddOutcomeComponent] });
  const fixture = TestBed.createComponent(RepositoryAddOutcomeComponent);
  const response: CreateRepositoryResponse = { repository: repository(eligibility), branchRules };
  fixture.componentRef.setInput('response', response);
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
    { name: 'skipped / access-failure', outcome: { kind: 'skipped', reason: 'access-failure', settingsUrl: SETTINGS_URL }, status: 'Skipped', tone: 'neutral', reasonText: 'push access' },
    { name: 'skipped / unverifiable', outcome: { kind: 'skipped', reason: 'unverifiable', settingsUrl: SETTINGS_URL }, status: 'Skipped', tone: 'neutral', reasonText: "couldn't be verified" },
    { name: 'skipped / no-rules-token', outcome: { kind: 'skipped', reason: 'no-rules-token', settingsUrl: SETTINGS_URL }, status: 'Skipped', tone: 'neutral', reasonText: 'branch-rules token' },
    { name: 'failed / insufficient-permission', outcome: { kind: 'failed', reason: 'insufficient-permission', settingsUrl: SETTINGS_URL, detail: 'Must have admin rights' }, status: 'Failed', tone: 'fail', reasonText: "isn't allowed" },
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
    const { el } = setup({ kind: 'skipped', reason: 'no-rules-token', settingsUrl: SETTINGS_URL, detail: 'stray' });

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

  describe('access row', () => {
    it.each([
      ['cannot-push:my-org/my-repo', 'Token cannot push to my-org/my-repo.'],
      ['no-credential:my-org', 'No credential for namespace my-org.'],
      ['credential-unreadable', "The account token can't be decrypted."],
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
});
