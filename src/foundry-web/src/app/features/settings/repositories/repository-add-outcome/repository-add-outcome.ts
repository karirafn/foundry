import { ChangeDetectionStrategy, Component, InputSignal, Signal, computed, input } from '@angular/core';
import {
  BranchRulesOutcome,
  CreateRepositoryResponse,
  EligibilityViolation,
  RepositoryEligibility,
  eligibilityStatusLabel,
} from '../repository.model';

export type AddOutcomeTone = 'pass' | 'fail' | 'neutral';

export interface AddOutcomeRow {
  key: 'access' | 'branch-rules' | 'eligibility';
  label: string;
  tone: AddOutcomeTone;
  status: string;
}

/** The default-branch rules eligibility requires — what Foundry applies when allowed. */
export const REQUIRED_BRANCH_RULES: readonly string[] = [
  'Reject direct pushes (require a pull request)',
  'Block force pushes',
  'Block deletion of the default branch',
];

const ACCESS_RULE_PREFIXES: readonly string[] = ['cannot-push:', 'no-credential:'];
const CREDENTIAL_UNREADABLE_RULE = 'credential-unreadable';

export function isAccessViolation(violation: EligibilityViolation): boolean {
  return violation.rule === CREDENTIAL_UNREADABLE_RULE
    || ACCESS_RULE_PREFIXES.some(prefix => violation.rule.startsWith(prefix));
}

export function accessViolations(eligibility: RepositoryEligibility | null): EligibilityViolation[] {
  return eligibility?.violations.filter(isAccessViolation) ?? [];
}

function accessRow(eligibility: RepositoryEligibility | null): AddOutcomeRow {
  const base = { key: 'access' as const, label: 'Access' };
  if (eligibility === null) {
    return { ...base, tone: 'neutral', status: 'Not checked' };
  }
  if (accessViolations(eligibility).length > 0) {
    return { ...base, tone: 'fail', status: 'No push access' };
  }
  if (eligibility.status === 'unreachable') {
    return { ...base, tone: 'neutral', status: 'Unverified' };
  }
  return { ...base, tone: 'pass', status: 'Confirmed' };
}

function branchRulesRow(outcome: BranchRulesOutcome): AddOutcomeRow {
  const base = { key: 'branch-rules' as const, label: 'Branch rules' };
  switch (outcome.kind) {
    case 'applied':
      return { ...base, tone: 'pass', status: 'Applied' };
    case 'already-present':
      return { ...base, tone: 'pass', status: 'Already in place' };
    case 'not-requested':
      return { ...base, tone: 'neutral', status: 'Not requested' };
    case 'skipped':
      return { ...base, tone: 'neutral', status: 'Skipped' };
    case 'failed':
      return { ...base, tone: 'fail', status: 'Failed' };
    default:
      return { ...base, tone: 'neutral', status: String(outcome.kind) };
  }
}

function eligibilityRow(eligibility: RepositoryEligibility | null): AddOutcomeRow {
  const base = { key: 'eligibility' as const, label: 'Eligibility' };
  if (eligibility === null) {
    return { ...base, tone: 'neutral', status: 'Not checked' };
  }
  const tone: AddOutcomeTone = eligibility.status === 'eligible'
    ? 'pass'
    : eligibility.status === 'ineligible' ? 'fail' : 'neutral';
  return { ...base, tone, status: eligibilityStatusLabel(eligibility.status) };
}

/** Plain-language explanation for a branch-rules outcome that was not `applied`. */
export function branchRulesExplanation(outcome: BranchRulesOutcome): string | null {
  switch (outcome.kind) {
    case 'applied':
      return null;
    case 'already-present':
      return 'The rules eligibility requires were already in place. Foundry made no changes.';
    case 'not-requested':
      return 'Apply branch rules was turned off, so Foundry made no changes.';
    case 'skipped':
      switch (outcome.reason) {
        case 'access-failure':
          return "Skipped: Foundry can't confirm push access to this repository, so it left the rules alone.";
        case 'unverifiable':
          return "Skipped: the repository's current rules couldn't be verified, so Foundry left them alone.";
        case 'no-rules-token':
          return 'Skipped: this account has no usable branch-rules token. Add one to the account to let Foundry apply rules.';
        default:
          return 'Skipped: Foundry did not change the rules.';
      }
    case 'failed':
      switch (outcome.reason) {
        case 'insufficient-permission':
          return "Failed: the branch-rules token isn't allowed to change rules on this repository. It needs administration access (GitHub) or the Maintainer role (GitLab).";
        case 'provider-error':
          return 'Failed: the provider rejected the change.';
        default:
          return 'Failed: the rules could not be applied.';
      }
    default:
      return null;
  }
}

/**
 * Outcome checklist for a repository add. Renders only data already received in the create
 * response — rows are revealed with a staggered CSS animation, never timed against server steps.
 */
@Component({
  selector: 'fd-repository-add-outcome',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="repository-add-outcome">
      <ol class="repository-add-outcome__rows" [attr.aria-label]="label()">
        @for (row of _rows(); track row.key) {
          <li
            class="repository-add-outcome__row repository-add-outcome__row--{{ row.tone }}"
            [attr.data-row]="row.key"
          >
            <span class="repository-add-outcome__icon" aria-hidden="true">{{ _icon(row.tone) }}</span>
            <span class="repository-add-outcome__label">{{ row.label }}</span>
            <span class="repository-add-outcome__status">{{ row.status }}</span>

            @if (row.key === 'access' && _accessViolations().length > 0) {
              <ul class="repository-add-outcome__violations">
                @for (violation of _accessViolations(); track violation.rule) {
                  <li>{{ violation.description }}</li>
                }
              </ul>
            }

            @if (row.key === 'branch-rules' && _explanation(); as explanation) {
              <div class="repository-add-outcome__details">
                <p class="repository-add-outcome__reason">{{ explanation }}</p>
                @if (_detail(); as detail) {
                  <p class="repository-add-outcome__detail">{{ detail }}</p>
                }
                <p class="repository-add-outcome__rules-heading">Eligibility requires these default-branch rules:</p>
                <ul class="repository-add-outcome__rules">
                  @for (rule of requiredRules; track rule) {
                    <li>{{ rule }}</li>
                  }
                </ul>
                @if (_settingsUrl(); as url) {
                  <a
                    class="repository-add-outcome__settings-link"
                    [href]="url"
                    target="_blank"
                    rel="noopener"
                  >Open branch rules settings<span class="sr-only"> (opens in a new tab)</span></a>
                }
              </div>
            }
          </li>
        }
      </ol>
    </div>
  `,
  styleUrl: './repository-add-outcome.scss',
})
export class RepositoryAddOutcomeComponent {
  readonly response: InputSignal<CreateRepositoryResponse> = input.required<CreateRepositoryResponse>();
  /** Accessible name of the checklist. */
  readonly label: InputSignal<string> = input<string>('Add result');

  protected readonly requiredRules = REQUIRED_BRANCH_RULES;

  protected readonly _rows: Signal<AddOutcomeRow[]> = computed(() => {
    const { repository, branchRules } = this.response();
    return [
      accessRow(repository.eligibility),
      branchRulesRow(branchRules),
      eligibilityRow(repository.eligibility),
    ];
  });

  protected readonly _accessViolations: Signal<EligibilityViolation[]> = computed(() =>
    accessViolations(this.response().repository.eligibility)
  );

  protected readonly _explanation: Signal<string | null> = computed(() =>
    branchRulesExplanation(this.response().branchRules)
  );

  protected readonly _detail: Signal<string | null> = computed(() => {
    const outcome = this.response().branchRules;
    return outcome.kind === 'failed' ? outcome.detail ?? null : null;
  });

  protected readonly _settingsUrl: Signal<string | null> = computed(() => {
    const outcome = this.response().branchRules;
    return outcome.kind === 'applied' ? null : outcome.settingsUrl ?? null;
  });

  protected _icon(tone: AddOutcomeTone): string {
    switch (tone) {
      case 'pass':
        return '✓';
      case 'fail':
        return '✕';
      case 'neutral':
        return '–';
    }
  }
}
