import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';
import { SettingsService } from './features/settings/settings.service';
import { AccountService } from './features/settings/accounts/account.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(),
    provideAppInitializer(async () => {
      const settingsService = inject(SettingsService);
      const accountService = inject(AccountService);
      await Promise.all([
        settingsService.loadSettings(),
        accountService.loadAccounts(),
      ]);
    }),
  ]
};
