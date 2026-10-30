import { Routes } from '@angular/router';
import { unsavedChangesGuard } from './guards/unsaved-changes.guard';

export const SETTINGS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./settings-layout/settings-layout').then((m) => m.SettingsLayoutComponent),
    children: [
      { path: '', redirectTo: 'general', pathMatch: 'full' },
      {
        path: 'general',
        loadComponent: () =>
          import('./general/settings-general').then((m) => m.SettingsGeneralComponent),
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
