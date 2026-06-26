import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { vi } from 'vitest';
import { IssueFilterRailComponent } from './issue-filter-rail';
import { IssueService } from '../issue.service';
import { STATE_GROUPS, isResolvedState } from '../issue-lifecycle.model';
import { IssueState } from '../issue.model';

function createMockIssueService(overrides: {
  counts?: Record<string, number>;
  selectedActiveStates?: ReadonlySet<IssueState>;
  selectedResolvedStates?: ReadonlySet<IssueState>;
} = {}) {
  const countsMap = overrides.counts ?? {};
  const activeStates = overrides.selectedActiveStates
    ?? new Set<IssueState>(['detected', 'queued', 'blocked', 'in_progress', 'review', 'failed', 'continuable_failed', 'continuation_queued', 'revision_queued', 'revision_in_progress', 'revision_failed']);
  const resolvedStates = overrides.selectedResolvedStates ?? new Set<IssueState>();

  const countsSignal = signal<Record<string, number>>(countsMap);
  const selectedActiveStatesSignal = signal<ReadonlySet<IssueState>>(activeStates);
  const selectedResolvedStatesSignal = signal<ReadonlySet<IssueState>>(resolvedStates);
  const toggleState = vi.fn();

  return {
    counts: countsSignal.asReadonly(),
    selectedActiveStates: selectedActiveStatesSignal.asReadonly(),
    selectedResolvedStates: selectedResolvedStatesSignal.asReadonly(),
    countFor: (state: IssueState): number => countsMap[state] ?? 0,
    isStateSelected: (state: IssueState): boolean => {
      if (isResolvedState(state)) {
        return selectedResolvedStatesSignal().has(state);
      }
      return selectedActiveStatesSignal().has(state);
    },
    toggleState,
  };
}

function setup(overrides: Parameters<typeof createMockIssueService>[0] = {}) {
  const mockService = createMockIssueService(overrides);

  TestBed.configureTestingModule({
    imports: [IssueFilterRailComponent],
    providers: [
      { provide: IssueService, useValue: mockService },
    ],
  });

  const fixture = TestBed.createComponent(IssueFilterRailComponent);
  fixture.detectChanges();
  return { fixture, mockService };
}

describe('IssueFilterRailComponent', () => {
  // Cycle 1: renders a section per STATE_GROUP with group label
  it('should render a section for each STATE_GROUP', () => {
    // Arrange / Act
    const { fixture } = setup();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const sections = el.querySelectorAll('.filter-rail__group');
    expect(sections.length).toBe(STATE_GROUPS.length);
  });

  it('should render each group label as a heading', () => {
    // Arrange / Act
    const { fixture } = setup();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const headings = el.querySelectorAll('.filter-rail__group-label');
    const labels = Array.from(headings).map(h => h.textContent?.trim());
    expect(labels).toContain('In progress');
    expect(labels).toContain('Waiting');
    expect(labels).toContain('Needs attention');
    expect(labels).toContain('Resolved');
  });

  // Cycle 2: renders a toggle button per state
  it('should render one toggle button per state across all groups', () => {
    // Arrange
    const totalStates = STATE_GROUPS.reduce((sum, g) => sum + g.states.length, 0);

    // Act
    const { fixture } = setup();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const buttons = el.querySelectorAll('.filter-rail__toggle');
    expect(buttons.length).toBe(totalStates);
  });

  // Cycle 3: toggle shows count from countFor
  it('should display the count for each state in the count pill', () => {
    // Arrange
    const counts: Record<string, number> = { in_progress: 3, detected: 7 };

    // Act
    const { fixture } = setup({ counts });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const pills = el.querySelectorAll('.filter-rail__count');
    const pillTexts = Array.from(pills).map(p => p.textContent?.trim());
    expect(pillTexts).toContain('3');
    expect(pillTexts).toContain('7');
  });

  // Cycle 4: toggle reflects isStateSelected via aria-pressed
  it('should set aria-pressed="true" when state is selected', () => {
    // Arrange
    const activeSet = new Set<IssueState>(['in_progress']);

    // Act
    const { fixture } = setup({ selectedActiveStates: activeSet });
    const el = fixture.nativeElement as HTMLElement;

    // Assert — in_progress is selected so its button should have aria-pressed="true"
    const toggles = Array.from(el.querySelectorAll<HTMLButtonElement>('.filter-rail__toggle'));
    const inProgressBtn = toggles.find(b => b.dataset['state'] === 'in_progress');
    expect(inProgressBtn?.getAttribute('aria-pressed')).toBe('true');
  });

  it('should set aria-pressed="false" when state is not selected', () => {
    // Arrange
    const activeSet = new Set<IssueState>(); // nothing selected

    // Act
    const { fixture } = setup({ selectedActiveStates: activeSet });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const toggles = Array.from(el.querySelectorAll<HTMLButtonElement>('.filter-rail__toggle'));
    const detectedBtn = toggles.find(b => b.dataset['state'] === 'detected');
    expect(detectedBtn?.getAttribute('aria-pressed')).toBe('false');
  });

  // Cycle 5: clicking a toggle calls issueService.toggleState
  it('should call toggleState with the correct state when a toggle is clicked', () => {
    // Arrange — give detected a count so the button is enabled and clickable
    const { fixture, mockService } = setup({ counts: { detected: 1 } });
    const el = fixture.nativeElement as HTMLElement;

    // Act
    const toggles = Array.from(el.querySelectorAll<HTMLButtonElement>('.filter-rail__toggle'));
    const detectedBtn = toggles.find(b => b.dataset['state'] === 'detected');
    detectedBtn?.click();

    // Assert
    expect(mockService.toggleState).toHaveBeenCalledWith('detected');
  });

  // Cycle 6: zero-count states are disabled and dimmed
  it('should disable a toggle button when count is zero', () => {
    // Arrange — no counts provided, all default to 0
    const { fixture } = setup({ counts: {} });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const toggles = Array.from(el.querySelectorAll<HTMLButtonElement>('.filter-rail__toggle'));
    const detectedBtn = toggles.find(b => b.dataset['state'] === 'detected');
    expect(detectedBtn?.disabled).toBe(true);
  });

  it('should apply dimmed modifier class when count is zero', () => {
    // Arrange
    const { fixture } = setup({ counts: {} });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const toggles = Array.from(el.querySelectorAll<HTMLButtonElement>('.filter-rail__toggle'));
    const detectedBtn = toggles.find(b => b.dataset['state'] === 'detected');
    expect(detectedBtn?.classList.contains('filter-rail__toggle--dimmed')).toBe(true);
  });

  it('should keep zero-count toggle in the DOM (not hidden)', () => {
    // Arrange
    const { fixture } = setup({ counts: {} });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const toggles = el.querySelectorAll('.filter-rail__toggle');
    const totalStates = STATE_GROUPS.reduce((sum, g) => sum + g.states.length, 0);
    expect(toggles.length).toBe(totalStates);
  });

  it('should not disable a toggle when count is greater than zero', () => {
    // Arrange
    const { fixture } = setup({ counts: { in_progress: 2 } });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const toggles = Array.from(el.querySelectorAll<HTMLButtonElement>('.filter-rail__toggle'));
    const inProgressBtn = toggles.find(b => b.dataset['state'] === 'in_progress');
    expect(inProgressBtn?.disabled).toBe(false);
  });

  // Cycle 7: resolved states are unpressed by default
  it('should render resolved state toggles with aria-pressed="false" by default', () => {
    // Arrange — selectedResolvedStates defaults to empty set
    const { fixture } = setup({
      counts: { completed: 5, unchanged: 2 },
    });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const toggles = Array.from(el.querySelectorAll<HTMLButtonElement>('.filter-rail__toggle'));
    const completedBtn = toggles.find(b => b.dataset['state'] === 'completed');
    const unchangedBtn = toggles.find(b => b.dataset['state'] === 'unchanged');
    expect(completedBtn?.getAttribute('aria-pressed')).toBe('false');
    expect(unchangedBtn?.getAttribute('aria-pressed')).toBe('false');
  });

  // Cycle 8: accessibility — rail has role="group" and aria-label
  it('should have role="group" on the rail container', () => {
    // Arrange / Act
    const { fixture } = setup();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const rail = el.querySelector('.filter-rail') as HTMLElement;
    expect(rail?.getAttribute('role')).toBe('group');
  });

  it('should have aria-label on the rail container', () => {
    // Arrange / Act
    const { fixture } = setup();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const rail = el.querySelector('.filter-rail') as HTMLElement;
    expect(rail?.getAttribute('aria-label')).toBe('Filter issues by state');
  });

  // Cycle 9: each toggle has a human-readable label and color dot
  it('should render a human-readable label for each state', () => {
    // Arrange / Act
    const { fixture } = setup();
    const el = fixture.nativeElement as HTMLElement;

    // Assert — spot-check a few
    const labels = Array.from(el.querySelectorAll('.filter-rail__state-label')).map(l => l.textContent?.trim());
    expect(labels).toContain('In Progress');
    expect(labels).toContain('Detected');
    expect(labels).toContain('Completed');
  });

  it('should render a color dot for each toggle', () => {
    // Arrange / Act
    const { fixture } = setup();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const dots = el.querySelectorAll('.filter-rail__dot');
    const totalStates = STATE_GROUPS.reduce((sum, g) => sum + g.states.length, 0);
    expect(dots.length).toBe(totalStates);
  });

  // Cycle 10: [WCAG 1.3.1] each group div has role="group" and aria-labelledby pointing at its label span
  it('should give each group div role="group"', () => {
    // Arrange / Act
    const { fixture } = setup();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const groups = Array.from(el.querySelectorAll('.filter-rail__group'));
    for (const group of groups) {
      expect(group.getAttribute('role')).toBe('group');
    }
  });

  it('should set aria-labelledby on each group div pointing at its label span id', () => {
    // Arrange / Act
    const { fixture } = setup();
    const el = fixture.nativeElement as HTMLElement;

    // Assert — each group's aria-labelledby resolves to its label span
    const groups = Array.from(el.querySelectorAll('.filter-rail__group'));
    for (const group of groups) {
      const labelledBy = group.getAttribute('aria-labelledby');
      expect(labelledBy).toBeTruthy();
      const labelEl = el.querySelector(`#${labelledBy}`);
      expect(labelEl).not.toBeNull();
      expect(labelEl?.classList.contains('filter-rail__group-label')).toBe(true);
    }
  });

  it('should generate stable ids from the group label text', () => {
    // Arrange / Act
    const { fixture } = setup();
    const el = fixture.nativeElement as HTMLElement;

    // Assert — spot-check that the "In progress" group uses the slugified id
    const groups = Array.from(el.querySelectorAll('.filter-rail__group'));
    const inProgressGroup = groups.find(g => {
      const label = g.querySelector('.filter-rail__group-label');
      return label?.textContent?.trim() === 'In progress';
    });
    expect(inProgressGroup?.getAttribute('aria-labelledby')).toBe('rail-group-in-progress');
  });

  // Cycle 11: [WCAG 2.4.6] toggle buttons have explicit aria-label with count + unit
  it('should set aria-label with plural "issues" when count is greater than one', () => {
    // Arrange
    const { fixture } = setup({ counts: { in_progress: 3 } });
    const el = fixture.nativeElement as HTMLElement;

    // Act
    const toggles = Array.from(el.querySelectorAll<HTMLButtonElement>('.filter-rail__toggle'));
    const btn = toggles.find(b => b.dataset['state'] === 'in_progress');

    // Assert
    expect(btn?.getAttribute('aria-label')).toBe('In Progress, 3 issues');
  });

  it('should set aria-label with singular "issue" when count is exactly one', () => {
    // Arrange
    const { fixture } = setup({ counts: { detected: 1 } });
    const el = fixture.nativeElement as HTMLElement;

    // Act
    const toggles = Array.from(el.querySelectorAll<HTMLButtonElement>('.filter-rail__toggle'));
    const btn = toggles.find(b => b.dataset['state'] === 'detected');

    // Assert
    expect(btn?.getAttribute('aria-label')).toBe('Detected, 1 issue');
  });

  it('should set aria-label with "no issues, filter unavailable" when count is zero', () => {
    // Arrange
    const { fixture } = setup({ counts: {} });
    const el = fixture.nativeElement as HTMLElement;

    // Act
    const toggles = Array.from(el.querySelectorAll<HTMLButtonElement>('.filter-rail__toggle'));
    const btn = toggles.find(b => b.dataset['state'] === 'queued');

    // Assert
    expect(btn?.getAttribute('aria-label')).toBe('Queued, no issues, filter unavailable');
  });
});
