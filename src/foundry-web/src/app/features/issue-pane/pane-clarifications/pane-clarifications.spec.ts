import { TestBed } from '@angular/core/testing';
import { AwaitingInputDetails, Clarification, ClarificationDecision, ClarificationFeedback, InputQuestion, IssueDetail } from '../../issues/issue.model';
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

  describe('a structured question', () => {
    const structured: InputQuestion = {
      $type: 'structured',
      question: 'Which API?',
      problem: 'Two exist.',
      impact: 'Clients break.',
      options: ['v1', 'v2', 'v3'],
      recommendedOption: 'b',
      recommendationReason: 'Clients moved.',
      evidence: ['src/api.ts:3 routes v2', 'src/client.ts:9 calls v1'],
    };
    const answeredAt = '2026-02-01T14:00:00Z';

    function openRound(q: InputQuestion = structured) {
      return setup(detail([round({ questions: [q] })], 'awaiting_input', awaiting({ questions: [q] })));
    }

    function closedRound(q: InputQuestion = structured) {
      return setup(detail([round({ questions: [q], input: [{ kind: 'comments', text: 'ok' }], answeredAt })]));
    }

    function options(el: Element): string[] {
      return Array.from(el.querySelectorAll('.pane-clarifications__options > li')).map(text);
    }

    it('shows its question, problem, why it matters and every option in letter order while awaiting an answer', () => {
      // Act
      const { el } = openRound();

      // Assert
      const li = el.querySelector('.pane-clarifications__question')!;
      expect(text(li.querySelector('.pane-clarifications__question-text'))).toBe('Which API?');
      expect(Array.from(li.querySelectorAll('.pane-clarifications__secondary')).map(text))
        .toEqual(['Problem: Two exist.', 'Why it matters: Clients break.']);
      expect(options(li)).toEqual(['a) v1', '✅ Recommended: b) v2: Clients moved.', 'c) v3']);
    });

    it.each([
      ['first', ['v1', 'v2'], 'a', 0],
      ['last', ['v1', 'v2', 'v3', 'v4'], 'd', 3],
    ])('marks the recommended option when it is %s, with its reason on the same row', (_, opts, letter, index) => {
      // Act
      const { el } = openRound({ ...structured, options: opts, recommendedOption: letter });

      // Assert
      const rows = Array.from(el.querySelectorAll('.pane-clarifications__options > li'));
      const marked = rows.filter((r) => r.classList.contains('pane-clarifications__option--recommended'));
      expect(marked).toEqual([rows[index]]);
      expect(text(marked[0].querySelector('.sr-only'))).toBe('Recommended:');
      expect(marked[0].querySelector('[aria-hidden="true"]')?.textContent).toContain('✅');
      expect(text(marked[0].querySelector('.pane-clarifications__reason'))).toBe(': Clients moved.');
      expect(rows.map((r) => text(r).replace('✅ Recommended: ', '').slice(0, 2))).toEqual(['a)', 'b)', 'c)', 'd)'].slice(0, opts.length));
    });

    it('collapses its evidence behind a closed Evidence disclosure', () => {
      // Act
      const { el } = openRound();

      // Assert
      const disclosure = el.querySelector<HTMLDetailsElement>('.pane-clarifications__question details')!;
      expect(text(disclosure.querySelector('summary'))).toBe('Evidence (2)');
      expect(disclosure.open).toBe(false);
      expect(Array.from(disclosure.querySelectorAll('.pane-clarifications__evidence li')).map(text))
        .toEqual(['src/api.ts:3 routes v2', 'src/client.ts:9 calls v1']);
    });

    it('shows no Evidence disclosure when it has no evidence', () => {
      // Act
      const { el } = openRound({ ...structured, evidence: [] });

      // Assert
      expect(el.querySelector('.pane-clarifications__question details')).toBeNull();
    });

    it('shows only the question and the recommended option once answered, with the rest in one Details disclosure', () => {
      // Act
      const { el } = closedRound();

      // Assert
      const li = el.querySelector('.pane-clarifications__question')!;
      expect(Array.from(li.querySelectorAll(':scope > span')).map(text)).toEqual(['Which API?', '✅ Recommended: b) v2']);
      const disclosures = li.querySelectorAll<HTMLDetailsElement>('details');
      expect(disclosures.length).toBe(1);
      const details = disclosures[0];
      expect(details.open).toBe(false);
      expect(text(details.querySelector('summary'))).toBe('Details for question 1');
      expect(Array.from(details.querySelectorAll('.pane-clarifications__secondary')).map(text))
        .toEqual(['Problem: Two exist.', 'Why it matters: Clients break.']);
      expect(options(details)).toEqual(['a) v1', '✅ Recommended: b) v2: Clients moved.', 'c) v3']);
      expect(text(details.querySelector('.pane-clarifications__label'))).toBe('Evidence (2)');
      expect(Array.from(details.querySelectorAll('.pane-clarifications__evidence li')).map(text))
        .toEqual(['src/api.ts:3 routes v2', 'src/client.ts:9 calls v1']);
    });

    it('shows no Evidence label inside Details when it has no evidence', () => {
      // Act
      const { el } = closedRound({ ...structured, evidence: [] });

      // Assert
      const details = el.querySelector('.pane-clarifications__question details')!;
      expect(details.querySelector('.pane-clarifications__label')).toBeNull();
      expect(details.querySelector('.pane-clarifications__evidence')).toBeNull();
    });

    it('renders each question of a mixed log in its own markup, legacy unchanged', () => {
      // Act
      const { el } = setup(detail([round({ questions: [question, structured] })], 'awaiting_input', awaiting()));

      // Assert
      const [legacy, structuredLi] = Array.from(el.querySelectorAll('.pane-clarifications__question'));
      expect(Array.from(legacy.querySelectorAll(':scope > span')).map(text))
        .toEqual(['Which API?', 'Two exist.', 'Recommended: The v2 one.']);
      expect(legacy.querySelector('ul, details')).toBeNull();
      expect(options(structuredLi)).toEqual(['a) v1', '✅ Recommended: b) v2: Clients moved.', 'c) v3']);
    });

    it('renders text that looks like HTML or markdown literally', () => {
      // Arrange
      const tricky: InputQuestion = {
        ...structured,
        question: '<b>Which</b> `api`?',
        options: ['<img src=x onerror="alert(1)">', '`v2`'],
        evidence: ['<script>x</script> `file.ts:1`'],
      };

      // Act
      const { el } = openRound(tricky);

      // Assert
      const li = el.querySelector('.pane-clarifications__question')!;
      expect(li.querySelector('b, img, script, code')).toBeNull();
      expect(li.querySelector('.pane-clarifications__question-text')!.textContent).toBe('<b>Which</b> `api`?');
      expect(options(li)).toEqual(['a) <img src=x onerror="alert(1)">', '✅ Recommended: b) `v2`: Clients moved.']);
      expect(li.querySelector('.pane-clarifications__evidence li')!.textContent).toBe('<script>x</script> `file.ts:1`');
    });

    describe('across the minute clock tick', () => {
      beforeEach(() => {
        vi.useFakeTimers();
      });

      afterEach(() => {
        vi.useRealTimers();
      });

      it('keeps an open disclosure open', () => {
        // Arrange
        const { fixture, el } = closedRound();
        const details = el.querySelector<HTMLDetailsElement>('.pane-clarifications__question details')!;
        details.open = true;

        // Act
        vi.advanceTimersByTime(60_000);
        fixture.detectChanges();

        // Assert
        const after = el.querySelector<HTMLDetailsElement>('.pane-clarifications__question details')!;
        expect(after).toBe(details);
        expect(after.open).toBe(true);
      });
    });
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

describe('PaneClarificationsComponent triage decisions', () => {
  const decision: ClarificationDecision = {
    question: 'Which retry cap?',
    decision: 'Three attempts.',
    ground: 'settled_business',
    evidence: ['src/Retry.cs:8 caps at three', 'docs/retry.md:4 says three'],
  };

  function decided(decisions: ClarificationDecision[] = [decision]): Clarification {
    return round({ questions: [], decisions });
  }

  function labels(el: HTMLElement): string[] {
    return Array.from(el.querySelectorAll('.pane-clarifications__label')).map(text);
  }

  it('shows a decided round with its chip, asked time and decisions, and no questions, input or post facts', () => {
    // Act
    const { el } = setup(detail([decided()], 'awaiting_input', awaiting()));

    // Assert
    expect(text(el.querySelector('.pane-clarifications__chip'))).toBe('Triage decided');
    expect(el.querySelector('.pane-clarifications__chip')!.classList).toContain('pane-clarifications__chip--neutral');
    expect(labels(el)).toEqual(['Triage decided']);
    expect(el.querySelector('.pane-clarifications__questions')).toBeNull();
    expect(el.querySelector('.pane-clarifications__facts')).toBeNull();
    expect(text(el.querySelector('.pane-clarifications__meta'))).toMatch(/^Asked /);
  });

  it('shows a mixed round\'s questions, then its input, then what triage also decided', () => {
    // Arrange
    const mixed = round({ decisions: [decision], input: [{ kind: 'comments', text: 'ok' }], answeredAt: '2026-02-01T14:00:00Z' });

    // Act
    const { el } = setup(detail([mixed]));

    // Assert
    expect(labels(el)).toEqual(['Questions', 'Input', 'Triage also decided']);
    const list = el.querySelector('.pane-clarifications__decisions')!;
    expect(text(el.querySelector(`#${list.getAttribute('aria-labelledby')}`))).toBe('Triage also decided');
  });

  it('shows each decision\'s question, ground and full text', () => {
    // Arrange
    const long = `${'Retry three times. '.repeat(50)}End.`;

    // Act
    const { el } = setup(detail([decided([{ ...decision, decision: long }, { ...decision, ground: 'implementation_detail' }])]));

    // Assert
    const items = Array.from(el.querySelectorAll('.pane-clarifications__decision'));
    expect(items.map((li) => text(li.querySelector('.pane-clarifications__question-text')))).toEqual(['Which retry cap?', 'Which retry cap?']);
    expect(items.map((li) => text(li.querySelector('.pane-clarifications__ground'))))
      .toEqual(['Settled by existing behaviour', 'Implementation detail']);
    expect(items[0].querySelector('.pane-clarifications__decision-body')!.textContent).toBe(long);
  });

  it('shows no ground tag for a ground it does not know', () => {
    // Act
    const { el } = setup(detail([decided([{ ...decision, ground: 'future' } as unknown as ClarificationDecision])]));

    // Assert
    expect(el.querySelector('.pane-clarifications__ground')).toBeNull();
  });

  it('collapses a decision\'s evidence behind a closed disclosure naming the decision', () => {
    // Act
    const { el } = setup(detail([decided()]));

    // Assert
    const disclosure = el.querySelector<HTMLDetailsElement>('.pane-clarifications__decision details')!;
    expect(disclosure.open).toBe(false);
    expect(text(disclosure.querySelector('summary'))).toBe('Evidence (2) for decision 1');
    expect(Array.from(disclosure.querySelectorAll('.pane-clarifications__evidence li')).map(text))
      .toEqual(['src/Retry.cs:8 caps at three', 'docs/retry.md:4 says three']);
  });

  it('shows no evidence disclosure for a decision without evidence', () => {
    // Act
    const { el } = setup(detail([decided([{ ...decision, evidence: [] }])]));

    // Assert
    expect(el.querySelector('.pane-clarifications__decision summary')).toBeNull();
  });

  it('renders a pipe table as a captioned table in a scrollable region', () => {
    // Arrange
    const table = '| State | Cap |\n|---|---|\n| queued | 3 |';

    // Act
    const { el } = setup(detail([decided([{ ...decision, decision: `Caps:\n${table}` }])]));

    // Assert
    const body = el.querySelector('.pane-clarifications__decision-body')!;
    const wrap = body.querySelector('.pane-clarifications__table-wrap')!;
    expect(wrap.getAttribute('role')).toBe('region');
    expect(wrap.getAttribute('aria-label')).toBe('Decision 1 table');
    expect(wrap.getAttribute('tabindex')).toBe('0');
    expect(text(wrap.querySelector('caption'))).toBe('Decision 1');
    expect(Array.from(wrap.querySelectorAll('th')).map((th) => [text(th), th.getAttribute('scope')])).toEqual([['State', 'col'], ['Cap', 'col']]);
    expect(Array.from(wrap.querySelectorAll('tbody td')).map(text)).toEqual(['queued', '3']);
    expect(text(body.firstElementChild)).toBe('Caps:');
  });

  it('renders decision text that looks like HTML as text', () => {
    // Arrange
    const tricky = { ...decision, question: '<b>Cap</b>?', decision: '<script>alert(1)</script>\n| <img src=x> | b |\n|---|---|\n| 1 | 2 |' };

    // Act
    const { el } = setup(detail([decided([tricky])]));

    // Assert
    const li = el.querySelector('.pane-clarifications__decision')!;
    expect(li.querySelector('script, img, b')).toBeNull();
    expect(li.querySelector('.pane-clarifications__question-text')!.textContent).toBe('<b>Cap</b>?');
    expect(li.querySelector('.pane-clarifications__decision-body')!.textContent).toContain('<script>alert(1)</script>');
    expect(text(li.querySelector('th'))).toBe('<img src=x>');
  });

  it('announces a decided round', () => {
    // Arrange
    const { fixture, el } = setup(detail([]));

    // Act
    fixture.componentRef.setInput('detail', detail([decided([decision, { ...decision, question: 'Which log level?' }])]));
    fixture.detectChanges();

    // Assert
    expect(status(el)).toBe('Triage decided 2 questions in round 1.');
  });
});

describe('PaneClarificationsComponent review feedback', () => {
  const RECEIVED_AT = '2026-02-02T09:30:00Z';

  function requested(feedback: Partial<ClarificationFeedback> = {}): Clarification {
    return round({
      questions: [],
      askedAt: RECEIVED_AT,
      decisions: [],
      feedback: { comments: [{ body: 'Use a date picker.', path: null, line: null }], omittedCommentCount: 0, ...feedback },
    });
  }

  function labels(el: HTMLElement): string[] {
    return Array.from(el.querySelectorAll('.pane-clarifications__label')).map(text);
  }

  function comments(el: HTMLElement): Element[] {
    return Array.from(el.querySelectorAll('ul[aria-label="Review comments"] > li'));
  }

  it('shows a requested round with its neutral Review feedback chip, received time, and no questions, input or post facts', () => {
    // Act
    const { el } = setup(detail([requested()], 'awaiting_input', awaiting()));

    // Assert
    const chip = el.querySelector('.pane-clarifications__chip')!;
    expect(text(chip)).toBe('Review feedback');
    expect(chip.classList).toContain('pane-clarifications__chip--neutral');
    expect(labels(el)).toEqual([]);
    expect(el.querySelector('.pane-clarifications__questions')).toBeNull();
    expect(el.querySelector('.pane-clarifications__facts')).toBeNull();
    expect(text(el.querySelector('.pane-clarifications__meta'))).toMatch(/^Received /);
    expect(el.querySelector('.pane-clarifications__meta time')!.getAttribute('datetime')).toBe(RECEIVED_AT);
  });

  it('shows each comment with its path and line when it has a path, and the body alone otherwise', () => {
    // Act
    const { el } = setup(detail([requested({
      comments: [
        { body: 'Use a date picker.', path: 'src/form.ts', line: 12 },
        { body: 'Looks good otherwise.', path: null, line: null },
        { body: 'A line without a path.', path: null, line: 4 },
      ],
    })]));

    // Assert
    const items = comments(el);
    expect(items.map((li) => text(li.querySelector('.pane-clarifications__mono')))).toEqual(['src/form.ts:12', '', '']);
    expect(items.map((li) => text(li.querySelector('.pane-clarifications__comment'))))
      .toEqual(['Use a date picker.', 'Looks good otherwise.', 'A line without a path.']);
  });

  it('ends the comments with a note naming how many were omitted', () => {
    // Act
    const { el } = setup(detail([requested({ omittedCommentCount: 4 })]));

    // Assert
    const items = comments(el);
    expect(items.length).toBe(2);
    expect(text(items[1].querySelector('.pane-clarifications__note'))).toBe('4 more comments omitted');
  });

  it('shows no comment list for a round with neither comments nor omitted ones', () => {
    // Act
    const { el } = setup(detail([requested({ comments: [] })]));

    // Assert
    expect(el.querySelector('ul[aria-label="Review comments"]')).toBeNull();
  });

  it('renders a comment and its path that look like HTML as text', () => {
    // Arrange
    const body = '<script>alert(1)</script><img src=x>';

    // Act
    const { el } = setup(detail([requested({ comments: [{ body, path: '<b>src</b>.ts', line: 1 }] })]));

    // Assert
    const li = comments(el)[0];
    expect(li.querySelector('script, img, b')).toBeNull();
    expect(li.querySelector('.pane-clarifications__comment')!.textContent).toBe(body);
    expect(text(li.querySelector('.pane-clarifications__mono'))).toBe('<b>src</b>.ts:1');
  });

  it('expands a long comment on its own, apart from an input comment at the same index', () => {
    // Arrange
    const long = 'line\n'.repeat(10);
    const answered = round({ input: [{ kind: 'comments', text: long }], answeredAt: '2026-02-01T14:00:00Z' });
    const { fixture, el } = setup(detail([answered, requested({ comments: [{ body: long, path: null, line: null }] })]));
    const [inputComment, feedbackComment] = Array.from(el.querySelectorAll('.pane-clarifications__comment'));
    const feedbackButton = comments(el)[0].querySelector<HTMLButtonElement>('.pane-clarifications__more')!;
    expect(feedbackButton.getAttribute('aria-controls')).toBe(feedbackComment.id);

    // Act
    feedbackButton.click();
    fixture.detectChanges();

    // Assert
    expect(feedbackComment.classList).not.toContain('pane-clarifications__comment--clamped');
    expect(feedbackButton.getAttribute('aria-expanded')).toBe('true');
    expect(inputComment.classList).toContain('pane-clarifications__comment--clamped');
  });

  it('expands one long review comment without the others', () => {
    // Arrange
    const long = 'line\n'.repeat(10);
    const { fixture, el } = setup(detail([requested({ comments: [{ body: long, path: null, line: null }, { body: long, path: null, line: null }] })]));
    const [first, second] = Array.from(el.querySelectorAll('.pane-clarifications__comment'));

    // Act
    el.querySelectorAll<HTMLButtonElement>('.pane-clarifications__more')[1].click();
    fixture.detectChanges();

    // Assert
    expect(first.classList).toContain('pane-clarifications__comment--clamped');
    expect(second.classList).not.toContain('pane-clarifications__comment--clamped');
    expect(first.id).not.toBe(second.id);
  });

  it('announces a requested round with its number', () => {
    // Arrange
    const answered = round({ input: [{ kind: 'comments', text: 'ok' }], answeredAt: '2026-02-01T14:00:00Z' });
    const { fixture, el } = setup(detail([answered]));

    // Act
    fixture.componentRef.setInput('detail', detail([answered, requested()]));
    fixture.detectChanges();

    // Assert
    expect(status(el)).toBe('Round 2: review feedback received.');
  });
});
