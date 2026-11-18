/** The kind of worker run an issue runs, or runs next, as the server declares it (#1059). */
export type RunKind = 'triage' | 'fresh' | 'continuation' | 'review_fix' | 'checks_fix' | 'conflict_fix' | 'input_resume';

/**
 * The board's lead-tag label for each run kind. A record over the full union, so a kind without a
 * label does not compile; the service builds its set of known kinds from these keys.
 */
export const RUN_KIND_LABELS: Readonly<Record<RunKind, string>> = {
  triage: 'triage',
  fresh: 'implement',
  continuation: 'continue',
  review_fix: 'review fix',
  checks_fix: 'checks fix',
  conflict_fix: 'conflict',
  input_resume: 'resume',
};
