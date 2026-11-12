import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  OnInit,
  Signal,
  afterNextRender,
  computed,
  effect,
  inject,
  untracked,
  viewChild,
} from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs/operators';
import { SpinnerComponent } from '../../../../shared/components/spinner/spinner';
import { UnsavedChanges } from '../../guards/unsaved-changes.guard';
import { PromptAssemblyComponent } from '../prompt-assembly';
import { PromptTextComponent } from '../prompt-text';
import { RoleCapabilityComponent, capabilityHint } from '../role-shared/role-capability';
import { RoleImageComponent } from '../role-shared/role-image';
import { WorkerRoleModelComponent } from '../worker-role-model';
import { WorkerRoleCard, WorkerRoleCatalog, WorkerRoleService } from '../worker-role.service';
import { RolePluginsComponent } from './role-plugins';

type ViewState = 'loading' | 'not-ready' | 'error' | 'not-found' | 'loaded';

const STATUS_ANNOUNCEMENTS: Readonly<Record<ViewState, string>> = {
  loading: 'Loading worker role',
  'not-ready': 'Worker roles are not ready yet',
  error: 'Could not load worker roles',
  'not-found': 'Worker role not found',
  loaded: '',
};

const LOAD_ERROR_FALLBACK = 'Failed to load worker roles';

/** One worker role: a summary rail with its editable model beside its prompts, plugins and verdict schema. */
@Component({
  selector: 'fd-role-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    SpinnerComponent,
    PromptAssemblyComponent,
    PromptTextComponent,
    RoleCapabilityComponent,
    RoleImageComponent,
    RolePluginsComponent,
    WorkerRoleModelComponent,
  ],
  template: `
    <span class="sr-only" role="status" aria-live="polite" aria-atomic="true">{{
      statusAnnouncement()
    }}</span>

    <section
      class="role-detail"
      [attr.aria-labelledby]="viewState() === 'loaded' ? 'role-detail-heading' : null"
    >
      @switch (viewState()) {
        @case ('loading') {
          <div class="role-detail__loading">
            <fd-spinner [size]="24" />
            <span class="sr-only">Loading worker role</span>
          </div>
        }
        @case ('not-ready') {
          <p class="role-detail__callout" tabindex="-1" #focusTarget>
            <span class="role-detail__callout-icon" aria-hidden="true">⚠</span>
            <span>
              <strong class="role-detail__callout-lead">Catalog not loaded yet:</strong>
              it is read during the worker image build. Check back once the build finishes.
            </span>
          </p>
          <div class="role-detail__actions">
            <button class="role-detail__retry-btn" type="button" (click)="retry()">Retry</button>
            <a class="role-detail__back-link" routerLink="/settings/roles">Back to worker roles</a>
          </div>
        }
        @case ('error') {
          <div class="role-detail__load-error" role="alert">
            <p class="role-detail__load-error-message" tabindex="-1" #focusTarget>
              {{ loadErrorText() }}
            </p>
            <div class="role-detail__actions">
              <button class="role-detail__retry-btn" type="button" (click)="retry()">Retry</button>
              <a class="role-detail__back-link" routerLink="/settings/roles">Back to worker roles</a>
            </div>
          </div>
        }
        @case ('not-found') {
          <div class="role-detail__not-found">
            <h2 class="role-detail__not-found-heading" tabindex="-1" #focusTarget>Role not found</h2>
            <p class="role-detail__not-found-description">
              The worker image declares no role named <code>{{ roleName() }}</code>. The image may
              have been rebuilt without it, or the link is out of date.
            </p>
            <a
              class="role-detail__back-link role-detail__back-link--button"
              routerLink="/settings/roles"
              >Back to worker roles</a
            >
          </div>
        }
        @case ('loaded') {
          @if (loaded(); as view) {
            <a class="role-detail__back-link" routerLink="/settings/roles">Back to worker roles</a>
            <header class="role-detail__header">
              <div class="role-detail__title-row">
                <h2 id="role-detail-heading" class="role-detail__heading" tabindex="-1" #focusTarget>
                  {{ view.role.name }}
                </h2>
                <fd-role-capability [capability]="view.role.capability" />
              </div>
              @if (view.role.purpose) {
                <p class="role-detail__purpose">{{ view.role.purpose }}</p>
              }
              @if (hint(); as h) {
                <p class="role-detail__capability-hint">{{ h }}</p>
              }
            </header>

            <div class="role-detail__body">
              <aside class="role-detail__rail" aria-label="Role summary">
                <fd-worker-role-model class="role-detail__rail-block" [role]="view.role" />
                <dl class="role-detail__facts role-detail__rail-block">
                  <div>
                    <dt>Verdict</dt>
                    <dd>{{ view.role.verdictSchema === null ? 'No' : 'Yes' }}</dd>
                  </div>
                  <div>
                    <dt>Skills</dt>
                    <dd>{{ skillsFact() }}</dd>
                  </div>
                  <div>
                    <dt>Agents</dt>
                    <dd>{{ agentsFact() }}</dd>
                  </div>
                </dl>
                <section class="role-detail__rail-block" aria-labelledby="role-detail-image-title">
                  <h3 id="role-detail-image-title" class="role-detail__rail-title">Worker image</h3>
                  <fd-role-image [image]="view.catalog.image" />
                </section>
              </aside>

              <div class="role-detail__content">
                <p class="role-detail__hint">
                  Placeholders such as
                  <span class="role-detail__placeholder-sample">&#123;issueNumber&#125;</span> are
                  filled in by the worker at dispatch time.
                </p>
                <fd-prompt-assembly
                  class="role-detail__block"
                  [sections]="view.catalog.promptSections"
                  [rolePromptSlot]="view.catalog.rolePromptSlot"
                  [principlesSlot]="view.catalog.principlesSlot"
                  [principleSections]="view.catalog.principleSections"
                  [roleName]="view.role.name"
                  [roleSystemPrompt]="view.role.systemPrompt"
                  [rolePrincipleSections]="view.role.principleSections"
                  [rolePrinciplesText]="view.role.principles"
                />
                <section class="role-detail__block" aria-labelledby="role-detail-task-title">
                  <h3 id="role-detail-task-title" class="role-detail__block-title">Task prompt</h3>
                  <fd-prompt-text [text]="view.role.taskPrompt" />
                </section>
                <fd-role-plugins
                  class="role-detail__block"
                  [role]="view.role"
                  [shared]="view.catalog.shared"
                />
                <section class="role-detail__block" aria-labelledby="role-detail-verdict-title">
                  <h3 id="role-detail-verdict-title" class="role-detail__block-title">
                    Verdict schema
                  </h3>
                  @if (verdictSchema(); as schema) {
                    <details class="role-detail__disclosure">
                      <summary>JSON schema</summary>
                      <pre
                        class="role-detail__schema"
                        role="region"
                        tabindex="0"
                        [attr.aria-label]="view.role.name + ' verdict schema'"
                      ><code>{{ schema }}</code></pre>
                    </details>
                  } @else {
                    <p class="role-detail__hint role-detail__no-schema">
                      None. This role returns no verdict.
                    </p>
                  }
                </section>
              </div>
            </div>
          }
        }
      }
    </section>
  `,
  styleUrl: './role-detail.scss',
})
export class RoleDetailComponent implements OnInit, UnsavedChanges {
  private readonly _roleService = inject(WorkerRoleService);
  private readonly _route = inject(ActivatedRoute);
  private readonly _injector = inject(Injector);

  private readonly _model = viewChild(WorkerRoleModelComponent);
  private readonly _focusTarget = viewChild<ElementRef<HTMLElement>>('focusTarget');

  protected readonly roleName: Signal<string | null> = toSignal(
    this._route.paramMap.pipe(map((params) => params.get('roleName'))),
    { initialValue: null },
  );

  /** The role and its catalog together, so the template never sees one without the other. */
  protected readonly loaded: Signal<{ role: WorkerRoleCard; catalog: WorkerRoleCatalog } | null> =
    computed(() => {
      const catalog = this._roleService.catalog();
      const role = catalog?.roles.find((r) => r.name === this.roleName());
      return catalog && role ? { role, catalog } : null;
    });

  /** A cached role keeps the page rendered whatever the status, so a refresh in flight doesn't blank it. */
  protected readonly viewState: Signal<ViewState> = computed(() => {
    if (this.loaded() !== null) {
      return 'loaded';
    }
    switch (this._roleService.status()) {
      case 'idle':
      case 'loading':
        return 'loading';
      case 'notReady':
        return 'not-ready';
      case 'error':
        return 'error';
      case 'loaded':
        return 'not-found';
    }
  });

  protected readonly statusAnnouncement: Signal<string> = computed(
    () => STATUS_ANNOUNCEMENTS[this.viewState()],
  );

  protected readonly loadErrorText: Signal<string> = computed(
    () => this._roleService.loadError() ?? LOAD_ERROR_FALLBACK,
  );

  protected readonly hint: Signal<string | null> = computed(() => {
    const view = this.loaded();
    return view ? capabilityHint(view.role.capability) : null;
  });

  protected readonly skillsFact: Signal<string> = computed(() => {
    const view = this.loaded();
    return view ? pluginFact(view.role.skills.length, view.catalog.shared.skills.length) : '';
  });

  protected readonly agentsFact: Signal<string> = computed(() => {
    const view = this.loaded();
    return view ? pluginFact(view.role.agents.length, view.catalog.shared.agents.length) : '';
  });

  protected readonly verdictSchema: Signal<string | null> = computed(() =>
    formatVerdictSchema(this.loaded()?.role.verdictSchema ?? null),
  );

  constructor() {
    effect(() => {
      const state = this.viewState();
      if (state === 'loading') {
        return;
      }
      untracked(() =>
        afterNextRender(
          () => {
            // The role heading only takes focus when it was lost (the spinner or Retry it was on went away);
            // in-app navigation already focuses it through the settings layout.
            if (state === 'loaded' && !isFocusLost()) {
              return;
            }
            this._focusTarget()?.nativeElement.focus();
          },
          { injector: this._injector },
        ),
      );
    });
  }

  ngOnInit(): void {
    if (this._roleService.status() !== 'loaded') {
      this._roleService.load();
    }
    // The model list is refreshed from the Models API each time a role opens.
    this._roleService.loadModelOptions(true);
  }

  hasUnsavedChanges(): boolean {
    return this._model()?.hasUnsavedChanges() ?? false;
  }

  protected retry(): void {
    this._roleService.load();
  }
}

function isFocusLost(): boolean {
  return document.activeElement === null || document.activeElement === document.body;
}

/** Own count `own`, shared count `shared`: "N + M shared", "N" with none shared, or "None" with neither. */
function pluginFact(own: number, shared: number): string {
  if (own === 0 && shared === 0) {
    return 'None';
  }
  return shared === 0 ? `${own}` : `${own} + ${shared} shared`;
}

/** Pretty-prints the schema when it is valid JSON; otherwise returns it unchanged. */
function formatVerdictSchema(schema: string | null): string | null {
  if (schema === null) {
    return null;
  }
  try {
    return JSON.stringify(JSON.parse(schema), null, 2);
  } catch {
    return schema;
  }
}
