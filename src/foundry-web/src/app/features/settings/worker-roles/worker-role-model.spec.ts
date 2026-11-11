import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { WorkerRoleModelComponent } from './worker-role-model';
import { WorkerRoleCard, WorkerRoleModelOptions, WorkerRoleService } from './worker-role.service';
import {
  buildModelOptions,
  buildWorkerRole,
  buildWorkerRoleCatalog,
} from '../testing/worker-role-catalog.fixture';

const API_URL = '/api/worker-roles';
const MODELS_URL = '/api/worker-roles/models';

/** Loads the catalog into the root service, so a saved model flows back into the role input. */
function setup(
  role: WorkerRoleCard = buildWorkerRole(),
  models: WorkerRoleModelOptions | 'fail' = buildModelOptions(),
) {
  TestBed.configureTestingModule({
    imports: [WorkerRoleModelComponent],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });
  const httpMock = TestBed.inject(HttpTestingController);
  const service = TestBed.inject(WorkerRoleService);
  service.load();
  httpMock.expectOne(API_URL).flush(buildWorkerRoleCatalog({ roles: [role] }));
  service.loadModelOptions(true);
  const modelsRequest = httpMock.expectOne((req) => req.url === MODELS_URL);
  if (models === 'fail') {
    modelsRequest.flush(null, { status: 500, statusText: 'Server Error' });
  } else {
    modelsRequest.flush(models);
  }

  const fixture = TestBed.createComponent(WorkerRoleModelComponent);
  fixture.componentRef.setInput('role', service.catalog()!.roles[0]);
  fixture.detectChanges();
  const rebind = (): void => {
    fixture.componentRef.setInput('role', service.catalog()!.roles[0]);
    fixture.detectChanges();
  };
  const el = fixture.nativeElement as HTMLElement;
  return {
    fixture,
    httpMock,
    el,
    rebind,
    select: () => el.querySelector<HTMLSelectElement>('select.role-model__select')!,
    saveButton: () => el.querySelector<HTMLButtonElement>('.role-model__save')!,
  };
}

function text(el: Element | null | undefined): string {
  return el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
}

function optionLabels(select: HTMLSelectElement): string[] {
  return Array.from(select.options).map((option) => text(option));
}

function choose(fixture: { detectChanges(): void }, select: HTMLSelectElement, value: string): void {
  select.value = value;
  select.dispatchEvent(new Event('change'));
  fixture.detectChanges();
}

describe('WorkerRoleModelComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  it('should title the editor "Model" as an h3', () => {
    // Act
    const { el } = setup();

    // Assert
    expect(text(el.querySelector('h3'))).toBe('Model');
  });

  it('should offer Default, the aliases and every listed model with deprecated ones marked', () => {
    // Act
    const { select } = setup();

    // Assert
    expect(optionLabels(select())).toEqual([
      'Default (account default)',
      'fable',
      'opus',
      'sonnet',
      'haiku',
      'opus[1m]',
      'sonnet[1m]',
      'Claude Opus 4.5 (claude-opus-4-5-20251101)',
      'Claude Sonnet 3.7 (claude-3-7-sonnet-20250219), deprecated',
    ]);
    expect(select().value).toBe('');
  });

  it('should select the saved model', () => {
    // Act
    const { select, saveButton } = setup(buildWorkerRole({ model: 'claude-opus-4-5-20251101' }));

    // Assert
    expect(select().value).toBe('claude-opus-4-5-20251101');
    expect(saveButton().disabled).toBe(true);
  });

  it('should save the chosen model for the role and confirm it', () => {
    // Arrange
    const { el, fixture, httpMock, rebind, select, saveButton } = setup();
    choose(fixture, select(), 'opus[1m]');

    // Act
    saveButton().click();
    const request = httpMock.expectOne('/api/worker-roles/implement/model');
    request.flush({ role: 'implement', model: 'opus[1m]' });
    rebind();

    // Assert
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ model: 'opus[1m]' });
    expect(select().value).toBe('opus[1m]');
    expect(text(el.querySelector('.role-model__status'))).toBe('Model saved');
    expect(saveButton().disabled).toBe(true);
  });

  it('should save Default as an empty model', () => {
    // Arrange
    const { fixture, httpMock, select, saveButton } = setup(buildWorkerRole({ model: 'opus' }));
    choose(fixture, select(), '');

    // Act
    saveButton().click();

    // Assert
    const request = httpMock.expectOne('/api/worker-roles/implement/model');
    expect(request.request.body).toEqual({ model: '' });
    request.flush({ role: 'implement', model: '' });
  });

  it('should show a rejected save under the model field', () => {
    // Arrange
    const { el, fixture, httpMock, select, saveButton } = setup();
    vi.spyOn(console, 'error').mockImplementation(() => {});
    choose(fixture, select(), 'claude-opus-4-5-20251101');
    saveButton().click();

    // Act
    httpMock.expectOne('/api/worker-roles/implement/model').flush(
      {
        detail: "'claude-opus-4-5-20251101' is not a Claude Code alias or a model listed by the Models API.",
        type: 'tag:foundry,2026:problems/Worker.RoleModelNotListed',
      },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    // Assert
    const error = el.querySelector('.role-model__error')!;
    expect(text(error)).toContain('is not a Claude Code alias');
    expect(select().getAttribute('aria-invalid')).toBe('true');
    expect(select().getAttribute('aria-describedby')).toContain(error.id);
  });

  it('should mark a saved model the list no longer offers as unavailable and keep it selected', () => {
    // Act
    const { select } = setup(buildWorkerRole({ model: 'claude-retired-1' }));

    // Assert
    expect(optionLabels(select())).toContain('claude-retired-1 (unavailable)');
    expect(select().value).toBe('claude-retired-1');
  });

  it('should still offer Default, the aliases and the saved model when the list could not be refreshed', () => {
    // Act
    const { el, select } = setup(
      buildWorkerRole({ model: 'claude-opus-4-5-20251101' }),
      buildModelOptions({
        models: [],
        refreshedAt: null,
        refreshError: 'The Models API could not be reached.',
      }),
    );

    // Assert
    const labels = optionLabels(select());
    expect(labels[0]).toBe('Default (account default)');
    expect(labels).toContain('opus');
    expect(labels).toContain('claude-opus-4-5-20251101 (saved, not in the current list)');
    expect(select().value).toBe('claude-opus-4-5-20251101');
    expect(text(el.querySelector('.role-model__refresh-error'))).toContain(
      'The model list could not be refreshed: The Models API could not be reached.',
    );
  });

  it('should fall back to the built-in aliases when the model request fails', () => {
    // Arrange
    vi.spyOn(console, 'error').mockImplementation(() => {});

    // Act
    const { el, select } = setup(buildWorkerRole(), 'fail');

    // Assert
    expect(optionLabels(select())).toEqual([
      'Default (account default)',
      'fable',
      'opus',
      'sonnet',
      'haiku',
      'opus[1m]',
      'sonnet[1m]',
    ]);
    expect(text(el.querySelector('.role-model__refresh-error'))).toContain('could not be refreshed');
  });

  it('should retry the model list refresh', () => {
    // Arrange
    const { el, fixture, httpMock, select } = setup(
      buildWorkerRole(),
      buildModelOptions({ models: [], refreshError: 'HTTP 503' }),
    );

    // Act
    el.querySelector<HTMLButtonElement>('.role-model__retry')!.click();
    httpMock.expectOne((req) => req.url === MODELS_URL).flush(buildModelOptions());
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.role-model__refresh-error')).toBeNull();
    expect(optionLabels(select())).toContain('Claude Opus 4.5 (claude-opus-4-5-20251101)');
  });

  describe('hasUnsavedChanges', () => {
    it('should report no unsaved changes while the saved model is selected', () => {
      // Act
      const { fixture } = setup();

      // Assert
      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
    });

    it('should report unsaved changes once another model is picked', () => {
      // Arrange
      const { fixture, select } = setup();

      // Act
      choose(fixture, select(), 'opus');

      // Assert
      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
    });

    it('should not report a change whose save is already in flight', () => {
      // Arrange
      const { fixture, httpMock, select, saveButton } = setup();
      choose(fixture, select(), 'opus');

      // Act
      saveButton().click();
      fixture.detectChanges();

      // Assert
      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
      httpMock.expectOne('/api/worker-roles/implement/model').flush({ role: 'implement', model: 'opus' });
    });

    it('should report no unsaved changes after the save lands', () => {
      // Arrange
      const { fixture, httpMock, rebind, select, saveButton } = setup();
      choose(fixture, select(), 'opus');
      saveButton().click();

      // Act
      httpMock.expectOne('/api/worker-roles/implement/model').flush({ role: 'implement', model: 'opus' });
      rebind();

      // Assert
      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
    });
  });
});
