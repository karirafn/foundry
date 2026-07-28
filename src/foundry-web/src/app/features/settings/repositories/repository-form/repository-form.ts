import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  InputSignal,
  OnInit,
  OutputEmitterRef,
  Signal,
  ViewChild,
  WritableSignal,
  afterNextRender,
  computed,
  effect,
  input,
  output,
  signal,
} from '@angular/core';
import { AccountSummary } from '../../accounts/account.model';
import { accountOptionLabel } from '../../account-label.util';
import {
  AvailableRepository,
  CreateRepositoryRequest,
  NO_WRITE_ACCESS_REASON,
  RepositorySummary,
  UpdateRepositoryRequest,
} from '../repository.model';

const DEFAULT_POLL_INTERVAL_MINUTES = 5;
const SECONDS_PER_MINUTE = 60;
const MIN_POLL_INTERVAL_MINUTES = 1;
const MAX_POLL_INTERVAL_MINUTES = 1440;

@Component({
  selector: 'fd-repository-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="repository-form">
      <button
        class="repository-form__cancel-link"
        type="button"
        (click)="cancel.emit()"
      >
        <span aria-hidden="true">←</span> Cancel
      </button>

      <h2 class="repository-form__heading" #formHeading tabindex="-1">
        {{ _isEditMode() ? 'Edit Repository' : 'Add Repository' }}
      </h2>

      @if (_isEditMode()) {
        <div class="repository-form__field">
          <span class="repository-form__field-label">Repository</span>
          <span class="repository-form__read-only-slug">{{ repository()!.slug }}</span>
        </div>
        <div class="repository-form__field">
          <span class="repository-form__field-label">Account</span>
          <span class="repository-form__read-only-account">{{ repository()!.accountName }}</span>
        </div>
      } @else {
        <div class="repository-form__field">
          <label class="repository-form__field-label" for="repository-account">Account</label>
          <select
            class="repository-form__select"
            id="repository-account"
            [value]="_selectedAccountId()"
            (change)="onAccountChange($any($event.target).value)"
          >
            <option value="" disabled>Select an account...</option>
            @for (account of accounts(); track account.id) {
              <option [value]="account.id">{{ accountOptionLabel(account) }}</option>
            }
          </select>
        </div>

        @if (_selectedAccountId()) {
          <div class="repository-form__field">
            <label class="repository-form__field-label" for="repository-picker-input">Repository</label>

            @if (loadingAvailable()) {
              <div class="repository-form__picker-loading" role="status">
                <span class="repository-form__picker-spinner" aria-hidden="true"></span>
                Loading repositories...
              </div>
            } @else if (loadAvailableError()) {
              <div class="repository-form__picker-error" role="alert">
                {{ loadAvailableError() }}
                <button
                  class="repository-form__picker-retry-btn"
                  type="button"
                  (click)="accountSelected.emit(_selectedAccountId())"
                >Retry</button>
              </div>
            } @else {
              <div class="repository-form__picker-wrapper">
                <input
                  class="repository-form__input"
                  type="text"
                  id="repository-picker-input"
                  role="combobox"
                  [attr.aria-expanded]="_pickerOpen()"
                  aria-controls="repository-picker-listbox"
                  [attr.aria-activedescendant]="_activeOptionId()"
                  aria-autocomplete="list"
                  [value]="_repoSlug()"
                  autocomplete="off"
                  (input)="onPickerInput($any($event.target).value)"
                  (click)="openPicker()"
                  (keydown)="onPickerKeydown($event)"
                />

                <div
                  class="repository-form__picker-empty-status"
                  [class.sr-only]="_emptyStatusText() === ''"
                  role="status"
                >{{ _emptyStatusText() }}@if (_showNoClaims()) {
                    <span class="repository-form__picker-empty-status-hint">Add a namespace claim to this account to monitor its repositories.</span>
                  }</div>

                <ul
                  class="repository-form__picker-listbox"
                  id="repository-picker-listbox"
                  role="listbox"
                  [hidden]="!_pickerOpen()"
                >
                  @if (_filteredRepositories().length === 0 && _emptyStatusText() === '') {
                    <li class="repository-form__picker-empty">No matching repositories</li>
                  }
                  @for (repo of _filteredRepositories(); track repo.slug; let i = $index) {
                    <li
                      class="repository-form__picker-option"
                      [class.repository-form__picker-option--active]="i === _activeOptionIndex()"
                      [class.repository-form__picker-option--disabled]="repo.isMonitored || !repo.canPush"
                      [class.repository-form__picker-option--monitored]="repo.isMonitored"
                      [id]="'repo-option-' + i"
                      role="option"
                      [attr.aria-selected]="(!repo.isMonitored && repo.canPush) && (_repoSlug() === repo.slug)"
                      [attr.aria-disabled]="(repo.isMonitored || !repo.canPush) ? 'true' : null"
                      [attr.aria-describedby]="(!repo.isMonitored && !repo.canPush) ? 'repo-option-reason-sr-' + i : null"
                      (click)="selectRepo(repo)"
                      (mousedown)="$event.preventDefault()"
                    >
                      <span class="repository-form__picker-option-gutter" aria-hidden="true">
                        @if (repo.isMonitored) {
                          <span class="repository-form__picker-check">✓</span>
                        }
                      </span>
                      <span class="repository-form__picker-option-slug">{{ repo.slug }}</span>
                      @if (repo.isMonitored) {
                        <span class="sr-only">already monitored</span>
                      }
                      @if (!repo.isMonitored && !repo.canPush) {
                        <span
                          class="repository-form__picker-option-reason"
                          aria-hidden="true"
                        >{{ _noWriteAccessReason }}</span>
                        <span
                          class="sr-only"
                          [id]="'repo-option-reason-sr-' + i"
                        >{{ _noWriteAccessReason }}</span>
                      }
                    </li>
                  }
                </ul>
              </div>
            }
          </div>
        }
      }

      <div class="repository-form__field">
        <label class="repository-form__field-label" for="repository-poll-interval">
          Poll Interval (minutes)
        </label>
        <input
          class="repository-form__input repository-form__input--narrow"
          type="number"
          id="repository-poll-interval"
          min="1"
          max="1440"
          [value]="_pollIntervalMinutes()"
          (input)="onPollIntervalInput($any($event.target).value)"
        />
      </div>

      @if (_isEditMode()) {
        <div class="repository-form__field repository-form__field--inline">
          <label class="repository-form__field-label" for="repository-active">Active</label>
          <input
            class="repository-form__toggle"
            type="checkbox"
            id="repository-active"
            [checked]="_isActive()"
            (change)="_isActive.set($any($event.target).checked)"
          />
        </div>
      }

      <div class="repository-form__save-error" role="alert">
        @if (saveError()) {
          {{ saveError() }}
        }
      </div>

      <button
        class="repository-form__save-btn"
        type="button"
        [disabled]="!_canSave()"
        (click)="onSave()"
      >Save</button>
    </div>
  `,
  styleUrl: './repository-form.scss',
})
export class RepositoryFormComponent implements OnInit {
  protected readonly _noWriteAccessReason = NO_WRITE_ACCESS_REASON;
  protected readonly accountOptionLabel = accountOptionLabel;

  readonly repository: InputSignal<RepositorySummary | null> = input<RepositorySummary | null>(null);
  readonly accounts: InputSignal<AccountSummary[]> = input<AccountSummary[]>([]);
  readonly availableRepositories: InputSignal<AvailableRepository[]> = input<AvailableRepository[]>([]);
  readonly loadingAvailable: InputSignal<boolean> = input<boolean>(false);
  readonly loadAvailableError: InputSignal<string | null> = input<string | null>(null);
  readonly saving: InputSignal<boolean> = input<boolean>(false);
  readonly saveError: InputSignal<string | null> = input<string | null>(null);
  readonly hasClaims: InputSignal<boolean> = input<boolean>(false);

  readonly save: OutputEmitterRef<CreateRepositoryRequest | UpdateRepositoryRequest> =
    output<CreateRepositoryRequest | UpdateRepositoryRequest>();
  readonly cancel: OutputEmitterRef<void> = output<void>();
  readonly accountSelected: OutputEmitterRef<string> = output<string>();

  @ViewChild('formHeading') readonly formHeading?: ElementRef<HTMLElement>;

  protected readonly _isEditMode: Signal<boolean> = computed(() => this.repository() !== null);

  protected readonly _selectedAccountId: WritableSignal<string> = signal('');
  protected readonly _repoSlug: WritableSignal<string> = signal('');
  protected readonly _pollIntervalMinutes: WritableSignal<number | ''> = signal(DEFAULT_POLL_INTERVAL_MINUTES);
  protected readonly _isActive: WritableSignal<boolean> = signal(true);
  protected readonly _pickerOpen: WritableSignal<boolean> = signal(false);
  protected readonly _activeOptionIndex: WritableSignal<number> = signal(-1);
  protected readonly _filterText: WritableSignal<string> = signal('');

  protected readonly _filteredRepositories: Signal<AvailableRepository[]> = computed(() => {
    const filter = this._filterText().toLowerCase();
    if (!filter) {
      return this.availableRepositories();
    }
    return this.availableRepositories().filter(r => r.slug.toLowerCase().includes(filter));
  });

  protected readonly _showNoClaims: Signal<boolean> = computed(
    () => this._pickerOpen() && !this.hasClaims()
  );

  protected readonly _emptyStatusText: Signal<string> = computed(() => {
    if (!this._pickerOpen()) {
      return '';
    }
    if (!this.hasClaims()) {
      return 'This account has no claimed namespaces.';
    }
    if (this.availableRepositories().length === 0) {
      return "No repositories under this account's claimed namespaces.";
    }
    return '';
  });

  protected readonly _activeOptionId: Signal<string | null> = computed(() => {
    const index = this._activeOptionIndex();
    if (index < 0) {
      return null;
    }
    return `repo-option-${index}`;
  });

  protected readonly _canSave: Signal<boolean> = computed(() => {
    if (this.saving()) {
      return false;
    }
    const interval = this._pollIntervalMinutes();
    if (interval !== '' && (interval < MIN_POLL_INTERVAL_MINUTES || interval > MAX_POLL_INTERVAL_MINUTES)) {
      return false;
    }
    if (this._isEditMode()) {
      return true;
    }
    return !!this._selectedAccountId() && !!this._repoSlug();
  });

  constructor() {
    afterNextRender(() => {
      this.formHeading?.nativeElement.focus();
    });

    effect(() => {
      this._filteredRepositories();
      this._activeOptionIndex.set(-1);
    });
  }

  ngOnInit(): void {
    const repo = this.repository();
    if (repo !== null) {
      const minutes = repo.pollIntervalSeconds !== null
        ? repo.pollIntervalSeconds / SECONDS_PER_MINUTE
        : '';
      this._pollIntervalMinutes.set(minutes);
      this._isActive.set(repo.isActive);
    }
  }

  onAccountChange(accountId: string): void {
    this._selectedAccountId.set(accountId);
    this._repoSlug.set('');
    this._filterText.set('');
    this._pickerOpen.set(false);
    this.accountSelected.emit(accountId);
  }

  onPickerInput(value: string): void {
    this._repoSlug.set(value);
    this._filterText.set(value);
    this._pickerOpen.set(true);
    this._activeOptionIndex.set(-1);
  }

  openPicker(): void {
    this._pickerOpen.set(true);
  }

  onPickerKeydown(event: KeyboardEvent): void {
    const filtered = this._filteredRepositories();
    const current = this._activeOptionIndex();

    if (event.key === 'ArrowDown') {
      event.preventDefault();
      this._pickerOpen.set(true);
      const next = this._nextSelectableIndex(filtered, current, 1);
      if (next !== -1) {
        this._activeOptionIndex.set(next);
      }
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      const next = this._nextSelectableIndex(filtered, current, -1);
      this._activeOptionIndex.set(next);
    } else if (event.key === 'Enter') {
      event.preventDefault();
      if (current >= 0 && current < filtered.length) {
        this.selectRepo(filtered[current]);
      }
    } else if (event.key === 'Escape') {
      this._pickerOpen.set(false);
      this._activeOptionIndex.set(-1);
    }
  }

  private _nextSelectableIndex(repos: AvailableRepository[], from: number, direction: 1 | -1): number {
    let i = from + direction;
    while (i >= 0 && i < repos.length) {
      if (!repos[i].isMonitored && repos[i].canPush) {
        return i;
      }
      i += direction;
    }
    return -1;
  }

  selectRepo(repo: AvailableRepository): void {
    if (repo.isMonitored || !repo.canPush) {
      return;
    }
    this._repoSlug.set(repo.slug);
    this._filterText.set('');
    this._pickerOpen.set(false);
    this._activeOptionIndex.set(-1);
  }

  onPollIntervalInput(value: string): void {
    if (value === '') {
      this._pollIntervalMinutes.set('');
    } else {
      this._pollIntervalMinutes.set(Number(value));
    }
  }

  onSave(): void {
    const minutes = this._pollIntervalMinutes();
    const pollIntervalSeconds = minutes === '' ? null : minutes * SECONDS_PER_MINUTE;

    if (this._isEditMode()) {
      const request: UpdateRepositoryRequest = {
        pollIntervalSeconds,
        isActive: this._isActive(),
      };
      this.save.emit(request);
    } else {
      const request: CreateRepositoryRequest = {
        slug: this._repoSlug(),
        pollIntervalSeconds,
      };
      this.save.emit(request);
    }
  }
}
