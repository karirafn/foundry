import { AwaitingInputDetails, Clarification, InputQuestion, IssueDetail } from '../../issues/issue.model';
import { buildClarifications, questionView } from './pane-clarifications.model';

const ASKED_AT = '2026-02-01T12:00:00Z';
const NOW = Date.parse('2026-02-03T13:00:00Z');

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

describe('buildClarifications', () => {
  it('returns no rounds for an empty log', () => {
    // Act
    const rounds = buildClarifications(detail([]), 'github', NOW);

    // Assert
    expect(rounds).toEqual([]);
  });

  it('numbers the rounds oldest first, with their questions and timestamps', () => {
    // Arrange
    const first = round({ input: [{ kind: 'comments', text: 'Use v2.' }], answeredAt: '2026-02-01T14:00:00Z' });
    const second = round({ askedAt: '2026-02-02T09:00:00Z', input: [{ kind: 'comments', text: 'Yes.' }], answeredAt: '2026-02-02T10:00:00Z' });

    // Act
    const rounds = buildClarifications(detail([first, second]), 'github', NOW);

    // Assert
    expect(rounds.map((r) => [r.number, r.askedAt, r.answeredAt])).toEqual([
      [1, ASKED_AT, '2026-02-01T14:00:00Z'],
      [2, '2026-02-02T09:00:00Z', '2026-02-02T10:00:00Z'],
    ]);
    expect(rounds[0].questions).toEqual([{ question: 'Which API?', context: 'Two exist.', recommendation: 'The v2 one.' }]);
    expect(rounds[0].status).toBe('answered');
    expect(rounds[0].statusLabel).toBe('Answered');
  });

  it('renders each input part kind', () => {
    // Arrange
    const answered = round({
      answeredAt: '2026-02-01T14:00:00Z',
      input: [
        { kind: 'comments', text: 'Use <b>v2</b>.' },
        { kind: 'descriptionEdited' },
        {
          kind: 'branchUpdated',
          fromSha: 'aaaaaaa1111111',
          toSha: 'bbbbbbb2222222',
          compareUrl: 'https://github.com/acme/api/compare/aaaaaaa1111111...bbbbbbb2222222',
        },
      ],
    });

    // Act
    const [view] = buildClarifications(detail([answered]), 'github', NOW);

    // Assert
    expect(view.input).toEqual([
      { type: 'comments', text: 'Use <b>v2</b>.', long: false },
      { type: 'note', text: 'Issue description edited' },
      {
        type: 'branch',
        label: 'Branch updated',
        shortRange: 'aaaaaaa..bbbbbbb',
        fullRange: 'aaaaaaa1111111..bbbbbbb2222222',
        href: 'https://github.com/acme/api/compare/aaaaaaa1111111...bbbbbbb2222222',
        ariaLabel: 'Compare aaaaaaa to bbbbbbb on GitHub',
      },
    ]);
  });

  it('names a recreated branch by its new head', () => {
    // Arrange
    const answered = round({
      answeredAt: '2026-02-01T14:00:00Z',
      input: [{ kind: 'branchUpdated', fromSha: null, toSha: 'ccccccc3333333', compareUrl: null }],
    });

    // Act
    const [view] = buildClarifications(detail([answered]), 'gitlab', NOW);

    // Assert
    expect(view.input).toEqual([
      {
        type: 'branch',
        label: 'Branch recreated',
        shortRange: 'ccccccc',
        fullRange: 'ccccccc3333333',
        href: null,
        ariaLabel: 'View commit ccccccc on GitLab',
      },
    ]);
  });

  it('marks a round closed by Continue without answer', () => {
    // Arrange
    const continued = round({ answeredAt: '2026-02-01T14:00:00Z', input: [{ kind: 'continuedWithoutAnswer' }] });

    // Act
    const [view] = buildClarifications(detail([continued]), 'github', NOW);

    // Assert
    expect(view.status).toBe('unanswered');
    expect(view.statusLabel).toBe('Continued without answer');
    expect(view.input).toEqual([{ type: 'note', text: 'Continued without answer' }]);
  });

  it.each([
    ['many lines', 'a\nb\nc\nd\ne\nf\ng', true],
    ['many characters', 'x'.repeat(481), true],
    ['a short reply', 'Use v2.', false],
  ])('collapses a comment with %s: %#', (_label, text, long) => {
    // Arrange
    const answered = round({ answeredAt: '2026-02-01T14:00:00Z', input: [{ kind: 'comments', text }] });

    // Act
    const [view] = buildClarifications(detail([answered]), 'github', NOW);

    // Assert
    expect(view.input[0]).toEqual({ type: 'comments', text, long });
  });

  it('leaves out an input part kind this dashboard does not know', () => {
    // Arrange
    const unknownPart = { kind: 'somethingNew' } as unknown as { kind: 'descriptionEdited' };
    const answered = round({ answeredAt: '2026-02-01T14:00:00Z', input: [unknownPart] });

    // Act
    const [view] = buildClarifications(detail([answered]), 'github', NOW);

    // Assert
    expect(view.input).toEqual([]);
  });

  describe('the open round', () => {
    it('shows a posted question with its link, the time waited and when the reminder is due', () => {
      // Act
      const [view] = buildClarifications(detail([round()], 'awaiting_input', awaiting()), 'github', NOW);

      // Assert
      expect(view.status).toBe('open');
      expect(view.statusLabel).toBe('Open');
      expect(view.open).toEqual({
        post: { status: 'posted', postedAt: '2026-02-01T12:05:00Z', commentUrl: 'https://github.com/acme/api/issues/42#c1' },
        waited: '2d',
        reminder: { status: 'due', at: '2026-02-04T12:05:00Z' },
      });
    });

    it('says when the reminder was sent', () => {
      // Arrange
      const details = awaiting({ reminderSentAt: '2026-02-04T12:05:00Z', reminderDueAt: null });

      // Act
      const [view] = buildClarifications(detail([round()], 'awaiting_input', details), 'github', NOW);

      // Assert
      expect(view.open?.reminder).toEqual({ status: 'sent', at: '2026-02-04T12:05:00Z' });
    });

    it('shows no reminder for a posted question whose reminder will not be sent', () => {
      // Arrange
      const details = awaiting({ reminderDueAt: null });

      // Act
      const [view] = buildClarifications(detail([round()], 'awaiting_input', details), 'github', NOW);

      // Assert
      expect(view.open?.reminder).toBeNull();
    });

    it('shows a pending post, with the reminder waiting for it', () => {
      // Arrange
      const details = awaiting({ postStatus: 'pending', postedAt: null, commentUrl: null, reminderDueAt: null });

      // Act
      const [view] = buildClarifications(detail([round()], 'awaiting_input', details), 'github', NOW);

      // Assert
      expect(view.open?.post).toEqual({ status: 'pending' });
      expect(view.open?.reminder).toEqual({ status: 'after_post' });
    });

    it('shows a failed post with its reason and next attempt', () => {
      // Arrange
      const details = awaiting({
        postStatus: 'failed',
        postedAt: null,
        commentUrl: null,
        postFailureReason: 'rate limited',
        nextPostAttemptAt: '2026-02-03T14:00:00Z',
        reminderDueAt: null,
      });

      // Act
      const [view] = buildClarifications(detail([round()], 'awaiting_input', details), 'github', NOW);

      // Assert
      expect(view.open?.post).toEqual({ status: 'failed', reason: 'rate limited', nextAttemptAt: '2026-02-03T14:00:00Z' });
    });

    it('measures the time waited from when the round opened', () => {
      // Act
      const [view] = buildClarifications(
        detail([round()], 'awaiting_input', awaiting()),
        'github',
        Date.parse(ASKED_AT) + 5 * 60_000,
      );

      // Assert
      expect(view.open?.waited).toBe('5m');
    });

    it('shows no post facts once the issue has left awaiting input', () => {
      // Act
      const [view] = buildClarifications(detail([round()], 'queued', awaiting()), 'github', NOW);

      // Assert
      expect(view.open).toBeNull();
    });
  });
});

describe('questionView', () => {
  const structured: InputQuestion = {
    $type: 'structured',
    question: 'Which API?',
    problem: 'Two exist.',
    impact: 'Clients break.',
    options: ['v1', 'v2'],
    recommendedOption: 'b',
    recommendationReason: 'Clients moved.',
    evidence: ['src/api.ts:3 routes v2'],
  };

  it('shows a structured question with its problem and its lettered recommended option', () => {
    // Act
    const view = questionView(structured);

    // Assert
    expect(view).toEqual({ question: 'Which API?', context: 'Two exist.', recommendation: '✅ b) v2: Clients moved.' });
  });

  it('shows a legacy question unchanged', () => {
    // Act
    const view = questionView(question);

    // Assert
    expect(view).toEqual({ question: 'Which API?', context: 'Two exist.', recommendation: 'The v2 one.' });
  });

  it('shows only the question when the recommended letter names no option', () => {
    // Act
    const view = questionView({ ...structured, recommendedOption: 'c' } as InputQuestion);

    // Assert
    expect(view).toEqual({ question: 'Which API?', context: null, recommendation: null });
  });

  it('shows only the question for an unknown type', () => {
    // Act
    const view = questionView({ $type: 'future', question: 'Which API?' } as unknown as InputQuestion);

    // Assert
    expect(view).toEqual({ question: 'Which API?', context: null, recommendation: null });
  });
});
