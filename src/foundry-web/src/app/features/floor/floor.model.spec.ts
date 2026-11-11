import { AgentActivity, InFlightTool } from '../../core/models/agent-activity.model';
import { IssueSummary } from '../issues/issue.model';
import {
  buildFloorLanes,
  buildPublishingSlots,
  formatElapsed,
  formatTokenCount,
  laneStepText,
  splitSubagents,
} from './floor.model';

function buildActivity(overrides: Partial<AgentActivity> = {}): AgentActivity {
  return {
    workerRunId: 'run-1',
    issueId: 'issue-1',
    role: 'implement',
    lastEventAt: null,
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

function buildIssue(overrides: Partial<IssueSummary> = {}): IssueSummary {
  return {
    id: 'issue-1',
    issueNumber: 42,
    title: 'Add the Floor',
    state: 'in_progress',
    repositorySlug: 'owner/repo',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/owner/repo/issues/42',
    ...overrides,
  };
}

const noTools = (): readonly InFlightTool[] => [];

describe('buildFloorLanes', () => {
  it('should build one lane per active run joined with its issue', () => {
    // Arrange
    const issue = buildIssue();
    const tool: InFlightTool = { name: 'Bash', summary: 'npm test', startedAt: null };

    // Act
    const lanes = buildFloorLanes([buildActivity()], [issue], true, () => [tool]);

    // Assert
    expect(lanes).toEqual([{ issueId: 'issue-1', issue, activity: buildActivity(), recentTools: [tool] }]);
  });

  it('should drop a run whose issue is no longer live once issues have loaded', () => {
    // Arrange
    const issue = buildIssue({ state: 'review' });

    // Act
    const lanes = buildFloorLanes([buildActivity()], [issue], true, noTools);

    // Assert
    expect(lanes).toEqual([]);
  });

  it('should drop a run whose issue left the active list once issues have loaded', () => {
    // Act
    const lanes = buildFloorLanes([buildActivity()], [], true, noTools);

    // Assert
    expect(lanes).toEqual([]);
  });

  it('should keep a run with an unknown issue while issues are still loading', () => {
    // Act
    const lanes = buildFloorLanes([buildActivity()], [], false, noTools);

    // Assert
    expect(lanes.map((l) => l.issueId)).toEqual(['issue-1']);
    expect(lanes[0].issue).toBeNull();
  });

  it('should not add lanes for live issues whose run has not reported activity', () => {
    // Arrange
    const running = buildIssue();
    const starting = buildIssue({ id: 'issue-2', issueNumber: 43, state: 'revision_in_progress' });

    // Act
    const lanes = buildFloorLanes([buildActivity()], [running, starting], true, noTools);

    // Assert
    expect(lanes.map((l) => l.issueId)).toEqual(['issue-1']);
  });

  it('should drop the agent lane of an issue that is publishing, which shows as a publishing slot instead', () => {
    // Arrange
    const publishing = buildIssue({ publishPhase: 'publishing' });

    // Act
    const lanes = buildFloorLanes([buildActivity()], [publishing], true, noTools);

    // Assert
    expect(lanes).toEqual([]);
  });
});

describe('buildPublishingSlots', () => {
  it('should build one slot per working issue with a publish phase, keeping the phase', () => {
    // Arrange
    const publishing = buildIssue({ id: 'a', publishPhase: 'publishing' });
    const retrying = buildIssue({ id: 'b', state: 'revision_in_progress', publishPhase: 'retrying' });
    const running = buildIssue({ id: 'c', publishPhase: null });
    const noField = buildIssue({ id: 'd' });

    // Act
    const slots = buildPublishingSlots([publishing, retrying, running, noField]);

    // Assert
    expect(slots).toEqual([
      { issue: publishing, phase: 'publishing' },
      { issue: retrying, phase: 'retrying' },
    ]);
  });

  it('should skip an issue with a publish phase that is not in a working state', () => {
    // Arrange
    const exhausted = buildIssue({ state: 'publish_failed', publishPhase: 'retrying' });
    const review = buildIssue({ id: 'r', state: 'review', publishPhase: 'publishing' });

    // Act
    const slots = buildPublishingSlots([exhausted, review]);

    // Assert
    expect(slots).toEqual([]);
  });
});

describe('splitSubagents', () => {
  it('should separate top-level parallel subagents from nested ones', () => {
    // Arrange
    const parallel = { subagentType: 'developer', description: 'Build', spawnDepth: 1, lastTool: null };
    const nested = { subagentType: 'reviewer', description: 'Review', spawnDepth: 2, lastTool: null };

    // Act
    const result = splitSubagents([parallel, nested]);

    // Assert
    expect(result.parallel).toEqual([parallel]);
    expect(result.nested).toEqual([nested]);
  });
});

describe('formatElapsed', () => {
  it.each([
    [0, '0s'],
    [-5_000, '0s'],
    [42_000, '42s'],
    [6 * 60_000, '6m'],
    [60 * 60_000, '1h'],
    [64 * 60_000, '1h 4m'],
  ])('should format %i ms as %s', (ms, expected) => {
    expect(formatElapsed(ms)).toBe(expected);
  });
});

describe('formatTokenCount', () => {
  it.each([
    [950, '950'],
    [1000, '1k'],
    [12_345, '12.3k'],
    [1_200_000, '1.2M'],
  ])('should format %i as %s', (count, expected) => {
    expect(formatTokenCount(count)).toBe(expected);
  });
});

describe('laneStepText', () => {
  it('should name the current task and its position', () => {
    // Arrange
    const task = { id: '3', subject: 'Run tests', activeForm: 'Running tests', status: 'in_progress' };
    const activity = buildActivity({
      tasks: [
        { id: '1', subject: 'A', activeForm: null, status: 'completed' },
        { id: '2', subject: 'B', activeForm: null, status: 'completed' },
        task,
        { id: '4', subject: 'D', activeForm: null, status: 'pending' },
      ],
      currentTask: task,
      completedTaskCount: 2,
    });

    // Act / Assert
    expect(laneStepText(activity)).toBe('Running tests, task 3 of 4');
  });

  it('should fall back to the latest subagent without a current task', () => {
    // Arrange
    const activity = buildActivity({
      subagents: [{ subagentType: 'developer', description: null, spawnDepth: 1, lastTool: 'Bash' }],
    });

    // Act / Assert
    expect(laneStepText(activity)).toBe('developer subagent working');
  });

  it('should not change when only token totals change', () => {
    // Arrange
    const before = buildActivity();
    const after = buildActivity({ tokens: { inputTokens: 5, outputTokens: 9 } });

    // Act / Assert
    expect(laneStepText(after)).toBe(laneStepText(before));
  });
});
