import { TestBed } from '@angular/core/testing';
import { WritableSignal, signal } from '@angular/core';
import { vi } from 'vitest';
import { IssueSummary } from '../issues/issue.model';
import { IssueService } from '../issues/issue.service';
import { AccountService } from '../settings/accounts/account.service';
import { RepositoryService } from '../settings/repositories/repository.service';
import { LoadOutcome } from '../../core/models/load-outcome';
import { BoardRepositoriesService } from './board-repositories.service';

function queued(overrides: Partial<IssueSummary> = {}): IssueSummary {
  return {
    id: 'issue-1',
    issueNumber: 1,
    title: 'An issue',
    state: 'queued',
    repositorySlug: 'acme/api',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/acme/api/issues/1',
    repositoryEligibilityStatus: 'eligible',
    stage: 'implement',
    needsAction: false,
    awaitsDispatch: true,
    waitsOnBlockers: false,
    codeStep: null,
    repositoryId: 'repo-1',
    enteredStateAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

interface Harness {
  service: BoardRepositoriesService;
  issues: WritableSignal<IssueSummary[]>;
  loadAll: ReturnType<typeof vi.fn>;
}

function setup(accountsOutcome: LoadOutcome = 'loaded'): Harness {
  const issues = signal<IssueSummary[]>([]);
  const loadAll = vi.fn(() => Promise.resolve<LoadOutcome>('loaded'));
  TestBed.configureTestingModule({
    providers: [
      BoardRepositoriesService,
      { provide: IssueService, useValue: { issues, initialLoading: signal(false) } },
      {
        provide: AccountService,
        useValue: { loadAccounts: () => Promise.resolve(accountsOutcome), accounts: signal([{ id: 'acc-1' }]) },
      },
      { provide: RepositoryService, useValue: { loadAllRepositories: loadAll } },
    ],
  });
  const service = TestBed.inject(BoardRepositoriesService);
  TestBed.tick();
  return { service, issues, loadAll };
}

async function settle(): Promise<void> {
  TestBed.tick();
  await Promise.resolve();
  await Promise.resolve();
}

describe('BoardRepositoriesService', () => {
  it('loads every account repository on reload', async () => {
    // Arrange
    const h = setup();

    // Act
    const outcome = await h.service.reload();

    // Assert
    expect(h.loadAll).toHaveBeenCalledWith(['acc-1']);
    expect(outcome).toBe('loaded');
  });

  it('reloads when a queued issue becomes held by an ineligible repository', async () => {
    // Arrange
    const h = setup();
    h.issues.set([queued()]);
    await settle();

    // Act
    h.issues.set([queued({ repositoryEligibilityStatus: 'ineligible' })]);
    await settle();

    // Assert
    expect(h.loadAll).toHaveBeenCalledTimes(1);
  });

  it('does not reload when nothing held changes', async () => {
    // Arrange
    const h = setup();
    h.issues.set([queued({ repositoryEligibilityStatus: 'ineligible' })]);
    await settle();
    h.loadAll.mockClear();

    // Act
    h.issues.set([queued({ repositoryEligibilityStatus: 'ineligible', title: 'Renamed' })]);
    await settle();

    // Assert
    expect(h.loadAll).not.toHaveBeenCalled();
  });

  it('reports the repositories load outcome', async () => {
    // Arrange
    const h = setup();
    h.loadAll.mockReturnValue(Promise.resolve('failed'));

    // Act
    const outcome = await h.service.reload();

    // Assert
    expect(outcome).toBe('failed');
  });

  it('reports a failed accounts load without loading repositories', async () => {
    // Arrange
    const h = setup('failed');

    // Act
    const outcome = await h.service.reload();

    // Assert
    expect(outcome).toBe('failed');
    expect(h.loadAll).not.toHaveBeenCalled();
  });
});
