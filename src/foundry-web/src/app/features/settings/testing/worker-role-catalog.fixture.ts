import { WorkerRoleCard, WorkerRoleCatalog } from '../worker-roles/worker-role.service';

export const ROLE_PROMPT_SLOT = '{rolePrompt}';

export function buildWorkerRole(overrides: Partial<WorkerRoleCard> = {}): WorkerRoleCard {
  return {
    name: 'implement',
    purpose: 'Implements the issue and opens a pull request.',
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
    ...overrides,
  };
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
    ...overrides,
  };
}
