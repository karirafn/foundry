import { ChangeDetectionStrategy, Component, OnInit, Signal, inject } from '@angular/core';
import { SpinnerComponent } from '../../../shared/components/spinner/spinner';
import { WorkerRoleCardComponent } from './worker-role-card';
import { WorkerRoleCatalog, WorkerRoleService } from './worker-role.service';

/** The worker role catalog shipped in the worker image, with each role's editable model (ADR 0095). */
@Component({
  selector: 'fd-settings-worker-roles',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SpinnerComponent, WorkerRoleCardComponent],
  template: `
    <div class="general-settings">
      <section class="general-settings__section" aria-labelledby="worker-roles-title">
        <h2 id="worker-roles-title" class="general-settings__section-title">Worker roles</h2>
        <p class="general-settings__section-description">
          Roles ship in the worker image. Each card shows the prompts, plugin skills and agents a role
          runs with, as read from the image. The model is the one setting you can change per role.
        </p>

        @switch (roleService.status()) {
          @case ('notReady') {
            <div class="general-settings__field" role="status">
              <p
                class="general-settings__callout general-settings__callout--warning worker-roles__not-ready"
              >
                <span class="general-settings__callout-icon" aria-hidden="true">⚠</span>
                <span>
                  <strong class="general-settings__callout-lead">Catalog not loaded yet:</strong>
                  it is read during the worker image build. Check back once the build finishes.
                </span>
              </p>
              <div class="general-settings__image-actions">
                <button class="general-settings__retry-btn" type="button" (click)="reload()">
                  Retry
                </button>
              </div>
            </div>
          }
          @case ('error') {
            <div class="general-settings__image-status" role="alert">
              <span class="general-settings__save-error worker-roles__error">{{
                roleService.loadError()
              }}</span>
              <button class="general-settings__retry-btn" type="button" (click)="reload()">
                Retry
              </button>
            </div>
          }
          @case ('loaded') {
            @if (catalog(); as c) {
              @if (!c.image.verified) {
                <p
                  class="general-settings__callout general-settings__callout--warning worker-roles__unverified"
                  role="note"
                >
                  <span class="general-settings__callout-icon" aria-hidden="true">⚠</span>
                  <span>
                    <strong class="general-settings__callout-lead">Unverified:</strong>
                    image building is disabled, so this catalog was read from disk and may differ
                    from the roles in the operator-supplied image <code>{{ c.image.tag }}</code
                    >.
                  </span>
                </p>
              }
              @if (c.roles.length === 0) {
                <p class="general-settings__section-description">
                  The worker image declares no roles.
                </p>
              }
            }
          }
          @default {
            <div
              class="general-settings__image-status"
              role="status"
              aria-label="Loading worker roles"
            >
              <fd-spinner [size]="20" />
            </div>
          }
        }
      </section>

      @if (roleService.status() === 'loaded') {
        @if (catalog(); as c) {
          @for (role of c.roles; track role.name) {
            <fd-worker-role-card class="general-settings__section" [role]="role" [catalog]="c" />
          }
        }
      }
    </div>
  `,
  styleUrl: './settings-worker-roles.scss',
})
export class SettingsWorkerRolesComponent implements OnInit {
  protected readonly roleService = inject(WorkerRoleService);
  protected readonly catalog: Signal<WorkerRoleCatalog | null> = this.roleService.catalog;

  ngOnInit(): void {
    this.roleService.load();
    // The model list is refreshed from the Models API each time the role view opens.
    this.roleService.loadModelOptions(true);
  }

  protected reload(): void {
    this.roleService.load();
  }
}
