import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { NgModel } from '@angular/forms';
import { SettingsService } from '../../../core/services/settings.service';
import {
  setupSettingsPage,
  flushSettings,
  API_KEY_RESPONSE,
} from '../testing/settings-page-harness';
import { SettingsPromptsComponent } from './settings-prompts';

describe('SettingsPromptsComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('should render the "Worker Prompts" section title', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsPromptsComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const headings = Array.from(el.querySelectorAll('h2'));
    const promptsHeading = headings.find(h => h.textContent?.trim() === 'Worker Prompts');

    // Assert
    expect(promptsHeading).toBeTruthy();
  });

  it('should render system prompt and worker prompt textareas with correct ids', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsPromptsComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const systemTextarea = el.querySelector('#systemPromptTemplate') as HTMLTextAreaElement;
    const workerTextarea = el.querySelector('#workerPromptTemplate') as HTMLTextAreaElement;

    // Assert
    expect(systemTextarea).toBeTruthy();
    expect(workerTextarea).toBeTruthy();
    expect(systemTextarea.tagName).toBe('TEXTAREA');
    expect(workerTextarea.tagName).toBe('TEXTAREA');
  });

  it('should have labels with correct "for" attributes matching textarea ids', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsPromptsComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const systemLabel = el.querySelector('label[for="systemPromptTemplate"]');
    const workerLabel = el.querySelector('label[for="workerPromptTemplate"]');

    // Assert
    expect(systemLabel).toBeTruthy();
    expect(workerLabel).toBeTruthy();
  });

  it('should set rows="6" on system prompt textarea and rows="4" on worker prompt textarea', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsPromptsComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const systemTextarea = el.querySelector('#systemPromptTemplate') as HTMLTextAreaElement;
    const workerTextarea = el.querySelector('#workerPromptTemplate') as HTMLTextAreaElement;

    // Assert
    expect(systemTextarea.rows).toBe(6);
    expect(workerTextarea.rows).toBe(4);
  });

  it('should set aria-describedby on system and branch textareas to include prompts-error', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsPromptsComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const systemTextarea = el.querySelector('#systemPromptTemplate') as HTMLTextAreaElement;
    const branchTextarea = el.querySelector('#branchNamingInstruction') as HTMLTextAreaElement;

    // Assert
    expect(systemTextarea.getAttribute('aria-describedby')).toContain('prompts-error');
    expect(branchTextarea.getAttribute('aria-describedby')).toContain('prompts-error');
  });

  it('should set aria-describedby on worker textarea to include both prompts-error and workerPromptHint', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsPromptsComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const workerTextarea = el.querySelector('#workerPromptTemplate') as HTMLTextAreaElement;
    const describedBy = workerTextarea.getAttribute('aria-describedby') ?? '';

    // Assert
    expect(describedBy).toContain('workerPromptHint');
    expect(describedBy).toContain('prompts-error');
  });

  it('should render a hint under the worker textarea indicating it must include {issueNumber}', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsPromptsComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const hint = el.querySelector('#workerPromptHint');

    // Assert
    expect(hint).toBeTruthy();
    expect(hint?.textContent).toContain('{issueNumber}');
  });

  it('should not set aria-invalid on textareas when there is no save error', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsPromptsComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const systemTextarea = el.querySelector('#systemPromptTemplate') as HTMLTextAreaElement;
    const workerTextarea = el.querySelector('#workerPromptTemplate') as HTMLTextAreaElement;
    const branchTextarea = el.querySelector('#branchNamingInstruction') as HTMLTextAreaElement;

    // Assert
    expect(systemTextarea.getAttribute('aria-invalid')).toBeNull();
    expect(workerTextarea.getAttribute('aria-invalid')).toBeNull();
    expect(branchTextarea.getAttribute('aria-invalid')).toBeNull();
  });

  it('should set aria-invalid="true" on all textareas when prompts save fails', () => {
    // Arrange
    const { fixture, service, httpMock } = setupSettingsPage(SettingsPromptsComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    service.updatePromptTemplates({ systemPromptTemplate: 'sys', workerPromptTemplate: 'worker', branchNamingInstruction: 'feat/branch' });
    httpMock.expectOne('/api/settings/prompts').flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect((el.querySelector('#systemPromptTemplate') as HTMLTextAreaElement).getAttribute('aria-invalid')).toBe('true');
    expect((el.querySelector('#workerPromptTemplate') as HTMLTextAreaElement).getAttribute('aria-invalid')).toBe('true');
    expect((el.querySelector('#branchNamingInstruction') as HTMLTextAreaElement).getAttribute('aria-invalid')).toBe('true');
  });

  it('should render a branch naming instruction input with id="branchNamingInstruction"', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsPromptsComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const branchInput = el.querySelector('#branchNamingInstruction') as HTMLElement;

    // Assert
    expect(branchInput).toBeTruthy();
  });

  it('should have a label with for="branchNamingInstruction"', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsPromptsComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const branchLabel = el.querySelector('label[for="branchNamingInstruction"]');

    // Assert
    expect(branchLabel).toBeTruthy();
    expect(branchLabel?.textContent?.trim()).toBe('Branch Naming Instruction');
  });

  it('should initialize branch naming instruction from loaded settings', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsPromptsComponent);
    fixture.detectChanges();
    flushSettings(httpMock, {
      ...API_KEY_RESPONSE,
      branchNamingInstruction: 'feat/{issueNumber}-description',
    });
    fixture.detectChanges();

    // Act
    const branchNgModel = fixture.debugElement.query(By.css('#branchNamingInstruction')).injector.get(NgModel);

    // Assert
    expect(branchNgModel.model).toBe('feat/{issueNumber}-description');
  });

  it('should call updatePromptTemplates with current values when Save is clicked', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsPromptsComponent);
    fixture.detectChanges();
    flushSettings(httpMock, {
      ...API_KEY_RESPONSE,
      systemPromptTemplate: 'System prompt text',
      workerPromptTemplate: 'Worker prompt text',
      branchNamingInstruction: 'feat/{issueNumber}-description',
    });
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const promptsForm = el.querySelector('.general-settings__prompts-form') as HTMLElement;
    const saveBtn = promptsForm.querySelector('.general-settings__save-btn') as HTMLButtonElement;
    saveBtn.click();

    // Assert
    const req = httpMock.expectOne('/api/settings/prompts');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({
      systemPromptTemplate: 'System prompt text',
      workerPromptTemplate: 'Worker prompt text',
      branchNamingInstruction: 'feat/{issueNumber}-description',
    });
    req.flush({
      ...API_KEY_RESPONSE,
      systemPromptTemplate: 'System prompt text',
      workerPromptTemplate: 'Worker prompt text',
      branchNamingInstruction: 'feat/{issueNumber}-description',
    });
  });

  it('should show "Prompt templates saved successfully" after a successful prompts save', () => {
    // Arrange
    const { fixture, service, httpMock } = setupSettingsPage(SettingsPromptsComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    service.updatePromptTemplates({ systemPromptTemplate: 'sys', workerPromptTemplate: 'worker', branchNamingInstruction: 'feat/branch' });
    httpMock.expectOne('/api/settings/prompts').flush({
      ...API_KEY_RESPONSE,
      systemPromptTemplate: 'sys',
      workerPromptTemplate: 'worker',
      branchNamingInstruction: 'feat/branch',
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const successEls = Array.from(el.querySelectorAll('[role="status"]'));
    const promptsSuccess = successEls.find(e => e.textContent?.includes('Prompt templates saved successfully'));
    expect(promptsSuccess).toBeTruthy();
  });

  it('should show error message when prompts save fails', () => {
    // Arrange
    const { fixture, service, httpMock } = setupSettingsPage(SettingsPromptsComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    service.updatePromptTemplates({ systemPromptTemplate: 'sys', workerPromptTemplate: 'worker', branchNamingInstruction: 'feat/branch' });
    httpMock.expectOne('/api/settings/prompts').flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const errorEl = el.querySelector('#prompts-error');
    expect(errorEl).toBeTruthy();
    expect(errorEl?.getAttribute('role')).toBe('alert');
    expect(errorEl?.textContent).toContain('Failed to save prompt templates');
  });

  it('should disable the Save button and show "Saving..." while prompts are saving', () => {
    // Arrange
    const { fixture, service, httpMock } = setupSettingsPage(SettingsPromptsComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    service.updatePromptTemplates({ systemPromptTemplate: 'sys', workerPromptTemplate: 'worker', branchNamingInstruction: 'feat/branch' });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const promptsForm = el.querySelector('.general-settings__prompts-form') as HTMLElement;
    const saveBtn = promptsForm.querySelector('.general-settings__save-btn') as HTMLButtonElement;
    expect(saveBtn.disabled).toBe(true);
    expect(saveBtn.textContent?.trim()).toBe('Saving...');

    httpMock.expectOne('/api/settings/prompts').flush({
      ...API_KEY_RESPONSE,
      systemPromptTemplate: 'sys',
      workerPromptTemplate: 'worker',
      branchNamingInstruction: 'feat/branch',
    });
  });

  it('should initialize textarea values from loaded settings', () => {
    // Arrange
    const { fixture, httpMock } = setupSettingsPage(SettingsPromptsComponent);
    fixture.detectChanges();
    flushSettings(httpMock, {
      ...API_KEY_RESPONSE,
      systemPromptTemplate: 'System prompt text',
      workerPromptTemplate: 'Worker prompt text',
    });
    fixture.detectChanges();

    // Act
    const systemNgModel = fixture.debugElement.query(By.css('#systemPromptTemplate')).injector.get(NgModel);
    const workerNgModel = fixture.debugElement.query(By.css('#workerPromptTemplate')).injector.get(NgModel);

    // Assert
    expect(systemNgModel.model).toBe('System prompt text');
    expect(workerNgModel.model).toBe('Worker prompt text');
  });

  describe('UnsavedChanges guard', () => {
    it('should return false from hasUnsavedChanges() when prompts match loaded settings', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsPromptsComponent);
      fixture.detectChanges();
      flushSettings(httpMock, {
        ...API_KEY_RESPONSE,
        systemPromptTemplate: 'sys',
        workerPromptTemplate: 'worker',
      });
      fixture.detectChanges();

      // Act & Assert
      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
    });

    it('should return true from hasUnsavedChanges() when system prompt differs from loaded value', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsPromptsComponent);
      fixture.detectChanges();
      flushSettings(httpMock, {
        ...API_KEY_RESPONSE,
        systemPromptTemplate: 'original sys',
        workerPromptTemplate: 'worker',
      });
      fixture.detectChanges();

      // Act — mutate system prompt
      const component = fixture.componentInstance as unknown as { _systemPromptValue: { set: (v: string) => void } };
      component._systemPromptValue.set('modified sys');
      fixture.detectChanges();

      // Assert
      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
    });

    it('should return true from hasUnsavedChanges() when worker prompt differs from loaded value', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsPromptsComponent);
      fixture.detectChanges();
      flushSettings(httpMock, {
        ...API_KEY_RESPONSE,
        systemPromptTemplate: 'sys',
        workerPromptTemplate: 'original worker',
      });
      fixture.detectChanges();

      // Act — mutate worker prompt
      const component = fixture.componentInstance as unknown as { _workerPromptValue: { set: (v: string) => void } };
      component._workerPromptValue.set('modified worker');
      fixture.detectChanges();

      // Assert
      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
    });

    it('should treat null service value as empty string for dirty check', () => {
      // Arrange — settings have null prompts
      const { fixture, httpMock } = setupSettingsPage(SettingsPromptsComponent);
      fixture.detectChanges();
      flushSettings(httpMock, {
        ...API_KEY_RESPONSE,
        systemPromptTemplate: null,
        workerPromptTemplate: null,
      });
      fixture.detectChanges();

      // Act & Assert — local signal initialized to '' matches null treated as ''
      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
    });

    it('should return true from hasUnsavedChanges() when branch naming instruction differs from loaded value', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsPromptsComponent);
      fixture.detectChanges();
      flushSettings(httpMock, {
        ...API_KEY_RESPONSE,
        systemPromptTemplate: 'sys',
        workerPromptTemplate: 'worker',
        branchNamingInstruction: 'original-branch',
      });
      fixture.detectChanges();

      // Act — mutate branch naming instruction
      const component = fixture.componentInstance as unknown as { _branchNamingValue: { set: (v: string) => void } };
      component._branchNamingValue.set('modified-branch');
      fixture.detectChanges();

      // Assert
      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
    });

    it('should return false from hasUnsavedChanges() when all three fields match loaded settings', () => {
      // Arrange
      const { fixture, httpMock } = setupSettingsPage(SettingsPromptsComponent);
      fixture.detectChanges();
      flushSettings(httpMock, {
        ...API_KEY_RESPONSE,
        systemPromptTemplate: 'sys',
        workerPromptTemplate: 'worker',
        branchNamingInstruction: 'feat/{issueNumber}-description',
      });
      fixture.detectChanges();

      // Act & Assert
      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
    });
  });
});
