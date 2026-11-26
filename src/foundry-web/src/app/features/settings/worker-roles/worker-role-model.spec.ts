import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { WorkerRoleModelComponent } from './worker-role-model';
import { ImplementDemand, WorkerRoleCard, WorkerRoleModelOptions, WorkerRoleService } from './worker-role.service';
import {
  buildImplementRole,
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
  demand: ImplementDemand | null = null,
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
  fixture.componentRef.setInput('demand', demand);
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

  describe('with a demand', () => {
    const ROUTINE_URL = '/api/worker-roles/implement/demand-models/routine';
    const DEMANDING_URL = '/api/worker-roles/implement/demand-models/demanding';

    function setupDemand(
      demand: ImplementDemand,
      role: WorkerRoleCard = buildImplementRole(),
      models: WorkerRoleModelOptions | 'fail' = buildModelOptions(),
    ) {
      const ctx = setup(role, models, demand);
      return {
        ...ctx,
        effort: () => ctx.el.querySelector<HTMLSelectElement>('select.role-model__select--effort')!,
        modelError: () => ctx.el.querySelector<HTMLElement>('.role-model__error--model')!,
        effortError: () => ctx.el.querySelector<HTMLElement>('.role-model__error--effort')!,
      };
    }

    function rejectWith(httpMock: HttpTestingController, url: string, code: string, detail: string): void {
      httpMock
        .expectOne(url)
        .flush({ detail, type: `tag:foundry,2026:problems/${code}` }, { status: 400, statusText: 'Bad Request' });
    }

    it.each([
      ['routine', 'Routine model'],
      ['demanding', 'Demanding model'],
    ] as const)('should title the %s setting "%s" and label its section with it', (demand, title) => {
      // Act
      const { el } = setupDemand(demand);

      // Assert
      const heading = el.querySelector('h3')!;
      expect(text(heading)).toBe(title);
      expect(el.querySelector('section')!.getAttribute('aria-labelledby')).toBe(heading.id);
    });

    it('should explain each demand and the effort in its hint', () => {
      // Act
      const routine = setupDemand('routine');
      const routineHint = text(routine.el.querySelector('.role-model__hint'));
      TestBed.resetTestingModule();
      const demanding = setupDemand('demanding');
      const demandingHint = text(demanding.el.querySelector('.role-model__hint'));

      // Assert
      expect(routineHint).toBe(
        'Used for every issue triage judges routine; Default leaves the model and effort to the account default. ' +
          'Effort sets how much the model reasons; Default leaves it to the model.',
      );
      expect(demandingHint).toBe(
        'Used for issues triage judges demanding, and for a routine issue whose code review or CI finds defects. ' +
          'Effort sets how much the model reasons; Default leaves it to the model.',
      );
    });

    it('should suffix every id with the demand', () => {
      // Act
      const { el, select, effort, modelError, effortError } = setupDemand('routine');

      // Assert
      expect(select().id).toBe('worker-role-implement-routine-model');
      expect(effort().id).toBe('worker-role-implement-routine-effort');
      expect(el.querySelector('h3')!.id).toBe('worker-role-implement-routine-model-title');
      expect(select().getAttribute('aria-describedby')).toBe(
        `worker-role-implement-routine-model-hint ${modelError().id}`,
      );
      expect(effort().getAttribute('aria-describedby')).toBe(
        `worker-role-implement-routine-model-hint ${effortError().id}`,
      );
      expect(modelError().id).not.toBe(effortError().id);
    });

    it('should label the model select for screen readers and the effort select visibly', () => {
      // Act
      const { el, select, effort } = setupDemand('demanding');

      // Assert
      const modelLabel = el.querySelector(`label[for="${select().id}"]`)!;
      expect(modelLabel.classList).toContain('sr-only');
      expect(text(modelLabel)).toBe('Model for the implement role, demanding demand');
      const effortLabel = el.querySelector(`label[for="${effort().id}"]`)!;
      expect(effortLabel.classList).toContain('role-model__label');
      expect(text(effortLabel)).toBe('Effort');
    });

    it('should offer Default and every effort level, with Default as the empty value', () => {
      // Act
      const { effort } = setupDemand('routine');

      // Assert
      expect(optionLabels(effort())).toEqual(['Default (model default)', 'low', 'medium', 'high', 'xhigh']);
      expect(Array.from(effort().options).map((o) => o.value)).toEqual(['', 'low', 'medium', 'high', 'xhigh']);
    });

    it("should select the demand's saved model and effort, not the role's", () => {
      // Act
      const { select, effort, saveButton } = setupDemand(
        'demanding',
        buildImplementRole({ model: 'sonnet', effort: 'low' }, { model: 'opus', effort: 'xhigh' }),
      );

      // Assert
      expect(select().value).toBe('opus');
      expect(effort().value).toBe('xhigh');
      expect(saveButton().disabled).toBe(true);
    });

    it('should put the controls in tab order model, effort, Save', () => {
      // Act
      const { el, select, effort, saveButton } = setupDemand('routine');

      // Assert
      const focusable = Array.from(el.querySelectorAll('select, button'));
      expect(focusable).toEqual([select(), effort(), saveButton()]);
    });

    it('should enable Save once only the effort changes', () => {
      // Arrange
      const { fixture, effort, saveButton } = setupDemand('routine');

      // Act
      choose(fixture, effort(), 'medium');

      // Assert
      expect(saveButton().disabled).toBe(false);
      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
    });

    it('should save the model and effort for the demand, never the role-wide model, and confirm it', () => {
      // Arrange
      const { el, fixture, httpMock, rebind, select, effort, saveButton } = setupDemand('routine');
      choose(fixture, select(), 'sonnet');
      choose(fixture, effort(), 'high');

      // Act
      saveButton().click();
      const request = httpMock.expectOne(ROUTINE_URL);
      request.flush({ demand: 'routine', model: 'sonnet', effort: 'high' });
      rebind();

      // Assert
      httpMock.expectNone('/api/worker-roles/implement/model');
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({ model: 'sonnet', effort: 'high' });
      expect(select().value).toBe('sonnet');
      expect(effort().value).toBe('high');
      expect(text(el.querySelector('.role-model__status'))).toBe('Routine model saved');
      expect(el.querySelector('.role-model__status')!.getAttribute('role')).toBe('status');
      expect(saveButton().disabled).toBe(true);
      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
    });

    it('should clear the saved confirmation on the next edit', () => {
      // Arrange
      const { el, fixture, httpMock, rebind, effort, saveButton } = setupDemand('demanding');
      choose(fixture, effort(), 'low');
      saveButton().click();
      httpMock.expectOne(DEMANDING_URL).flush({ demand: 'demanding', model: '', effort: 'low' });
      rebind();
      expect(text(el.querySelector('.role-model__status'))).toBe('Demanding model saved');

      // Act
      choose(fixture, effort(), 'medium');

      // Assert
      expect(text(el.querySelector('.role-model__status'))).toBe('');
    });

    it('should disable its selects and Save and read "Saving..." while its save is in flight', () => {
      // Arrange
      const { fixture, httpMock, select, effort, saveButton } = setupDemand('routine');
      choose(fixture, select(), 'opus');

      // Act
      saveButton().click();
      fixture.detectChanges();

      // Assert
      expect(select().disabled).toBe(true);
      expect(effort().disabled).toBe(true);
      expect(saveButton().disabled).toBe(true);
      expect(text(saveButton())).toBe('Saving...');
      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
      httpMock.expectOne(ROUTINE_URL).flush({ demand: 'routine', model: 'opus', effort: '' });
    });

    it('should stay editable while the other demand saves', () => {
      // Arrange
      const { fixture, httpMock, select, effort, saveButton } = setupDemand('routine');

      // Act
      TestBed.inject(WorkerRoleService).saveDemandModel('implement', 'demanding', 'opus', '');
      fixture.detectChanges();

      // Assert
      expect(select().disabled).toBe(false);
      expect(effort().disabled).toBe(false);
      expect(text(saveButton())).toBe('Save');
      httpMock.expectOne(DEMANDING_URL).flush({ demand: 'demanding', model: 'opus', effort: '' });
    });

    it.each(['Worker.RoleModelNotListed', 'Worker.RoleModelTooLong'])(
      'should show %s under the model select and mark it invalid',
      (code) => {
        // Arrange
        const { fixture, httpMock, select, effort, saveButton, modelError, effortError } = setupDemand('routine');
        vi.spyOn(console, 'error').mockImplementation(() => {});
        choose(fixture, select(), 'claude-opus-4-5-20251101');
        saveButton().click();

        // Act
        rejectWith(httpMock, ROUTINE_URL, code, 'The model is rejected.');
        fixture.detectChanges();

        // Assert
        expect(text(modelError())).toBe('The model is rejected.');
        expect(modelError().getAttribute('role')).toBe('alert');
        expect(text(effortError())).toBe('');
        expect(select().getAttribute('aria-invalid')).toBe('true');
        expect(effort().getAttribute('aria-invalid')).toBeNull();
      },
    );

    it('should show Worker.TierEffortInvalid under the effort select and mark it invalid', () => {
      // Arrange
      const { fixture, httpMock, select, effort, saveButton, modelError, effortError } = setupDemand('routine');
      vi.spyOn(console, 'error').mockImplementation(() => {});
      choose(fixture, effort(), 'high');
      saveButton().click();

      // Act
      rejectWith(httpMock, ROUTINE_URL, 'Worker.TierEffortInvalid', "'high' is not an effort level.");
      fixture.detectChanges();

      // Assert
      expect(text(effortError())).toBe("'high' is not an effort level.");
      expect(effortError().getAttribute('role')).toBe('alert');
      expect(text(modelError())).toBe('');
      expect(effort().getAttribute('aria-invalid')).toBe('true');
      expect(select().getAttribute('aria-invalid')).toBeNull();
    });

    it('should show any other failure under the model row', () => {
      // Arrange
      const { fixture, httpMock, select, saveButton, modelError, effortError } = setupDemand('demanding');
      vi.spyOn(console, 'error').mockImplementation(() => {});
      choose(fixture, select(), 'opus');
      saveButton().click();

      // Act
      httpMock.expectOne(DEMANDING_URL).flush(null, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      // Assert
      expect(text(modelError())).toBe('Failed to save the model');
      expect(text(effortError())).toBe('');
    });

    it("should not show the other demand's error", () => {
      // Arrange
      const { fixture, httpMock, modelError } = setupDemand('routine');
      vi.spyOn(console, 'error').mockImplementation(() => {});

      // Act
      TestBed.inject(WorkerRoleService).saveDemandModel('implement', 'demanding', 'claude-typo', '');
      rejectWith(httpMock, DEMANDING_URL, 'Worker.RoleModelNotListed', 'Not listed.');
      fixture.detectChanges();

      // Assert
      expect(text(modelError())).toBe('');
    });

    it("should mark a demand's saved model the list no longer offers as unavailable", () => {
      // Act
      const { select } = setupDemand('routine', buildImplementRole({ model: 'claude-retired-1' }));

      // Assert
      expect(optionLabels(select())).toContain('claude-retired-1 (unavailable)');
      expect(select().value).toBe('claude-retired-1');
    });

    it('should show the refresh-error callout on the routine setting only', () => {
      // Arrange
      const options = buildModelOptions({ models: [], refreshError: 'HTTP 503' });

      // Act
      const routine = setupDemand('routine', buildImplementRole(), options);
      const routineCallout = routine.el.querySelector('.role-model__refresh-error');
      TestBed.resetTestingModule();
      const demanding = setupDemand('demanding', buildImplementRole(), options);

      // Assert
      expect(routineCallout).not.toBeNull();
      expect(demanding.el.querySelector('.role-model__refresh-error')).toBeNull();
    });
  });
});
