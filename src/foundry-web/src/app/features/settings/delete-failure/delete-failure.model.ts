import { HttpErrorResponse } from '@angular/common/http';
import { TimeoutError } from 'rxjs';
import { extractErrorMessage } from '../../../core/http/extract-error-message';
import { IssueState } from '../../../shared/utils/issue-state';

export type BlockingIssueState = Extract<
  IssueState,
  'triage_in_progress' | 'in_progress' | 'revision_in_progress' | 'review_in_progress'
>;

export interface BlockingIssue {
  readonly repositorySlug: string;
  readonly issueNumber: number;
  readonly state: BlockingIssueState;
}

export type DeleteFailure =
  | { readonly kind: 'refused'; readonly blockingIssues: readonly [BlockingIssue, ...BlockingIssue[]] }
  | { readonly kind: 'failed'; readonly message: string };

const BLOCKING_STATES: readonly BlockingIssueState[] = [
  'triage_in_progress',
  'in_progress',
  'revision_in_progress',
  'review_in_progress',
];

/**
 * A 409 whose every `blockingIssues` entry parses is a refusal; anything else falls back to the server's
 * `detail`, so a malformed or unknown entry never hides what the server said.
 */
export function deleteFailureFrom(err: HttpErrorResponse | TimeoutError): DeleteFailure {
  const blockingIssues = err instanceof HttpErrorResponse && err.status === 409
    ? parseBlockingIssues(err.error)
    : null;

  if (blockingIssues) {
    return { kind: 'refused', blockingIssues };
  }

  return { kind: 'failed', message: extractErrorMessage(err) ?? err.message };
}

function parseBlockingIssues(body: unknown): [BlockingIssue, ...BlockingIssue[]] | null {
  if (body === null || typeof body !== 'object') {
    return null;
  }

  const entries: unknown = (body as { blockingIssues?: unknown }).blockingIssues;
  if (!Array.isArray(entries) || entries.length === 0) {
    return null;
  }

  const parsed = entries.map(parseBlockingIssue);
  if (parsed.some(issue => issue === null)) {
    return null;
  }

  return parsed as [BlockingIssue, ...BlockingIssue[]];
}

function parseBlockingIssue(entry: unknown): BlockingIssue | null {
  if (entry === null || typeof entry !== 'object') {
    return null;
  }

  const { repositorySlug, issueNumber, state } = entry as Record<string, unknown>;
  if (
    typeof repositorySlug !== 'string' ||
    typeof issueNumber !== 'number' ||
    !BLOCKING_STATES.includes(state as BlockingIssueState)
  ) {
    return null;
  }

  return { repositorySlug, issueNumber, state: state as BlockingIssueState };
}
