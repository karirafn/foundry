import { Routes } from '@angular/router';

export const SETUP_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./setup-wizard/setup-wizard').then((m) => m.SetupWizardComponent),
  },
];
