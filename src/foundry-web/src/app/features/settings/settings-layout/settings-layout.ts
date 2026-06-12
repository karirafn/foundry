import { Component, ChangeDetectionStrategy, OnInit, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SettingsService } from '../settings.service';

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
          <span class="sr-only">Loading settings</span>
        </div>
      }

      @if (!settingsService.loading() && !settingsService.loadError()) {
        <div class="settings-layout__body">
          <nav class="settings-layout__sidebar" aria-label="Settings navigation">
            <a
              class="settings-layout__nav-link"
              routerLink="general"
              routerLinkActive="settings-layout__nav-link--active"
              #generalLink="routerLinkActive"
              [attr.aria-current]="generalLink.isActive ? 'page' : null"
            >General</a>
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
          <main class="settings-layout__content">
            <router-outlet />
          </main>
        </div>
      }
    </div>
  `,
  styleUrl: './settings-layout.scss',
})
export class SettingsLayoutComponent implements OnInit {
  protected readonly settingsService = inject(SettingsService);

  ngOnInit(): void {
    this.settingsService.loadSettings();
  }
}
