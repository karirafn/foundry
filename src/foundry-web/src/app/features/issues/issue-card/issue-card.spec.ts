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

function createComponent(issue: IssueSummary = mockIssue, expanded = false, lastActivityAt: string | null = null, commitCount: number | null = null, isNextUp = false, queuePosition: number | null = null) {
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
  if (commitCount !== null) {
    fixture.componentRef.setInput('commitCount', commitCount);
  }
  if (isNextUp) {
    fixture.componentRef.setInput('isNextUp', true);
  }
  fixture.componentRef.setInput('queuePosition', queuePosition);
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

  // Cycle 22: commit count phrases in the activity line
  it('should show "no commits yet" when commitCount is 0', () => {
    // Arrange
    const liveIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };
    const recentAt = new Date(Date.now() - 2 * 60 * 1000).toISOString();

    // Act
    const fixture = createComponent(liveIssue, false, recentAt, 0);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const activity = el.querySelector('.issue-card__activity') as HTMLElement;
    expect(activity?.textContent).toContain('no commits yet');
  });

  it('should show "1 commit" (singular) when commitCount is 1', () => {
    // Arrange
    const liveIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };
    const recentAt = new Date(Date.now() - 2 * 60 * 1000).toISOString();

    // Act
    const fixture = createComponent(liveIssue, false, recentAt, 1);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const activity = el.querySelector('.issue-card__activity') as HTMLElement;
    expect(activity?.textContent).toContain('1 commit');
    expect(activity?.textContent).not.toContain('1 commits');
  });

  it('should show "2 commits" (plural) when commitCount is 2', () => {
    // Arrange
    const liveIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };
    const recentAt = new Date(Date.now() - 2 * 60 * 1000).toISOString();

    // Act
    const fixture = createComponent(liveIssue, false, recentAt, 2);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const activity = el.querySelector('.issue-card__activity') as HTMLElement;
    expect(activity?.textContent).toContain('2 commits');
  });

  it('should show "N commits" for N >= 2', () => {
    // Arrange
    const liveIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };
    const recentAt = new Date(Date.now() - 2 * 60 * 1000).toISOString();

    // Act
    const fixture = createComponent(liveIssue, false, recentAt, 5);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const activity = el.querySelector('.issue-card__activity') as HTMLElement;
    expect(activity?.textContent).toContain('5 commits');
  });

  // Cycle 23: silence threshold — no silence segment when < 5 minutes
  it('should NOT show silence segment when silent duration is less than 5 minutes', () => {
    // Arrange — 4 minutes 59 seconds silent (just under threshold)
    const liveIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };
    const justUnderThreshold = new Date(Date.now() - (5 * 60 * 1000 - 1000)).toISOString();

    // Act
    const fixture = createComponent(liveIssue, false, justUnderThreshold, 0);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const activity = el.querySelector('.issue-card__activity') as HTMLElement;
    expect(activity?.textContent).not.toContain('silent');
  });

  it('should show silence segment when silent duration is exactly 5 minutes', () => {
    // Arrange — exactly 5 minutes silent
    const liveIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };
    const exactlyFiveMin = new Date(Date.now() - 5 * 60 * 1000).toISOString();

    // Act
    const fixture = createComponent(liveIssue, false, exactlyFiveMin, 0);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const activity = el.querySelector('.issue-card__activity') as HTMLElement;
    expect(activity?.textContent).toContain('silent 5m');
  });

  it('should show silence segment when silent duration is more than 5 minutes', () => {
    // Arrange — 7 minutes silent
    const liveIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };
    const sevenMinutesAgo = new Date(Date.now() - 7 * 60 * 1000).toISOString();

    // Act
    const fixture = createComponent(liveIssue, false, sevenMinutesAgo, 0);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const activity = el.querySelector('.issue-card__activity') as HTMLElement;
    expect(activity?.textContent).toContain('silent 7m');
  });

  it('should place the silence segment after the commit segment', () => {
    // Arrange — 7 minutes silent, 3 commits
    const liveIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };
    const sevenMinutesAgo = new Date(Date.now() - 7 * 60 * 1000).toISOString();

    // Act
    const fixture = createComponent(liveIssue, false, sevenMinutesAgo, 3);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — commit segment appears before silence segment in text
    const text = el.querySelector('.issue-card__activity')?.textContent ?? '';
    const commitIdx = text.indexOf('3 commits');
    const silentIdx = text.indexOf('silent');
    expect(commitIdx).toBeGreaterThan(-1);
    expect(silentIdx).toBeGreaterThan(commitIdx);
  });

  it('should render "no commits yet · silent 7m" for 0 commits and 7 minutes silent', () => {
    // Arrange
    const liveIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };
    const sevenMinutesAgo = new Date(Date.now() - 7 * 60 * 1000).toISOString();

    // Act
    const fixture = createComponent(liveIssue, false, sevenMinutesAgo, 0);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const text = el.querySelector('.issue-card__activity')?.textContent ?? '';
    expect(text).toContain('no commits yet');
    expect(text).toContain('silent 7m');
  });

  it('should NOT show activity line when issue is not in LIVE_STATES even with commitCount', () => {
    // Arrange
    const failedIssue: IssueSummary = { ...mockIssue, state: 'failed' };
    const recentAt = new Date(Date.now() - 2 * 60 * 1000).toISOString();

    // Act
    const fixture = createComponent(failedIssue, false, recentAt, 5);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — no activity line regardless of commitCount
    const activity = el.querySelector('.issue-card__activity');
    expect(activity).toBeFalsy();
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

  // Cycle 13: "Next up" pill REMOVED — gutter ordinal replaces it; .issue-card__next-up must never render
  it('should NEVER render .issue-card__next-up even when isNextUp is true', () => {
    // Arrange
    const queuedIssue: IssueSummary = { ...mockIssue, state: 'queued' };

    // Act
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

    // Assert — pill removed; ordinal gutter replaces it
    const pill = el.querySelector('.issue-card__next-up');
    expect(pill).toBeFalsy();
  });

  // Cycle 13b: .issue-card__next-up never renders regardless of isNextUp being false
  it('should NOT render .issue-card__next-up when isNextUp is false', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const pill = el.querySelector('.issue-card__next-up');
    expect(pill).toBeFalsy();
  });

  // Cycle 13d: queued-tier card renders the gutter ordinal; ordinal uses rem (no inline px font-size)
  it('should render .issue-card__queue-position gutter for a queued-tier card with isNextUp=true (rem font-size enforced via stylesheet)', () => {
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
    fixture.componentRef.setInput('queuePosition', 1);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    // Assert — gutter element renders; no inline px font-size (rem applied by stylesheet)
    const gutter = el.querySelector('.issue-card__queue-position') as HTMLElement;
    expect(gutter).toBeTruthy();
    expect(gutter?.style?.fontSize).not.toMatch(/px$/);
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

  // Cycle 21: BEM modifier for accent states
  it('should apply issue-card--working class for in_progress state', () => {
    // Arrange
    const inProgressIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };

    // Act
    const fixture = createComponent(inProgressIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    expect(card.classList.contains('issue-card--working')).toBe(true);
    expect(card.classList.contains('issue-card--ready')).toBe(false);
  });

  it('should apply issue-card--working class for revision_in_progress state', () => {
    // Arrange
    const revisionInProgressIssue: IssueSummary = { ...mockIssue, state: 'revision_in_progress' };

    // Act
    const fixture = createComponent(revisionInProgressIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    expect(card.classList.contains('issue-card--working')).toBe(true);
    expect(card.classList.contains('issue-card--ready')).toBe(false);
  });

  it('should apply issue-card--ready class for review state', () => {
    // Arrange
    const reviewIssue: IssueSummary = { ...mockIssue, state: 'review' };

    // Act
    const fixture = createComponent(reviewIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    expect(card.classList.contains('issue-card--ready')).toBe(true);
    expect(card.classList.contains('issue-card--working')).toBe(false);
  });

  it('should not apply any accent modifier class for failed state', () => {
    // Arrange
    const failedIssue: IssueSummary = { ...mockIssue, state: 'failed' };

    // Act
    const fixture = createComponent(failedIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    expect(card.classList.contains('issue-card--working')).toBe(false);
    expect(card.classList.contains('issue-card--ready')).toBe(false);
  });

  it('should not apply any accent modifier class for completed state', () => {
    // Arrange
    const completedIssue: IssueSummary = { ...mockIssue, state: 'completed' };

    // Act
    const fixture = createComponent(completedIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    expect(card.classList.contains('issue-card--working')).toBe(false);
    expect(card.classList.contains('issue-card--ready')).toBe(false);
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

  // Finding 1: aria-label includes activity with expanded units
  it('should include activity with commit count in aria-label for a live issue', () => {
    // Arrange
    const liveIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };
    const recentAt = new Date(Date.now() - 2 * 60 * 1000).toISOString();

    // Act
    const fixture = createComponent(liveIssue, false, recentAt, 3);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — aria-label should contain screen-reader-friendly activity info
    const card = el.querySelector('.issue-card') as HTMLElement;
    const label = card?.getAttribute('aria-label') ?? '';
    expect(label).toContain('Active: 3 commits.');
  });

  it('should include "1 commit" (singular) in aria-label for a live issue with 1 commit', () => {
    // Arrange
    const liveIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };
    const recentAt = new Date(Date.now() - 2 * 60 * 1000).toISOString();

    // Act
    const fixture = createComponent(liveIssue, false, recentAt, 1);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    const label = card?.getAttribute('aria-label') ?? '';
    expect(label).toContain('Active: 1 commit.');
  });

  it('should include "no commits yet" in aria-label for a live issue with 0 commits', () => {
    // Arrange
    const liveIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };
    const recentAt = new Date(Date.now() - 2 * 60 * 1000).toISOString();

    // Act
    const fixture = createComponent(liveIssue, false, recentAt, 0);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    const label = card?.getAttribute('aria-label') ?? '';
    expect(label).toContain('Active: no commits yet.');
  });

  it('should include expanded silence duration in aria-label when silent >= 5 minutes', () => {
    // Arrange
    const liveIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };
    const sevenMinutesAgo = new Date(Date.now() - 7 * 60 * 1000).toISOString();

    // Act
    const fixture = createComponent(liveIssue, false, sevenMinutesAgo, 3);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — aria-label uses "7 minutes", not "7m"
    const card = el.querySelector('.issue-card') as HTMLElement;
    const label = card?.getAttribute('aria-label') ?? '';
    expect(label).toContain('silent 7 minutes');
  });

  it('should not include activity in aria-label for a non-live issue', () => {
    // Arrange
    const failedIssue: IssueSummary = { ...mockIssue, state: 'failed' };
    const recentAt = new Date(Date.now() - 2 * 60 * 1000).toISOString();

    // Act
    const fixture = createComponent(failedIssue, false, recentAt, 3);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    const label = card?.getAttribute('aria-label') ?? '';
    expect(label).not.toContain('Active:');
  });

  // Finding 3: null commitCount = pre-handshake (no commit phrase)
  it('should not show commit phrase when commitCount is null (pre-handshake)', () => {
    // Arrange
    const liveIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };
    const recentAt = new Date(Date.now() - 2 * 60 * 1000).toISOString();

    // Act — commitCount not set (null = pre-handshake)
    const fixture = createComponent(liveIssue, false, recentAt, null);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — activity line shows just "active" with no commit phrase
    const activity = el.querySelector('.issue-card__activity') as HTMLElement;
    expect(activity?.textContent).not.toContain('commits');
    expect(activity?.textContent).not.toContain('no commits yet');
  });

  it('should show "no commits yet" when commitCount is observed 0 (not pre-handshake)', () => {
    // Arrange
    const liveIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };
    const recentAt = new Date(Date.now() - 2 * 60 * 1000).toISOString();

    // Act — commitCount explicitly set to 0 (observed via SignalR)
    const fixture = createComponent(liveIssue, false, recentAt, 0);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const activity = el.querySelector('.issue-card__activity') as HTMLElement;
    expect(activity?.textContent).toContain('no commits yet');
  });

  it('should not include commit phrase in aria-label when commitCount is null', () => {
    // Arrange
    const liveIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };
    const recentAt = new Date(Date.now() - 2 * 60 * 1000).toISOString();

    // Act
    const fixture = createComponent(liveIssue, false, recentAt, null);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    const label = card?.getAttribute('aria-label') ?? '';
    expect(label).not.toContain('commits');
  });

  // Finding 2: aria-label silence phrase must be reactive to the ticker (a11y regression guard).
  // issueAriaLabel must be a computed signal so that advancing the ticker causes Angular's
  // signal graph to re-evaluate the aria-label binding on the same cadence as _activityLine.
  it('should re-evaluate aria-label when the ticker advances (issueAriaLabel is a computed signal)', () => {
    // Arrange
    const liveIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };
    const tickerSignal = signal(0);
    TestBed.configureTestingModule({
      imports: [IssueCardComponent],
      providers: [
        { provide: TickerService, useValue: { tick: tickerSignal } },
      ],
    });
    const fixture = TestBed.createComponent(IssueCardComponent);
    fixture.componentRef.setInput('issue', liveIssue);
    fixture.componentRef.setInput('expanded', false);
    const sevenMinutesAgo = new Date(Date.now() - 7 * 60 * 1000).toISOString();
    fixture.componentRef.setInput('lastActivityAt', sevenMinutesAgo);
    fixture.componentRef.setInput('commitCount', 2);
    fixture.detectChanges();

    // Act — issueAriaLabel() must be a computed; verify it is a Signal (has a .()
    // accessor shape), not just a plain method, so Angular tracks its dependency on tick()
    const component = fixture.componentInstance;
    // A computed signal is callable and its type-level shape is Signal<string>.
    // The template reads it as issueAriaLabel() which works for both a method and a computed.
    // To prove it's reactive: read it inside an effect/computed context — if it's a plain
    // method it won't track tick; if it's a computed it will. We verify via the DOM that
    // the aria-label updates even when ONLY the ticker changes.
    const card = fixture.nativeElement.querySelector('.issue-card') as HTMLElement;
    const labelBefore = card?.getAttribute('aria-label') ?? '';
    expect(labelBefore).toContain('silent');

    // Advance the ticker — if issueAriaLabel is a computed that reads tick(),
    // Angular will mark the aria-label binding dirty and re-render it.
    tickerSignal.set(1);
    fixture.detectChanges();

    const labelAfter = card?.getAttribute('aria-label') ?? '';
    // The label must still contain the silence phrase after the ticker fires.
    expect(labelAfter).toContain('silent');
    expect(labelAfter).toContain('minutes');

    // Verify issueAriaLabel is a Signal (computed), not a plain string-returning method:
    // A computed signal has a distinct prototype vs a class method — we check it is
    // accessible as a property (signal accessor) and not just a function reference.
    // The concrete check: reading issueAriaLabel directly (without calling it) should
    // be a function whose .name property identifies it as a computed signal created by
    // Angular's computed() factory, not a plain prototype method.
    const descriptor = Object.getOwnPropertyDescriptor(component, 'issueAriaLabel');
    // A computed signal is stored as an own property (not on the prototype), while a
    // plain method lives on the prototype. If issueAriaLabel is a computed, it will
    // be an own property of the component instance.
    expect(descriptor).toBeDefined();
  });

  // --- Cycle 30: queue-position gutter ---

  // Behaviour: queued-tier card with queuePosition renders the gutter
  it('should render .issue-card__queue-position gutter for a queued-tier card with a position', () => {
    // Arrange
    const queuedIssue: IssueSummary = { ...mockIssue, state: 'queued' };

    // Act
    const fixture = createComponent(queuedIssue, false, null, null, false, 3);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const gutter = el.querySelector('.issue-card__queue-position');
    expect(gutter).toBeTruthy();
  });

  // Behaviour: gutter shows the ordinal number for a dispatchable queued card
  it('should show the queue position number in the gutter for a dispatchable queued card', () => {
    // Arrange
    const queuedIssue: IssueSummary = { ...mockIssue, state: 'queued' };

    // Act
    const fixture = createComponent(queuedIssue, false, null, null, false, 5);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const gutter = el.querySelector('.issue-card__queue-position') as HTMLElement;
    expect(gutter?.textContent?.trim()).toBe('5');
  });

  // Behaviour: rank-1 card (isNextUp + position 1) carries the --next modifier class
  it('should apply --next modifier on the gutter when isNextUp is true and queuePosition is 1', () => {
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
    fixture.componentRef.setInput('queuePosition', 1);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const gutter = el.querySelector('.issue-card__queue-position') as HTMLElement;
    expect(gutter?.classList.contains('issue-card__queue-position--next')).toBe(true);
  });

  // Behaviour: rank-2+ card does NOT carry --next modifier
  it('should NOT apply --next modifier on the gutter when queuePosition is > 1', () => {
    // Arrange
    const queuedIssue: IssueSummary = { ...mockIssue, state: 'queued' };

    // Act
    const fixture = createComponent(queuedIssue, false, null, null, false, 2);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const gutter = el.querySelector('.issue-card__queue-position') as HTMLElement;
    expect(gutter?.classList.contains('issue-card__queue-position--next')).toBe(false);
  });

  // Behaviour: ineligible/unreachable queued card (queuePosition null) → gutter shows em dash
  it('should show em dash in gutter when queuePosition is null for a queued-tier card', () => {
    // Arrange
    const queuedIssue: IssueSummary = {
      ...mockIssue,
      state: 'queued',
      repositoryEligibilityStatus: 'ineligible',
    };

    // Act — queuePosition defaults to null; the gutter still renders for queued-tier cards
    const fixture = createComponent(queuedIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const gutter = el.querySelector('.issue-card__queue-position') as HTMLElement;
    expect(gutter).toBeTruthy();
    expect(gutter?.textContent?.trim()).toBe('—'); // em dash U+2014
  });

  // Behaviour: non-queued-tier card renders NO gutter
  it('should NOT render .issue-card__queue-position gutter for a non-queued-tier card', () => {
    // Arrange — in_progress is not in QUEUED_TIER_STATES
    const inProgressIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };

    // Act
    const fixture = createComponent(inProgressIssue, false, null, null, false, 1);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const gutter = el.querySelector('.issue-card__queue-position');
    expect(gutter).toBeFalsy();
  });

  // Behaviour: card gets --has-gutter modifier when queued-tier
  it('should apply issue-card--has-gutter class for a queued-tier card', () => {
    // Arrange
    const queuedIssue: IssueSummary = { ...mockIssue, state: 'queued' };

    // Act
    const fixture = createComponent(queuedIssue);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    expect(card?.classList.contains('issue-card--has-gutter')).toBe(true);
  });

  // Behaviour: non-queued-tier card does NOT get --has-gutter modifier
  it('should NOT apply issue-card--has-gutter class for a non-queued-tier card', () => {
    // Arrange
    const inProgressIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };

    // Act
    const fixture = createComponent(inProgressIssue);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    expect(card?.classList.contains('issue-card--has-gutter')).toBe(false);
  });

  // Behaviour: gutter element carries aria-hidden="true"
  it('should have aria-hidden="true" on the gutter element', () => {
    // Arrange
    const queuedIssue: IssueSummary = { ...mockIssue, state: 'queued' };

    // Act
    const fixture = createComponent(queuedIssue, false, null, null, false, 2);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const gutter = el.querySelector('.issue-card__queue-position') as HTMLElement;
    expect(gutter?.getAttribute('aria-hidden')).toBe('true');
  });

  // Behaviour: queued card at position >= 2 — aria-label includes "Queue position N"
  it('should include "Queue position 2" in aria-label for a dispatchable queued card at position 2', () => {
    // Arrange
    const queuedIssue: IssueSummary = { ...mockIssue, state: 'queued' };

    // Act
    const fixture = createComponent(queuedIssue, false, null, null, false, 2);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    expect(card?.getAttribute('aria-label')).toContain('Queue position 2');
  });

  // Behaviour: rank-1 (isNextUp) — aria-label begins "Next up." but does NOT include "Queue position 1"
  it('should begin aria-label with "Next up." for rank-1 card and NOT include "Queue position 1"', () => {
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
    fixture.componentRef.setInput('queuePosition', 1);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    const label = card?.getAttribute('aria-label') ?? '';
    expect(label.startsWith('Next up. ')).toBe(true);
    expect(label).not.toContain('Queue position 1');
  });

  // Behaviour: non-dispatchable card (null position, queued-tier) — no queue-position clause in aria-label
  it('should NOT include queue-position clause in aria-label when queuePosition is null', () => {
    // Arrange
    const queuedIssue: IssueSummary = {
      ...mockIssue,
      state: 'queued',
      repositoryEligibilityStatus: 'ineligible',
    };

    // Act — queuePosition defaults to null in the helper
    const fixture = createComponent(queuedIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    expect(card?.getAttribute('aria-label')).not.toContain('Queue position');
  });
});
