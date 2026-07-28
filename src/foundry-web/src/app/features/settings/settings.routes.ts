import { Routes } from '@angular/router';

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
        path: 'accounts',
        loadComponent: () =>
          import('./accounts/settings-accounts/settings-accounts').then((m) => m.SettingsAccountsComponent),
      },
      {
        path: 'repositories',
        loadComponent: () =>
          import('./repositories/settings-repositories').then((m) => m.SettingsRepositoriesComponent),
      },
    ],
  },
];
