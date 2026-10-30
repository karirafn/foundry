import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  OnInit,
  ViewChild,
  afterNextRender,
  inject,
  runInInjectionContext,
} from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs/operators';
import { SettingsService } from '../../../core/services/settings.service';

const HEADING_SELECTOR = 'h1, h2';

@Component({
  selector: 'fd-settings-layout',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  template: `
    <div class="settings-layout">
      <header class="settings-layout__header">
        <a class="settings-layout__back-link" routerLink="/issues">&larr; Back to issues</a>
        <h1 class="settings-layout__heading">Settings</h1>
      </header>

      @if (settingsService.loadError()) {
        <div class="settings-layout__load-error" role="alert">
          <span class="settings-layout__load-error-message">{{ settingsService.loadError() }}</span>
          <button
            class="settings-layout__retry-btn"
            type="button"
            (click)="settingsService.loadSettings()"
          >Retry</button>
        </div>
      }

      @if (settingsService.loading()) {
        <div class="settings-layout__loading" role="status" aria-label="Loading settings">
          <span class="settings-layout__loading-spinner" aria-hidden="true"></span>
        </div>
      }

      @if (!settingsService.loading() && !settingsService.loadError()) {
        <div class="settings-layout__body">
          <nav class="settings-layout__sidebar" aria-label="Settings navigation">
            <a
              class="settings-layout__nav-link"
              routerLink="dispatch"
              routerLinkActive="settings-layout__nav-link--active"
              #dispatchLink="routerLinkActive"
              [attr.aria-current]="dispatchLink.isActive ? 'page' : null"
            >Dispatch</a>
            <a
              class="settings-layout__nav-link"
              routerLink="worker-image"
              routerLinkActive="settings-layout__nav-link--active"
              #workerImageLink="routerLinkActive"
              [attr.aria-current]="workerImageLink.isActive ? 'page' : null"
            >Worker image</a>
            <a
              class="settings-layout__nav-link"
              routerLink="worker-environment"
              routerLinkActive="settings-layout__nav-link--active"
              #workerEnvironmentLink="routerLinkActive"
              [attr.aria-current]="workerEnvironmentLink.isActive ? 'page' : null"
            >Worker environment</a>
            <a
              class="settings-layout__nav-link"
              routerLink="claude-code"
              routerLinkActive="settings-layout__nav-link--active"
              #claudeCodeLink="routerLinkActive"
              [attr.aria-current]="claudeCodeLink.isActive ? 'page' : null"
            >Claude Code</a>
            <a
              class="settings-layout__nav-link"
              routerLink="prompts"
              routerLinkActive="settings-layout__nav-link--active"
              #promptsLink="routerLinkActive"
              [attr.aria-current]="promptsLink.isActive ? 'page' : null"
            >Prompts</a>
            <a
              class="settings-layout__nav-link"
              routerLink="provider-hosts"
              routerLinkActive="settings-layout__nav-link--active"
              #providerHostsLink="routerLinkActive"
              [attr.aria-current]="providerHostsLink.isActive ? 'page' : null"
            >Provider hosts</a>

            <hr class="settings-layout__nav-divider" aria-hidden="true" />

            <a
              class="settings-layout__nav-link"
              routerLink="accounts"
              routerLinkActive="settings-layout__nav-link--active"
              #accountsLink="routerLinkActive"
              [attr.aria-current]="accountsLink.isActive ? 'page' : null"
            >Accounts</a>
            <a
              class="settings-layout__nav-link"
              routerLink="repositories"
              routerLinkActive="settings-layout__nav-link--active"
              #repositoriesLink="routerLinkActive"
              [attr.aria-current]="repositoriesLink.isActive ? 'page' : null"
            >Repositories</a>
          </nav>
          <section #contentSection class="settings-layout__content" aria-label="Page content">
            <router-outlet />
          </section>
        </div>
      }
    </div>
  `,
  styleUrl: './settings-layout.scss',
})
export class SettingsLayoutComponent implements OnInit {
  protected readonly settingsService = inject(SettingsService);
  private readonly _router = inject(Router);
  private readonly _injector = inject(Injector);

  @ViewChild('contentSection') private readonly _contentSection?: ElementRef<HTMLElement>;

  private _firstNavigationSeen = false;

  constructor() {
    this._router.events
      .pipe(
        filter((e): e is NavigationEnd => e instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => {
        if (!this._firstNavigationSeen) {
          this._firstNavigationSeen = true;
          return;
        }

        runInInjectionContext(this._injector, () => {
          afterNextRender(() => {
            this._moveFocusToContent();
          });
        });
      });
  }

  ngOnInit(): void {
    this.settingsService.loadSettings();
  }

  private _moveFocusToContent(): void {
    const contentEl = this._contentSection?.nativeElement;
    if (!contentEl) {
      return;
    }

    const heading = contentEl.querySelector<HTMLElement>(HEADING_SELECTOR);
    if (heading) {
      heading.setAttribute('tabindex', '-1');
      heading.focus();
    } else {
      contentEl.setAttribute('tabindex', '-1');
      contentEl.focus();
    }
  }
}
