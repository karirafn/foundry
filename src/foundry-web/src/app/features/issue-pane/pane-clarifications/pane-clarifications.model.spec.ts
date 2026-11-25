import { AwaitingInputDetails, Clarification, ClarificationDecision, ClarificationFeedback, InputQuestion, IssueDetail } from '../../issues/issue.model';
import { buildClarifications, decisionBlocks, questionView } from './pane-clarifications.model';

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
    runKind: null,
    repositoryId: 'repo-1',
    enteredStateAt: ASKED_AT,
    round: 1,
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
    expect(rounds[0].questions).toEqual([{ kind: 'legacy', question: 'Which API?', context: 'Two exist.', recommendation: 'The v2 one.' }]);
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

  it('maps a structured question, lettering its options and flagging only the recommended one', () => {
    // Act
    const view = questionView({ ...structured, options: ['v1', 'v2', 'v3'] });

    // Assert
    expect(view).toEqual({
      kind: 'structured',
      question: 'Which API?',
      problem: 'Two exist.',
      impact: 'Clients break.',
      options: [
        { letter: 'a', text: 'v1', recommended: false },
        { letter: 'b', text: 'v2', recommended: true },
        { letter: 'c', text: 'v3', recommended: false },
      ],
      recommended: { letter: 'b', text: 'v2', recommended: true },
      reason: 'Clients moved.',
      evidence: ['src/api.ts:3 routes v2'],
    });
  });

  it('shows a legacy question unchanged', () => {
    // Act
    const view = questionView(question);

    // Assert
    expect(view).toEqual({ kind: 'legacy', question: 'Which API?', context: 'Two exist.', recommendation: 'The v2 one.' });
  });

  it('maps a recommended letter outside the options to the question-only legacy view', () => {
    // Act
    const view = questionView({ ...structured, recommendedOption: 'c' } as InputQuestion);

    // Assert
    expect(view).toEqual({ kind: 'legacy', question: 'Which API?', context: null, recommendation: null });
  });

  it('maps an unknown type to the question-only legacy view', () => {
    // Act
    const view = questionView({ $type: 'future', question: 'Which API?' } as unknown as InputQuestion);

    // Assert
    expect(view).toEqual({ kind: 'legacy', question: 'Which API?', context: null, recommendation: null });
  });
});

describe('triage decisions', () => {
  const decision: ClarificationDecision = {
    question: 'Which retry cap?',
    decision: 'Three attempts.',
    ground: 'settled_business',
    evidence: ['src/Retry.cs:8 caps at three'],
  };

  function decided(overrides: Partial<Clarification> = {}): Clarification {
    return round({ questions: [], decisions: [decision], ...overrides });
  }

  it('marks a round with decisions and no questions decided, even though it has no input', () => {
    // Act
    const [view] = buildClarifications(detail([decided()], 'awaiting_input', awaiting()), 'github', NOW);

    // Assert
    expect(view.status).toBe('decided');
    expect(view.statusLabel).toBe('Triage decided');
    expect(view.open).toBeNull();
  });

  it.each([
    ['open', null, null],
    ['answered', [{ kind: 'comments', text: 'ok' }], '2026-02-01T14:00:00Z'],
    ['unanswered', [{ kind: 'continuedWithoutAnswer' }], '2026-02-01T14:00:00Z'],
  ] as const)('keeps a round with questions and decisions %s', (expected, input, answeredAt) => {
    // Act
    const [view] = buildClarifications(
      detail([round({ decisions: [decision], input: input === null ? null : [...input], answeredAt })]),
      'github',
      NOW,
    );

    // Assert
    expect(view.status).toBe(expected);
  });

  it('gives a round from a server without decisions an empty decision list', () => {
    // Act
    const [view] = buildClarifications(detail([round()]), 'github', NOW);

    // Assert
    expect(view.decisions).toEqual([]);
  });

  it('maps each decision with its ground label, blocks and evidence', () => {
    // Act
    const [view] = buildClarifications(detail([decided()]), 'github', NOW);

    // Assert
    expect(view.decisions).toEqual([
      {
        question: 'Which retry cap?',
        ground: 'settled_business',
        groundLabel: 'Settled by existing behaviour',
        blocks: [{ type: 'lines', text: 'Three attempts.' }],
        evidence: ['src/Retry.cs:8 caps at three'],
      },
    ]);
  });

  it('labels an implementation-detail decision', () => {
    // Act
    const [view] = buildClarifications(detail([decided({ decisions: [{ ...decision, ground: 'implementation_detail' }] })]), 'github', NOW);

    // Assert
    expect(view.decisions[0].groundLabel).toBe('Implementation detail');
  });

  it('omits the label of a ground this dashboard does not know', () => {
    // Arrange
    const future = { ...decision, ground: 'future_ground' } as unknown as ClarificationDecision;

    // Act
    const [view] = buildClarifications(detail([decided({ decisions: [future] })]), 'github', NOW);

    // Assert
    expect(view.decisions[0].groundLabel).toBeNull();
  });
});

describe('decisionBlocks', () => {
  it('turns consecutive pipe lines with a separator row into a table without the separator', () => {
    // Act
    const blocks = decisionBlocks('| State | Cap |\n|---|:---:|\n| queued | 3 |\n| failed | 0 |');

    // Assert
    expect(blocks).toEqual([{ type: 'table', header: ['State', 'Cap'], rows: [['queued', '3'], ['failed', '0']] }]);
  });

  it('keeps a table whose rows disagree on their column count as lines', () => {
    // Arrange
    const text = '| State | Cap |\n|---|---|\n| queued | 3 | extra |';

    // Act
    const blocks = decisionBlocks(text);

    // Assert
    expect(blocks).toEqual([{ type: 'lines', text }]);
  });

  it('keeps pipe lines without a separator row as lines', () => {
    // Arrange
    const text = '| State | Cap |\n| queued | 3 |';

    // Act
    const blocks = decisionBlocks(text);

    // Assert
    expect(blocks).toEqual([{ type: 'lines', text }]);
  });

  it('keeps plain text as one block of lines', () => {
    // Act
    const blocks = decisionBlocks('Three attempts.\nThen fail.');

    // Assert
    expect(blocks).toEqual([{ type: 'lines', text: 'Three attempts.\nThen fail.' }]);
  });

  it('splits text around a table into lines, table, lines', () => {
    // Act
    const blocks = decisionBlocks('Caps per state:\n| State | Cap |\n| --- | --- |\n| queued | 3 |\nOthers stay uncapped.');

    // Assert
    expect(blocks).toEqual([
      { type: 'lines', text: 'Caps per state:' },
      { type: 'table', header: ['State', 'Cap'], rows: [['queued', '3']] },
      { type: 'lines', text: 'Others stay uncapped.' },
    ]);
  });
});

describe('review feedback', () => {
  const RECEIVED_AT = '2026-02-02T09:30:00Z';

  function requested(feedback: Partial<ClarificationFeedback> = {}): Clarification {
    return round({
      questions: [],
      askedAt: RECEIVED_AT,
      decisions: [],
      feedback: { comments: [{ body: 'Use a date picker.', path: null, line: null }], omittedCommentCount: 0, ...feedback },
    });
  }

  it('marks a round with feedback requested, even though it has no input', () => {
    // Act
    const [view] = buildClarifications(detail([requested()], 'awaiting_input', awaiting()), 'github', NOW);

    // Assert
    expect(view.status).toBe('requested');
    expect(view.statusLabel).toBe('Review feedback');
    expect(view.kind).toBe('requested');
    expect(view.open).toBeNull();
  });

  it('shows a requested round received when its newest comment arrived, with no questions or input', () => {
    // Act
    const [view] = buildClarifications(detail([requested()]), 'github', NOW);

    // Assert
    expect(view.askedAt).toBe(RECEIVED_AT);
    expect(view.answeredAt).toBeNull();
    expect(view.questions).toEqual([]);
    expect(view.input).toEqual([]);
  });

  it.each([
    ['open', null, null],
    ['answered', [{ kind: 'comments', text: 'ok' }], '2026-02-01T14:00:00Z'],
    ['unanswered', [{ kind: 'continuedWithoutAnswer' }], '2026-02-01T14:00:00Z'],
  ] as const)('keeps an asked round %s with no feedback', (expected, input, answeredAt) => {
    // Act
    const [view] = buildClarifications(
      detail([round({ input: input === null ? null : [...input], answeredAt })]),
      'github',
      NOW,
    );

    // Assert
    expect(view.status).toBe(expected);
    expect(view.kind).toBe('asked');
    expect(view.feedback).toEqual([]);
  });

  it('keeps a decided round decided when the server sends a null feedback', () => {
    // Act
    const [view] = buildClarifications(
      detail([round({ questions: [], decisions: [{ question: 'q', decision: 'd', ground: 'settled_business', evidence: [] }], feedback: null })]),
      'github',
      NOW,
    );

    // Assert
    expect(view.status).toBe('decided');
  });

  it('locates a comment by its path and line, or its path alone', () => {
    // Act
    const [view] = buildClarifications(
      detail([requested({
        comments: [
          { body: 'Inline', path: 'src/Form.ts', line: 12 },
          { body: 'File-level', path: 'src/Form.ts', line: null },
        ],
      })]),
      'github',
      NOW,
    );

    // Assert
    expect(view.feedback).toEqual([
      { type: 'comment', location: 'src/Form.ts:12', text: 'Inline', long: false },
      { type: 'comment', location: 'src/Form.ts', text: 'File-level', long: false },
    ]);
  });

  it.each([
    ['no path or line', null, null],
    ['a line but no path', null, 7],
  ] as const)('shows a comment with %s without a location', (_case, path, line) => {
    // Act
    const [view] = buildClarifications(detail([requested({ comments: [{ body: 'General', path, line }] })]), 'github', NOW);

    // Assert
    expect(view.feedback).toEqual([{ type: 'comment', location: null, text: 'General', long: false }]);
  });

  it('marks a long comment', () => {
    // Act
    const [view] = buildClarifications(
      detail([requested({ comments: [{ body: 'x'.repeat(481), path: null, line: null }] })]),
      'github',
      NOW,
    );

    // Assert
    expect(view.feedback[0]).toMatchObject({ type: 'comment', long: true });
  });

  it.each([
    [1, '1 more comment omitted'],
    [3, '3 more comments omitted'],
  ])('ends with a note when %i comments were omitted', (omitted, text) => {
    // Act
    const [view] = buildClarifications(detail([requested({ omittedCommentCount: omitted })]), 'github', NOW);

    // Assert
    expect(view.feedback.at(-1)).toEqual({ type: 'note', text });
  });

  it('adds no note when no comment was omitted', () => {
    // Act
    const [view] = buildClarifications(detail([requested()]), 'github', NOW);

    // Assert
    expect(view.feedback.map((item) => item.type)).toEqual(['comment']);
  });

  it('shows only the note for a round whose comments were all omitted', () => {
    // Act
    const [view] = buildClarifications(detail([requested({ comments: [], omittedCommentCount: 2 })]), 'github', NOW);

    // Assert
    expect(view.feedback).toEqual([{ type: 'note', text: '2 more comments omitted' }]);
  });
});
