import { TestBed } from '@angular/core/testing';
import { WritableSignal, signal } from '@angular/core';
import { vi } from 'vitest';
import { ConnectionStatus, IssueSignalRService } from '../../core/services/issue-signalr.service';
import { IssueSummary } from '../issues/issue.model';
import { IssueService } from '../issues/issue.service';
import { AccountService } from '../settings/accounts/account.service';
import { RepositoryService } from '../settings/repositories/repository.service';
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
  status: WritableSignal<ConnectionStatus>;
  loadAll: ReturnType<typeof vi.fn>;
}

function setup(): Harness {
  const issues = signal<IssueSummary[]>([]);
  const status = signal<ConnectionStatus>('connected');
  const loadAll = vi.fn();
  TestBed.configureTestingModule({
    providers: [
      BoardRepositoriesService,
      { provide: IssueService, useValue: { issues, initialLoading: signal(false) } },
      { provide: IssueSignalRService, useValue: { connectionStatus: status } },
      {
        provide: AccountService,
        useValue: { loadAccounts: () => Promise.resolve(), accounts: signal([{ id: 'acc-1' }]) },
      },
      { provide: RepositoryService, useValue: { loadAllRepositories: loadAll } },
    ],
  });
  const service = TestBed.inject(BoardRepositoriesService);
  TestBed.tick();
  return { service, issues, status, loadAll };
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
    h.service.reload();
    await settle();

    // Assert
    expect(h.loadAll).toHaveBeenCalledWith(['acc-1']);
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

  it('reloads after a reconnect', async () => {
    // Arrange
    const h = setup();
    h.status.set('reconnecting');
    await settle();

    // Act
    h.status.set('connected');
    await settle();

    // Assert
    expect(h.loadAll).toHaveBeenCalledTimes(1);
  });
});
