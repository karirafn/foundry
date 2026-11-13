import { Component, WritableSignal, inject, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Location } from '@angular/common';
import { provideLocationMocks } from '@angular/common/testing';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { IssueDetail, IssueSummary } from '../issues/issue.model';
import { IssueService } from '../issues/issue.service';
import { IssueSelection, IssueSelectionScope, provideIssueSelection } from './issue-selection';

function summary(id: string): IssueSummary {
  return {
    id,
    issueNumber: 1,
    title: `Issue ${id}`,
    state: 'in_progress',
    repositorySlug: 'acme/api',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/acme/api/issues/1',
    stage: 'implement',
    needsAction: false,
    awaitsDispatch: false,
    codeStep: null,
  };
}

interface StubIssueService {
  issues: WritableSignal<IssueSummary[]>;
  resolvedIssues: WritableSignal<IssueSummary[]>;
  initialLoading: WritableSignal<boolean>;
  selectedIssueId: WritableSignal<string | null>;
  issueDetail: WritableSignal<IssueDetail | null>;
  detailError: WritableSignal<string | null>;
  detailMissing: WritableSignal<boolean>;
  selectIssue: (id: string | null) => void;
}

function stubIssueService(): StubIssueService {
  const stub: StubIssueService = {
    issues: signal([summary('a'), summary('b')]),
    resolvedIssues: signal([]),
    initialLoading: signal(false),
    selectedIssueId: signal(null),
    issueDetail: signal(null),
    detailError: signal(null),
    detailMissing: signal(false),
    selectIssue: (id) => {
      stub.selectedIssueId.set(id);
      stub.issueDetail.set(null);
      stub.detailError.set(null);
      stub.detailMissing.set(false);
    },
  };
  return stub;
}

@Component({
  template: `
    <h1 tabindex="-1">Page</h1>
    <div data-issue-card="a"><button type="button" class="card-a">A</button></div>
    @if (showB()) {
      <div data-issue-id="b"><button type="button" class="card-b">B</button></div>
    }
  `,
})
class BoardHostComponent {
  readonly selection = inject(IssueSelection);
  readonly showB = signal(true);
}

async function setup(scope: IssueSelectionScope = 'board', url = '/') {
  const stub = stubIssueService();
  TestBed.configureTestingModule({
    providers: [provideRouter([]), provideLocationMocks(), { provide: IssueService, useValue: stub }],
  });
  TestBed.overrideComponent(BoardHostComponent, { set: { providers: provideIssueSelection(scope) } });
  const router = TestBed.inject(Router);
  router.setUpLocationChangeListener();
  await router.navigateByUrl(url);
  const fixture = TestBed.createComponent(BoardHostComponent);
  document.body.appendChild(fixture.nativeElement);
  const render = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    await fixture.whenStable();
  };
  await render();
  const el = fixture.nativeElement as HTMLElement;
  return { fixture, el, stub, router, render, selection: fixture.componentInstance.selection };
}

describe('IssueSelection', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    document.body.innerHTML = '';
  });

  it('selects the opened issue in the service and the URL', async () => {
    // Arrange
    const { el, stub, router, render, selection } = await setup();

    // Act
    selection.open('a', el.querySelector('.card-a') as HTMLElement);
    await render();

    // Assert
    expect(selection.openId()).toBe('a');
    expect(stub.selectedIssueId()).toBe('a');
    expect(router.url).toBe('/?issue=a');
  });

  it('stacks one history entry for two quick opens, so Back closes the pane', async () => {
    // Arrange
    const { el, router, render, selection } = await setup();
    selection.open('a', el.querySelector('.card-a') as HTMLElement);
    selection.open('b', el.querySelector('.card-b') as HTMLElement);
    await render();
    expect(router.url).toBe('/?issue=b');

    // Act
    selection.close();
    await render();

    // Assert
    expect(router.url).toBe('/');
    expect(selection.openId()).toBeNull();
  });

  it('closes a deep-linked pane by removing the parameter, not by going back', async () => {
    // Arrange
    const { render, selection, router, stub } = await setup('board', '/?issue=a');
    expect(selection.openId()).toBe('a');
    const back = vi.spyOn(TestBed.inject(Location), 'back');

    // Act
    selection.close();
    await render();

    // Assert
    expect(back).not.toHaveBeenCalled();
    expect(router.url).toBe('/');
    expect(selection.openId()).toBeNull();
    expect(stub.selectedIssueId()).toBeNull();
  });

  it('waits for the first board load before checking a deep link', async () => {
    // Arrange
    const stubbed = await setup('board', '/');
    stubbed.stub.initialLoading.set(true);
    stubbed.stub.issues.set([]);
    await stubbed.router.navigateByUrl('/?issue=a');
    await stubbed.render();
    expect(stubbed.selection.openId()).toBeNull();
    expect(stubbed.router.url).toBe('/?issue=a');

    // Act
    stubbed.stub.issues.set([summary('a')]);
    stubbed.stub.initialLoading.set(false);
    await stubbed.render();

    // Assert
    expect(stubbed.selection.openId()).toBe('a');
  });

  it('keeps the summary of an open issue that leaves the board', async () => {
    // Arrange
    const { el, stub, render, selection } = await setup();
    selection.open('a', el.querySelector('.card-a') as HTMLElement);
    await render();

    // Act
    stub.issues.set([summary('b')]);
    await render();

    // Assert
    expect(selection.openId()).toBe('a');
    expect(selection.summary()?.id).toBe('a');
  });

  it('returns focus to the card button when the invoker is gone', async () => {
    // Arrange
    const { el, render, selection } = await setup();
    const detached = document.createElement('button');
    selection.open('a', detached);
    await render();

    // Act
    selection.close();
    await render();

    // Assert
    expect(document.activeElement).toBe(el.querySelector('.card-a'));
  });

  it('returns focus to an issue list card by its data-issue-id', async () => {
    // Arrange
    const { render, selection, el } = await setup();
    selection.open('b', document.createElement('button'));
    await render();

    // Act
    selection.close();
    await render();

    // Assert
    expect(document.activeElement).toBe(el.querySelector('.card-b'));
  });

  it('returns focus to the page heading when the card has left the page', async () => {
    // Arrange
    const { fixture, render, selection, el } = await setup();
    selection.open('b', el.querySelector('.card-b') as HTMLElement);
    await render();
    fixture.componentInstance.showB.set(false);
    await render();

    // Act
    selection.close();
    await render();

    // Assert
    expect(document.activeElement).toBe(el.querySelector('h1'));
  });

  describe('history scope', () => {
    it('opens a deep link whose detail fails with a server error, so the detail can offer a retry', async () => {
      // Arrange
      const { stub, render, selection } = await setup('history', '/?issue=a');
      expect(selection.openId()).toBeNull();

      // Act
      stub.detailError.set('Failed to load issue details');
      await render();

      // Assert
      expect(selection.openId()).toBe('a');
    });

    it('rejects a deep link whose detail is not found', async () => {
      // Arrange
      const { stub, render, selection, router } = await setup('history', '/?issue=a');

      // Act
      stub.detailError.set('Failed to load issue details');
      stub.detailMissing.set(true);
      await render();

      // Assert
      expect(selection.openId()).toBeNull();
      expect(stub.selectedIssueId()).toBeNull();
      expect(router.url).toBe('/');
    });
  });
});
