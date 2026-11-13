import { TestBed } from '@angular/core/testing';
import { WritableSignal, signal } from '@angular/core';
import { Subject } from 'rxjs';
import { AgentActivity, InFlightTool } from '../../../core/models/agent-activity.model';
import { ConnectionStatus } from '../../../core/services/hub-connection';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import { PaneActivityComponent } from './pane-activity';

const NOW = Date.parse('2026-11-13T12:00:00Z');

function activity(overrides: Partial<AgentActivity> = {}): AgentActivity {
  const tasks = [
    { id: '1', subject: 'Read the code', activeForm: 'Reading the code', status: 'completed' },
    { id: '2', subject: 'Write tests', activeForm: 'Writing tests', status: 'completed' },
    { id: '3', subject: 'Run unit tests', activeForm: 'Running unit tests', status: 'in_progress' },
    { id: '4', subject: 'Commit', activeForm: null, status: 'pending' },
  ];
  return {
    workerRunId: 'run-1',
    issueId: 'issue-1',
    role: 'implement',
    lastEventAt: new Date(NOW - 12_000).toISOString(),
    tasks,
    currentTask: tasks[2],
    completedTaskCount: 2,
    subagents: [],
    activeSkill: null,
    inFlightTool: { name: 'Bash', summary: 'dotnet test --filter Board', startedAt: '2026-11-13T11:59:50Z' },
    tokens: { inputTokens: 100, outputTokens: null },
    ...overrides,
  };
}

interface Fakes {
  readonly activity: WritableSignal<AgentActivity | null>;
  readonly tools: WritableSignal<readonly InFlightTool[]>;
  readonly status: WritableSignal<ConnectionStatus>;
  readonly log: Subject<string>;
  readonly streamLog: ReturnType<typeof vi.fn>;
}

function setup(initial: AgentActivity | null = activity()) {
  vi.useFakeTimers({ now: NOW, toFake: ['Date', 'setInterval', 'clearInterval'] });
  const fakes: Fakes = {
    activity: signal(initial),
    tools: signal([]),
    status: signal('connected'),
    log: new Subject<string>(),
    streamLog: vi.fn(),
  };
  fakes.streamLog.mockImplementation(() => fakes.log.asObservable());
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [PaneActivityComponent],
    providers: [
      {
        provide: WorkerSignalRService,
        useValue: {
          agentActivityForIssue: () => fakes.activity(),
          recentToolsForRun: () => fakes.tools(),
          connectionStatus: fakes.status,
          streamLog: fakes.streamLog,
        },
      },
    ],
  });
  const fixture = TestBed.createComponent(PaneActivityComponent);
  fixture.componentRef.setInput('issueId', 'issue-1');
  fixture.componentRef.setInput('workerRunId', 'run-1');
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  return { fixture, el, fakes };
}

function text(node: Element | null | undefined): string {
  return (node?.textContent ?? '').replace(/\s+/g, ' ').trim();
}

describe('PaneActivityComponent', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('heads the section with an Activity heading that names it', () => {
    // Act
    const { el } = setup();

    // Assert
    const section = el.querySelector('section')!;
    const heading = el.querySelector(`#${section.getAttribute('aria-labelledby')}`);
    expect(heading?.tagName.toLowerCase()).toBe('h3');
    expect(text(heading?.querySelector('.pane-activity__title'))).toBe('Activity');
  });

  it('shows the current task with "task N of M"', () => {
    // Act
    const { el } = setup();

    // Assert
    expect(text(el.querySelector('.pane-activity__step'))).toBe('Running unit tests');
    expect(text(el.querySelector('.pane-activity__ordinal'))).toBe('task 3 of 4');
  });

  it('shows the step without an ordinal when the agent keeps no task list', () => {
    // Act
    const { el } = setup(activity({ tasks: [], currentTask: null, completedTaskCount: 0 }));

    // Assert
    expect(text(el.querySelector('.pane-activity__step'))).toBe('Working');
    expect(el.querySelector('.pane-activity__ordinal')).toBeNull();
    expect(el.querySelector('.pane-activity__bar')).toBeNull();
  });

  it('fills the hidden progress bar by the share of completed tasks', () => {
    // Act
    const { el } = setup();

    // Assert
    const bar = el.querySelector<HTMLElement>('.pane-activity__bar')!;
    expect(bar.getAttribute('aria-hidden')).toBe('true');
    expect(bar.querySelector<HTMLElement>('.pane-activity__fill')?.style.width).toBe('50%');
  });

  it('shows the latest tool with its summary', () => {
    // Act
    const { el } = setup();

    // Assert
    const tool = el.querySelector('.pane-activity__tool');
    expect(text(tool?.querySelector('.pane-activity__tool-name'))).toBe('Bash');
    expect(text(tool?.querySelector('.pane-activity__tool-summary'))).toBe('dotnet test --filter Board');
  });

  it('falls back to the most recent finished tool when none is in flight', () => {
    // Arrange
    const { fixture, el, fakes } = setup(activity({ inFlightTool: null }));

    // Act
    fakes.tools.set([
      { name: 'Read', summary: 'a.ts', startedAt: null },
      { name: 'Edit', summary: 'b.ts', startedAt: null },
    ]);
    fixture.detectChanges();

    // Assert
    expect(text(el.querySelector('.pane-activity__tool-name'))).toBe('Edit');
  });

  it('shows the time since the last event and advances it on the 5 s clock', () => {
    // Arrange
    const { fixture, el } = setup();
    expect(text(el.querySelector('.pane-activity__elapsed'))).toBe('last event 12s ago');

    // Act
    vi.advanceTimersByTime(5_000);
    fixture.detectChanges();

    // Assert
    expect(text(el.querySelector('.pane-activity__elapsed'))).toBe('last event 17s ago');
  });

  it('turns the elapsed text into a warning when the worker is silent', () => {
    // Act
    const { el } = setup(activity({ lastEventAt: new Date(NOW - 6 * 60_000).toISOString() }));

    // Assert
    const elapsed = el.querySelector('.pane-activity__elapsed');
    expect(text(elapsed)).toBe('silent 6m · Bash running');
    expect(elapsed?.classList.contains('pane-activity__elapsed--silent')).toBe(true);
  });

  it('says the connection is lost and freezes the elapsed text while disconnected', () => {
    // Arrange
    const { fixture, el, fakes } = setup();

    // Act
    fakes.status.set('reconnecting');
    vi.advanceTimersByTime(10_000);
    fixture.detectChanges();

    // Assert
    const note = el.querySelector('.pane-activity__stale');
    expect(text(note)).toBe('Connection lost — showing last known state');
    expect(text(el.querySelector('.pane-activity__elapsed'))).toBe('last event 12s ago');
  });

  it('announces step changes in one polite region, not clock ticks', () => {
    // Arrange
    const { fixture, el, fakes } = setup();
    const region = el.querySelector('[aria-live="polite"].pane-activity__announcer');
    expect(text(region)).toBe('Running unit tests, task 3 of 4');

    // Act
    vi.advanceTimersByTime(5_000);
    fakes.activity.set(activity({ tokens: { inputTokens: 999, outputTokens: null } }));
    fixture.detectChanges();
    const afterTick = text(region);
    const next = activity();
    fakes.activity.set(activity({ currentTask: next.tasks[3], completedTaskCount: 3 }));
    fixture.detectChanges();

    // Assert
    expect(afterTick).toBe('Running unit tests, task 3 of 4');
    expect(text(region)).toBe('Commit, task 4 of 4');
  });

  it('waits for the first agent event before showing a step', () => {
    // Act
    const { el } = setup(null);

    // Assert
    expect(text(el.querySelector('.pane-activity__waiting'))).toBe('Waiting for the first agent event.');
    expect(el.querySelector('.pane-activity__step')).toBeNull();
  });

  it('streams the live log collapsed behind Show log in a bounded region', () => {
    // Arrange
    const { fixture, el, fakes } = setup();

    // Act
    fakes.log.next('line one');
    fixture.detectChanges();

    // Assert
    expect(fakes.streamLog).toHaveBeenCalledWith('run-1');
    const log = el.querySelector<HTMLElement>('fd-log-view')!;
    expect(log.style.getPropertyValue('--fd-log-view-max-height')).toBe('16rem');
    const toggle = log.querySelector('.log-view__toggle')!;
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(text(toggle)).toContain('Show log');
    expect(Array.from(log.querySelectorAll('.log-view__line')).map((l) => l.textContent)).toEqual(['line one']);
  });

  it('streams the log once per run', () => {
    // Arrange
    const { fixture, fakes } = setup();

    // Act
    fakes.activity.set(activity({ tokens: { inputTokens: 500, outputTokens: null } }));
    fixture.detectChanges();

    // Assert
    expect(fakes.streamLog).toHaveBeenCalledTimes(1);
  });
});
