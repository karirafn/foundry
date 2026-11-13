import { TestBed } from '@angular/core/testing';
import { AgentActivity } from '../../../core/models/agent-activity.model';
import { IssueSummary } from '../../issues/issue.model';
import { FloorLane } from '../floor.model';
import { FloorLaneComponent } from './floor-lane';

const NOW = Date.parse('2026-01-01T12:00:00Z');

function buildActivity(overrides: Partial<AgentActivity> = {}): AgentActivity {
  return {
    workerRunId: 'run-1',
    issueId: 'issue-1',
    role: 'implement',
    lastEventAt: '2026-01-01T11:59:30Z',
    tasks: [
      { id: '1', subject: 'Read the issue', activeForm: null, status: 'completed' },
      { id: '2', subject: 'Run tests', activeForm: 'Running tests', status: 'in_progress' },
      { id: '3', subject: 'Open PR', activeForm: null, status: 'pending' },
    ],
    currentTask: { id: '2', subject: 'Run tests', activeForm: 'Running tests', status: 'in_progress' },
    completedTaskCount: 1,
    subagents: [],
    activeSkill: null,
    inFlightTool: null,
    tokens: { inputTokens: 1_212_345, outputTokens: null },
    ...overrides,
  };
}

const ISSUE: IssueSummary = {
  id: 'issue-1',
  issueNumber: 42,
  title: 'Add the Floor',
  state: 'in_progress',
  repositorySlug: 'owner/repo',
  detectedAt: '2026-01-01T00:00:00Z',
  url: 'https://github.com/owner/repo/issues/42',
  stage: 'implement',
  needsAction: false,
  awaitsDispatch: false,
  waitsOnBlockers: false,
  codeStep: null,
  repositoryId: 'repo-1',
  enteredStateAt: '2026-01-01T00:00:00Z',
};

function render(lane: Partial<FloorLane> = {}, stale = false) {
  TestBed.configureTestingModule({ imports: [FloorLaneComponent] });
  const fixture = TestBed.createComponent(FloorLaneComponent);
  fixture.componentRef.setInput('lane', {
    issueId: 'issue-1',
    issue: ISSUE,
    activity: buildActivity(),
    recentTools: [],
    ...lane,
  });
  fixture.componentRef.setInput('now', NOW);
  fixture.componentRef.setInput('stale', stale);
  fixture.detectChanges();
  return { fixture, el: fixture.nativeElement as HTMLElement };
}

describe('FloorLaneComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('should show the issue number, title and repository', () => {
    // Act
    const { el } = render();

    // Assert
    expect(el.querySelector('.floor-lane__title')?.textContent).toContain('#42');
    expect(el.querySelector('.floor-lane__title')?.textContent).toContain('Add the Floor');
    expect(el.querySelector('.floor-lane__repo')?.textContent).toContain('owner/repo');
  });

  it('should show the run role tag before the repository slug', () => {
    // Act
    const { el } = render({ activity: buildActivity({ role: 'triage' }) });

    // Assert
    const repo = el.querySelector('.floor-lane__repo');
    const tag = repo?.firstElementChild;
    expect(tag?.tagName.toLowerCase()).toBe('fd-role-tag');
    expect(tag?.textContent).toBe('Role: triage');
    expect(repo?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Role: triage owner/repo');
  });

  it('should show no role tag when the activity carries no role', () => {
    // Act
    const { el } = render({ activity: buildActivity({ role: null }) });

    // Assert
    expect(el.querySelector('fd-role-tag')).toBeNull();
    expect(el.querySelector('.floor-lane__repo')?.textContent?.trim()).toBe('owner/repo');
  });

  it('should highlight the current task and show N of M done', () => {
    // Act
    const { el } = render();

    // Assert
    const current = el.querySelector('.floor-lane__task--current');
    expect(current?.getAttribute('aria-current')).toBe('step');
    expect(current?.textContent).toContain('Running tests');
    expect(el.querySelectorAll('.floor-lane__task').length).toBe(3);
    expect(el.querySelector('.floor-lane__progress')?.textContent).toContain('1 of 3 done');
  });

  it('should show parallel and nested subagents separately with the active skill', () => {
    // Act
    const { el } = render({
      activity: buildActivity({
        activeSkill: 'code-review',
        subagents: [
          { subagentType: 'developer', description: 'Implement', spawnDepth: 1, lastTool: null },
          { subagentType: 'tester', description: 'Write specs', spawnDepth: 1, lastTool: null },
          { subagentType: 'explorer', description: 'Find usages', spawnDepth: 2, lastTool: null },
        ],
      }),
    });

    // Assert
    const labels = Array.from(el.querySelectorAll('.floor-lane__label')).map((n) => n.textContent?.trim());
    expect(labels).toEqual(['Skill', 'Parallel', 'Nested']);
    const lists = el.querySelectorAll('.floor-lane__subagents');
    expect(lists[0].querySelectorAll('li').length).toBe(2);
    expect(lists[1].textContent).toContain('explorer');
    expect(el.querySelector('.floor-lane__skill')?.textContent).toContain('code-review');
  });

  it('should collapse subagents beyond the visible limit behind a disclosure', () => {
    // Arrange
    const subagents = Array.from({ length: 5 }, (_, i) => ({
      subagentType: `agent-${i}`, description: null, spawnDepth: 1, lastTool: null,
    }));

    // Act
    const { el } = render({ activity: buildActivity({ subagents }) });

    // Assert
    expect(el.querySelector('.floor-lane__overflow summary')?.textContent).toContain('2 more parallel');
  });

  it('should list recent tools newest first', () => {
    // Act
    const { el } = render({
      recentTools: [
        { name: 'Read', summary: 'src/app.ts', startedAt: null },
        { name: 'Bash', summary: 'npm test', startedAt: null },
      ],
    });

    // Assert
    const tools = Array.from(el.querySelectorAll('.floor-lane__tool-name')).map((n) => n.textContent);
    expect(tools).toEqual(['Bash', 'Read']);
  });

  it('should show billable input and hide output while the run is live', () => {
    // Act
    const { el } = render();

    // Assert
    const terms = Array.from(el.querySelectorAll('.floor-lane__tokens dt')).map((n) => n.textContent);
    const tokens = el.querySelector('.floor-lane__tokens')?.textContent ?? '';
    expect(terms).toEqual(['In']);
    expect(tokens).toContain('1.2M');
    expect(tokens).not.toContain('Cache');
  });

  it('should show final output once the run reports it', () => {
    // Act
    const { el } = render({ activity: buildActivity({ tokens: { inputTokens: 12_345, outputTokens: 950 } }) });

    // Assert
    const terms = Array.from(el.querySelectorAll('.floor-lane__tokens dt')).map((n) => n.textContent);
    const tokens = el.querySelector('.floor-lane__tokens')?.textContent ?? '';
    expect(terms).toEqual(['In', 'Out']);
    expect(tokens).toContain('12.3k');
    expect(tokens).toContain('950');
  });

  it('should show time since the last event below the silence threshold', () => {
    // Act
    const { el } = render();

    // Assert
    const lastEvent = el.querySelector('.floor-lane__last-event');
    expect(lastEvent?.textContent).toContain('last event 30s ago');
    expect(el.querySelector('.floor-lane--silent')).toBeNull();
  });

  it('should style the lane as silent past the threshold and name the in-flight tool', () => {
    // Act
    const { el } = render({
      activity: buildActivity({
        lastEventAt: '2026-01-01T11:54:00Z',
        inFlightTool: { name: 'Bash', summary: 'npm test', startedAt: null },
      }),
    });

    // Assert
    expect(el.querySelector('.floor-lane--silent')).not.toBeNull();
    expect(el.querySelector('.floor-lane__last-event--silent')?.textContent).toContain('silent 6m · Bash running');
  });

  it('should keep token and clock updates out of the polite live region', () => {
    // Arrange
    const { fixture, el } = render();
    const live = el.querySelector('[aria-live="polite"]');
    const before = live?.textContent;

    // Act
    fixture.componentRef.setInput('lane', {
      issueId: 'issue-1',
      issue: ISSUE,
      activity: buildActivity({ tokens: { inputTokens: 99_999, outputTokens: 1 } }),
      recentTools: [{ name: 'Bash', summary: null, startedAt: null }],
    });
    fixture.componentRef.setInput('now', NOW + 60_000);
    fixture.detectChanges();

    // Assert
    expect(before?.trim()).toBe('Issue #42: Running tests, task 2 of 3');
    expect(live?.textContent).toBe(before);
  });

  it('should show stale styling while disconnected', () => {
    // Act
    const { el } = render({}, true);

    // Assert
    expect(el.querySelector('.floor-lane--stale')).not.toBeNull();
    expect(el.querySelector('.floor-lane__stale-note')).not.toBeNull();
  });
});
