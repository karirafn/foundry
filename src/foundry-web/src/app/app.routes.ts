import { Routes } from '@angular/router';
import { setupGuard } from './features/setup/setup.guard';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    canActivate: [setupGuard],
    loadChildren: () =>
      import('./features/board/board.routes').then((m) => m.BOARD_ROUTES),
  },
  {
    path: 'issues',
    canActivate: [setupGuard],
    loadChildren: () =>
      import('./features/issues/issue.routes').then((m) => m.ISSUE_ROUTES),
  },
  {
    path: 'floor',
    canActivate: [setupGuard],
    loadChildren: () =>
      import('./features/floor/floor.routes').then((m) => m.FLOOR_ROUTES),
  },
  {
    path: 'settings',
    canActivate: [setupGuard],
    loadChildren: () =>
      import('./features/settings/settings.routes').then((m) => m.SETTINGS_ROUTES),
  },
  {
    path: 'setup',
    loadChildren: () =>
      import('./features/setup/setup.routes').then((m) => m.SETUP_ROUTES),
  },
];
