import { Component, WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Subject, of } from 'rxjs';
import { vi } from 'vitest';
import { WorkerSignalRService } from '../../../../core/services/worker-signalr.service';
import { IssueSummary } from '../../../issues/issue.model';
import { IssueService } from '../../../issues/issue.service';
import { ItemView } from '../../board-state.service';
import { NeedsItem, buildBoardBands } from '../../board.model';
import { LifecycleRowComponent } from '../lifecycle-row';
import { RowActionComponent } from './row-action';

function issue(id: string, overrides: Partial<IssueSummary>): IssueSummary {
  return {
    id,
    issueNumber: 873,
    title: 'GraphQL reads record rate limits',
    state: 'failed',
    repositorySlug: 'acme/foundry',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/acme/foundry/issues/873',
    providerType: 'github',
    stage: 'implement',
    needsAction: true,
    awaitsDispatch: false,
    waitsOnBlockers: false,
    runKind: null,
    repositoryId: 'repo-1',
    enteredStateAt: '2026-01-01T00:00:00Z',
    round: 1,
    ...overrides,
  };
}

function view(summary: IssueSummary): ItemView<NeedsItem> {
  const item = buildBoardBands([summary], [], []).needs[0];
  return { ...item, phase: 'live', arrived: false, moved: false };
}

@Component({
  selector: 'fd-row-action-host',
  imports: [LifecycleRowComponent, RowActionComponent],
  template: `
    @for (item of items(); track item.issue.id) {
      <fd-lifecycle-row [item]="item" [now]="0" (open)="rowOpens = rowOpens + 1">
        <fd-row-action rowAction [item]="item" (open)="reviewInvokers.push($event)" />
      </fd-lifecycle-row>
    }
  `,
})
class HostComponent {
  readonly items: WritableSignal<readonly ItemView<NeedsItem>[]> = signal([]);
  rowOpens = 0;
  readonly reviewInvokers: HTMLElement[] = [];
}

function fakeIssueService() {
  return {
    retryIssue: vi.fn((_id: string) => of<string | null>(null)),
    retryTriage: vi.fn((_id: string) => of<string | null>(null)),
    retryReview: vi.fn((_id: string) => of<string | null>(null)),
    retryQuestionPost: vi.fn((_id: string) => of<string | null>(null)),
    fetchDetail: vi.fn((_id: string) => of<{ stateDetails: { workerRunId: string } } | null>(null)),
  };
}

interface Harness {
  fixture: ComponentFixture<HostComponent>;
  el: HTMLElement;
  issues: ReturnType<typeof fakeIssueService>;
  http: HttpTestingController;
}

function render(...summaries: IssueSummary[]): Harness {
  const issues = fakeIssueService();
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: IssueService, useValue: issues },
      {
        provide: WorkerSignalRService,
        useValue: { activeAgentCount: signal(0), agentActivityForIssue: () => null },
      },
    ],
  });
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentInstance.items.set(summaries.map(view));
  document.body.appendChild(fixture.nativeElement);
  fixture.detectChanges();
  return { fixture, el: fixture.nativeElement, issues, http: TestBed.inject(HttpTestingController) };
}

function control(el: Element): HTMLElement | null {
  return el.querySelector('.row-action__btn');
}

function openDialog(): HTMLDialogElement | null {
  return Array.from(document.querySelectorAll<HTMLDialogElement>('dialog.confirm-dialog')).find((d) => d.open) ?? null;
}

describe('RowActionComponent', () => {
  afterEach(() => document.body.replaceChildren());

  it('opens the posted question comment in a new tab to answer it', () => {
    // Arrange
    const comment = 'https://github.com/acme/foundry/issues/873#issuecomment-5';

    // Act
    const { el } = render(
      issue('a', { state: 'awaiting_input', questionPostStatus: 'posted', questionCommentUrl: comment }),
    );

    // Assert
    const link = control(el) as HTMLAnchorElement;
    expect(link.tagName).toBe('A');
    expect(link.getAttribute('href')).toBe(comment);
    expect(link.getAttribute('target')).toBe('_blank');
    expect(link.getAttribute('rel')).toBe('noopener noreferrer');
    expect(link.textContent?.trim()).toBe('Answer ↗');
    expect(link.getAttribute('aria-label')).toBe('Answer questions for issue #873 (opens GitHub in a new tab)');
  });

  it('opens the issue to answer when the comment URL is unknown', () => {
    // Act
    const { el } = render(issue('a', { state: 'awaiting_input', questionPostStatus: 'posted', questionCommentUrl: null }));

    // Assert
    expect(control(el)?.getAttribute('href')).toBe('https://github.com/acme/foundry/issues/873');
  });

  it('shows no button while the questions are posted, and says so', () => {
    // Act
    const { el } = render(issue('a', { state: 'awaiting_input', questionPostStatus: 'pending' }));

    // Assert
    expect(control(el)).toBeNull();
    expect(el.querySelector('.lifecycle-row__why')?.textContent).toBe('Posting questions…');
  });

  it('retries a failed question post', () => {
    // Arrange
    const { el, fixture, issues } = render(issue('a', { state: 'awaiting_input', questionPostStatus: 'failed' }));
    const button = control(el)!;

    // Act
    button.click();
    fixture.detectChanges();

    // Assert
    expect(button.textContent?.trim()).toBe('Retry post');
    expect(issues.retryQuestionPost).toHaveBeenCalledWith('a');
  });

  it('opens the checks of a pull request whose checks failed', () => {
    // Act
    const { el } = render(
      issue('a', {
        state: 'review',
        stage: 'review',
        checksOutcome: 'still_failing',
        pullRequestUrl: 'https://github.com/acme/foundry/pull/9',
      }),
    );

    // Assert
    expect(control(el)?.getAttribute('href')).toBe('https://github.com/acme/foundry/pull/9/checks');
    expect(control(el)?.textContent?.trim()).toBe('Open checks ↗');
  });

  it('shows no button for a link that fails the safe-href check', () => {
    // Act
    const { el } = render(
      issue('a', { state: 'review', stage: 'review', pullRequestUrl: 'javascript:alert(1)' }),
    );

    // Assert
    expect(control(el)).toBeNull();
  });

  it('retries publishing once confirmed, with the run of the loaded detail', async () => {
    // Arrange
    const { el, fixture, issues, http } = render(issue('a', { state: 'publish_failed' }));
    issues.fetchDetail.mockReturnValue(of({ stateDetails: { workerRunId: 'run-42' } }));
    control(el)!.click();
    const dialog = openDialog();

    // Act
    (dialog?.querySelector('.confirm-dialog__confirm') as HTMLButtonElement).click();
    await Promise.resolve();
    fixture.detectChanges();

    // Assert
    expect(dialog?.querySelector('.confirm-dialog__title')?.textContent).toBe('Retry publishing?');
    expect(dialog?.querySelector('.confirm-dialog__cancel')?.textContent?.trim()).toBe('Not now');
    const post = http.expectOne('/api/workers/runs/run-42/publish-retry');
    expect(post.request.method).toBe('POST');
    expect(control(el)?.textContent?.trim()).toBe('Retrying publish…');
    post.flush(null);
  });

  it('never opens the pane when an action button is activated', () => {
    // Arrange
    const { el, fixture } = render(issue('a', { state: 'failed' }));

    // Act
    control(el)!.click();
    fixture.detectChanges();

    // Assert
    expect(fixture.componentInstance.rowOpens).toBe(0);
    expect(fixture.componentInstance.reviewInvokers).toEqual([]);
  });

  it('opens the pane from Review, with the button as its invoker', () => {
    // Arrange
    const { el, fixture } = render(issue('a', { state: 'publish_blocked' }));
    const button = control(el)!;

    // Act
    button.click();

    // Assert
    expect(button.getAttribute('aria-haspopup')).toBe('dialog');
    expect(fixture.componentInstance.reviewInvokers).toEqual([button]);
    expect(fixture.componentInstance.rowOpens).toBe(0);
  });

  it('shows the pending label and busy state on the acting row only', () => {
    // Arrange
    const { el, fixture, issues } = render(issue('a', { state: 'failed' }), issue('b', { state: 'failed', issueNumber: 874 }));
    issues.retryIssue.mockReturnValue(new Subject<string | null>());
    const [first, second] = Array.from(el.querySelectorAll<HTMLButtonElement>('.row-action__btn'));

    // Act
    first.click();
    fixture.detectChanges();

    // Assert
    expect([first.textContent?.trim(), first.getAttribute('aria-busy'), first.getAttribute('aria-disabled')]).toEqual([
      'Retrying…',
      'true',
      'true',
    ]);
    expect(first.getAttribute('aria-label')).toBe('Retrying… issue #873');
    expect([second.textContent?.trim(), second.getAttribute('aria-busy'), second.getAttribute('aria-disabled')]).toEqual([
      'Retry',
      null,
      null,
    ]);
  });

  it('ignores clicks while its action is pending', () => {
    // Arrange
    const { el, fixture, issues } = render(issue('a', { state: 'failed' }));
    issues.retryIssue.mockReturnValue(new Subject<string | null>());
    const button = control(el)!;
    button.click();
    fixture.detectChanges();

    // Act
    button.click();

    // Assert
    expect(issues.retryIssue).toHaveBeenCalledTimes(1);
  });

  it("shows the service's error under the acting row as an alert", () => {
    // Arrange
    const { el, fixture, issues } = render(issue('a', { state: 'failed' }), issue('b', { state: 'failed', issueNumber: 874 }));
    issues.retryIssue.mockReturnValue(of('Failed to retry issue.'));
    const rows = Array.from(el.querySelectorAll('fd-lifecycle-row'));

    // Act
    control(rows[0])!.click();
    fixture.detectChanges();

    // Assert
    const alerts = rows.map((row) => row.querySelector('[role="alert"]')?.textContent);
    expect(alerts).toEqual(['Failed to retry issue.', '']);
    expect(control(rows[0])?.textContent?.trim()).toBe('Retry');
  });
});
