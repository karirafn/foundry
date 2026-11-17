import { ChangeDetectionStrategy, Component, InputSignal, OutputEmitterRef, Signal, WritableSignal, computed, inject, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CdkDragDrop, CdkDropList, CdkDrag, CdkDragHandle } from '@angular/cdk/drag-drop';
import {
  ApplyBranchRulesResponse,
  EligibilityStatus,
  RepositoryStatusTag,
  RepositoryStatusTone,
  RepositorySummary,
  canApplyBranchRules,
  eligibilityStatusLabel,
  repositorySettingTags,
  repositoryStatusTag,
  repositoryUpdatesLine,
  splitRepositorySlug,
} from '../repository.model';
import { RepositoryAddProgressComponent } from '../repository-add-progress/repository-add-progress';
import { RepositoryAddOutcomeComponent } from '../repository-add-outcome/repository-add-outcome';
import { RepositoryService } from '../repository.service';
import { ProviderIconComponent } from '../../../../shared/components/provider-icon/provider-icon';
import { SpinnerComponent } from '../../../../shared/components/spinner/spinner';
import { TooltipDirective } from '../../../../shared/directives/tooltip/tooltip.directive';
import { unreachableExplanation, rateLimitTooltip } from '../repository-eligibility-unreachable.util';
import { unreachablePackageSourceUrl } from '../../package-sources/package-source.model';

interface RepositoryRowView {
  readonly repo: RepositorySummary;
  readonly namespace: string;
  readonly name: string;
  readonly updates: string;
  readonly settingTags: readonly string[];
  readonly status: RepositoryStatusTag | null;
  /** From eligibility, not the status tag: an apply outcome on a now-eligible repository stays neutral. */
  readonly problemTone: RepositoryStatusTone;
  readonly hasEligibilityProblem: boolean;
}

function problemToneOf(repo: RepositorySummary): RepositoryStatusTone {
  switch (repo.eligibility?.status) {
    case 'ineligible':
      return 'ineligible';
    case 'unreachable':
      return 'warning';
    default:
      return 'neutral';
  }
}

@Component({
  selector: 'fd-repository-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    RepositoryAddProgressComponent,
    RepositoryAddOutcomeComponent,
    ProviderIconComponent,
    SpinnerComponent,
    TooltipDirective,
    CdkDropList,
    CdkDrag,
    CdkDragHandle,
  ],
  template: `
    <span
      class="sr-only repository-list__announcement"
      aria-live="polite"
      aria-atomic="true"
    >{{ _announcement() }}</span>

    @if (error()) {
      <div class="repository-list__error" role="alert">
        <span class="repository-list__error-message">{{ error() }}</span>
        <button
          class="repository-list__retry-btn"
          type="button"
          (click)="retry.emit()"
        >Retry</button>
      </div>
    }

    @if (loading()) {
      <div class="repository-list__loading" role="status" aria-label="Loading repositories">
        <fd-spinner [size]="24" />
        <span class="sr-only">Loading repositories</span>
      </div>
    }

    @if (!loading() && !error() && repositories().length === 0) {
      <div class="repository-list__empty">
        <p class="repository-list__empty-heading">No repositories monitored</p>
        <p class="repository-list__empty-description">
          Add your first repository to start monitoring for issues.
        </p>
      </div>
    }

    @if (!loading() && !error() && repositories().length > 0) {
      @if (reordering()) {
        <p id="repository-reorder-hint" class="repository-list__reorder-hint">Drag a row by its number, use the arrows, or focus a number and press the arrow keys. Home and End move to the top or bottom.</p>
      }

      @if (_moveError()) {
        <div class="repository-list__move-error" role="alert">{{ _moveError() }}</div>
      }

      <ul
        class="repository-list__list"
        role="list"
        aria-label="Repository priority order"
        [attr.aria-describedby]="reordering() ? 'repository-reorder-hint' : null"
        cdkDropList
        (cdkDropListDropped)="onDrop($event)"
      >
        @for (row of _rows(); track row.repo.id; let i = $index, first = $first, last = $last) {
          <li
            class="repository-list__row"
            role="listitem"
            [id]="'repo-item-' + row.repo.id"
            cdkDrag
            [cdkDragDisabled]="!_multipleRepos()"
            [cdkDragStartDelay]="_dragStartDelay"
            cdkDragLockAxis="y"
            [attr.aria-roledescription]="_multipleRepos() ? 'reorderable item' : null"
          >
            @if (reordering()) {
              <button
                type="button"
                class="repository-list__rank repository-list__rank--button"
                cdkDragHandle
                [attr.aria-label]="'Reorder ' + row.repo.slug + ', priority ' + (i + 1) + ', use arrow keys to move'"
                aria-describedby="repository-reorder-hint"
                (keydown.arrowup)="onMoveKey($event, i, -1)"
                (keydown.arrowdown)="onMoveKey($event, i, 1)"
                (keydown.home)="onMoveKey($event, i, -i)"
                (keydown.end)="onMoveKey($event, i, repositories().length - 1 - i)"
              >{{ i + 1 }}</button>
            } @else {
              <span class="repository-list__rank" [class.repository-list__rank--draggable]="_multipleRepos()" cdkDragHandle><span class="sr-only">Priority </span>{{ i + 1 }}@if (_multipleRepos()) {<svg class="repository-list__grip" aria-hidden="true" viewBox="0 0 24 24" fill="currentColor"><circle cx="9" cy="6" r="1.5" /><circle cx="15" cy="6" r="1.5" /><circle cx="9" cy="12" r="1.5" /><circle cx="15" cy="12" r="1.5" /><circle cx="9" cy="18" r="1.5" /><circle cx="15" cy="18" r="1.5" /></svg>}</span>
            }

            <fd-provider-icon class="repository-list__provider" [providerType]="row.repo.providerType" />

            <div class="repository-list__main">
              <div class="repository-list__title-line">
                <!-- One line, no whitespace between tags: the anchor's text must equal the slug exactly. -->
                <a class="repository-list__slug" [routerLink]="['/settings/repositories', row.repo.id]"><span class="repository-list__slug-namespace">{{ row.namespace }}</span><span class="repository-list__slug-name">{{ row.name }}</span></a>
                @if (row.status; as status) {
                  <span class="repository-list__status repository-list__status--inline repository-list__status--{{ status.tone }}">{{ status.label }}</span>
                }
              </div>
              <p class="repository-list__meta">
                <span class="repository-list__updates">{{ row.updates }}</span>
                @for (tag of row.settingTags; track tag) {
                  <span class="repository-list__tag">{{ tag }}</span>
                }
              </p>
            </div>

            <div class="repository-list__side">
              @if (row.status; as status) {
                <span class="repository-list__status repository-list__status--side repository-list__status--{{ status.tone }}">{{ status.label }}</span>
              }
              @if (reordering()) {
                <button
                  type="button"
                  class="repository-list__icon-btn repository-list__move-btn repository-list__move-up-btn"
                  [disabled]="first"
                  [attr.aria-label]="'Move ' + row.repo.slug + ' up'"
                  (click)="onMove(i, -1)"
                ><svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m6 15 6-6 6 6" /></svg></button>
                <button
                  type="button"
                  class="repository-list__icon-btn repository-list__move-btn repository-list__move-down-btn"
                  [disabled]="last"
                  [attr.aria-label]="'Move ' + row.repo.slug + ' down'"
                  (click)="onMove(i, 1)"
                ><svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m6 9 6 6 6-6" /></svg></button>
              } @else {
                <button
                  type="button"
                  class="repository-list__icon-btn repository-list__delete-btn"
                  [attr.aria-label]="'Delete repository ' + row.repo.slug"
                  [attr.title]="'Delete repository ' + row.repo.slug"
                  (click)="delete.emit(row.repo)"
                ><svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="3 6 5 6 21 6" /><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6" /><path d="M10 11v6" /><path d="M14 11v6" /><path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" /></svg></button>
              }
            </div>

            @if (row.hasEligibilityProblem || _applyingId() === row.repo.id || _applyResult()?.id === row.repo.id) {
              <div
                class="repository-list__problem repository-list__problem--{{ row.problemTone }}"
                role="group"
                [attr.aria-label]="'Eligibility for ' + row.repo.slug"
              >
                @if (row.hasEligibilityProblem) {
                  <ul class="repository-list__problem-list">
                    @if (row.repo.eligibility!.status === 'ineligible') {
                      @for (violation of row.repo.eligibility!.violations; track violation.rule) {
                        <li>{{ violation.description }} @if (_packageSourceUrl(violation.rule); as sourceUrl) {<a
                            class="repository-list__problem-link"
                            routerLink="/settings/package-sources"
                            [queryParams]="{ source: sourceUrl }"
                            [attr.aria-label]="'View package source ' + sourceUrl + ' in Package sources settings'"
                          >View package source</a>}</li>
                      }
                    } @else {
                      <li>{{ _unreachableExplanation(row.repo) }}</li>
                    }
                  </ul>
                  @if (_recheckError()?.id === row.repo.id) {
                    <p class="repository-list__problem-error" role="alert">{{ _recheckError()!.message }}</p>
                  }
                  @if (_applyError()?.id === row.repo.id) {
                    <p class="repository-list__problem-error" role="alert">{{ _applyError()!.message }}</p>
                  }
                  <div class="repository-list__problem-actions">
                    @if (_canApplyBranchRules(row.repo)) {
                      <button
                        type="button"
                        class="repository-list__btn repository-list__btn--primary repository-list__apply-rules-btn"
                        [disabled]="_applyingId() !== null || _recheckingId() !== null"
                        (click)="onApplyBranchRules(row.repo)"
                      >Apply branch rules</button>
                    }
                    <button
                      type="button"
                      class="repository-list__btn repository-list__recheck-btn"
                      [disabled]="_recheckingId() !== null || _applyingId() !== null || _isRateLimited(row.repo)"
                      [fdTooltip]="_isRateLimited(row.repo) ? _rateLimitTooltip(row.repo) : null"
                      (click)="onRecheck(row.repo)"
                    >{{ _recheckingId() === row.repo.id ? 'Re-checking…' : 'Re-check' }}</button>
                  </div>
                }
                @if (_applyingId() === row.repo.id) {
                  <div class="repository-list__apply-rules">
                    <fd-repository-add-progress label="Applying branch rules…" />
                  </div>
                } @else if (_applyResult()?.id === row.repo.id) {
                  <div class="repository-list__apply-rules" aria-live="polite">
                    <fd-repository-add-outcome
                      [response]="_applyResult()!.response"
                      [label]="'Apply branch rules result for ' + row.repo.slug"
                    />
                    <button
                      type="button"
                      class="repository-list__btn repository-list__apply-rules-dismiss"
                      (click)="onDismissApplyResult()"
                    >Dismiss</button>
                  </div>
                }
              </div>
            }
          </li>
        }
      </ul>
    }
  `,
  styleUrl: './repository-list.scss',
})
export class RepositoryListComponent {
  private readonly _repositoryService = inject(RepositoryService);

  readonly repositories: InputSignal<RepositorySummary[]> = input<RepositorySummary[]>([]);
  readonly loading: InputSignal<boolean> = input<boolean>(false);
  readonly error: InputSignal<string | null> = input<string | null>(null);
  /** Page-level reorder mode: keyboard rank handles and move buttons replace delete. */
  readonly reordering: InputSignal<boolean> = input<boolean>(false);

  readonly delete: OutputEmitterRef<RepositorySummary> = output<RepositorySummary>();
  readonly retry: OutputEmitterRef<void> = output<void>();

  protected readonly _packageSourceUrl = unreachablePackageSourceUrl;

  protected readonly _recheckingId: WritableSignal<string | null> = signal(null);
  protected readonly _recheckError: WritableSignal<{ id: string; message: string } | null> = signal(null);
  protected readonly _applyingId: WritableSignal<string | null> = signal(null);
  protected readonly _applyError: WritableSignal<{ id: string; message: string } | null> = signal(null);
  protected readonly _applyResult: WritableSignal<{ id: string; response: ApplyBranchRulesResponse } | null> = signal(null);
  protected readonly _announcement: WritableSignal<string> = signal('');
  protected readonly _moveError: WritableSignal<string | null> = signal(null);
  protected readonly _multipleRepos = computed(() => this.repositories().length > 1);
  // A touch drag starts only after a press, so a tap or a scroll never reorders.
  protected readonly _dragStartDelay = { touch: 250, mouse: 0 } as const;

  // Relative times recompute when the repositories change; nothing ticks in between.
  protected readonly _rows: Signal<RepositoryRowView[]> = computed(() => {
    const now = Date.now();
    return this.repositories().map(repo => {
      const { namespace, name } = splitRepositorySlug(repo.slug);
      return {
        repo,
        namespace,
        name,
        updates: repositoryUpdatesLine(repo, now),
        settingTags: repositorySettingTags(repo),
        status: repositoryStatusTag(repo),
        problemTone: problemToneOf(repo),
        hasEligibilityProblem: repo.eligibility !== null && repo.eligibility.status !== 'eligible',
      };
    });
  });

  protected _unreachableExplanation(repo: RepositorySummary): string {
    return unreachableExplanation(repo.eligibility?.reason ?? null, repo.providerType);
  }

  protected _isRateLimited(repo: RepositorySummary): boolean {
    return repo.eligibility?.reason === 'rate-limited';
  }

  protected _rateLimitTooltip(repo: RepositorySummary): string {
    return rateLimitTooltip(repo.providerType);
  }

  onDrop(event: CdkDragDrop<RepositorySummary[]>): void {
    if (event.previousIndex === event.currentIndex) {
      return;
    }
    const repo = this.repositories()[event.previousIndex];
    this._doMove(repo, event.currentIndex, () => {
      setTimeout(() => {
        const movedItem = document.getElementById(`repo-item-${repo.id}`);
        const focusTarget =
          movedItem?.querySelector<HTMLElement>('.repository-list__rank--button') ??
          movedItem?.querySelector<HTMLElement>('.repository-list__move-up-btn') ??
          movedItem?.querySelector<HTMLElement>('.repository-list__move-down-btn');
        focusTarget?.focus();
      });
    });
  }

  onMove(currentIndex: number, delta: number): void {
    const newIndex = currentIndex + delta;
    if (newIndex < 0 || newIndex >= this.repositories().length) {
      return;
    }
    const repo = this.repositories()[currentIndex];
    const isMovingDown = delta > 0;
    this._doMove(repo, newIndex, () => {
      setTimeout(() => {
        const movedItem = document.getElementById(`repo-item-${repo.id}`);
        if (!movedItem) {
          return;
        }
        const totalRepos = this._repositoryService.repositories().length;
        const actualIndex = this._repositoryService.repositories().findIndex(r => r.id === repo.id);
        const isAtTop = actualIndex === 0;
        const isAtBottom = actualIndex === totalRepos - 1;

        if (isMovingDown && isAtBottom) {
          const focusTarget =
            movedItem.querySelector<HTMLElement>('.repository-list__move-up-btn') ??
            movedItem.querySelector<HTMLElement>('.repository-list__rank--button');
          focusTarget?.focus();
        } else if (!isMovingDown && isAtTop) {
          const focusTarget =
            movedItem.querySelector<HTMLElement>('.repository-list__move-down-btn') ??
            movedItem.querySelector<HTMLElement>('.repository-list__rank--button');
          focusTarget?.focus();
        } else {
          const btnClass = isMovingDown
            ? '.repository-list__move-down-btn'
            : '.repository-list__move-up-btn';
          const focusTarget =
            movedItem.querySelector<HTMLElement>(btnClass) ??
            movedItem.querySelector<HTMLElement>('.repository-list__rank--button');
          focusTarget?.focus();
        }
      });
    });
  }

  onMoveKey(event: Event, currentIndex: number, delta: number): void {
    if (delta === 0) {
      return;
    }
    event.preventDefault();
    const newIndex = Math.max(0, Math.min(this.repositories().length - 1, currentIndex + delta));
    if (newIndex === currentIndex) {
      return;
    }
    const repo = this.repositories()[currentIndex];
    this._doMove(repo, newIndex, () => {
      const handle = document.getElementById(`repo-item-${repo.id}`)?.querySelector<HTMLElement>('.repository-list__rank--button');
      handle?.focus();
    });
  }

  private _doMove(repo: RepositorySummary, newIndex: number, afterMove?: () => void): void {
    this._moveError.set(null);
    this._announcement.set('');
    this._repositoryService.moveRepository(repo.id, newIndex).subscribe({
      next: () => {
        const repos = this._repositoryService.repositories();
        const actualIndex = repos.findIndex(r => r.id === repo.id);
        const displayIndex = actualIndex !== -1 ? actualIndex + 1 : newIndex + 1;
        this._announcement.set(`${repo.slug} moved to position ${displayIndex}`);
        afterMove?.();
      },
      error: () => {
        this._moveError.set(`Failed to reorder ${repo.slug}. Please try again.`);
        this._announcement.set(`Failed to reorder ${repo.slug}`);
      },
    });
  }

  onRecheck(repo: RepositorySummary): void {
    if (this._recheckingId() !== null) {
      return;
    }
    this._recheckError.set(null);
    this._recheckingId.set(repo.id);
    this._announcement.set(`${repo.slug}: Re-checking...`);

    this._repositoryService.recheckEligibility(repo.accountId, repo.id).subscribe({
      next: (updated: RepositorySummary) => {
        this._recheckingId.set(null);
        const status = updated.eligibility?.status;
        const label = status ? eligibilityStatusLabel(status as EligibilityStatus) : '';
        this._announcement.set(status ? `${repo.slug}: ${label}` : '');
      },
      error: () => {
        this._recheckingId.set(null);
        this._recheckError.set({ id: repo.id, message: 'Re-check failed. Please try again.' });
        this._announcement.set(`${repo.slug}: Re-check failed`);
      },
    });
  }

  protected _canApplyBranchRules(repo: RepositorySummary): boolean {
    return canApplyBranchRules(repo.eligibility);
  }

  onApplyBranchRules(repo: RepositorySummary): void {
    // Disabled while any request is pending — a repeated click never sends a second write.
    if (this._applyingId() !== null) {
      return;
    }
    this._applyError.set(null);
    this._applyResult.set(null);
    this._applyingId.set(repo.id);
    this._announcement.set(`${repo.slug}: Applying branch rules...`);

    this._repositoryService.applyBranchRules(repo.accountId, repo.id).subscribe({
      next: (response: ApplyBranchRulesResponse) => {
        this._applyingId.set(null);
        this._applyResult.set({ id: repo.id, response });
        const status = response.repository.eligibility?.status;
        const label = status ? eligibilityStatusLabel(status as EligibilityStatus) : '';
        this._announcement.set(status ? `${repo.slug}: ${label}` : '');
      },
      error: () => {
        this._applyingId.set(null);
        this._applyError.set({ id: repo.id, message: 'Applying branch rules failed. Please try again.' });
        this._announcement.set(`${repo.slug}: Applying branch rules failed`);
      },
    });
  }

  onDismissApplyResult(): void {
    this._applyResult.set(null);
  }
}
