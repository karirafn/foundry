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
        loadComponent: () =>
          import('./dispatch/settings-dispatch').then((m) => m.SettingsDispatchComponent),
        canDeactivate: [unsavedChangesGuard],
      },
      {
        path: 'worker-environment',
        loadComponent: () =>
          import('./worker-environment/settings-worker-environment').then(
            (m) => m.SettingsWorkerEnvironmentComponent,
          ),
        canDeactivate: [unsavedChangesGuard],
      },
      {
        path: 'worker-image',
        loadComponent: () =>
          import('./worker-image/settings-worker-image').then(
            (m) => m.SettingsWorkerImageComponent,
          ),
        canDeactivate: [unsavedChangesGuard],
      },
      {
        path: 'accounts',
        loadComponent: () =>
          import('./accounts/settings-accounts/settings-accounts').then((m) => m.SettingsAccountsComponent),
      },
      {
        path: 'repositories',
        loadComponent: () =>
          import('./repositories/settings-repositories/settings-repositories').then((m) => m.SettingsRepositoriesComponent),
      },
      {
        path: 'repositories/:repositoryId',
        loadComponent: () =>
          import('./repositories/repository-page/repository-page').then((m) => m.RepositoryPageComponent),
      },
    ],
  },
];
