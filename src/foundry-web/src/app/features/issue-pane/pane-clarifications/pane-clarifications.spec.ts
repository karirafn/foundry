import { TestBed } from '@angular/core/testing';
import { AwaitingInputDetails, Clarification, InputQuestion, IssueDetail } from '../../issues/issue.model';
import { PaneClarificationsComponent } from './pane-clarifications';

const ASKED_AT = '2026-02-01T12:00:00Z';
const question: InputQuestion = { $type: 'legacy', question: 'Which API?', context: 'Two exist.', recommendation: 'The v2 one.' };

function round(overrides: Partial<Clarification> = {}): Clarification {
  return { questions: [question], input: null, askedAt: ASKED_AT, answeredAt: null, ...overrides };
}

function awaiting(overrides: Partial<AwaitingInputDetails> = {}): AwaitingInputDetails {
  return {
    questions: [question],
    postStatus: 'posted',
    postedAt: '2026-02-01T12:05:00Z',
    commentUrl: 'https://github.com/acme/api/issues/42#c1',
    postFailureReason: null,
    nextPostAttemptAt: null,
    resumeTarget: 'revision',
    reminderSentAt: null,
    reminderDueAt: '2026-02-04T12:05:00Z',
    ...overrides,
  };
}

function detail(
  clarifications: Clarification[],
  state: IssueDetail['state'] = 'queued',
  awaitingInput: AwaitingInputDetails | null = null,
): IssueDetail {
  return {
    id: 'abc',
    issueNumber: 42,
    title: 'Add retry',
    state,
    repositorySlug: 'acme/api',
    detectedAt: ASKED_AT,
    url: 'https://github.com/acme/api/issues/42',
    stage: null,
    needsAction: false,
    awaitsDispatch: false,
    waitsOnBlockers: false,
    codeStep: null,
    runKind: null,
    repositoryId: 'repo-1',
    enteredStateAt: ASKED_AT,
    author: 'dev',
    labels: [],
    stateDetails: {
      workerRunId: null,
      branchName: null,
      feedbackCutoffAt: null,
      failureReason: null,
      failedAt: null,
      completedAt: null,
      blockedBy: null,
      violations: null,
      transientRetry: null,
      awaitingInput,
    },
    kind: { source: 'unclassified', kind: null, conflictingTriageKind: null },
    triage: null,
    clarifications,
  };
}

function setup(value: IssueDetail | null) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({ imports: [PaneClarificationsComponent] });
  const fixture = TestBed.createComponent(PaneClarificationsComponent);
  fixture.componentRef.setInput('detail', value);
  fixture.componentRef.setInput('providerType', 'github');
  fixture.detectChanges();
  return { fixture, el: fixture.nativeElement as HTMLElement };
}

function text(node: Element | null | undefined): string {
  return (node?.textContent ?? '').replace(/\s+/g, ' ').trim();
}

function value(el: HTMLElement, key: string): HTMLElement | null {
  const dt = Array.from(el.querySelectorAll('dt')).find((d) => text(d) === key);
  return (dt?.nextElementSibling as HTMLElement | null) ?? null;
}

function status(el: HTMLElement): string {
  return text(el.querySelector('[role="status"]'));
}

describe('PaneClarificationsComponent', () => {
  it('shows no section for an empty log', () => {
    // Act
    const { el } = setup(detail([]));

    // Assert
    expect(el.querySelector('section')).toBeNull();
  });

  it('shows no section while the detail loads', () => {
    // Act
    const { el } = setup(null);

    // Assert
    expect(el.querySelector('section')).toBeNull();
  });

  it('heads the section with a Clarifications heading and each round with its own heading', () => {
    // Arrange
    const answered = round({ input: [{ kind: 'comments', text: 'Use v2.' }], answeredAt: '2026-02-01T14:00:00Z' });

    // Act
    const { el } = setup(detail([answered, round({ askedAt: '2026-02-02T09:00:00Z' })], 'awaiting_input', awaiting()));

    // Assert
    const section = el.querySelector('section')!;
    const heading = el.querySelector(`#${section.getAttribute('aria-labelledby')}`);
    expect(heading?.tagName.toLowerCase()).toBe('h3');
    expect(text(heading)).toBe('Clarifications');
    expect(Array.from(el.querySelectorAll('h4')).map(text)).toEqual(['Round 1', 'Round 2']);
    expect(Array.from(el.querySelectorAll('.pane-clarifications__chip')).map(text)).toEqual(['Answered', 'Open']);
  });

  it('lists the questions with their context and recommendation', () => {
    // Act
    const { el } = setup(detail([round()], 'awaiting_input', awaiting()));

    // Assert
    const list = el.querySelector('.pane-clarifications__questions')!;
    expect(text(el.querySelector(`#${list.getAttribute('aria-labelledby')}`))).toBe('Questions');
    expect(Array.from(list.querySelectorAll('li > span')).map(text)).toEqual(['Which API?', 'Two exist.', 'Recommended: The v2 one.']);
  });

  it('shows a structured question with its problem and lettered recommended option in the existing slots', () => {
    // Arrange
    const structured: InputQuestion = {
      $type: 'structured',
      question: 'Which API?',
      problem: 'Two exist.',
      impact: 'Clients break.',
      options: ['v1', 'v2'],
      recommendedOption: 'b',
      recommendationReason: 'Clients moved.',
      evidence: [],
    };

    // Act
    const { el } = setup(detail([round({ questions: [structured] })], 'awaiting_input', awaiting({ questions: [structured] })));

    // Assert
    const list = el.querySelector('.pane-clarifications__questions')!;
    expect(Array.from(list.querySelectorAll('li > span')).map(text))
      .toEqual(['Which API?', 'Two exist.', 'Recommended: ✅ b) v2: Clients moved.']);
  });

  it('renders comment text as text, never as HTML', () => {
    // Arrange
    const answered = round({
      input: [{ kind: 'comments', text: '<img src=x onerror="alert(1)">Use v2.' }],
      answeredAt: '2026-02-01T14:00:00Z',
    });

    // Act
    const { el } = setup(detail([answered]));

    // Assert
    const comment = el.querySelector('.pane-clarifications__comment')!;
    expect(comment.querySelector('img')).toBeNull();
    expect(comment.textContent).toBe('<img src=x onerror="alert(1)">Use v2.');
  });

  it('collapses a long comment behind a Show more control', () => {
    // Arrange
    const answered = round({ input: [{ kind: 'comments', text: 'line\n'.repeat(10) }], answeredAt: '2026-02-01T14:00:00Z' });
    const { fixture, el } = setup(detail([answered]));
    const comment = el.querySelector('.pane-clarifications__comment')!;
    const button = el.querySelector<HTMLButtonElement>('.pane-clarifications__more')!;
    expect(comment.classList).toContain('pane-clarifications__comment--clamped');
    expect(button.getAttribute('aria-controls')).toBe(comment.id);

    // Act
    button.click();
    fixture.detectChanges();

    // Assert
    expect(comment.classList).not.toContain('pane-clarifications__comment--clamped');
    expect(button.getAttribute('aria-expanded')).toBe('true');
    expect(text(button)).toBe('Show less');
  });

  it('renders a description edit, a branch update linked to the compare view, and a continue', () => {
    // Arrange
    const answered = round({
      answeredAt: '2026-02-01T14:00:00Z',
      input: [
        { kind: 'descriptionEdited' },
        {
          kind: 'branchUpdated',
          fromSha: 'aaaaaaa1111111',
          toSha: 'bbbbbbb2222222',
          compareUrl: 'https://github.com/acme/api/compare/aaaaaaa1111111...bbbbbbb2222222',
        },
        { kind: 'continuedWithoutAnswer' },
      ],
    });

    // Act
    const { el } = setup(detail([answered]));

    // Assert
    const parts = Array.from(el.querySelectorAll('.pane-clarifications__part')).map(text);
    expect(parts).toEqual(['Issue description edited', 'Branch updated aaaaaaa..bbbbbbb', 'Continued without answer']);
    const link = el.querySelector<HTMLAnchorElement>('.pane-clarifications__part a')!;
    expect(link.href).toBe('https://github.com/acme/api/compare/aaaaaaa1111111...bbbbbbb2222222');
    expect(link.getAttribute('aria-label')).toBe('Compare aaaaaaa to bbbbbbb on GitHub');
    expect(link.rel).toBe('noopener noreferrer');
  });

  it('shows the asked and answered times', () => {
    // Arrange
    const answered = round({ input: [{ kind: 'comments', text: 'Use v2.' }], answeredAt: '2026-02-01T14:00:00Z' });

    // Act
    const { el } = setup(detail([answered]));

    // Assert
    const times = Array.from(el.querySelectorAll('.pane-clarifications__meta time')).map((t) => t.getAttribute('datetime'));
    expect(times).toEqual([ASKED_AT, '2026-02-01T14:00:00Z']);
  });

  it('shows the open round\'s post with its link, the time waited and the reminder due time', () => {
    // Act
    const { el } = setup(detail([round()], 'awaiting_input', awaiting()));

    // Assert
    const post = value(el, 'Post')!;
    expect(text(post)).toMatch(/^Posted .+ View question$/);
    expect(post.querySelector('a')?.getAttribute('aria-label')).toBe('View the questions on issue #42');
    expect(text(value(el, 'Waiting'))).toMatch(/^\d+[mhd]$|^just now$/);
    expect(text(value(el, 'Reminder'))).toMatch(/^Due /);
    expect(value(el, 'Reminder')?.querySelector('time')?.getAttribute('datetime')).toBe('2026-02-04T12:05:00Z');
  });

  it('shows a failed post without a link with its reason and the Retry post hint', () => {
    // Arrange
    const details = awaiting({ postStatus: 'failed', postedAt: null, commentUrl: null, postFailureReason: 'Not Found', reminderDueAt: null });

    // Act
    const { el } = setup(detail([round()], 'awaiting_input', details));

    // Assert
    const post = value(el, 'Post')!;
    expect(text(post)).toBe('Failed: Not Found. Use Retry post to post the questions again.');
    expect(post.querySelector('a')).toBeNull();
    expect(text(value(el, 'Reminder'))).toBe('Scheduled once the questions are posted');
  });

  it('announces a round closing, but not the first log it shows', () => {
    // Arrange
    const { fixture, el } = setup(detail([round()], 'awaiting_input', awaiting()));
    expect(status(el)).toBe('');

    // Act
    fixture.componentRef.setInput(
      'detail',
      detail([round({ input: [{ kind: 'comments', text: 'Use v2.' }], answeredAt: '2026-02-02T09:00:00Z' })]),
    );
    fixture.detectChanges();

    // Assert
    expect(status(el)).toBe('Clarification round 1 answered.');
  });

  it('announces a round opening, keeping the round it saw across a reload', () => {
    // Arrange
    const answered = round({ input: [{ kind: 'comments', text: 'Use v2.' }], answeredAt: '2026-02-01T14:00:00Z' });
    const { fixture, el } = setup(detail([answered]));
    fixture.componentRef.setInput('detail', null);
    fixture.detectChanges();

    // Act
    fixture.componentRef.setInput('detail', detail([answered, round({ askedAt: '2026-02-02T09:00:00Z' })], 'awaiting_input', awaiting()));
    fixture.detectChanges();

    // Assert
    expect(status(el)).toBe('Clarification round 2 opened.');
  });
});
