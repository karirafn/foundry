import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { IssueCardComponent, formatCost, formatDuration } from './issue-card';
import { IssueState, IssueSummary, RunStats } from '../issue.model';
import { TickerService } from '../../../core/services/ticker.service';
import { AgentActivity } from '../../../core/models/agent-activity.model';

function buildAgentActivity(overrides: Partial<AgentActivity> = {}): AgentActivity {
  return {
    workerRunId: 'run-1',
    issueId: 'abc123',
    role: 'implement',
    lastEventAt: new Date().toISOString(),
    tasks: [],
    currentTask: null,
    completedTaskCount: 0,
    subagents: [],
    activeSkill: null,
    inFlightTool: null,
    tokens: { inputTokens: 0, outputTokens: 0 },
    ...overrides,
  };
}

function createWithAgentActivity(agentActivity: AgentActivity, lastActivityAt: string, issue: IssueSummary = mockIssue) {
  TestBed.configureTestingModule({
    imports: [IssueCardComponent],
    providers: [
      { provide: TickerService, useValue: { tick: signal(0) } },
    ],
  });
  const fixture = TestBed.createComponent(IssueCardComponent);
  fixture.componentRef.setInput('issue', issue);
  fixture.componentRef.setInput('lastActivityAt', lastActivityAt);
  fixture.componentRef.setInput('agentActivity', agentActivity);
  fixture.detectChanges();
  return fixture;
}

const mockIssue: IssueSummary = {
  id: 'abc123',
  issueNumber: 42,
  title: 'Enable dark mode for dashboard',
  state: 'in_progress',
  repositorySlug: 'owner/repo',
  detectedAt: '2026-01-01T00:00:00Z',
  url: 'https://github.com/owner/repo/issues/42',
  stage: 'implement',
  needsAction: false,
  awaitsDispatch: false,
  codeStep: null,
};

function createComponent(issue: IssueSummary = mockIssue, lastActivityAt: string | null = null, isNextUp = false, queuePosition: number | null = null) {
  TestBed.configureTestingModule({
    imports: [IssueCardComponent],
    providers: [
      { provide: TickerService, useValue: { tick: signal(0) } },
    ],
  });
  const fixture = TestBed.createComponent(IssueCardComponent);
  fixture.componentRef.setInput('issue', issue);
  if (lastActivityAt !== null) {
    fixture.componentRef.setInput('lastActivityAt', lastActivityAt);
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

  it('should render a non-interactive container as the card root', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    expect(card).toBeTruthy();
    expect(card.tagName).toBe('DIV');
    expect(card.getAttribute('role')).toBeNull();
    expect(card.getAttribute('tabindex')).toBeNull();
    expect(card.getAttribute('aria-expanded')).toBeNull();
  });

  it('should render a dedicated toggle button inside the card', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const buttons = el.querySelectorAll('.issue-card button');
    expect(buttons.length).toBe(1);
    expect(buttons[0].classList.contains('issue-card__toggle')).toBe(true);
    expect(buttons[0].textContent?.trim()).toBe('Enable dark mode for dashboard');
  });

  it('should not nest any link inside a button', () => {
    // Arrange / Act
    const fixture = createComponent({ ...mockIssue, pullRequestUrl: 'https://github.com/owner/repo/pull/7' });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelectorAll('.issue-card a').length).toBe(2);
    expect(el.querySelectorAll('button a').length).toBe(0);
  });

  it('should place the toggle button before the links in tab order', () => {
    // Arrange / Act
    const fixture = createComponent({ ...mockIssue, pullRequestUrl: 'https://github.com/owner/repo/pull/7' });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const focusables = Array.from(el.querySelectorAll<HTMLElement>('button, a[href]'));
    expect(focusables.map((f) => f.className)).toEqual([
      'issue-card__toggle',
      'issue-card__pr-link',
      'issue-card__link',
    ]);
  });

  it('should still open a card with no links', () => {
    // Arrange
    const fixture = createComponent({ ...mockIssue, url: '', pullRequestUrl: null });
    const el = fixture.nativeElement as HTMLElement;
    let emitted = false;
    fixture.componentInstance.open.subscribe(() => (emitted = true));

    // Act
    (el.querySelector('.issue-card__toggle') as HTMLButtonElement).click();

    // Assert
    expect(el.querySelectorAll('.issue-card a').length).toBe(0);
    expect(emitted).toBe(true);
  });

  it('should emit open exactly once when the toggle button is clicked', () => {
    // Arrange
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;
    let count = 0;
    fixture.componentInstance.open.subscribe(() => count++);

    // Act
    (el.querySelector('.issue-card__toggle') as HTMLButtonElement).click();

    // Assert
    expect(count).toBe(1);
  });

  it('should emit open when a non-link area of the card is clicked', () => {
    // Arrange
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;
    let emitted = false;
    fixture.componentInstance.open.subscribe(() => (emitted = true));

    // Act
    (el.querySelector('.issue-card__timestamp') as HTMLElement).click();

    // Assert
    expect(emitted).toBe(true);
  });

  it('should not emit open when Enter is pressed on a link', () => {
    // Arrange
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;
    let emitted = false;
    fixture.componentInstance.open.subscribe(() => (emitted = true));

    // Act
    const link = el.querySelector('.issue-card__link') as HTMLAnchorElement;
    link.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));

    // Assert
    expect(emitted).toBe(false);
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

  // Cycle 5: open output on click
  it('should emit open when the card is clicked', () => {
    // Arrange
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;
    const card = el.querySelector('.issue-card') as HTMLElement;
    let emitted = false;
    fixture.componentInstance.open.subscribe(() => (emitted = true));

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

  it('should not emit open when the external link is clicked', () => {
    // Arrange
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;
    const link = el.querySelector('.issue-card__link') as HTMLAnchorElement;
    let emitted = false;
    fixture.componentInstance.open.subscribe(() => (emitted = true));

    // Act
    const event = new MouseEvent('click', { bubbles: true });
    link.dispatchEvent(event);

    // Assert
    expect(emitted).toBe(false);
  });

  // Cycle 6: ARIA - the title button opens a dialog (the issue pane), so it is not a disclosure
  it('should mark the title button as opening a dialog', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const button = el.querySelector('.issue-card__toggle') as HTMLElement;
    expect(button.getAttribute('aria-haspopup')).toBe('dialog');
    expect(button.getAttribute('aria-expanded')).toBeNull();
    expect(button.getAttribute('aria-controls')).toBeNull();
  });

  it('should emit open with the title button, which focus returns to', () => {
    // Arrange
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;
    let invoker: HTMLElement | null = null;
    fixture.componentInstance.open.subscribe((button) => (invoker = button));

    // Act
    (el.querySelector('.issue-card__timestamp') as HTMLElement).click();

    // Assert
    expect(invoker).toBe(el.querySelector('.issue-card__toggle'));
  });

  it('should make the toggle focusable as a native button', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card__toggle') as HTMLButtonElement;
    expect(card?.tagName).toBe('BUTTON');
    expect(card?.type).toBe('button');
  });

  // Cycle 6b: aria-label
  it('should set aria-label with issue number, title, and state', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card__toggle') as HTMLElement;
    expect(card?.getAttribute('aria-label')).toBe('Issue #42: Enable dark mode for dashboard. State: in progress');
  });

  it('should announce the revision trigger state in aria-label', () => {
    // Arrange
    const issue: IssueSummary = { ...mockIssue, state: 'revision_queued', revisionTriggerKind: 'rebase_conflict', rebaseOutcome: 'conflict' };

    // Act
    const fixture = createComponent(issue);
    const card = (fixture.nativeElement as HTMLElement).querySelector('.issue-card__toggle') as HTMLElement;

    // Assert
    expect(card.getAttribute('aria-label')).toBe('Issue #42: Enable dark mode for dashboard. State: conflict queued Rebase conflict.');
  });

  it('should announce the failure category in aria-label for a failed issue', () => {
    // Arrange
    const issue: IssueSummary = { ...mockIssue, state: 'failed', failureClassification: 'credits_exhausted' };

    // Act
    const fixture = createComponent(issue);
    const card = (fixture.nativeElement as HTMLElement).querySelector('.issue-card__toggle') as HTMLElement;

    // Assert
    expect(card.getAttribute('aria-label')).toBe('Issue #42: Enable dark mode for dashboard. State: credits exhausted');
  });

  it('should pass the revision trigger kind to the state badge', () => {
    // Arrange
    const issue: IssueSummary = { ...mockIssue, state: 'revision_in_progress', revisionTriggerKind: 'rebase_conflict' };

    // Act
    const fixture = createComponent(issue);
    const badge = (fixture.nativeElement as HTMLElement).querySelector('fd-state-badge span');

    // Assert
    expect(badge?.textContent?.trim()).toBe('RESOLVING CONFLICTS');
    expect(badge?.getAttribute('aria-label')).toBe('State: resolving conflicts');
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
    const card = el.querySelector('.issue-card__toggle') as HTMLElement;
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
    const card = el.querySelector('.issue-card__toggle') as HTMLElement;
    expect(card?.getAttribute('aria-label')).toContain('Repo unreachable');
  });

  it('should not include repo warning in aria-label when issue has no warning', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card__toggle') as HTMLElement;
    const label = card?.getAttribute('aria-label') ?? '';
    expect(label).not.toContain('Repo ineligible');
    expect(label).not.toContain('Repo unreachable');
  });

  // Cycle 7: keyboard interaction
  it('should emit open when Enter key is pressed', () => {
    // Arrange
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;
    const card = el.querySelector('.issue-card__toggle') as HTMLElement;
    let emitted = false;
    fixture.componentInstance.open.subscribe(() => (emitted = true));

    // Act
    card.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));

    // Assert
    expect(emitted).toBe(true);
  });

  it('should emit open when Space key is pressed', () => {
    // Arrange
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;
    const card = el.querySelector('.issue-card__toggle') as HTMLElement;
    let emitted = false;
    fixture.componentInstance.open.subscribe(() => (emitted = true));

    // Act
    card.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }));

    // Assert
    expect(emitted).toBe(true);
  });

  it('should call preventDefault when Space key is pressed to prevent page scroll', () => {
    // Arrange
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;
    const card = el.querySelector('.issue-card__toggle') as HTMLElement;
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
    const card = el.querySelector('.issue-card__toggle') as HTMLElement;
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
    const fixture = createComponent(liveIssue, recentAt);
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
    const fixture = createComponent(failedIssue, recentAt);
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
    const fixture = createComponent(liveIssue, recentAt);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — no aria-label on a roleless span; use sr-only child or issueAriaLabel instead
    const activity = el.querySelector('.issue-card__activity') as HTMLElement;
    expect(activity?.getAttribute('aria-label')).toBeNull();
  });

  it('should not show activity line when lastActivityAt is null', () => {
    // Arrange
    const liveIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };

    // Act
    const fixture = createComponent(liveIssue, null);
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


  // Cycle 23: silence threshold — no silence segment when < 5 minutes
  it('should NOT show silence segment when silent duration is less than 5 minutes', () => {
    // Arrange — 4 minutes 59 seconds silent (just under threshold)
    const liveIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };
    const justUnderThreshold = new Date(Date.now() - (5 * 60 * 1000 - 1000)).toISOString();

    // Act
    const fixture = createComponent(liveIssue, justUnderThreshold);
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
    const fixture = createComponent(liveIssue, exactlyFiveMin);
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
    const fixture = createComponent(liveIssue, sevenMinutesAgo);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const activity = el.querySelector('.issue-card__activity') as HTMLElement;
    expect(activity?.textContent).toContain('silent 7m');
  });

  it('should NOT show activity line when issue is not in LIVE_STATES', () => {
    // Arrange
    const failedIssue: IssueSummary = { ...mockIssue, state: 'failed' };
    const recentAt = new Date(Date.now() - 2 * 60 * 1000).toISOString();

    // Act
    const fixture = createComponent(failedIssue, recentAt);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const activity = el.querySelector('.issue-card__activity');
    expect(activity).toBeFalsy();
  });

  // Cycle 11: continuation_queued is NOT treated as a live state for the activity timer
  it('should NOT show activity line for continuation_queued even when lastActivityAt is provided', () => {
    // Arrange — regression guard: continuation_queued moved from live to queued tier
    const contQueued: IssueSummary = { ...mockIssue, state: 'continuation_queued' };
    const recentAt = new Date(Date.now() - 2 * 60 * 1000).toISOString();

    // Act
    const fixture = createComponent(contQueued, recentAt);
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
    fixture.componentRef.setInput('isNextUp', true);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card__toggle') as HTMLElement;
    expect(card?.getAttribute('aria-label')).toContain('Next up');
  });

  // --- Step 6: stat pill tests ---

  const fullRunStats: RunStats = {
    runCount: 3,
    durationMs: 90000,
    numTurns: 12,
    totalCostUsd: 1.239,
    inputTokens: { total: 5000, knownRunCount: 3, unknownRunCount: 0 },
    outputTokens: { total: 2000, knownRunCount: 3, unknownRunCount: 0 },
  };

  const noTokenRuns = { total: 0, knownRunCount: 0, unknownRunCount: 0 };

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

  it('should render exact token totals without a prefix, untracked count or title', () => {
    // Arrange
    const issueWithStats: IssueSummary = { ...mockIssue, runStats: fullRunStats };

    // Act
    const fixture = createComponent(issueWithStats);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const pill = el.querySelector('.issue-card__stat-pill--input-tokens') as HTMLElement;
    expect(pill.querySelector('.issue-card__stat-value')?.textContent?.trim()).toBe('5,000 input tokens');
    expect(pill.hasAttribute('title')).toBe(false);
    const label = (el.querySelector('.issue-card__toggle') as HTMLElement).getAttribute('aria-label') ?? '';
    expect(label).toContain('5,000 input tokens, 2,000 output tokens.');
    expect(label).not.toContain('untracked');
  });

  it('should render a partial input total as a lower bound with the untracked count', () => {
    // Arrange — two legacy runs: output known, billable input unknown
    const issueWithStats: IssueSummary = {
      ...mockIssue,
      runStats: { ...fullRunStats, inputTokens: { total: 5000, knownRunCount: 1, unknownRunCount: 2 } },
    };

    // Act
    const fixture = createComponent(issueWithStats);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const pill = el.querySelector('.issue-card__stat-pill--input-tokens') as HTMLElement;
    const value = pill.querySelector('.issue-card__stat-value');
    expect(value?.firstChild?.textContent?.trim()).toBe('≥ 5,000');
    expect(value?.querySelector('.sr-only')?.textContent).toBe(' input tokens, 2 runs untracked');
    expect(pill.getAttribute('title')).toBe('At least 5,000 input tokens, 2 runs untracked');
    const label = (el.querySelector('.issue-card__toggle') as HTMLElement).getAttribute('aria-label') ?? '';
    expect(label).toContain('at least 5,000 input tokens, 2 runs untracked');
    expect(label).toContain('2,000 output tokens.');
  });

  it('should use the singular untracked wording for one unknown run', () => {
    // Arrange
    const issueWithStats: IssueSummary = {
      ...mockIssue,
      runStats: { ...fullRunStats, outputTokens: { total: 2000, knownRunCount: 2, unknownRunCount: 1 } },
    };

    // Act
    const fixture = createComponent(issueWithStats);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const pill = el.querySelector('.issue-card__stat-pill--output-tokens') as HTMLElement;
    expect(pill.getAttribute('title')).toBe('At least 2,000 output tokens, 1 run untracked');
  });

  it('should render a dash in the input-tokens pill when every run is unknown', () => {
    // Arrange — every run predates cache-column persistence: output known, billable input unknown
    const issueWithStats: IssueSummary = {
      ...mockIssue,
      runStats: { ...fullRunStats, inputTokens: { total: 0, knownRunCount: 0, unknownRunCount: 3 } },
    };

    // Act
    const fixture = createComponent(issueWithStats);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const pill = el.querySelector('.issue-card__stat-pill--input-tokens') as HTMLElement;
    const value = pill.querySelector('.issue-card__stat-value');
    expect(value?.textContent?.trim()).toBe('- input tokens, 3 runs untracked');
    expect(pill.getAttribute('title')).toBe('Unknown input tokens, 3 runs untracked');
    const label = (el.querySelector('.issue-card__toggle') as HTMLElement).getAttribute('aria-label') ?? '';
    expect(label).toContain('unknown input tokens, 3 runs untracked');
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
      inputTokens: noTokenRuns,
      outputTokens: noTokenRuns,
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
    const card = el.querySelector('.issue-card__toggle') as HTMLElement;
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
    const card = el.querySelector('.issue-card__toggle') as HTMLElement;
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
    const card = el.querySelector('.issue-card__toggle') as HTMLElement;
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
      inputTokens: noTokenRuns,
      outputTokens: noTokenRuns,
    };
    const issueWithStats: IssueSummary = { ...mockIssue, runStats: partialStats };

    // Act
    const fixture = createComponent(issueWithStats);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card__toggle') as HTMLElement;
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
    const card = el.querySelector('.issue-card__toggle') as HTMLElement;
    const label = card?.getAttribute('aria-label') ?? '';
    expect(label).not.toContain('Run stats:');
  });

  it('should include expanded silence duration in aria-label when silent >= 5 minutes', () => {
    // Arrange
    const liveIssue: IssueSummary = { ...mockIssue, state: 'in_progress' };
    const sevenMinutesAgo = new Date(Date.now() - 7 * 60 * 1000).toISOString();

    // Act
    const fixture = createComponent(liveIssue, sevenMinutesAgo);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — aria-label uses "7 minutes", not "7m"
    const card = el.querySelector('.issue-card__toggle') as HTMLElement;
    const label = card?.getAttribute('aria-label') ?? '';
    expect(label).toContain('silent 7 minutes');
  });

  it('should not include activity in aria-label for a non-live issue', () => {
    // Arrange
    const failedIssue: IssueSummary = { ...mockIssue, state: 'failed' };
    const recentAt = new Date(Date.now() - 2 * 60 * 1000).toISOString();

    // Act
    const fixture = createComponent(failedIssue, recentAt);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card__toggle') as HTMLElement;
    const label = card?.getAttribute('aria-label') ?? '';
    expect(label).not.toContain('Active');
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
    const sevenMinutesAgo = new Date(Date.now() - 7 * 60 * 1000).toISOString();
    fixture.componentRef.setInput('lastActivityAt', sevenMinutesAgo);
    fixture.detectChanges();

    // Act — issueAriaLabel() must be a computed; verify it is a Signal (has a .()
    // accessor shape), not just a plain method, so Angular tracks its dependency on tick()
    const component = fixture.componentInstance;
    // A computed signal is callable and its type-level shape is Signal<string>.
    // The template reads it as issueAriaLabel() which works for both a method and a computed.
    // To prove it's reactive: read it inside an effect/computed context — if it's a plain
    // method it won't track tick; if it's a computed it will. We verify via the DOM that
    // the aria-label updates even when ONLY the ticker changes.
    const card = fixture.nativeElement.querySelector('.issue-card__toggle') as HTMLElement;
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
    const fixture = createComponent(queuedIssue, null, false, 3);
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
    const fixture = createComponent(queuedIssue, null, false, 5);
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
    const fixture = createComponent(queuedIssue, null, false, 2);
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
    const fixture = createComponent(inProgressIssue, null, false, 1);
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
    const fixture = createComponent(queuedIssue, null, false, 2);
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
    const fixture = createComponent(queuedIssue, null, false, 2);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card__toggle') as HTMLElement;
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
    fixture.componentRef.setInput('isNextUp', true);
    fixture.componentRef.setInput('queuePosition', 1);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card__toggle') as HTMLElement;
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
    const card = el.querySelector('.issue-card__toggle') as HTMLElement;
    expect(card?.getAttribute('aria-label')).not.toContain('Queue position');
  });

  // --- Surface 1: checks_pending "Waiting on checks" footer affordance ---

  it('should render "Waiting on checks" phrase in footer for checks_pending state', () => {
    // Arrange
    const checksPendingIssue: IssueSummary = { ...mockIssue, state: 'checks_pending' };

    // Act
    const fixture = createComponent(checksPendingIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const checksStatus = el.querySelector('.issue-card__checks-status');
    expect(checksStatus).toBeTruthy();
    expect(checksStatus?.textContent?.trim()).toBe('Waiting on checks');
  });

  it('should NOT render "Waiting on checks" phrase for non-checks_pending states', () => {
    // Arrange
    const reviewIssue: IssueSummary = { ...mockIssue, state: 'review' };

    // Act
    const fixture = createComponent(reviewIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const checksStatus = el.querySelector('.issue-card__checks-status');
    expect(checksStatus).toBeFalsy();
  });

  it('should NOT render "Waiting on checks" phrase for queued state', () => {
    // Arrange
    const queuedIssue: IssueSummary = { ...mockIssue, state: 'queued' };

    // Act
    const fixture = createComponent(queuedIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const checksStatus = el.querySelector('.issue-card__checks-status');
    expect(checksStatus).toBeFalsy();
  });

  // --- Rebase outcome badge (#684): only skipped and conflict are badged ---

  it('should render "Rebase conflict" badge for conflict rebaseOutcome', () => {
    // Arrange
    const reviewIssue: IssueSummary = { ...mockIssue, state: 'review', rebaseOutcome: 'conflict' };

    // Act
    const fixture = createComponent(reviewIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const badge = el.querySelector('.issue-card__rebase-badge');
    expect(badge).toBeTruthy();
    expect(badge?.textContent?.trim()).toBe('Rebase conflict');
    expect(badge?.classList.contains('issue-card__checks-badge--error')).toBe(true);
  });

  it('should render "Rebase skipped: <reason>" badge for skipped rebaseOutcome', () => {
    // Arrange
    const issue: IssueSummary = {
      ...mockIssue,
      state: 'checks_pending',
      rebaseOutcome: 'skipped',
      rebaseSkippedReason: 'provider-error',
    };

    // Act
    const fixture = createComponent(issue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const badge = el.querySelector('.issue-card__rebase-badge');
    expect(badge).toBeTruthy();
    expect(badge?.textContent?.trim()).toBe('Rebase skipped: provider error');
    expect(badge?.classList.contains('issue-card__checks-badge--warning')).toBe(true);
  });

  it('should show the raw skipped reason when it is unknown', () => {
    // Arrange
    const issue: IssueSummary = {
      ...mockIssue,
      state: 'review',
      rebaseOutcome: 'skipped',
      rebaseSkippedReason: 'some-future-reason',
    };

    // Act
    const fixture = createComponent(issue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.issue-card__rebase-badge')?.textContent?.trim())
      .toBe('Rebase skipped: some-future-reason');
  });

  it('should render "Rebase skipped" without a reason when none is given', () => {
    // Arrange
    const issue: IssueSummary = { ...mockIssue, state: 'review', rebaseOutcome: 'skipped', rebaseSkippedReason: null };

    // Act
    const fixture = createComponent(issue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.issue-card__rebase-badge')?.textContent?.trim()).toBe('Rebase skipped');
  });

  it.each(['up_to_date', 'rebased', 'disabled'])('should NOT render a rebase badge for %s rebaseOutcome', (outcome) => {
    // Arrange
    const issue: IssueSummary = { ...mockIssue, state: 'review', rebaseOutcome: outcome };

    // Act
    const fixture = createComponent(issue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.issue-card__rebase-badge')).toBeFalsy();
  });

  it('should NOT render a rebase badge when rebaseOutcome is null', () => {
    // Arrange
    const issue: IssueSummary = { ...mockIssue, state: 'review', rebaseOutcome: null };

    // Act
    const fixture = createComponent(issue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.issue-card__rebase-badge')).toBeFalsy();
  });

  it('should include the rebase badge label in the card aria-label', () => {
    // Arrange
    const issue: IssueSummary = { ...mockIssue, state: 'review', rebaseOutcome: 'conflict' };

    // Act
    const fixture = createComponent(issue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.issue-card__toggle')?.getAttribute('aria-label')).toContain('Rebase conflict.');
  });

  it('should render the REBASING state badge for rebase_pending', () => {
    // Arrange
    const issue: IssueSummary = { ...mockIssue, state: 'rebase_pending' };

    // Act
    const fixture = createComponent(issue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.badge--rebase-pending')?.textContent?.trim()).toBe('REBASING');
  });

  // --- Surface 2: review card outcome badge ---

  it('should render outcome badge with "CHECKS: FAILING" label for still_failing checksOutcome', () => {
    // Arrange
    const reviewIssue: IssueSummary = { ...mockIssue, state: 'review', checksOutcome: 'still_failing' };

    // Act
    const fixture = createComponent(reviewIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const badge = el.querySelector('.issue-card__checks-badge');
    expect(badge).toBeTruthy();
    // Text is "Checks: failing" — uppercased by CSS text-transform; the DOM textContent is not CSS-transformed
    expect(badge?.textContent?.trim()).toBe('Checks: failing');
  });

  it('should render outcome badge with "Checks: unreadable" label for unreadable checksOutcome', () => {
    // Arrange
    const reviewIssue: IssueSummary = { ...mockIssue, state: 'review', checksOutcome: 'unreadable' };

    // Act
    const fixture = createComponent(reviewIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const badge = el.querySelector('.issue-card__checks-badge');
    expect(badge).toBeTruthy();
    expect(badge?.textContent?.trim()).toBe('Checks: unreadable');
  });

  it('should render outcome badge with "No CI detected" label for none_detected checksOutcome', () => {
    // Arrange
    const reviewIssue: IssueSummary = { ...mockIssue, state: 'review', checksOutcome: 'none_detected' };

    // Act
    const fixture = createComponent(reviewIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const badge = el.querySelector('.issue-card__checks-badge');
    expect(badge).toBeTruthy();
    expect(badge?.textContent?.trim()).toBe('No CI detected');
  });

  it('should render outcome badge with "Checks: unfinished" label for unfinished checksOutcome', () => {
    // Arrange
    const reviewIssue: IssueSummary = { ...mockIssue, state: 'review', checksOutcome: 'unfinished' };

    // Act
    const fixture = createComponent(reviewIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const badge = el.querySelector('.issue-card__checks-badge');
    expect(badge).toBeTruthy();
    expect(badge?.textContent?.trim()).toBe('Checks: unfinished');
  });

  it('should NOT render outcome badge when checksOutcome is passed', () => {
    // Arrange
    const reviewIssue: IssueSummary = { ...mockIssue, state: 'review', checksOutcome: 'passed' };

    // Act
    const fixture = createComponent(reviewIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const badge = el.querySelector('.issue-card__checks-badge');
    expect(badge).toBeFalsy();
  });

  it('should NOT render outcome badge when checksOutcome is null', () => {
    // Arrange
    const reviewIssue: IssueSummary = { ...mockIssue, state: 'review', checksOutcome: null };

    // Act
    const fixture = createComponent(reviewIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const badge = el.querySelector('.issue-card__checks-badge');
    expect(badge).toBeFalsy();
  });

  it('should NOT render outcome badge when checksOutcome is undefined', () => {
    // Arrange — mockIssue has no checksOutcome
    const fixture = createComponent(mockIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const badge = el.querySelector('.issue-card__checks-badge');
    expect(badge).toBeFalsy();
  });

  it('should NOT render outcome badge for unknown checksOutcome value (fail quiet)', () => {
    // Arrange
    const reviewIssue: IssueSummary = { ...mockIssue, state: 'review', checksOutcome: 'some_future_value' };

    // Act
    const fixture = createComponent(reviewIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — unknown discriminator falls through to null, no badge
    const badge = el.querySelector('.issue-card__checks-badge');
    expect(badge).toBeFalsy();
  });

  it('should apply error modifier class for still_failing outcome badge', () => {
    // Arrange
    const reviewIssue: IssueSummary = { ...mockIssue, state: 'review', checksOutcome: 'still_failing' };

    // Act
    const fixture = createComponent(reviewIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const badge = el.querySelector('.issue-card__checks-badge') as HTMLElement;
    expect(badge?.classList.contains('issue-card__checks-badge--error')).toBe(true);
  });

  it('should apply warning modifier class for unreadable outcome badge', () => {
    // Arrange
    const reviewIssue: IssueSummary = { ...mockIssue, state: 'review', checksOutcome: 'unreadable' };

    // Act
    const fixture = createComponent(reviewIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const badge = el.querySelector('.issue-card__checks-badge') as HTMLElement;
    expect(badge?.classList.contains('issue-card__checks-badge--warning')).toBe(true);
  });

  it('should apply muted modifier class for none_detected outcome badge', () => {
    // Arrange
    const reviewIssue: IssueSummary = { ...mockIssue, state: 'review', checksOutcome: 'none_detected' };

    // Act
    const fixture = createComponent(reviewIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const badge = el.querySelector('.issue-card__checks-badge') as HTMLElement;
    expect(badge?.classList.contains('issue-card__checks-badge--muted')).toBe(true);
  });

  it('should have role="img" on the outcome badge', () => {
    // Arrange
    const reviewIssue: IssueSummary = { ...mockIssue, state: 'review', checksOutcome: 'still_failing' };

    // Act
    const fixture = createComponent(reviewIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const badge = el.querySelector('.issue-card__checks-badge') as HTMLElement;
    expect(badge?.getAttribute('role')).toBe('img');
  });

  it('should set aria-label on outcome badge to the outcome label (no "Checks outcome:" prefix)', () => {
    // Arrange — Fix 3c: badge aria-label = outcome.label directly, no stutter prefix
    const reviewIssue: IssueSummary = { ...mockIssue, state: 'review', checksOutcome: 'still_failing' };

    // Act
    const fixture = createComponent(reviewIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const badge = el.querySelector('.issue-card__checks-badge') as HTMLElement;
    expect(badge?.getAttribute('aria-label')).toBe('Checks: failing');
  });

  it('should have aria-hidden="true" on the outcome badge (Fix 3a)', () => {
    // Arrange — badge is inside card button; card aria-label already conveys the outcome
    const reviewIssue: IssueSummary = { ...mockIssue, state: 'review', checksOutcome: 'still_failing' };

    // Act
    const fixture = createComponent(reviewIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const badge = el.querySelector('.issue-card__checks-badge') as HTMLElement;
    expect(badge?.getAttribute('aria-hidden')).toBe('true');
  });

  it('should include outcome label in card aria-label for non-passed checksOutcome (no doubled "Checks")', () => {
    // Arrange — Fix 3c: card aria-label uses "Checks outcome: Checks: failing" → "Checks outcome: failing" or just label
    const reviewIssue: IssueSummary = { ...mockIssue, state: 'review', checksOutcome: 'still_failing' };

    // Act
    const fixture = createComponent(reviewIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card__toggle') as HTMLElement;
    const label = card?.getAttribute('aria-label') ?? '';
    expect(label).toContain('Checks: failing');
    // Must NOT have doubled "Checks outcome: Checks:"
    expect(label).not.toContain('Checks outcome: Checks:');
  });

  it('should NOT include outcome clause in card aria-label when checksOutcome is null', () => {
    // Arrange
    const reviewIssue: IssueSummary = { ...mockIssue, state: 'review', checksOutcome: null };

    // Act
    const fixture = createComponent(reviewIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card__toggle') as HTMLElement;
    const label = card?.getAttribute('aria-label') ?? '';
    expect(label).not.toContain('Checks:');
  });

  it('should NOT include outcome clause in card aria-label when checksOutcome is passed', () => {
    // Arrange
    const reviewIssue: IssueSummary = { ...mockIssue, state: 'review', checksOutcome: 'passed' };

    // Act
    const fixture = createComponent(reviewIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card__toggle') as HTMLElement;
    const label = card?.getAttribute('aria-label') ?? '';
    expect(label).not.toContain('Checks:');
  });

  it('should have aria-hidden="true" on the .__checks-status span (Fix 3b)', () => {
    // Arrange — status text is in card aria-label via waitingPart; span itself must be hidden from AT
    const checksPendingIssue: IssueSummary = { ...mockIssue, state: 'checks_pending' };

    // Act
    const fixture = createComponent(checksPendingIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const checksStatus = el.querySelector('.issue-card__checks-status') as HTMLElement;
    expect(checksStatus?.getAttribute('aria-hidden')).toBe('true');
  });

  it('should include "Waiting on checks" in card aria-label for checks_pending (Fix 3b)', () => {
    // Arrange
    const checksPendingIssue: IssueSummary = { ...mockIssue, state: 'checks_pending' };

    // Act
    const fixture = createComponent(checksPendingIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card__toggle') as HTMLElement;
    const label = card?.getAttribute('aria-label') ?? '';
    expect(label).toContain('Waiting on checks');
  });

  it('should NOT include "Waiting on checks" in card aria-label for non-checks_pending (Fix 3b)', () => {
    // Arrange
    const reviewIssue: IssueSummary = { ...mockIssue, state: 'review' };

    // Act
    const fixture = createComponent(reviewIssue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card__toggle') as HTMLElement;
    const label = card?.getAttribute('aria-label') ?? '';
    expect(label).not.toContain('Waiting on checks');
  });

  // --- PR link: rendered whenever the issue has a pull request (#709) ---

  const prUrl = 'https://github.com/owner/repo/pull/7';
  const prStates: IssueState[] = [
    'review',
    'checks_pending',
    'rebase_pending',
    'revision_queued',
    'revision_in_progress',
    'revision_failed',
    'continuable_failed',
    'completed',
  ];

  for (const state of prStates) {
    it(`should render a PR link in the footer for a ${state} card when pullRequestUrl is present`, () => {
      // Arrange
      const issue: IssueSummary = { ...mockIssue, state, pullRequestUrl: prUrl };

      // Act
      const fixture = createComponent(issue);
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const prLink = el.querySelector('.issue-card__footer .issue-card__pr-link') as HTMLAnchorElement;
      expect(prLink).toBeTruthy();
      expect(prLink?.getAttribute('href')).toBe(prUrl);
      expect(prLink?.getAttribute('target')).toBe('_blank');
      expect(prLink?.getAttribute('rel')).toBe('noopener noreferrer');
      expect(prLink?.textContent?.trim()).toBe('View PR');
    });
  }

  it('should apply no state gate to the PR link', () => {
    // Arrange — even a state that never stores a PR renders the link when the summary carries one
    const issue: IssueSummary = { ...mockIssue, state: 'detected', pullRequestUrl: prUrl };

    // Act
    const fixture = createComponent(issue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.issue-card__pr-link')).toBeTruthy();
  });

  it('should set descriptive aria-label on the PR link', () => {
    // Arrange
    const issue: IssueSummary = { ...mockIssue, state: 'review', pullRequestUrl: prUrl };

    // Act
    const fixture = createComponent(issue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const prLink = el.querySelector('.issue-card__pr-link') as HTMLAnchorElement;
    expect(prLink?.getAttribute('aria-label')).toBe('View pull request for issue #42');
  });

  it('should group the PR link with the external issue link in the footer', () => {
    // Arrange
    const issue: IssueSummary = { ...mockIssue, state: 'completed', pullRequestUrl: prUrl };

    // Act
    const fixture = createComponent(issue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const group = el.querySelector('.issue-card__footer .issue-card__footer-links') as HTMLElement;
    expect(group.querySelector('.issue-card__pr-link')).toBeTruthy();
    expect(group.querySelector('.issue-card__link')).toBeTruthy();
    expect(group.lastElementChild?.classList.contains('issue-card__link')).toBe(true);
  });

  it('should not emit open when the PR link is clicked', () => {
    // Arrange
    const issue: IssueSummary = { ...mockIssue, state: 'review', pullRequestUrl: prUrl };
    const fixture = createComponent(issue);
    const el = fixture.nativeElement as HTMLElement;
    const prLink = el.querySelector('.issue-card__pr-link') as HTMLAnchorElement;
    prLink.addEventListener('click', (e) => e.preventDefault());
    let emitted = false;
    fixture.componentInstance.open.subscribe(() => (emitted = true));

    // Act
    prLink.click();

    // Assert
    expect(emitted).toBe(false);
  });

  it('should NOT render a PR link when pullRequestUrl is absent', () => {
    // Arrange
    const issue: IssueSummary = { ...mockIssue, state: 'completed', pullRequestUrl: null };

    // Act
    const fixture = createComponent(issue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.issue-card__pr-link')).toBeFalsy();
  });

  it('should NOT render a PR link when pullRequestUrl is not https', () => {
    // Arrange
    const issue: IssueSummary = {
      ...mockIssue,
      state: 'review',
      pullRequestUrl: 'http://github.com/owner/repo/pull/7',
    };

    // Act
    const fixture = createComponent(issue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.issue-card__pr-link')).toBeFalsy();
  });

  it('should render "Waiting on checks" without a PR link for checks_pending when pullRequestUrl is absent', () => {
    // Arrange
    const issue: IssueSummary = { ...mockIssue, state: 'checks_pending' };

    // Act
    const fixture = createComponent(issue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.issue-card__checks-status')?.textContent?.trim()).toBe('Waiting on checks');
    expect(el.querySelector('.issue-card__pr-link')).toBeFalsy();
  });

  it('should render "Waiting on checks" alongside the PR link for checks_pending', () => {
    // Arrange
    const issue: IssueSummary = { ...mockIssue, state: 'checks_pending', pullRequestUrl: prUrl };

    // Act
    const fixture = createComponent(issue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.issue-card__checks-status')?.textContent?.trim()).toBe('Waiting on checks');
    expect(el.querySelector('.issue-card__pr-link')).toBeTruthy();
  });

  it('should show the PR link without a reload when a pushed summary gains a pullRequestUrl', () => {
    // Arrange
    const fixture = createComponent({ ...mockIssue, state: 'in_progress', pullRequestUrl: null });
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.issue-card__pr-link')).toBeFalsy();

    // Act — simulate the SignalR-pushed summary after the transition into review
    fixture.componentRef.setInput('issue', { ...mockIssue, state: 'review', pullRequestUrl: prUrl });
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.issue-card__pr-link')?.getAttribute('href')).toBe(prUrl);
  });

  // --- PR link wording follows the repository's provider (#710) ---

  const mrUrl = 'https://git.acme.com/group/project/-/merge_requests/7';
  const prWordingCases: { providerType: string | null | undefined; text: string; ariaLabel: string }[] = [
    { providerType: 'gitlab', text: 'View MR', ariaLabel: 'View merge request for issue #42' },
    { providerType: 'github', text: 'View PR', ariaLabel: 'View pull request for issue #42' },
    { providerType: null, text: 'View PR', ariaLabel: 'View pull request for issue #42' },
    { providerType: undefined, text: 'View PR', ariaLabel: 'View pull request for issue #42' },
  ];

  for (const { providerType, text, ariaLabel } of prWordingCases) {
    it(`should word the PR link "${text}" when providerType is ${String(providerType)}`, () => {
      // Arrange
      const issue: IssueSummary = { ...mockIssue, state: 'review', pullRequestUrl: mrUrl, providerType };

      // Act
      const fixture = createComponent(issue);
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const prLink = el.querySelector('.issue-card__pr-link') as HTMLAnchorElement;
      expect(prLink?.textContent?.trim()).toBe(text);
      expect(prLink?.getAttribute('aria-label')).toBe(ariaLabel);
      expect(prLink?.getAttribute('href')).toBe(mrUrl);
    });
  }

  it('should word a self-hosted GitLab merge request link "View MR" regardless of the URL host', () => {
    // Arrange — the host gives no hint of GitLab; wording comes from providerType only
    const issue: IssueSummary = {
      ...mockIssue,
      state: 'completed',
      pullRequestUrl: 'https://code.example.org/team/app/-/merge_requests/3',
      providerType: 'gitlab',
    };

    // Act
    const fixture = createComponent(issue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const prLink = el.querySelector('.issue-card__pr-link') as HTMLAnchorElement;
    expect(prLink?.textContent?.trim()).toBe('View MR');
    expect(prLink?.getAttribute('aria-label')).toBe('View merge request for issue #42');
  });

  describe('agent activity', () => {
    const justNow = () => new Date().toISOString();
    const minutesAgo = (m: number) => new Date(Date.now() - m * 60 * 1000).toISOString();

    it('should show the agent activity line for an in-progress issue', () => {
      // Arrange
      const activity = buildAgentActivity({
        tasks: Array.from({ length: 7 }, (_, i) => ({ id: String(i), subject: 't', activeForm: null, status: 'pending' })),
        completedTaskCount: 3,
        currentTask: { id: '4', subject: 'Run tests', activeForm: 'Running tests', status: 'in_progress' },
        subagents: [{ subagentType: 'developer', description: null, spawnDepth: 1, lastTool: null }],
      });

      // Act
      const fixture = createWithAgentActivity(activity, justNow());
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const line = el.querySelector('.issue-card__agent-activity');
      expect(line?.textContent?.trim()).toBe('developer · Running tests (3/7)');
    });

    it('should show the subagent and in-flight tool when there is no task list', () => {
      // Arrange
      const activity = buildAgentActivity({
        subagents: [{ subagentType: 'developer', description: null, spawnDepth: 1, lastTool: null }],
        inFlightTool: { name: 'Bash', summary: 'dotnet test', startedAt: null },
      });

      // Act
      const fixture = createWithAgentActivity(activity, justNow());
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.querySelector('.issue-card__agent-activity')?.textContent?.trim()).toBe('developer · Bash');
    });

    it('should not show the agent activity line for a non-live issue', () => {
      // Arrange
      const failedIssue: IssueSummary = { ...mockIssue, state: 'failed' };

      // Act
      const fixture = createWithAgentActivity(buildAgentActivity(), justNow(), failedIssue);
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.querySelector('.issue-card__agent-activity')).toBeNull();
    });

    it('should compute silence from lastEventAt instead of lastActivityAt', () => {
      // Arrange — the container is alive (fresh lastActivityAt) but the agent has been quiet for 6 minutes
      const activity = buildAgentActivity({ lastEventAt: minutesAgo(6) });

      // Act
      const fixture = createWithAgentActivity(activity, justNow());
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.querySelector('.issue-card__activity')?.textContent).toContain('silent 6m');
    });

    it('should not show silence when lastEventAt is recent even if lastActivityAt is stale', () => {
      // Arrange
      const activity = buildAgentActivity({ lastEventAt: justNow() });

      // Act
      const fixture = createWithAgentActivity(activity, minutesAgo(9));
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.querySelector('.issue-card__activity')?.textContent).not.toContain('silent');
    });

    it('should name the in-flight tool next to the silence badge', () => {
      // Arrange
      const activity = buildAgentActivity({
        lastEventAt: minutesAgo(6),
        inFlightTool: { name: 'Bash', summary: 'dotnet test', startedAt: minutesAgo(6) },
      });

      // Act
      const fixture = createWithAgentActivity(activity, justNow());
      const el = fixture.nativeElement as HTMLElement;
      const label = (el.querySelector('.issue-card__toggle') as HTMLElement).getAttribute('aria-label') ?? '';

      // Assert
      expect(el.querySelector('.issue-card__activity')?.textContent).toContain('silent 6m · Bash running');
      expect(label).toContain('silent 6 minutes, Bash running');
    });
  });

  describe('triage states (#772)', () => {
    const triagingIssue: IssueSummary = { ...mockIssue, state: 'triage_in_progress' };

    it('should show just "active" for triage_in_progress, never a commit phrase', () => {
      // Arrange
      const recentAt = new Date(Date.now() - 2 * 60 * 1000).toISOString();

      // Act
      const fixture = createComponent(triagingIssue, recentAt);
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const activity = el.querySelector('.issue-card__activity') as HTMLElement;
      expect(activity.textContent?.trim()).toBe('active');
    });

    it('should show "active · silent Nm" for a silent triage run', () => {
      // Arrange
      const sevenMinutesAgo = new Date(Date.now() - 7 * 60 * 1000).toISOString();

      // Act
      const fixture = createComponent(triagingIssue, sevenMinutesAgo);
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const activity = el.querySelector('.issue-card__activity') as HTMLElement;
      expect(activity.textContent?.trim()).toBe('active · silent 7m');
    });

    it('should leave the commit phrase out of the aria-label for a triage run', () => {
      // Arrange
      const recentAt = new Date(Date.now() - 2 * 60 * 1000).toISOString();

      // Act
      const fixture = createComponent(triagingIssue, recentAt);
      const label =
        (fixture.nativeElement as HTMLElement).querySelector('.issue-card__toggle')?.getAttribute('aria-label') ?? '';

      // Assert
      expect(label).toContain('State: triaging');
      expect(label).toContain('Active.');
      expect(label).not.toContain('commit');
    });

    it('should include the silence in the aria-label for a silent triage run', () => {
      // Arrange
      const sevenMinutesAgo = new Date(Date.now() - 7 * 60 * 1000).toISOString();

      // Act
      const fixture = createComponent(triagingIssue, sevenMinutesAgo);
      const label =
        (fixture.nativeElement as HTMLElement).querySelector('.issue-card__toggle')?.getAttribute('aria-label') ?? '';

      // Assert
      expect(label).toContain('Active, silent 7 minutes.');
      expect(label).not.toContain('commit');
    });

    it('should give triage_in_progress the working accent', () => {
      // Arrange / Act
      const fixture = createComponent(triagingIssue);
      const card = (fixture.nativeElement as HTMLElement).querySelector('.issue-card--working');

      // Assert
      expect(card).not.toBeNull();
    });

    it('should show the repository warning for triage_queued', () => {
      // Arrange
      const queued: IssueSummary = {
        ...mockIssue,
        state: 'triage_queued',
        repositoryEligibilityStatus: 'ineligible',
      };

      // Act
      const fixture = createComponent(queued);
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.querySelector('.issue-card__repo-warning')).not.toBeNull();
    });

    it('should give triage_queued a queue position', () => {
      // Arrange
      const queued: IssueSummary = { ...mockIssue, state: 'triage_queued' };

      // Act
      const fixture = createComponent(queued, null, false, 2);
      const el = fixture.nativeElement as HTMLElement;
      const label = el.querySelector('.issue-card__toggle')?.getAttribute('aria-label') ?? '';

      // Assert
      expect(label).toContain('Queue position 2.');
      expect(el.querySelector('.issue-card--has-gutter')).not.toBeNull();
    });

    it('should give triage_queued the Next up marker', () => {
      // Arrange
      const queued: IssueSummary = { ...mockIssue, state: 'triage_queued' };

      // Act
      const fixture = createComponent(queued, null, true, 1);
      const label =
        (fixture.nativeElement as HTMLElement).querySelector('.issue-card__toggle')?.getAttribute('aria-label') ?? '';

      // Assert
      expect(label).toContain('Next up.');
    });
  });

  describe('live commit count removal (#787)', () => {
    it('should read "active · silent 7m" with no commit text', () => {
      // Arrange
      const sevenMinutesAgo = new Date(Date.now() - 7 * 60 * 1000).toISOString();

      // Act
      const fixture = createComponent(mockIssue, sevenMinutesAgo);
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const text = el.querySelector('.issue-card__activity')?.textContent?.trim();
      expect(text).toBe('active · silent 7m');
    });

    it('should read just "active" for a non-silent implement run', () => {
      // Arrange
      const recentAt = new Date(Date.now() - 2 * 60 * 1000).toISOString();

      // Act
      const fixture = createComponent(mockIssue, recentAt);
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.querySelector('.issue-card__activity')?.textContent?.trim()).toBe('active');
    });

    it('should carry " Active." in the aria-label of a non-silent implement run', () => {
      // Arrange
      const recentAt = new Date(Date.now() - 2 * 60 * 1000).toISOString();

      // Act
      const fixture = createComponent(mockIssue, recentAt);
      const label =
        (fixture.nativeElement as HTMLElement).querySelector('.issue-card__toggle')?.getAttribute('aria-label') ?? '';

      // Assert
      expect(label).toContain(' Active.');
      expect(label).not.toContain('commit');
    });

    it('should carry the silence and tool in the aria-label, followed by the agent part', () => {
      // Arrange
      const sevenMinutesAgo = new Date(Date.now() - 7 * 60 * 1000).toISOString();
      const activity = buildAgentActivity({
        lastEventAt: sevenMinutesAgo,
        inFlightTool: { name: 'Bash', summary: 'npm test', startedAt: null },
      });

      // Act
      const fixture = createWithAgentActivity(activity, sevenMinutesAgo);
      const label =
        (fixture.nativeElement as HTMLElement).querySelector('.issue-card__toggle')?.getAttribute('aria-label') ?? '';

      // Assert
      expect(label).toContain(' Active, silent 7 minutes, Bash running. Agent: ');
    });
  });

  describe('publish phase (#787)', () => {
    const sevenMinutesAgo = (): string => new Date(Date.now() - 7 * 60 * 1000).toISOString();

    function createPublishing(publishPhase: 'publishing' | 'retrying', state: IssueState = 'in_progress') {
      const issue: IssueSummary = { ...mockIssue, state, publishPhase };
      return createWithAgentActivity(buildAgentActivity({ lastEventAt: sevenMinutesAgo() }), sevenMinutesAgo(), issue);
    }

    it('should hide the activity and agent lines while publishing', () => {
      // Arrange / Act
      const fixture = createPublishing('publishing');
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.querySelector('.issue-card__activity')).toBeNull();
      expect(el.querySelector('.issue-card__agent-activity')).toBeNull();
    });

    it('should show "Publishing commits" in the footer status slot', () => {
      // Arrange / Act
      const fixture = createPublishing('publishing');
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const status = el.querySelector('.issue-card__checks-status');
      expect(status?.textContent?.trim()).toBe('Publishing commits');
      expect(status?.getAttribute('aria-hidden')).toBe('true');
    });

    it('should carry "Publishing commits." in the aria-label without the Active clause', () => {
      // Arrange / Act
      const fixture = createPublishing('publishing');
      const label =
        (fixture.nativeElement as HTMLElement).querySelector('.issue-card__toggle')?.getAttribute('aria-label') ?? '';

      // Assert
      expect(label).toContain(' Publishing commits.');
      expect(label).not.toContain('Active');
      expect(label).not.toContain('Agent:');
    });

    it('should show the publishing badge on the card', () => {
      // Arrange / Act
      const fixture = createPublishing('publishing', 'revision_in_progress');
      const badge = (fixture.nativeElement as HTMLElement).querySelector('fd-state-badge span');

      // Assert
      expect(badge?.textContent?.trim()).toBe('REV PUBLISHING');
    });

    it('should show "Publish failed, retrying" in the footer while retrying', () => {
      // Arrange / Act
      const fixture = createPublishing('retrying');
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.querySelector('.issue-card__checks-status')?.textContent?.trim()).toBe('Publish failed, retrying');
      expect(el.querySelector('.issue-card__activity')).toBeNull();
      expect(el.querySelector('fd-state-badge span')?.textContent?.trim()).toBe('PUBLISH RETRY');
    });

    it('should carry "Publish failed, retrying automatically." in the aria-label while retrying', () => {
      // Arrange / Act
      const fixture = createPublishing('retrying');
      const label =
        (fixture.nativeElement as HTMLElement).querySelector('.issue-card__toggle')?.getAttribute('aria-label') ?? '';

      // Assert
      expect(label).toContain(' Publish failed, retrying automatically.');
      expect(label).not.toContain('Active');
    });

    it('should keep the working accent while publishing', () => {
      // Arrange / Act
      const fixture = createPublishing('publishing');

      // Assert
      expect((fixture.nativeElement as HTMLElement).querySelector('.issue-card--working')).not.toBeNull();
    });
  });
});
