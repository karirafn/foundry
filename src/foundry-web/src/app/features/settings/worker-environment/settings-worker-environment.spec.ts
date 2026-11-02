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
const SKILLS_MOUNT = { containerPath: '/work/skills', hostPath: '/srv/skills', readOnly: true };

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

function click(page: Page, selector: string): void {
  (page.el.querySelector(selector) as HTMLElement).click();
  page.fixture.detectChanges();
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

  it('should render one row per loaded mount', async () => {
    // Arrange / Act
    const page = await loadPage({ mounts: [SKILLS_MOUNT] });

    // Assert
    expect(input(page.el, 'containerPath-0').value).toBe('/work/skills');
    expect(input(page.el, 'hostPath-0').value).toBe('/srv/skills');
    expect(input(page.el, 'readOnly-0').checked).toBe(true);
  });

  it('should show the empty-state copy when there are no mounts', async () => {
    // Arrange / Act
    const page = await loadPage();

    // Assert
    expect(page.el.textContent).toContain('No mounts configured');
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

  it('should add a read-only mount row and focus its container path', async () => {
    // Arrange
    const page = await loadPage();

    // Act
    click(page, '#addMount');
    await page.fixture.whenStable();

    // Assert
    expect(input(page.el, 'readOnly-0').checked).toBe(true);
    expect(document.activeElement).toBe(input(page.el, 'containerPath-0'));
  });

  it('should remove a mount row and focus Add mount when the list empties', async () => {
    // Arrange
    const page = await loadPage({ mounts: [SKILLS_MOUNT] });

    // Act
    click(page, '#removeMount-0');
    await page.fixture.whenStable();

    // Assert
    expect(input(page.el, 'containerPath-0')).toBeNull();
    expect(document.activeElement).toBe(page.el.querySelector('#addMount'));
  });

  it('should label the remove button with its row number', async () => {
    // Arrange / Act
    const page = await loadPage({ mounts: [SKILLS_MOUNT] });

    // Assert
    expect(page.el.querySelector('#removeMount-0')?.getAttribute('aria-label')).toBe('Remove mount row 1');
  });

  it('should show a range error and disable Save for an out-of-range memory', async () => {
    // Arrange
    const page = await loadPage();

    // Act
    type(page, 'memoryLimitMb', '100');
    blur(page, 'memoryLimitMb');

    // Assert
    expect(text(page.el, 'memoryLimitMb-error')).toContain('between 512 and 65536 MB');
    expect(input(page.el, 'memoryLimitMb').getAttribute('aria-invalid')).toBe('true');
    expect(saveButton(page.el).disabled).toBe(true);
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

  it('should reject a relative container path', async () => {
    // Arrange
    const page = await loadPage({ mounts: [SKILLS_MOUNT] });

    // Act
    type(page, 'containerPath-0', 'work/skills');
    blur(page, 'containerPath-0');

    // Assert
    expect(text(page.el, 'containerPath-0-error')).toBe('Container path must start with /.');
  });

  it('should accept a Windows drive-rooted host path', async () => {
    // Arrange
    const page = await loadPage({ mounts: [SKILLS_MOUNT] });

    // Act
    type(page, 'hostPath-0', 'C:\\dev\\ai\\skills');
    blur(page, 'hostPath-0');

    // Assert
    expect(text(page.el, 'hostPath-0-error')).toBe('');
    expect(saveButton(page.el).disabled).toBe(false);
  });

  it('should reject a host path with a parent segment', async () => {
    // Arrange
    const page = await loadPage({ mounts: [SKILLS_MOUNT] });

    // Act
    type(page, 'hostPath-0', 'C:\\dev\\..\\secrets');
    blur(page, 'hostPath-0');

    // Assert
    expect(text(page.el, 'hostPath-0-error')).toBe("Path must not contain '..' segments.");
  });

  it('should flag a duplicate container path on the later row', async () => {
    // Arrange
    const page = await loadPage({ mounts: [SKILLS_MOUNT, { containerPath: '/work/other', hostPath: '/srv/o', readOnly: false }] });

    // Act
    type(page, 'containerPath-1', '/work/skills/');
    blur(page, 'containerPath-1');

    // Assert
    expect(text(page.el, 'containerPath-1-error')).toContain('already used');
    expect(saveButton(page.el).disabled).toBe(true);
  });

  it('should PUT the edited environment on submit', async () => {
    // Arrange
    const page = await loadPage({ mounts: [SKILLS_MOUNT] });
    type(page, 'memoryLimitMb', '4096');

    // Act
    saveButton(page.el).click();
    const req = page.httpMock.expectOne(ENV_URL);
    req.flush({ ...API_KEY_RESPONSE, memoryLimitMb: 4096, mounts: [SKILLS_MOUNT] });

    // Assert
    expect(req.request.body).toEqual({ memoryLimitMb: 4096, cpuLimit: 2, pidsLimit: 512, mounts: [SKILLS_MOUNT] });
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

  it('should attach a sensitive-path error to the matching row control', async () => {
    // Arrange
    const page = await loadPage({ mounts: [SKILLS_MOUNT] });
    type(page, 'containerPath-0', '/etc');

    // Act
    saveButton(page.el).click();
    page.httpMock.expectOne(ENV_URL).flush(
      { detail: "Container path '/etc' is a sensitive location.", type: 'tag:foundry,2026:problems/Settings.SensitiveMountContainerPath' },
      { status: 400, statusText: 'Bad Request' },
    );
    await page.fixture.whenStable();
    page.fixture.detectChanges();

    // Assert
    expect(text(page.el, 'containerPath-0-error')).toContain('sensitive location');
    expect(input(page.el, 'containerPath-0').getAttribute('aria-invalid')).toBe('true');
    expect(saveButton(page.el).disabled).toBe(true);
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
