import { Routes } from '@angular/router';
import { unsavedChangesGuard } from './guards/unsaved-changes.guard';

export const SETTINGS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./settings-layout/settings-layout').then((m) => m.SettingsLayoutComponent),
    children: [
      { path: '', redirectTo: 'dispatch', pathMatch: 'full' },
      { path: 'general', redirectTo: 'dispatch', pathMatch: 'full' },
      {
        path: 'dispatch',
        title: 'Dispatch',
        loadComponent: () =>
          import('./dispatch/settings-dispatch').then((m) => m.SettingsDispatchComponent),
        canDeactivate: [unsavedChangesGuard],
      },
      {
        path: 'worker-environment',
        title: 'Worker environment',
        loadComponent: () =>
          import('./worker-environment/settings-worker-environment').then(
            (m) => m.SettingsWorkerEnvironmentComponent,
          ),
        canDeactivate: [unsavedChangesGuard],
      },
      {
        path: 'worker-image',
        title: 'Worker image',
        loadComponent: () =>
          import('./worker-image/settings-worker-image').then(
            (m) => m.SettingsWorkerImageComponent,
          ),
        canDeactivate: [unsavedChangesGuard],
      },
      {
        path: 'claude-code',
        title: 'Claude Code',
        loadComponent: () =>
          import('./claude-code/settings-claude-code').then(
            (m) => m.SettingsClaudeCodeComponent,
          ),
        canDeactivate: [unsavedChangesGuard],
      },
      {
        path: 'prompts',
        title: 'Prompts',
        loadComponent: () =>
          import('./prompts/settings-prompts').then((m) => m.SettingsPromptsComponent),
        canDeactivate: [unsavedChangesGuard],
      },
      {
        path: 'provider-hosts',
        title: 'Provider hosts',
        loadComponent: () =>
          import('./provider-hosts/settings-provider-hosts').then(
            (m) => m.SettingsProviderHostsComponent,
          ),
        canDeactivate: [unsavedChangesGuard],
      },
      {
        path: 'accounts',
        title: 'Accounts',
        loadComponent: () =>
          import('./accounts/settings-accounts/settings-accounts').then((m) => m.SettingsAccountsComponent),
      },
      {
        path: 'repositories',
        title: 'Repositories',
        loadComponent: () =>
          import('./repositories/settings-repositories/settings-repositories').then((m) => m.SettingsRepositoriesComponent),
      },
      {
        path: 'repositories/:repositoryId',
        title: 'Repository',
        loadComponent: () =>
          import('./repositories/repository-page/repository-page').then((m) => m.RepositoryPageComponent),
      },
    ],
  },
];
