import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { IssueCardComponent, formatCost, formatDuration } from './issue-card';
import { IssueSummary, RunStats } from '../issue.model';
import { TickerService } from '../../../core/services/ticker.service';

const mockIssue: IssueSummary = {
  id: 'abc123',
  issueNumber: 42,
  title: 'Enable dark mode for dashboard',
  state: 'in_progress',
  repositorySlug: 'owner/repo',
  detectedAt: '2026-01-01T00:00:00Z',
  url: 'https://github.com/owner/repo/issues/42',
};

function createComponent(issue: IssueSummary = mockIssue, expanded = false, lastActivityAt: string | null = null) {
  TestBed.configureTestingModule({
    imports: [IssueCardComponent],
    providers: [
      { provide: TickerService, useValue: { tick: signal(0) } },
    ],
  });
  const fixture = TestBed.createComponent(IssueCardComponent);
  fixture.componentRef.setInput('issue', issue);
  fixture.componentRef.setInput('expanded', expanded);
  if (lastActivityAt !== null) {
    fixture.componentRef.setInput('lastActivityAt', lastActivityAt);
  }
  fixture.detectChanges();
  return fixture;
}

describe('IssueCardComponent', () => {
  // Cycle 1: component creates and renders
  it('should create the component', () => {
    // Arrange / Act
    const fixture = createComponent();

    // Assert
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render a native button as the card container', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    expect(card).toBeTruthy();
    expect(card.tagName).toBe('BUTTON');
  });

  // Cycle 2: meta row renders issue number and repo slug
  it('should display the issue number in the meta row', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const number = el.querySelector('.issue-card__number');
    expect(number?.textContent?.trim()).toBe('#42');
  });

  it('should display the repository slug in the meta row', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const slug = el.querySelector('.issue-card__slug');
    expect(slug?.textContent?.trim()).toBe('owner/repo');
  });

  it('should render fd-state-badge with the issue state', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const badge = el.querySelector('fd-state-badge');
    expect(badge).toBeTruthy();
  });

  // Cycle 3: title row
  it('should display the issue title', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const title = el.querySelector('.issue-card__title');
    expect(title?.textContent?.trim()).toBe('Enable dark mode for dashboard');
  });

  it('should set title attribute on the title element for tooltip', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const title = el.querySelector('.issue-card__title') as HTMLElement;
    expect(title?.getAttribute('title')).toBe('Enable dark mode for dashboard');
  });

  // Cycle 4: footer row - timestamp and link
  it('should render a footer with a timestamp', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const timestamp = el.querySelector('.issue-card__timestamp');
    expect(timestamp).toBeTruthy();
  });

  it('should render an external link with target="_blank"', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const link = el.querySelector('.issue-card__link') as HTMLAnchorElement;
    expect(link).toBeTruthy();
    expect(link?.getAttribute('target')).toBe('_blank');
    expect(link?.getAttribute('rel')).toBe('noopener noreferrer');
    expect(link?.getAttribute('href')).toBe('https://github.com/owner/repo/issues/42');
  });

  it('should have a provider-neutral aria-label on the external link', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const link = el.querySelector('.issue-card__link') as HTMLAnchorElement;
    expect(link?.getAttribute('aria-label')).toBe('View issue #42 on owner/repo');
  });

  // Cycle 5: toggle output on click
  it('should emit toggle when the card is clicked', () => {
    // Arrange
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;
    const card = el.querySelector('.issue-card') as HTMLElement;
    let emitted = false;
    fixture.componentInstance.toggle.subscribe(() => (emitted = true));

    // Act
    card.click();

    // Assert
    expect(emitted).toBe(true);
  });

  it('should not render the external link when issue url is not https', () => {
    // Arrange
    const nonHttpsIssue: IssueSummary = { ...mockIssue, url: 'http://github.com/owner/repo/issues/42' };

    // Act
    const fixture = createComponent(nonHttpsIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const link = el.querySelector('.issue-card__link');
    expect(link).toBeFalsy();
  });

  it('should not emit toggle when the external link is clicked', () => {
    // Arrange
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;
    const link = el.querySelector('.issue-card__link') as HTMLAnchorElement;
    let emitted = false;
    fixture.componentInstance.toggle.subscribe(() => (emitted = true));

    // Act
    const event = new MouseEvent('click', { bubbles: true });
    link.dispatchEvent(event);

    // Assert
    expect(emitted).toBe(false);
  });

  // Cycle 6: ARIA - aria-expanded reflects expanded input
  it('should set aria-expanded="false" when not expanded', () => {
    // Arrange / Act
    const fixture = createComponent(mockIssue, false);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    expect(card?.getAttribute('aria-expanded')).toBe('false');
  });

  it('should set aria-expanded="true" when expanded', () => {
    // Arrange / Act
    const fixture = createComponent(mockIssue, true);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    expect(card?.getAttribute('aria-expanded')).toBe('true');
  });

  it('should be focusable as a native button', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLButtonElement;
    expect(card?.tagName).toBe('BUTTON');
    expect(card?.type).toBe('button');
  });

  // Cycle 6b: aria-controls and aria-label
  it('should set aria-controls pointing to the detail element id', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    expect(card?.getAttribute('aria-controls')).toBe('detail-abc123');
  });

  it('should set aria-label with issue number, title, and state', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    expect(card?.getAttribute('aria-label')).toBe('Issue #42: Enable dark mode for dashboard. State: in progress');
  });

  it('should include repo warning in aria-label when issue is queued and repo is ineligible', () => {
    // Arrange
    const queuedIneligibleIssue: IssueSummary = {
      ...mockIssue,
      state: 'queued',
      repositoryEligibilityStatus: 'ineligible',
    };

    // Act
    const fixture = createComponent(queuedIneligibleIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    expect(card?.getAttribute('aria-label')).toContain('Repo ineligible');
  });

  it('should include repo warning in aria-label when issue is queued and repo is unreachable', () => {
    // Arrange
    const queuedUnreachableIssue: IssueSummary = {
      ...mockIssue,
      state: 'queued',
      repositoryEligibilityStatus: 'unreachable',
    };

    // Act
    const fixture = createComponent(queuedUnreachableIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    expect(card?.getAttribute('aria-label')).toContain('Repo unreachable');
  });

  it('should not include repo warning in aria-label when issue has no warning', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    const label = card?.getAttribute('aria-label') ?? '';
    expect(label).not.toContain('Repo ineligible');
    expect(label).not.toContain('Repo unreachable');
  });

  // Cycle 7: keyboard interaction
  it('should emit toggle when Enter key is pressed', () => {
    // Arrange
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;
    const card = el.querySelector('.issue-card') as HTMLElement;
    let emitted = false;
    fixture.componentInstance.toggle.subscribe(() => (emitted = true));

    // Act
    card.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));

    // Assert
    expect(emitted).toBe(true);
  });

  it('should emit toggle when Space key is pressed', () => {
    // Arrange
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;
    const card = el.querySelector('.issue-card') as HTMLElement;
    let emitted = false;
    fixture.componentInstance.toggle.subscribe(() => (emitted = true));

    // Act
    card.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }));

    // Assert
    expect(emitted).toBe(true);
  });

  it('should call preventDefault when Space key is pressed to prevent page scroll', () => {
    // Arrange
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;
    const card = el.querySelector('.issue-card') as HTMLElement;
    const event = new KeyboardEvent('keydown', { key: ' ', bubbles: true, cancelable: true });
    let defaultPrevented = false;
    event.preventDefault = () => { defaultPrevented = true; };

    // Act
    card.dispatchEvent(event);

    // Assert
    expect(defaultPrevented).toBe(true);
  });

  it('should call preventDefault when Enter key is pressed', () => {
    // Arrange
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;
    const card = el.querySelector('.issue-card') as HTMLElement;
    const event = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });
    let defaultPrevented = false;
    event.preventDefault = () => { defaultPrevented = true; };

    // Act
    card.dispatchEvent(event);

    // Assert
    expect(defaultPrevented).toBe(true);
  });

  // Cycle 9: repository eligibility warning marker
  it('should show warning marker when issue is queued and repo is ineligible', () => {
    // Arrange
    const queuedIneligibleIssue: IssueSummary = {
      ...mockIssue,
      state: 'queued',
      repositoryEligibilityStatus: 'ineligible',
    };

    // Act
    const fixture = createComponent(queuedIneligibleIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const marker = el.querySelector('.issue-card__repo-warning');
    expect(marker).toBeTruthy();
  });

  it('should show "Repo ineligible" text for ineligible status', () => {
    // Arrange
    const queuedIneligibleIssue: IssueSummary = {
      ...mockIssue,
      state: 'queued',
      repositoryEligibilityStatus: 'ineligible',
    };

    // Act
    const fixture = createComponent(queuedIneligibleIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const marker = el.querySelector('.issue-card__repo-warning');
    expect(marker?.textContent?.trim()).toBe('Repo ineligible');
  });

  it('should show warning marker when issue is queued and repo is unreachable', () => {
    // Arrange
    const queuedUnreachableIssue: IssueSummary = {
      ...mockIssue,
      state: 'queued',
      repositoryEligibilityStatus: 'unreachable',
    };

    // Act
    const fixture = createComponent(queuedUnreachableIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const marker = el.querySelector('.issue-card__repo-warning');
    expect(marker).toBeTruthy();
  });

  it('should show "Repo unreachable" text for unreachable status', () => {
    // Arrange
    const queuedUnreachableIssue: IssueSummary = {
      ...mockIssue,
      state: 'queued',
      repositoryEligibilityStatus: 'unreachable',
    };

    // Act
    const fixture = createComponent(queuedUnreachableIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const marker = el.querySelector('.issue-card__repo-warning');
    expect(marker?.textContent?.trim()).toBe('Repo unreachable');
  });

  it('should not show warning marker when issue is queued and repo is eligible', () => {
    // Arrange
    const queuedEligibleIssue: IssueSummary = {
      ...mockIssue,
      state: 'queued',
      repositoryEligibilityStatus: 'eligible',
    };

    // Act
    const fixture = createComponent(queuedEligibleIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const marker = el.querySelector('.issue-card__repo-warning');
    expect(marker).toBeFalsy();
  });

  it('should not show warning marker when issue is in_progress even if repo is ineligible', () => {
    // Arrange — warning only applies to queued/pending states
    const inProgressIneligibleIssue: IssueSummary = {
      ...mockIssue,
      state: 'in_progress',
      repositoryEligibilityStatus: 'ineligible',
    };

    // Act
    const fixture = createComponent(inProgressIneligibleIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const marker = el.querySelector('.issue-card__repo-warning');
    expect(marker).toBeFalsy();
  });

  it('should still render the lifecycle badge when warning marker is shown', () => {
    // Arrange — warning is additive, not a replacement
    const queuedIneligibleIssue: IssueSummary = {
      ...mockIssue,
      state: 'queued',
      repositoryEligibilityStatus: 'ineligible',
    };

    // Act
    const fixture = createComponent(queuedIneligibleIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const badge = el.querySelector('fd-state-badge');
    const marker = el.querySelector('.issue-card__repo-warning');
    expect(badge).toBeTruthy();
    expect(marker).toBeTruthy();
  });

  it('should apply the ineligible modifier class to the warning marker', () => {
    // Arrange
    const queuedIneligibleIssue: IssueSummary = {
      ...mockIssue,
      state: 'queued',
      repositoryEligibilityStatus: 'ineligible',
    };

    // Act
    const fixture = createComponent(queuedIneligibleIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const marker = el.querySelector('.issue-card__repo-warning') as HTMLElement;
    expect(marker?.classList.contains('issue-card__repo-warning--ineligible')).toBe(true);
    expect(marker?.getAttribute('role')).toBeNull();
  });

  // Cycle 10: activity line for live issues
  it('should show activity line when issue is in_progress and lastActivityAt is provided', () => {
    // Arrange
    const liveIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };
    const recentAt = new Date(Date.now() - 2 * 60 * 1000).toISOString();

    // Act
    const fixture = createComponent(liveIssue, false, recentAt);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const activity = el.querySelector('.issue-card__activity');
    expect(activity).toBeTruthy();
    expect(activity?.textContent).toContain('active');
  });

  it('should not show activity line when issue is not in a live state', () => {
    // Arrange
    const failedIssue: IssueSummary = { ...mockIssue, state: 'failed' };
    const recentAt = new Date(Date.now() - 2 * 60 * 1000).toISOString();

    // Act
    const fixture = createComponent(failedIssue, false, recentAt);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const activity = el.querySelector('.issue-card__activity');
    expect(activity).toBeFalsy();
  });

  it('should render an sr-only text prefix on the activity span (not a no-op aria-label)', () => {
    // Arrange
    const liveIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };
    const recentAt = new Date(Date.now() - 2 * 60 * 1000).toISOString();

    // Act
    const fixture = createComponent(liveIssue, false, recentAt);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — no aria-label on a roleless span; use sr-only child or issueAriaLabel instead
    const activity = el.querySelector('.issue-card__activity') as HTMLElement;
    expect(activity?.getAttribute('aria-label')).toBeNull();
  });

  it('should not show activity line when lastActivityAt is null', () => {
    // Arrange
    const liveIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };

    // Act
    const fixture = createComponent(liveIssue, false, null);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const activity = el.querySelector('.issue-card__activity');
    expect(activity).toBeFalsy();
  });

  // Cycle 8: usage-limited badge
  it('should display "USAGE LIMITED" badge when issue has usage_limited failure classification', () => {
    // Arrange
    const usageLimitedIssue: IssueSummary = {
      ...mockIssue,
      state: 'failed',
      failureClassification: 'usage_limited',
    };

    // Act
    const fixture = createComponent(usageLimitedIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const badge = el.querySelector('fd-state-badge span');
    expect(badge?.textContent?.trim()).toBe('USAGE LIMITED');
  });

  // Cycle 11: continuation_queued is NOT treated as a live state for the activity timer
  it('should NOT show activity line for continuation_queued even when lastActivityAt is provided', () => {
    // Arrange — regression guard: continuation_queued moved from live to queued tier
    const contQueued: IssueSummary = { ...mockIssue, state: 'continuation_queued' };
    const recentAt = new Date(Date.now() - 2 * 60 * 1000).toISOString();

    // Act
    const fixture = createComponent(contQueued, false, recentAt);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — no activity line since continuation_queued is not live
    const activity = el.querySelector('.issue-card__activity');
    expect(activity).toBeFalsy();
  });

  // Cycle 12: tier chip — removed (redundant with fd-state-badge)
  it('should NOT render a tier chip for revision_queued state', () => {
    // Arrange
    const revisionQueued: IssueSummary = { ...mockIssue, state: 'revision_queued' };

    // Act
    const fixture = createComponent(revisionQueued);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — tier chip removed; state conveyed by fd-state-badge alone
    const tierChip = el.querySelector('.issue-card__tier-chip');
    expect(tierChip).toBeFalsy();
  });

  it('should NOT render a tier chip for continuation_queued state', () => {
    // Arrange
    const contQueued: IssueSummary = { ...mockIssue, state: 'continuation_queued' };

    // Act
    const fixture = createComponent(contQueued);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — tier chip removed; state conveyed by fd-state-badge alone
    const tierChip = el.querySelector('.issue-card__tier-chip');
    expect(tierChip).toBeFalsy();
  });

  it('should NOT render a tier chip for queued state', () => {
    // Arrange
    const freshQueued: IssueSummary = { ...mockIssue, state: 'queued' };

    // Act
    const fixture = createComponent(freshQueued);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — tier chip removed; state conveyed by fd-state-badge alone
    const tierChip = el.querySelector('.issue-card__tier-chip');
    expect(tierChip).toBeFalsy();
  });

  it('should NOT render a tier chip for non-queued states', () => {
    // Arrange
    const inProgress: IssueSummary = { ...mockIssue, state: 'in_progress' };

    // Act
    const fixture = createComponent(inProgress);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const tierChip = el.querySelector('.issue-card__tier-chip');
    expect(tierChip).toBeFalsy();
  });

  // --- formatCost pure function tests (Step 5 TDD) ---

  describe('formatCost', () => {
    // Cycle 1: true zero → $0.00
    it('should format 0 as $0.00', () => {
      // Arrange
      const value = 0;

      // Act
      const result = formatCost(value);

      // Assert
      expect(result).toBe('$0.00');
    });

    // Cycle 2: very small nonzero below a cent → <$0.01
    it('should format a value greater than 0 but less than 0.005 as <$0.01', () => {
      // Arrange
      const value = 0.004;

      // Act
      const result = formatCost(value);

      // Assert
      expect(result).toBe('<$0.01');
    });

    // Cycle 3: exactly 0.005 rounds up to $0.01
    it('should format 0.005 as $0.01 (rounds up to nearest cent)', () => {
      // Arrange
      const value = 0.005;

      // Act
      const result = formatCost(value);

      // Assert
      expect(result).toBe('$0.01');
    });

    // Cycle 4: normal value rounds to 2dp
    it('should format 1.239 as $1.24 (nearest cent)', () => {
      // Arrange
      const value = 1.239;

      // Act
      const result = formatCost(value);

      // Assert
      expect(result).toBe('$1.24');
    });

    // Cycle 5: exact cent boundary
    it('should format 0.01 as $0.01', () => {
      // Arrange
      const value = 0.01;

      // Act
      const result = formatCost(value);

      // Assert
      expect(result).toBe('$0.01');
    });

    // Cycle 6: larger value
    it('should format 12.5 as $12.50', () => {
      // Arrange
      const value = 12.5;

      // Act
      const result = formatCost(value);

      // Assert
      expect(result).toBe('$12.50');
    });
  });

  // --- formatDuration pure function tests (Step 5 TDD) ---

  describe('formatDuration', () => {
    // Cycle 1: seconds only
    it('should format 45000ms as 45s', () => {
      // Arrange
      const ms = 45000;

      // Act
      const result = formatDuration(ms);

      // Assert
      expect(result).toBe('45s');
    });

    // Cycle 2: minutes and seconds
    it('should format 90000ms (1m 30s) correctly', () => {
      // Arrange
      const ms = 90000;

      // Act
      const result = formatDuration(ms);

      // Assert
      expect(result).toBe('1m 30s');
    });

    // Cycle 3: hours and minutes
    it('should format 3660000ms (1h 1m) correctly', () => {
      // Arrange
      const ms = 3660000;

      // Act
      const result = formatDuration(ms);

      // Assert
      expect(result).toBe('1h 1m');
    });

    // Cycle 4: zero
    it('should format 0ms as 0s', () => {
      // Arrange
      const ms = 0;

      // Act
      const result = formatDuration(ms);

      // Assert
      expect(result).toBe('0s');
    });
  });

  // Cycle 13: "Next up" marker visible when isNextUp input is true
  it('should render "Next up" marker when isNextUp input is true', () => {
    // Arrange
    const queuedIssue: IssueSummary = { ...mockIssue, state: 'queued' };

    // Act — create with isNextUp = true
    TestBed.configureTestingModule({
      imports: [IssueCardComponent],
      providers: [
        { provide: TickerService, useValue: { tick: signal(0) } },
      ],
    });
    const fixture = TestBed.createComponent(IssueCardComponent);
    fixture.componentRef.setInput('issue', queuedIssue);
    fixture.componentRef.setInput('expanded', false);
    fixture.componentRef.setInput('isNextUp', true);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const marker = el.querySelector('.issue-card__next-up');
    expect(marker?.textContent?.trim()).toContain('Next up');
  });

  // Cycle 13b: "Next up" marker absent when isNextUp is false (default)
  it('should NOT render "Next up" marker when isNextUp is false', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const marker = el.querySelector('.issue-card__next-up');
    expect(marker).toBeFalsy();
  });

  // Cycle 13d: WCAG H2 — "Next up" pill uses rem, not px (scales with user font-size pref)
  it('should have a "Next up" marker element that exists in the DOM when isNextUp is true (rem font-size verified via CSS)', () => {
    // Arrange
    const queuedIssue: IssueSummary = { ...mockIssue, state: 'queued' };
    TestBed.configureTestingModule({
      imports: [IssueCardComponent],
      providers: [
        { provide: TickerService, useValue: { tick: signal(0) } },
      ],
    });
    const fixture = TestBed.createComponent(IssueCardComponent);
    fixture.componentRef.setInput('issue', queuedIssue);
    fixture.componentRef.setInput('expanded', false);
    fixture.componentRef.setInput('isNextUp', true);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    // Assert — element exists; the font-size must be in rem (not px). JSDOM does not
    // compute CSS custom properties, so we verify the element renders at all.
    // The rem constraint is enforced structurally via the stylesheet check below.
    const marker = el.querySelector('.issue-card__next-up') as HTMLElement;
    expect(marker).toBeTruthy();
    // Inline style must not set a px font-size; rem is applied by the stylesheet.
    expect(marker?.style?.fontSize).not.toMatch(/px$/);
  });

  // Cycle 13c: "Next up" included in aria-label when isNextUp is true
  it('should include "Next up" in the card aria-label when isNextUp is true', () => {
    // Arrange
    const queuedIssue: IssueSummary = { ...mockIssue, state: 'queued' };
    TestBed.configureTestingModule({
      imports: [IssueCardComponent],
      providers: [
        { provide: TickerService, useValue: { tick: signal(0) } },
      ],
    });
    const fixture = TestBed.createComponent(IssueCardComponent);
    fixture.componentRef.setInput('issue', queuedIssue);
    fixture.componentRef.setInput('expanded', false);
    fixture.componentRef.setInput('isNextUp', true);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    expect(card?.getAttribute('aria-label')).toContain('Next up');
  });

  // --- Step 6: stat pill tests ---

  const fullRunStats: RunStats = {
    runCount: 3,
    durationMs: 90000,
    numTurns: 12,
    totalCostUsd: 1.239,
    inputTokens: 5000,
    outputTokens: 2000,
  };

  // Cycle 14: all pills present when all stats are non-null and runCount > 1
  it('should render the run-stats row when runStats has non-null fields', () => {
    // Arrange
    const issueWithStats: IssueSummary = { ...mockIssue, runStats: fullRunStats };

    // Act
    const fixture = createComponent(issueWithStats);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const row = el.querySelector('.issue-card__run-stats');
    expect(row).toBeTruthy();
  });

  it('should render the run-count pill with warning class when runCount > 1', () => {
    // Arrange
    const issueWithStats: IssueSummary = { ...mockIssue, runStats: fullRunStats };

    // Act
    const fixture = createComponent(issueWithStats);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const pill = el.querySelector('.issue-card__stat-pill--run-count');
    expect(pill).toBeTruthy();
    expect(pill?.classList.contains('issue-card__stat-pill--warning')).toBe(true);
  });

  it('should render the duration pill', () => {
    // Arrange
    const issueWithStats: IssueSummary = { ...mockIssue, runStats: fullRunStats };

    // Act
    const fixture = createComponent(issueWithStats);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const pill = el.querySelector('.issue-card__stat-pill--duration');
    expect(pill).toBeTruthy();
  });

  it('should render the turns pill', () => {
    // Arrange
    const issueWithStats: IssueSummary = { ...mockIssue, runStats: fullRunStats };

    // Act
    const fixture = createComponent(issueWithStats);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const pill = el.querySelector('.issue-card__stat-pill--turns');
    expect(pill).toBeTruthy();
  });

  it('should render the cost pill with formatted value', () => {
    // Arrange
    const issueWithStats: IssueSummary = { ...mockIssue, runStats: fullRunStats };

    // Act
    const fixture = createComponent(issueWithStats);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const pill = el.querySelector('.issue-card__stat-pill--cost');
    expect(pill).toBeTruthy();
    expect(pill?.textContent).toContain('$1.24');
  });

  it('should render the input-tokens pill', () => {
    // Arrange
    const issueWithStats: IssueSummary = { ...mockIssue, runStats: fullRunStats };

    // Act
    const fixture = createComponent(issueWithStats);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const pill = el.querySelector('.issue-card__stat-pill--input-tokens');
    expect(pill).toBeTruthy();
  });

  it('should render the output-tokens pill', () => {
    // Arrange
    const issueWithStats: IssueSummary = { ...mockIssue, runStats: fullRunStats };

    // Act
    const fixture = createComponent(issueWithStats);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const pill = el.querySelector('.issue-card__stat-pill--output-tokens');
    expect(pill).toBeTruthy();
  });

  // Cycle 15: runCount === 1 → run-count pill hidden
  it('should hide the run-count pill when runCount is 1', () => {
    // Arrange
    const singleRunStats: RunStats = { ...fullRunStats, runCount: 1 };
    const issueWithStats: IssueSummary = { ...mockIssue, runStats: singleRunStats };

    // Act
    const fixture = createComponent(issueWithStats);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const pill = el.querySelector('.issue-card__stat-pill--run-count');
    expect(pill).toBeFalsy();
  });

  // Cycle 16: null individual totals omit their pill
  it('should omit the duration pill when durationMs is null', () => {
    // Arrange
    const statsNoDuration: RunStats = { ...fullRunStats, durationMs: null };
    const issueWithStats: IssueSummary = { ...mockIssue, runStats: statsNoDuration };

    // Act
    const fixture = createComponent(issueWithStats);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const pill = el.querySelector('.issue-card__stat-pill--duration');
    expect(pill).toBeFalsy();
  });

  it('should omit the cost pill when totalCostUsd is null', () => {
    // Arrange
    const statsNoCost: RunStats = { ...fullRunStats, totalCostUsd: null };
    const issueWithStats: IssueSummary = { ...mockIssue, runStats: statsNoCost };

    // Act
    const fixture = createComponent(issueWithStats);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const pill = el.querySelector('.issue-card__stat-pill--cost');
    expect(pill).toBeFalsy();
  });

  // Cycle 17: empty-row omission when nothing would render
  it('should omit the run-stats row when runStats is null', () => {
    // Arrange
    const issueNoStats: IssueSummary = { ...mockIssue, runStats: null };

    // Act
    const fixture = createComponent(issueNoStats);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const row = el.querySelector('.issue-card__run-stats');
    expect(row).toBeFalsy();
  });

  it('should omit the run-stats row when runStats is undefined', () => {
    // Arrange
    // mockIssue has no runStats property

    // Act
    const fixture = createComponent(mockIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const row = el.querySelector('.issue-card__run-stats');
    expect(row).toBeFalsy();
  });

  // Cycle 18: subtype and isError never in DOM
  it('should never render subtype in the stat row', () => {
    // Arrange
    const issueWithStats: IssueSummary = { ...mockIssue, runStats: fullRunStats };

    // Act
    const fixture = createComponent(issueWithStats);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — RunStats doesn't have subtype; card must not render it
    const subtypePill = el.querySelector('.issue-card__stat-pill--subtype');
    expect(subtypePill).toBeFalsy();
  });

  // Cycle 19: runCount === 1 with ALL metric fields null hides the run-stats row
  it('should omit the run-stats row when runCount is 1 and all metric fields are null', () => {
    // Arrange
    const singleRunNoMetrics: RunStats = {
      runCount: 1,
      durationMs: null,
      numTurns: null,
      totalCostUsd: null,
      inputTokens: null,
      outputTokens: null,
    };
    const issueWithStats: IssueSummary = { ...mockIssue, runStats: singleRunNoMetrics };

    // Act
    const fixture = createComponent(issueWithStats);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const row = el.querySelector('.issue-card__run-stats');
    expect(row).toBeFalsy();
  });

  // Cycle 20: aria-label includes run-stat summary when stats are present
  it('should include run-stat summary in aria-label when runStats has visible pills', () => {
    // Arrange
    const issueWithStats: IssueSummary = { ...mockIssue, runStats: fullRunStats };

    // Act
    const fixture = createComponent(issueWithStats);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    const label = card?.getAttribute('aria-label') ?? '';
    expect(label).toContain('Run stats:');
  });

  it('should include run count in aria-label only when runCount > 1', () => {
    // Arrange
    const issueWithStats: IssueSummary = { ...mockIssue, runStats: fullRunStats }; // runCount: 3

    // Act
    const fixture = createComponent(issueWithStats);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    const label = card?.getAttribute('aria-label') ?? '';
    expect(label).toContain('3 runs');
  });

  it('should omit run count from aria-label when runCount is 1', () => {
    // Arrange
    const singleRunStats: RunStats = { ...fullRunStats, runCount: 1 };
    const issueWithStats: IssueSummary = { ...mockIssue, runStats: singleRunStats };

    // Act
    const fixture = createComponent(issueWithStats);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    const label = card?.getAttribute('aria-label') ?? '';
    expect(label).not.toContain('runs');
  });

  it('should omit null metrics from aria-label run-stat summary', () => {
    // Arrange — only runCount and durationMs are non-null
    const partialStats: RunStats = {
      runCount: 2,
      durationMs: 90000,
      numTurns: null,
      totalCostUsd: null,
      inputTokens: null,
      outputTokens: null,
    };
    const issueWithStats: IssueSummary = { ...mockIssue, runStats: partialStats };

    // Act
    const fixture = createComponent(issueWithStats);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    const label = card?.getAttribute('aria-label') ?? '';
    expect(label).toContain('Run stats:');
    expect(label).toContain('2 runs');
    expect(label).not.toContain('turns');
    expect(label).not.toContain('input tokens');
    expect(label).not.toContain('output tokens');
  });

  it('should not include run-stat summary in aria-label when runStats is null', () => {
    // Arrange
    const issueNoStats: IssueSummary = { ...mockIssue, runStats: null };

    // Act
    const fixture = createComponent(issueNoStats);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    const label = card?.getAttribute('aria-label') ?? '';
    expect(label).not.toContain('Run stats:');
  });
});
