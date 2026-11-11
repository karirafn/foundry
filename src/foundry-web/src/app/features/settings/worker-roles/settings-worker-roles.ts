import { ChangeDetectionStrategy, Component, OnInit, Signal, inject } from '@angular/core';
import { SpinnerComponent } from '../../../shared/components/spinner/spinner';
import { RoleCardGridComponent } from './role-card/role-card-grid';
import { RoleImageComponent } from './role-shared/role-image';
import { WorkerRoleCatalog, WorkerRoleService } from './worker-role.service';

/** The worker role catalog shipped in the worker image, as a grid of cards that open each role (ADR 0095). */
@Component({
  selector: 'fd-settings-worker-roles',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SpinnerComponent, RoleCardGridComponent, RoleImageComponent],
  template: `
    <div class="general-settings">
      <section class="general-settings__section" aria-labelledby="worker-roles-title">
        <h2 id="worker-roles-title" class="general-settings__section-title">Worker roles</h2>
        <p class="general-settings__section-description">
          Roles ship in the worker image. Open a role to read its prompts, skills, agents and verdict
          schema, or to change the model it runs on.
        </p>

        <!-- A cached catalog stays on screen while it reloads, so coming back from a role keeps the grid. -->
        @if (catalog(); as c) {
          <fd-role-image [image]="c.image" />
          @if (c.roles.length === 0) {
            <p class="general-settings__section-description">The worker image declares no roles.</p>
          } @else {
            <fd-role-card-grid [roles]="c.roles" />
          }
        } @else {
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
        }
      </section>
    </div>
  `,
  styleUrl: './settings-worker-roles.scss',
})
export class SettingsWorkerRolesComponent implements OnInit {
  protected readonly roleService = inject(WorkerRoleService);
  protected readonly catalog: Signal<WorkerRoleCatalog | null> = this.roleService.catalog;

  ngOnInit(): void {
    this.roleService.load();
  }

  protected reload(): void {
    this.roleService.load();
  }
}
