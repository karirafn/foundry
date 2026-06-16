import { Routes } from '@angular/router';
import { setupGuard } from './features/setup/setup.guard';

export const routes: Routes = [
  { path: '', redirectTo: 'issues', pathMatch: 'full' },
  {
    path: 'issues',
    canActivate: [setupGuard],
    loadChildren: () =>
      import('./features/issues/issue.routes').then((m) => m.ISSUE_ROUTES),
  },
  {
    path: 'settings',
    loadChildren: () =>
      import('./features/settings/settings.routes').then((m) => m.SETTINGS_ROUTES),
  },
  {
    path: 'setup',
    loadChildren: () =>
      import('./features/setup/setup.routes').then((m) => m.SETUP_ROUTES),
  },
];
