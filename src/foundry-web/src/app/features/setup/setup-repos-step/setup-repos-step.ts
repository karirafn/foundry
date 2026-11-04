import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  OutputEmitterRef,
  Signal,
  WritableSignal,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, defer, from, of } from 'rxjs';
import { extractErrorMessage } from '../../../core/http/extract-error-message';
import { catchError, concatMap, finalize, map } from 'rxjs/operators';
import { RepositoryService } from '../../settings/repositories/repository.service';
import { AvailableRepository, CreateRepositoryResponse } from '../../settings/repositories/repository.model';
import { SpinnerComponent } from '../../../shared/components/spinner/spinner';
import { RepositoryAddProgressComponent } from '../../settings/repositories/repository-add-progress/repository-add-progress';
import { RepositoryAddOutcomeComponent } from '../../settings/repositories/repository-add-outcome/repository-add-outcome';

const ERROR_TRUNCATE_LENGTH = 200;

type AddRowKind = 'queued' | 'adding' | 'added' | 'error';

/** Per-repository state of a wizard batch add. Each row carries only what its own response returned. */
interface AddRow {
  slug: string;
  kind: AddRowKind;
  response: CreateRepositoryResponse | null;
  error: string | null;
}

@Component({
  selector: 'fd-setup-repos-step',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SpinnerComponent, RepositoryAddProgressComponent, RepositoryAddOutcomeComponent],
  template: `
    <div class="setup-repos-step">
      <h2 class="setup-repos-step__title">Select Repositories</h2>
      <p class="setup-repos-step__description">
        Choose which repositories Foundry should monitor for issues.
      </p>

      @if (_rows().length > 0) {
        <ul class="setup-repos-step__results" aria-label="Repositories being added">
          @for (row of _rows(); track row.slug) {
            <li class="setup-repos-step__result setup-repos-step__result--{{ row.kind }}" [attr.data-slug]="row.slug">
              <span class="setup-repos-step__repo-slug">{{ row.slug }}</span>
              @switch (row.kind) {
                @case ('queued') {
                  <span class="setup-repos-step__result-queued">Queued</span>
                }
                @case ('adding') {
                  <fd-repository-add-progress />
                }
                @case ('error') {
                  <span class="setup-repos-step__result-error" role="alert">Not added: {{ row.error }}</span>
                }
              }
              @if (row.response; as response) {
                <div class="setup-repos-step__result-outcome" aria-live="polite">
                  <fd-repository-add-outcome [response]="response" />
                </div>
              }
            </li>
          }
        </ul>
      } @else if (_repositoryService.loadingAvailable()) {
        <div class="setup-repos-step__loading" role="status">
          <fd-spinner [size]="16" />
          Loading repositories...
        </div>
      } @else if (_repositoryService.loadAvailableError()) {
        <div class="setup-repos-step__load-error" role="alert">
          {{ _repositoryService.loadAvailableError() }}
          <button
            class="setup-repos-step__retry-btn"
            type="button"
            (click)="onRetry()"
          >Retry</button>
        </div>
      } @else {
        <div class="setup-repos-step__filter-wrapper">
          <input
            class="setup-repos-step__filter-input"
            type="text"
            placeholder="Filter repositories..."
            aria-label="Filter repositories"
            [value]="_filterText()"
            (input)="_filterText.set($any($event.target).value)"
          />
        </div>

        <div
          class="setup-repos-step__empty-status"
          [class.sr-only]="_emptyStatusMessage() === ''"
          role="status"
        >
          @if (!_repositoryService.availableHasClaims()) {
            This account has no claimed namespaces.
            <span class="setup-repos-step__empty-status-hint">Add a namespace claim to this account to monitor its repositories.</span>
          } @else if (_repositoryService.availableRepositories().length === 0) {
            No repositories under this account's claimed namespaces.
          }
        </div>

        <ul class="setup-repos-step__repo-list" role="list">
          @if (_repositoryService.availableHasClaims() && _repositoryService.availableRepositories().length > 0 && _filteredRepositories().length === 0) {
            <li class="setup-repos-step__repo-empty">No matching repositories</li>
          }
          @for (repo of _filteredRepositories(); track repo.slug) {
            <li
              class="setup-repos-step__repo-item"
              [class.setup-repos-step__repo-item--disabled]="repo.isMonitored || !repo.canPush"
              [class.setup-repos-step__repo-item--monitored]="repo.isMonitored"
            >
              @if (repo.isMonitored) {
                <div class="setup-repos-step__repo-label">
                  <span class="setup-repos-step__repo-gutter" aria-hidden="true">
                    <span class="setup-repos-step__repo-check">✓</span>
                  </span>
                  <span class="setup-repos-step__repo-slug">{{ repo.slug }}</span>
                  <span class="sr-only">already monitored</span>
                  @if (repo.isPrivate) {
                    <span class="setup-repos-step__repo-private-badge" aria-label="private">Private</span>
                  }
                </div>
              } @else {
                <label class="setup-repos-step__repo-label">
                  <span class="setup-repos-step__repo-gutter" aria-hidden="true">
                    <input
                      class="setup-repos-step__repo-checkbox"
                      type="checkbox"
                      [checked]="_selectedSlugs().has(repo.slug) && repo.canPush"
                      [disabled]="!repo.canPush"
                      [attr.aria-describedby]="(!repo.canPush) ? 'repo-reason-' + repo.slug.replaceAll('/', '-') : null"
                      (change)="onToggle(repo.slug, $any($event.target).checked)"
                    />
                  </span>
                  <span class="setup-repos-step__repo-slug">{{ repo.slug }}</span>
                  @if (repo.isPrivate) {
                    <span class="setup-repos-step__repo-private-badge" aria-label="private">Private</span>
                  }
                  @if (!repo.canPush) {
                    <span
                      class="setup-repos-step__repo-reason"
                      aria-hidden="true"
                    >{{ _repositoryService.availableNoPushAccessExplanation() }}</span>
                  }
                </label>
                @if (!repo.canPush) {
                  <span
                    class="sr-only"
                    [id]="'repo-reason-' + repo.slug.replaceAll('/', '-')"
                  >{{ _repositoryService.availableNoPushAccessExplanation() }}</span>
                }
              }
            </li>
          }
        </ul>
      }

      <div class="setup-repos-step__apply-rules">
        <label class="setup-repos-step__apply-rules-label">
          <input
            class="setup-repos-step__apply-rules-checkbox"
            type="checkbox"
            aria-describedby="setup-apply-branch-rules-hint"
            [checked]="_applyBranchRules()"
            [disabled]="_rows().length > 0"
            (change)="_applyBranchRules.set($any($event.target).checked)"
          />
          Apply branch rules
        </label>
        <span class="setup-repos-step__apply-rules-hint" id="setup-apply-branch-rules-hint">
          Applies to every repository added in this step. Where the default branch is missing the rules eligibility requires, Foundry adds them using the account's branch-rules token.
        </span>
      </div>

      <div class="setup-repos-step__save-error" role="alert">{{ _saveError() ?? '' }}</div>

      <div class="setup-repos-step__actions">
        <button
          class="setup-repos-step__back-btn"
          type="button"
          [disabled]="_saving()"
          (click)="back.emit()"
        >Back</button>

        <div class="setup-repos-step__secondary-actions">
          @if (_rows().length === 0) {
            <button
              class="setup-repos-step__skip-btn"
              type="button"
              (click)="onSkip()"
            >Skip</button>
          }

          <button
            class="setup-repos-step__finish-btn"
            type="button"
            [disabled]="!_canFinish()"
            (click)="_batchFinished() ? complete.emit() : onFinish()"
          >{{ _finishLabel() }}</button>
        </div>
      </div>
    </div>
  `,
  styleUrl: './setup-repos-step.scss',
})
export class SetupReposStepComponent implements OnInit {
  protected readonly _repositoryService = inject(RepositoryService);

  readonly accountId = input.required<string>();

  readonly complete: OutputEmitterRef<void> = output<void>();
  readonly back: OutputEmitterRef<void> = output<void>();

  protected readonly _filterText: WritableSignal<string> = signal('');
  protected readonly _selectedSlugs: WritableSignal<Set<string>> = signal(new Set<string>());
  protected readonly _saving: WritableSignal<boolean> = signal(false);
  protected readonly _saveError: WritableSignal<string | null> = signal(null);
  protected readonly _applyBranchRules: WritableSignal<boolean> = signal(true);
  protected readonly _rows: WritableSignal<AddRow[]> = signal([]);

  protected readonly _batchFinished: Signal<boolean> = computed(() => this._rows().length > 0 && !this._saving());

  protected readonly _finishLabel: Signal<string> = computed(() => {
    if (this._saving()) {
      return 'Adding…';
    }
    return this._batchFinished() ? 'Continue' : 'Finish';
  });

  protected readonly _filteredRepositories: Signal<AvailableRepository[]> = computed(() => {
    const filter = this._filterText().toLowerCase();
    const repos = this._repositoryService.availableRepositories();
    if (!filter) {
      return repos;
    }
    return repos.filter(r => r.slug.toLowerCase().includes(filter));
  });

  protected readonly _emptyStatusMessage: Signal<string> = computed(() => {
    if (!this._repositoryService.availableHasClaims()) {
      return 'This account has no claimed namespaces.';
    }
    if (this._repositoryService.availableRepositories().length === 0) {
      return "No repositories under this account's claimed namespaces.";
    }
    return '';
  });

  protected readonly _canFinish: Signal<boolean> = computed(() => {
    if (this._saving()) {
      return false;
    }
    return this._batchFinished() || this._selectedSlugs().size > 0;
  });

  ngOnInit(): void {
    this._repositoryService.loadAvailableRepositories(this.accountId());
  }

  onToggle(slug: string, checked: boolean): void {
    const repo = this._repositoryService.availableRepositories().find(r => r.slug === slug);
    if (repo?.isMonitored || !repo?.canPush) {
      return;
    }
    this._selectedSlugs.update(current => {
      const next = new Set(current);
      if (checked) {
        next.add(slug);
      } else {
        next.delete(slug);
      }
      return next;
    });
  }

  onRetry(): void {
    this._repositoryService.loadAvailableRepositories(this.accountId());
  }

  onSkip(): void {
    this.complete.emit();
  }

  onFinish(): void {
    const accountId = this.accountId();
    const applyBranchRules = this._applyBranchRules();
    const slugs = Array.from(this._selectedSlugs());
    const total = slugs.length;

    this._saving.set(true);
    this._saveError.set(null);
    this._rows.set(slugs.map(slug => ({ slug, kind: 'queued', response: null, error: null })));

    // Sequential, and a failure in one row never aborts the batch: each row records its own result.
    from(slugs).pipe(
      concatMap(slug => this._addOne(accountId, slug, applyBranchRules)),
      finalize(() => this._saving.set(false)),
    ).subscribe({
      complete: () => {
        const rows = this._rows();
        const failed = rows.filter(r => r.kind === 'error');
        if (failed.length > 0) {
          const added = rows.filter(r => r.kind === 'added').length;
          this._saveError.set(`Created ${added} of ${total} repositories. Error: ${failed[0].error}`);
        }
      },
    });
  }

  private _addOne(accountId: string, slug: string, applyBranchRules: boolean): Observable<void> {
    return defer(() => {
      this._updateRow(slug, { kind: 'adding' });
      return this._repositoryService.createRepository(accountId, {
        slug,
        pollIntervalSeconds: null,
        maxConcurrentWorkers: null,
        applyBranchRules,
      });
    }).pipe(
      map(response => this._updateRow(slug, { kind: 'added', response })),
      catchError((err: HttpErrorResponse) => {
        const message = (extractErrorMessage(err) ?? 'Failed to create repository').slice(0, ERROR_TRUNCATE_LENGTH);
        this._updateRow(slug, { kind: 'error', error: message });
        return of(undefined);
      }),
    );
  }

  private _updateRow(slug: string, patch: Partial<AddRow>): void {
    this._rows.update(rows => rows.map(r => (r.slug === slug ? { ...r, ...patch } : r)));
  }
}
