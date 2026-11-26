import {
  DemandModel,
  WorkerRoleCard,
  WorkerRoleCatalog,
  WorkerRoleModelOptions,
} from '../worker-roles/worker-role.service';

export const ROLE_PROMPT_SLOT = '{rolePrompt}';
export const PRINCIPLES_SLOT = '{principles}';
export const DECISIONS_SLOT = '{decisions}';
export const DECISION_RULE =
  'Ask a person only about uncertain business logic.\n\nDecide everything else yourself.';
export const PRINCIPLE_SECTIONS: readonly string[] = [
  'Testing',
  'Design',
  'Code',
  'Domain modelling',
];
export const IMPLEMENT_PRINCIPLES =
  'Engineering principles.\n\nTesting:\n- Tests first.\n\nDesign:\n- Safe defaults.\n\nCode:\n- Explain why.\n\nDomain modelling:\n- Domain terms.';

export function buildWorkerRole(overrides: Partial<WorkerRoleCard> = {}): WorkerRoleCard {
  return {
    name: 'implement',
    purpose: 'Implements the issue and opens a pull request.',
    capability: 'full',
    systemPrompt: 'You are implementing issue #{issueNumber} in {repository}.',
    taskPrompt: 'Implement issue #{issueNumber}.\n\n{issueContent}',
    skills: [
      {
        name: 'tdd',
        description: 'Red-green-refactor loop',
        path: 'skills/tdd/SKILL.md',
        content: '# TDD\nWrite a failing test for {issueNumber} first.',
      },
    ],
    agents: [
      {
        name: 'reviewer',
        description: 'Reviews the diff before pushing',
        path: 'agents/reviewer.md',
        content: '# Reviewer\nReview the diff.',
      },
    ],
    verdictSchema: null,
    model: '',
    demandModels: [],
    principleSections: [...PRINCIPLE_SECTIONS],
    principles: IMPLEMENT_PRINCIPLES,
    decisionRule: DECISION_RULE,
    ...overrides,
  };
}

/** The implement role's per-demand settings as the API returns them: routine first. */
export function buildDemandModels(
  routine: Partial<DemandModel> = {},
  demanding: Partial<DemandModel> = {},
): DemandModel[] {
  return [
    { demand: 'routine', model: '', effort: '', ...routine },
    { demand: 'demanding', model: '', effort: '', ...demanding },
  ];
}

/** The implement role as the API returns it: its model is set per demand, so the role-wide model is empty. */
export function buildImplementRole(
  routine: Partial<DemandModel> = {},
  demanding: Partial<DemandModel> = {},
  overrides: Partial<WorkerRoleCard> = {},
): WorkerRoleCard {
  return buildWorkerRole({
    model: '',
    demandModels: buildDemandModels(routine, demanding),
    ...overrides,
  });
}

export function buildWorkerRoleCatalog(
  overrides: Partial<WorkerRoleCatalog> = {},
): WorkerRoleCatalog {
  return {
    image: {
      tag: 'foundry-worker:local',
      imageId: 'sha256:abc',
      builtAt: '2026-11-01T10:30:00Z',
      commit: '0123456789abcdef0123456789abcdef01234567',
      verified: true,
    },
    roles: [buildWorkerRole()],
    shared: {
      description: 'Loaded for every role',
      skills: [
        {
          name: 'commit-style',
          description: 'Conventional commit messages',
          path: 'shared/skills/commit-style/SKILL.md',
          content: 'Use conventional commits.',
        },
      ],
      agents: [],
    },
    promptSections: [
      {
        key: 'base',
        title: 'Base instructions',
        condition: 'Always',
        content: 'Work on {branchName}.',
      },
      {
        key: 'principles',
        title: 'Worker principles',
        condition: 'Always',
        content: PRINCIPLES_SLOT,
      },
      { key: 'role', title: 'Role prompt', condition: 'Always', content: ROLE_PROMPT_SLOT },
      {
        key: 'dotnet',
        title: '.NET toolchain',
        condition: 'DotnetImage',
        content: 'Use dotnet test.',
      },
      { key: 'docker', title: 'Docker', condition: 'DockerImage', content: 'Docker is available.' },
      { key: 'fresh', title: 'Fresh run', condition: 'Fresh', content: 'Start from main.' },
      { key: 'cont', title: 'Continuation', condition: 'Continuation', content: 'Resume work.' },
      { key: 'rev', title: 'Revision', condition: 'Revision', content: 'Address review.' },
      {
        key: 'rebase',
        title: 'Rebase',
        condition: 'RebaseConflict',
        content: 'Resolve conflicts.',
      },
    ],
    rolePromptSlot: ROLE_PROMPT_SLOT,
    principlesSlot: PRINCIPLES_SLOT,
    decisionsSlot: DECISIONS_SLOT,
    principleSections: [...PRINCIPLE_SECTIONS],
    ...overrides,
  };
}

export function buildModelOptions(
  overrides: Partial<WorkerRoleModelOptions> = {},
): WorkerRoleModelOptions {
  return {
    aliases: ['fable', 'opus', 'sonnet', 'haiku', 'opus[1m]', 'sonnet[1m]'],
    models: [
      { id: 'claude-opus-4-5-20251101', displayName: 'Claude Opus 4.5', deprecated: false },
      { id: 'claude-3-7-sonnet-20250219', displayName: 'Claude Sonnet 3.7', deprecated: true },
    ],
    refreshedAt: '2026-11-06T12:00:00Z',
    refreshError: null,
    ...overrides,
  };
}
