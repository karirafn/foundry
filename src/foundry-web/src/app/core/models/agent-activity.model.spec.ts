import { AgentActivity, agentActivitySummary } from './agent-activity.model';

function buildActivity(overrides: Partial<AgentActivity> = {}): AgentActivity {
  return {
    workerRunId: 'run-1',
    issueId: 'issue-1',
    lastEventAt: null,
    tasks: [],
    currentTask: null,
    completedTaskCount: 0,
    subagents: [],
    activeSkill: null,
    inFlightTool: null,
    tokens: { inputTokens: 0, outputTokens: 0, cacheReadInputTokens: 0, cacheCreationInputTokens: 0 },
    ...overrides,
  };
}

const tasks = Array.from({ length: 7 }, (_, i) => ({
  id: String(i + 1),
  subject: `Task ${i + 1}`,
  activeForm: null,
  status: i < 3 ? 'completed' : 'pending',
}));

describe('agentActivitySummary', () => {
  it('should render subagent type, current task activeForm and task progress', () => {
    // Arrange
    const activity = buildActivity({
      tasks,
      completedTaskCount: 3,
      currentTask: { id: '4', subject: 'Run tests', activeForm: 'Running tests', status: 'in_progress' },
      subagents: [{ subagentType: 'developer', description: 'Implement', spawnDepth: 1, lastTool: 'Bash' }],
    });

    // Act / Assert
    expect(agentActivitySummary(activity)).toBe('developer · Running tests (3/7)');
  });

  it('should label the main agent when no subagent is active', () => {
    // Arrange
    const activity = buildActivity({
      tasks,
      completedTaskCount: 3,
      currentTask: { id: '4', subject: 'Run tests', activeForm: null, status: 'in_progress' },
    });

    // Act / Assert
    expect(agentActivitySummary(activity)).toBe('main agent · Run tests (3/7)');
  });

  it('should use the latest subagent when several are active', () => {
    // Arrange
    const activity = buildActivity({
      subagents: [
        { subagentType: 'developer', description: null, spawnDepth: 1, lastTool: null },
        { subagentType: 'Explore', description: null, spawnDepth: 2, lastTool: null },
      ],
    });

    // Act / Assert
    expect(agentActivitySummary(activity)).toBe('Explore');
  });

  it('should show the in-flight tool when there is no task list', () => {
    // Arrange
    const activity = buildActivity({
      subagents: [{ subagentType: 'developer', description: null, spawnDepth: 1, lastTool: null }],
      inFlightTool: { name: 'Bash', summary: 'dotnet test', startedAt: null },
    });

    // Act / Assert
    expect(agentActivitySummary(activity)).toBe('developer · Bash');
  });

  it('should show progress with the in-flight tool when no task is in progress', () => {
    // Arrange
    const activity = buildActivity({
      tasks,
      completedTaskCount: 3,
      inFlightTool: { name: 'Read', summary: null, startedAt: null },
    });

    // Act / Assert
    expect(agentActivitySummary(activity)).toBe('main agent · Read (3/7)');
  });
});
