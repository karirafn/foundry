import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture } from '@angular/core/testing';
import {
  API_KEY_RESPONSE,
  flushSettings,
  setupSettingsPage,
} from '../testing/settings-page-harness';
import { SettingsWorkerEnvironmentComponent } from './settings-worker-environment';

const ENV_URL = '/api/settings/worker-environment';

interface Page {
  fixture: ComponentFixture<SettingsWorkerEnvironmentComponent>;
  httpMock: HttpTestingController;
  el: HTMLElement;
}

async function loadPage(overrides: object = {}): Promise<Page> {
  const { fixture, httpMock } = setupSettingsPage(SettingsWorkerEnvironmentComponent);
  fixture.detectChanges();
  flushSettings(httpMock, { ...API_KEY_RESPONSE, ...overrides });
  await fixture.whenStable();
  fixture.detectChanges();
  return { fixture, httpMock, el: fixture.nativeElement as HTMLElement };
}

function input(el: HTMLElement, id: string): HTMLInputElement {
  return el.querySelector(`#${id}`) as HTMLInputElement;
}

function type(page: Page, id: string, value: string): void {
  const field: HTMLInputElement = input(page.el, id);
  field.value = value;
  field.dispatchEvent(new Event('input'));
  page.fixture.detectChanges();
}

function blur(page: Page, id: string): void {
  input(page.el, id).dispatchEvent(new Event('blur'));
  page.fixture.detectChanges();
}

function saveButton(el: HTMLElement): HTMLButtonElement {
  return el.querySelector('button[type="submit"]') as HTMLButtonElement;
}

function text(el: HTMLElement, id: string): string {
  return el.querySelector(`#${id}`)?.textContent?.trim() ?? '';
}

describe('SettingsWorkerEnvironmentComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('should seed the limit inputs from the loaded settings', async () => {
    // Arrange / Act
    const page = await loadPage({ memoryLimitMb: 4096, cpuLimit: 1.5, pidsLimit: 256 });

    // Assert
    expect([input(page.el, 'memoryLimitMb').value, input(page.el, 'cpuLimit').value, input(page.el, 'pidsLimit').value])
      .toEqual(['4096', '1.5', '256']);
  });

  it('should disable Save while the form is pristine', async () => {
    // Arrange / Act
    const page = await loadPage();

    // Assert
    expect(saveButton(page.el).disabled).toBe(true);
  });

  it('should enable Save and report unsaved changes after an edit', async () => {
    // Arrange
    const page = await loadPage();

    // Act
    type(page, 'pidsLimit', '1024');

    // Assert
    expect(saveButton(page.el).disabled).toBe(false);
    expect(page.fixture.componentInstance.hasUnsavedChanges()).toBe(true);
  });

  it('should mark inputs required and expose the numeric bounds', async () => {
    // Arrange / Act
    const page = await loadPage();

    // Assert
    expect(['memoryLimitMb', 'cpuLimit', 'pidsLimit']
      .every((id) => input(page.el, id).getAttribute('aria-required') === 'true')).toBe(true);
    expect([input(page.el, 'memoryLimitMb').min, input(page.el, 'memoryLimitMb').max, input(page.el, 'cpuLimit').min, input(page.el, 'cpuLimit').max, input(page.el, 'pidsLimit').min, input(page.el, 'pidsLimit').max])
      .toEqual(['512', '65536', '0.5', '32', '64', '8192']);
  });

  it('should keep Save enabled for an invalid dirty form and reveal errors on submit without a PUT', async () => {
    // Arrange
    const page = await loadPage();
    type(page, 'memoryLimitMb', '100');

    // Act
    const enabled: boolean = !saveButton(page.el).disabled;
    saveButton(page.el).click();
    await page.fixture.whenStable();
    page.fixture.detectChanges();

    // Assert
    expect(enabled).toBe(true);
    expect(text(page.el, 'memoryLimitMb-error')).toContain('between 512 and 65536 MB');
    expect(document.activeElement).toBe(input(page.el, 'memoryLimitMb'));
  });

  it('should disable the form while saving so edits cannot be lost', async () => {
    // Arrange
    const page = await loadPage();
    type(page, 'pidsLimit', '1024');

    // Act
    saveButton(page.el).click();
    page.fixture.detectChanges();
    const disabled: boolean = (page.el.querySelector('fieldset') as HTMLFieldSetElement).disabled;
    const announced: string = page.el.querySelector('[role="status"]')?.textContent?.trim() ?? '';
    page.httpMock.expectOne(ENV_URL).flush({ ...API_KEY_RESPONSE, pidsLimit: 1024 });
    await page.fixture.whenStable();
    page.fixture.detectChanges();

    // Assert
    expect(disabled).toBe(true);
    expect(announced).toBe('Saving worker environment...');
    expect((page.el.querySelector('fieldset') as HTMLFieldSetElement).disabled).toBe(false);
  });

  it('should summarise a field-keyed server error and focus the offending control', async () => {
    // Arrange
    const page = await loadPage();
    type(page, 'cpuLimit', '4');
    saveButton(page.el).click();

    // Act
    page.httpMock.expectOne(ENV_URL).flush(
      { detail: 'CPU limit 4 is not allowed.', type: 'tag:foundry,2026:problems/Settings.InvalidCpuLimit' },
      { status: 400, statusText: 'Bad Request' },
    );
    await page.fixture.whenStable();
    page.fixture.detectChanges();
    await page.fixture.whenStable();

    // Assert
    expect(page.el.querySelector('.general-settings__save-error')?.textContent?.trim()).toBe('1 field needs attention.');
    expect(document.activeElement).toBe(input(page.el, 'cpuLimit'));
  });

  it('should clear only the edited control server error', async () => {
    // Arrange
    const page = await loadPage();
    type(page, 'cpuLimit', '4');
    saveButton(page.el).click();
    page.httpMock.expectOne(ENV_URL).flush(
      { detail: 'CPU limit 4 is not allowed.', type: 'tag:foundry,2026:problems/Settings.InvalidCpuLimit' },
      { status: 400, statusText: 'Bad Request' },
    );
    await page.fixture.whenStable();
    page.fixture.detectChanges();

    // Act
    type(page, 'pidsLimit', '1024');

    // Assert
    expect(text(page.el, 'cpuLimit-error')).toBe('CPU limit 4 is not allowed.');
  });

  it('should show a range error for an out-of-range memory', async () => {
    // Arrange
    const page = await loadPage();

    // Act
    type(page, 'memoryLimitMb', '100');
    blur(page, 'memoryLimitMb');

    // Assert
    expect(text(page.el, 'memoryLimitMb-error')).toContain('between 512 and 65536 MB');
    expect(input(page.el, 'memoryLimitMb').getAttribute('aria-invalid')).toBe('true');
  });

  it('should not set aria-invalid on a clean control', async () => {
    // Arrange / Act
    const page = await loadPage();

    // Assert
    expect(input(page.el, 'memoryLimitMb').hasAttribute('aria-invalid')).toBe(false);
  });

  it('should require a value for the CPU limit', async () => {
    // Arrange
    const page = await loadPage();

    // Act
    type(page, 'cpuLimit', '');
    blur(page, 'cpuLimit');

    // Assert
    expect(text(page.el, 'cpuLimit-error')).toBe('CPU is required.');
  });

  it('should reject a fractional PIDs limit', async () => {
    // Arrange
    const page = await loadPage();

    // Act
    type(page, 'pidsLimit', '100.5');
    blur(page, 'pidsLimit');

    // Assert
    expect(text(page.el, 'pidsLimit-error')).toBe('PIDs must be a whole number.');
  });

  it('should PUT the edited environment on submit', async () => {
    // Arrange
    const page = await loadPage();
    type(page, 'memoryLimitMb', '4096');

    // Act
    saveButton(page.el).click();
    const req = page.httpMock.expectOne(ENV_URL);
    req.flush({ ...API_KEY_RESPONSE, memoryLimitMb: 4096 });

    // Assert
    expect(req.request.body).toEqual({
      memoryLimitMb: 4096,
      cpuLimit: 2,
      pidsLimit: 512,
    });
  });

  it('should show the saved status and return to pristine after a successful save', async () => {
    // Arrange
    const page = await loadPage();
    type(page, 'memoryLimitMb', '4096');

    // Act
    saveButton(page.el).click();
    page.httpMock.expectOne(ENV_URL).flush({ ...API_KEY_RESPONSE, memoryLimitMb: 4096 });
    await page.fixture.whenStable();
    page.fixture.detectChanges();

    // Assert
    expect(page.el.querySelector('[role="status"]')?.textContent).toContain('Worker environment saved successfully');
    expect(page.fixture.componentInstance.hasUnsavedChanges()).toBe(false);
  });

  it('should attach a limit error to its limit control', async () => {
    // Arrange
    const page = await loadPage();
    type(page, 'cpuLimit', '4');

    // Act
    saveButton(page.el).click();
    page.httpMock.expectOne(ENV_URL).flush(
      { detail: 'CPU limit 4 is not allowed.', type: 'tag:foundry,2026:problems/Settings.InvalidCpuLimit' },
      { status: 400, statusText: 'Bad Request' },
    );
    await page.fixture.whenStable();
    page.fixture.detectChanges();

    // Assert
    expect(text(page.el, 'cpuLimit-error')).toBe('CPU limit 4 is not allowed.');
  });

  it('should show unmapped server errors in the form summary and clear them on edit', async () => {
    // Arrange
    const page = await loadPage();
    type(page, 'pidsLimit', '1024');
    saveButton(page.el).click();
    page.httpMock.expectOne(ENV_URL).flush(
      { detail: 'Settings not found.', type: 'tag:foundry,2026:problems/Settings.NotFound' },
      { status: 404, statusText: 'Not Found' },
    );
    await page.fixture.whenStable();
    page.fixture.detectChanges();
    const shown: string = page.el.querySelector('.general-settings__save-error')?.textContent?.trim() ?? '';

    // Act
    type(page, 'pidsLimit', '2048');

    // Assert
    expect(shown).toBe('Settings not found.');
    expect(page.el.querySelector('.general-settings__save-error')?.textContent?.trim()).toBe('');
  });

  it('should disable Save and show Saving... while the request is in flight', async () => {
    // Arrange
    const page = await loadPage();
    type(page, 'pidsLimit', '1024');

    // Act
    saveButton(page.el).click();
    page.fixture.detectChanges();
    const button: HTMLButtonElement = saveButton(page.el);
    page.httpMock.expectOne(ENV_URL).flush({ ...API_KEY_RESPONSE, pidsLimit: 1024 });

    // Assert
    expect(button.disabled).toBe(true);
    expect(button.textContent?.trim()).toBe('Saving...');
  });
});
