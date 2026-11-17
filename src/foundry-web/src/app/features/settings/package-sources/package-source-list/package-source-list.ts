import { ChangeDetectionStrategy, Component, InputSignal, OutputEmitterRef, Signal, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { RowActionsComponent } from '../../../../shared/components/row-actions/row-actions';
import { SpinnerComponent } from '../../../../shared/components/spinner/spinner';
import { TooltipDirective } from '../../../../shared/directives/tooltip/tooltip.directive';
import { PackageSourceStatusComponent } from '../package-source-status/package-source-status';
import { PackageSource, PendingCheck, packageSourceAnchorId } from '../package-source.model';

const MAX_VISIBLE_REPOSITORIES = 4;

@Component({
  selector: 'fd-package-source-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RowActionsComponent, SpinnerComponent, TooltipDirective, PackageSourceStatusComponent],
  template: `
    @if (error()) {
      <div class="package-source-list__error" role="alert">
        <span class="package-source-list__error-message">Failed to load package sources</span>
        <button class="package-source-list__retry-btn" type="button" (click)="retry.emit()">Retry</button>
      </div>
    }

    @if (loading()) {
      <div class="package-source-list__loading" role="status">
        <fd-spinner [size]="24" />
        <span class="sr-only">Loading package sources</span>
      </div>
    }

    @if (!loading() && !error() && sources().length === 0) {
      <div class="package-source-list__empty" role="status">
        <p class="package-source-list__empty-heading">No package sources detected</p>
        <p class="package-source-list__empty-description">
          None of your monitored repositories declares a NuGet source other than nuget.org in its root nuget.config.
        </p>
        <a class="package-source-list__empty-link" routerLink="/settings/repositories">Manage repositories</a>
      </div>
    }

    @if (!loading() && !error() && sources().length > 0) {
      @if (_unreachableCount() > 0) {
        <div class="package-source-list__summary">
          <strong>{{ _unreachableSummary() }}</strong>
          Repositories that declare them are Ineligible and dispatch nothing until a check succeeds.
        </div>
      }
      <ul class="package-source-list__list" role="list">
        @for (source of _sorted(); track source.id) {
          @let visibleRepos = source.repositories.slice(0, MAX_VISIBLE_REPOSITORIES);
          @let overflowRepos = source.repositories.slice(MAX_VISIBLE_REPOSITORIES);
          @let overflowLabel = overflowRepos.length + ' more repositories: ' + _slugs(overflowRepos);
          <li
            class="package-source-list__item"
            [class.package-source-list__item--targeted]="targetUrl() === source.url"
            [id]="anchorId(source.url)"
          >
            <div class="package-source-list__title-row">
              <span class="package-source-list__url" tabindex="-1">{{ source.url }}</span>
              @if (source.credential) {
                <fd-row-actions
                  class="package-source-list__actions"
                  [editLabel]="'Replace credential for ' + source.url"
                  [deleteBusy]="removingId() === source.id"
                  [deleteLabel]="removingId() === source.id ? ('Removing credential for ' + source.url + '…') : ('Remove credential for ' + source.url)"
                  (edit)="editCredential.emit(source)"
                  (delete)="removeCredential.emit(source)"
                />
              } @else {
                <button
                  class="package-source-list__add-btn"
                  type="button"
                  [attr.aria-label]="'Add credential for ' + source.url"
                  (click)="editCredential.emit(source)"
                >+ Add credential</button>
              }
            </div>

            <fd-package-source-status
              [source]="source"
              [pending]="pendingChecks().get(source.id) ?? null"
              (editCredential)="editCredential.emit(source)"
            />

            <div
              class="package-source-list__strip"
              role="group"
              [attr.aria-label]="'Repositories declaring ' + source.url"
            >
              <span class="package-source-list__strip-label">Declared by</span>
              @for (repo of visibleRepos; track repo.id) {
                <a class="package-source-list__chip" [routerLink]="['/settings/repositories', repo.id]">{{ repo.slug }}</a>
              }
              @if (overflowRepos.length > 0) {
                <span
                  class="package-source-list__chip"
                  role="img"
                  tabindex="0"
                  [attr.aria-label]="overflowLabel"
                  [fdTooltip]="overflowLabel"
                >+{{ overflowRepos.length }}</span>
              }
            </div>
          </li>
        }
      </ul>
    }
  `,
  styleUrl: './package-source-list.scss',
})
export class PackageSourceListComponent {
  readonly sources: InputSignal<PackageSource[]> = input<PackageSource[]>([]);
  readonly loading: InputSignal<boolean> = input<boolean>(false);
  readonly error: InputSignal<string | null> = input<string | null>(null);
  readonly removingId: InputSignal<string | null> = input<string | null>(null);
  readonly pendingChecks: InputSignal<ReadonlyMap<string, PendingCheck>> = input<ReadonlyMap<string, PendingCheck>>(new Map());
  readonly targetUrl: InputSignal<string | null> = input<string | null>(null);

  readonly editCredential: OutputEmitterRef<PackageSource> = output<PackageSource>();
  readonly removeCredential: OutputEmitterRef<PackageSource> = output<PackageSource>();
  readonly retry: OutputEmitterRef<void> = output<void>();

  protected readonly MAX_VISIBLE_REPOSITORIES = MAX_VISIBLE_REPOSITORIES;
  protected readonly anchorId = packageSourceAnchorId;

  protected readonly _sorted: Signal<PackageSource[]> = computed(() =>
    [...this.sources()].sort((a, b) => (a.url < b.url ? -1 : a.url > b.url ? 1 : 0)),
  );

  protected readonly _unreachableCount: Signal<number> = computed(
    () => this.sources().filter((s) => s.status.kind === 'unreachable').length,
  );

  protected readonly _unreachableSummary: Signal<string> = computed(() => {
    const n = this._unreachableCount();
    return n === 1 ? '1 package source is unreachable.' : `${n} package sources are unreachable.`;
  });

  protected _slugs(repos: { slug: string }[]): string {
    return repos.map((r) => r.slug).join(', ');
  }
}
