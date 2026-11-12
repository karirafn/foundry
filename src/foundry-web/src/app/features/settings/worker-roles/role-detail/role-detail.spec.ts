import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { RoleDetailComponent } from './role-detail';
import { unsavedChangesGuard } from '../../guards/unsaved-changes.guard';
import { ConfirmDialogService } from '../../../../shared/components/confirm-dialog/confirm-dialog.service';
import { WorkerRoleCard, WorkerRoleCatalog, WorkerRoleService } from '../worker-role.service';
import {
  buildModelOptions,
  buildWorkerRole,
  buildWorkerRoleCatalog,
} from '../../testing/worker-role-catalog.fixture';

const API_URL = '/api/worker-roles';
const MODELS_URL = '/api/worker-roles/models';

@Component({ template: '<h2>Worker roles</h2>' })
class RoleListStubComponent {}

async function open(roleName = 'implement') {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([
        { path: 'settings/roles', component: RoleListStubComponent },
        {
          path: 'settings/roles/:roleName',
          component: RoleDetailComponent,
          canDeactivate: [unsavedChangesGuard],
        },
      ]),
    ],
  });
  const httpMock = TestBed.inject(HttpTestingController);
  const harness = await RouterTestingHarness.create(`/settings/roles/${roleName}`);
  httpMock.expectOne((req) => req.url === MODELS_URL).flush(buildModelOptions());
  return { harness, httpMock, el: () => harness.routeNativeElement as HTMLElement };
}

async function openLoaded(catalog: WorkerRoleCatalog = buildWorkerRoleCatalog(), roleName = 'implement') {
  const ctx = await open(roleName);
  ctx.httpMock.expectOne(API_URL).flush(catalog);
  ctx.harness.detectChanges();
  await ctx.harness.fixture.whenStable();
  return ctx;
}

async function openRole(role: Partial<WorkerRoleCard>) {
  return openLoaded(buildWorkerRoleCatalog({ roles: [buildWorkerRole(role)] }));
}

function text(el: Element | null | undefined): string {
  return el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
}

function fact(el: HTMLElement, term: string): string {
  const dt = Array.from(el.querySelectorAll('.role-detail__facts dt')).find((d) => text(d) === term);
  return text(dt?.nextElementSibling);
}

function announcer(el: HTMLElement): string {
  return text(el.querySelector('.sr-only[role="status"][aria-live="polite"]'));
}

describe('RoleDetailComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  describe('view states', () => {
    it('should load the catalog on a deep link and show a spinner until it arrives', async () => {
      // Act
      const { el, httpMock } = await open();

      // Assert
      expect(el().querySelector('.role-detail__loading fd-spinner')).not.toBeNull();
      expect(announcer(el())).toBe('Loading worker role');
      httpMock.expectOne(API_URL).flush(buildWorkerRoleCatalog());
    });

    it('should render the role once the deep-linked catalog loads', async () => {
      // Act
      const { el } = await openLoaded();

      // Assert
      expect(text(el().querySelector('h2#role-detail-heading'))).toBe('implement');
      expect(el().querySelector('section')!.getAttribute('aria-labelledby')).toBe('role-detail-heading');
      expect(announcer(el())).toBe('');
    });

    it('should not reload a catalog that is already loaded', async () => {
      // Arrange
      TestBed.configureTestingModule({
        providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
      });
      const httpMock = TestBed.inject(HttpTestingController);
      TestBed.inject(WorkerRoleService).load();
      httpMock.expectOne(API_URL).flush(buildWorkerRoleCatalog());
      TestBed.inject(Router).resetConfig([
        { path: 'settings/roles/:roleName', component: RoleDetailComponent },
      ]);

      // Act
      const harness = await RouterTestingHarness.create('/settings/roles/implement');
      httpMock.expectOne((req) => req.url === MODELS_URL).flush(buildModelOptions());

      // Assert
      httpMock.expectNone(API_URL);
      expect(text(harness.routeNativeElement!.querySelector('h2'))).toBe('implement');
    });

    it('should keep the role rendered while a catalog refresh is in flight', async () => {
      // Arrange
      const { el, harness, httpMock } = await openLoaded();

      // Act
      TestBed.inject(WorkerRoleService).load();
      harness.detectChanges();

      // Assert
      expect(text(el().querySelector('h2'))).toBe('implement');
      httpMock.expectOne(API_URL).flush(buildWorkerRoleCatalog());
    });

    it('should explain the catalog is not ready, with Retry and a back link, and focus the callout', async () => {
      // Arrange
      const { el, harness, httpMock } = await open();

      // Act
      httpMock.expectOne(API_URL).flush(null, { status: 503, statusText: 'Service Unavailable' });
      harness.detectChanges();
      await harness.fixture.whenStable();

      // Assert
      const callout = el().querySelector<HTMLElement>('.role-detail__callout')!;
      expect(text(callout)).toContain('Catalog not loaded yet');
      expect(callout.getAttribute('tabindex')).toBe('-1');
      expect(document.activeElement).toBe(callout);
      expect(text(el().querySelector('.role-detail__retry-btn'))).toBe('Retry');
      expect(el().querySelector('a.role-detail__back-link')!.getAttribute('href')).toBe('/settings/roles');
      expect(announcer(el())).toBe('Worker roles are not ready yet');
    });

    it('should retry the catalog load', async () => {
      // Arrange
      const { el, harness, httpMock } = await open();
      httpMock.expectOne(API_URL).flush(null, { status: 503, statusText: 'Service Unavailable' });
      harness.detectChanges();

      // Act
      el().querySelector<HTMLButtonElement>('.role-detail__retry-btn')!.click();
      harness.detectChanges();

      // Assert
      expect(el().querySelector('.role-detail__loading')).not.toBeNull();
      httpMock.expectOne(API_URL).flush(buildWorkerRoleCatalog());
      harness.detectChanges();
      expect(text(el().querySelector('h2'))).toBe('implement');
    });

    it('should alert the load error, with Retry and a back link, and focus the message', async () => {
      // Arrange
      vi.spyOn(console, 'error').mockImplementation(() => {});
      const { el, harness, httpMock } = await open();

      // Act
      httpMock.expectOne(API_URL).flush(null, { status: 500, statusText: 'Server Error' });
      harness.detectChanges();
      await harness.fixture.whenStable();

      // Assert
      const message = el().querySelector<HTMLElement>('[role="alert"] .role-detail__load-error-message')!;
      expect(text(message)).toBe('Failed to load worker roles');
      expect(document.activeElement).toBe(message);
      expect(el().querySelector('[role="alert"] .role-detail__retry-btn')).not.toBeNull();
      expect(el().querySelector('a.role-detail__back-link')).not.toBeNull();
      expect(announcer(el())).toBe('Could not load worker roles');
    });

    it('should show "Role not found" for an unknown name and focus its heading', async () => {
      // Act
      const { el } = await openLoaded(buildWorkerRoleCatalog(), 'ghost');

      // Assert
      const heading = el().querySelector<HTMLElement>('h2.role-detail__not-found-heading')!;
      expect(text(heading)).toBe('Role not found');
      expect(document.activeElement).toBe(heading);
      expect(text(el().querySelector('.role-detail__not-found-description'))).toBe(
        'The worker image declares no role named ghost. The image may have been rebuilt without it, or the link is out of date.',
      );
      const back = el().querySelector('a.role-detail__back-link--button')!;
      expect(text(back)).toBe('Back to worker roles');
      expect(back.getAttribute('href')).toBe('/settings/roles');
      expect(el().querySelector('section')!.hasAttribute('aria-labelledby')).toBe(false);
      expect(announcer(el())).toBe('Worker role not found');
    });
  });

  describe('header', () => {
    it('should show the back link, the name, the capability badge, the purpose and the hint', async () => {
      // Act
      const { el } = await openRole({ capability: 'restricted' });

      // Assert
      const back = el().querySelector('a.role-detail__back-link')!;
      expect(text(back)).toBe('Back to worker roles');
      expect(back.getAttribute('href')).toBe('/settings/roles');
      expect(el().querySelector('.role-detail__title-row fd-role-capability')).not.toBeNull();
      expect(text(el().querySelector('.role-detail__purpose'))).toBe(
        'Implements the issue and opens a pull request.',
      );
      expect(text(el().querySelector('.role-detail__capability-hint'))).toBe(
        'Runs read-only: no command tools, no provider token and no branch. It returns a verdict only.',
      );
    });

    it('should make the role name the first h2, focusable from code', async () => {
      // Act
      const { el } = await openLoaded();

      // Assert
      const heading = el().querySelector('h2')!;
      expect(heading.id).toBe('role-detail-heading');
      expect(heading.getAttribute('tabindex')).toBe('-1');
    });

    it('should focus the role name when focus was lost while the catalog loaded', async () => {
      // Arrange
      const { el, harness, httpMock } = await open();
      (document.activeElement as HTMLElement | null)?.blur();

      // Act
      httpMock.expectOne(API_URL).flush(buildWorkerRoleCatalog());
      harness.detectChanges();
      await harness.fixture.whenStable();

      // Assert
      expect(document.activeElement).toBe(el().querySelector('h2'));
    });

    it('should omit the purpose when the role has none', async () => {
      // Act
      const { el } = await openRole({ purpose: null });

      // Assert
      expect(el().querySelector('.role-detail__purpose')).toBeNull();
    });

    it('should show no hint for an unrecognised capability', async () => {
      // Act
      const { el } = await openRole({ capability: 'sandboxed' });

      // Assert
      expect(el().querySelector('.role-detail__capability-hint')).toBeNull();
    });
  });

  describe('rail', () => {
    it('should hold the model editor, the facts and the worker image in a labelled aside', async () => {
      // Act
      const { el } = await openLoaded();

      // Assert
      const rail = el().querySelector('aside.role-detail__rail')!;
      expect(rail.getAttribute('aria-label')).toBe('Role summary');
      expect(rail.querySelector('fd-worker-role-model')).not.toBeNull();
      expect(rail.querySelector('dl.role-detail__facts')).not.toBeNull();
      expect(text(rail.querySelector('h3.role-detail__rail-title'))).toBe('Worker image');
      expect(rail.querySelector('fd-role-image')).not.toBeNull();
    });

    it('should show the unverified warning in the rail image block', async () => {
      // Arrange
      const catalog = buildWorkerRoleCatalog();
      catalog.image = { ...catalog.image, verified: false };

      // Act
      const { el } = await openLoaded(catalog);

      // Assert
      expect(el().querySelector('aside fd-role-image .role-image__unverified')).not.toBeNull();
    });

    it('should show Verdict "No" when the role has no verdict schema', async () => {
      // Act
      const { el } = await openRole({ verdictSchema: null });

      // Assert
      expect(fact(el(), 'Verdict')).toBe('No');
    });

    it('should show Verdict "Yes" when the role has a verdict schema', async () => {
      // Act
      const { el } = await openRole({ verdictSchema: '{"type":"object"}' });

      // Assert
      expect(fact(el(), 'Verdict')).toBe('Yes');
    });

    it('should show own and shared counts as "N + M shared"', async () => {
      // Act
      const { el } = await openLoaded();

      // Assert
      expect(fact(el(), 'Skills')).toBe('1 + 1 shared');
    });

    it('should show only the own count when nothing of that kind is shared', async () => {
      // Act
      const { el } = await openLoaded();

      // Assert
      expect(fact(el(), 'Agents')).toBe('1');
    });

    it('should show "0 + M shared" when the role has none of its own', async () => {
      // Act
      const { el } = await openRole({ skills: [] });

      // Assert
      expect(fact(el(), 'Skills')).toBe('0 + 1 shared');
    });

    it('should show "None" when neither the role nor the shared plugin has any', async () => {
      // Act
      const { el } = await openRole({ agents: [] });

      // Assert
      expect(fact(el(), 'Agents')).toBe('None');
    });
  });

  describe('content', () => {
    it('should explain placeholders with a sample chip', async () => {
      // Act
      const { el } = await openLoaded();

      // Assert
      const hint = el().querySelector('.role-detail__content > .role-detail__hint')!;
      expect(text(hint)).toBe(
        'Placeholders such as {issueNumber} are filled in by the worker at dispatch time.',
      );
      expect(text(hint.querySelector('.role-detail__placeholder-sample'))).toBe('{issueNumber}');
    });

    it('should show the role system prompt inline in its prompt-assembly slot', async () => {
      // Act
      const { el } = await openLoaded();

      // Assert
      const slot = el().querySelector<HTMLDetailsElement>('.prompt-assembly__section--role')!;
      expect(slot.open).toBe(true);
      expect(slot.querySelector('pre')!.textContent).toBe(
        'You are implementing issue #{issueNumber} in {repository}.',
      );
      expect(text(slot.querySelector('fd-role-tag'))).toBe('Role: implement');
    });

    it("should show the role's own principles and the sections it selects in the principles slot", async () => {
      // Act
      const { el } = await openRole({
        name: 'implement',
        principleSections: ['Design', 'Domain modelling'],
        principles: 'Engineering principles.\n\nDesign:\n- Safe defaults.',
      });

      // Assert
      const slot = Array.from(el().querySelectorAll<HTMLDetailsElement>('details.prompt-assembly__section')).find(
        (d) => text(d.querySelector('.prompt-assembly__title-text')) === 'Worker principles',
      )!;
      expect(text(slot.querySelector('.prompt-assembly__note'))).toBe('Includes: Design, Domain modelling');
      expect(slot.querySelector('pre')!.textContent).toBe('Engineering principles.\n\nDesign:\n- Safe defaults.');
    });

    it('should show the task prompt verbatim after the assembly', async () => {
      // Act
      const { el } = await openLoaded();

      // Assert
      const blocks = Array.from(el().querySelectorAll('.role-detail__content > .role-detail__block'));
      expect(blocks.map((b) => b.tagName.toLowerCase())).toEqual([
        'fd-prompt-assembly',
        'section',
        'fd-role-plugins',
        'section',
      ]);
      expect(text(blocks[1].querySelector('h3'))).toBe('Task prompt');
      expect(blocks[1].querySelector('pre')!.textContent).toBe(
        'Implement issue #{issueNumber}.\n\n{issueContent}',
      );
    });

    it('should render markup in prompt text as text, never as HTML', async () => {
      // Act
      const { el } = await openRole({ taskPrompt: '<img src=x onerror="alert(1)"> {issueNumber}' });

      // Assert
      const task = el().querySelectorAll('.role-detail__block')[1].querySelector('pre')!;
      expect(task.querySelector('img')).toBeNull();
      expect(task.textContent).toBe('<img src=x onerror="alert(1)"> {issueNumber}');
    });

    it('should say the role returns no verdict when the verdict schema is absent', async () => {
      // Act
      const { el } = await openLoaded();

      // Assert
      expect(el().querySelector('.role-detail__schema')).toBeNull();
      expect(text(el().querySelector('.role-detail__no-schema'))).toBe(
        'None. This role returns no verdict.',
      );
    });

    it('should pretty-print a JSON verdict schema in a collapsed disclosure', async () => {
      // Act
      const { el } = await openRole({ verdictSchema: '{"type":"object","required":["verdict"]}' });

      // Assert
      const disclosure = el().querySelector<HTMLDetailsElement>('details.role-detail__disclosure')!;
      expect(disclosure.open).toBe(false);
      expect(text(disclosure.querySelector('summary'))).toBe('JSON schema');
      const schema = disclosure.querySelector('.role-detail__schema')!;
      expect(schema.textContent).toBe(JSON.stringify({ type: 'object', required: ['verdict'] }, null, 2));
      expect(schema.getAttribute('role')).toBe('region');
      expect(schema.getAttribute('aria-label')).toBe('implement verdict schema');
      expect(el().querySelector('.role-detail__no-schema')).toBeNull();
    });

    it('should show a verdict schema that is not valid JSON verbatim', async () => {
      // Act
      const { el } = await openRole({ verdictSchema: 'not { json' });

      // Assert
      expect(el().querySelector('.role-detail__schema')!.textContent).toBe('not { json');
    });
  });

  describe('unsaved changes', () => {
    function pickModel(el: HTMLElement, value: string): void {
      const select = el.querySelector<HTMLSelectElement>('select.role-model__select')!;
      select.value = value;
      select.dispatchEvent(new Event('change'));
    }

    it('should report no unsaved changes while the saved model is selected', async () => {
      // Arrange
      const { harness } = await openLoaded();

      // Act
      const component = harness.routeDebugElement!.componentInstance as RoleDetailComponent;

      // Assert
      expect(component.hasUnsavedChanges()).toBe(false);
    });

    it('should delegate unsaved changes to the model editor', async () => {
      // Arrange
      const { el, harness } = await openLoaded();

      // Act
      pickModel(el(), 'opus');
      harness.detectChanges();

      // Assert
      const component = harness.routeDebugElement!.componentInstance as RoleDetailComponent;
      expect(component.hasUnsavedChanges()).toBe(true);
    });

    it('should ask before leaving with an unsaved model', async () => {
      // Arrange
      const { el, harness } = await openLoaded();
      const confirm = vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(false);
      pickModel(el(), 'opus');
      harness.detectChanges();

      // Act
      const navigated = await TestBed.inject(Router).navigateByUrl('/settings/roles');

      // Assert
      expect(confirm).toHaveBeenCalledOnce();
      expect(navigated).toBe(false);
    });

    it('should leave without asking once the model is saved', async () => {
      // Arrange
      const { el, harness, httpMock } = await openLoaded();
      const confirm = vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm');
      pickModel(el(), 'opus');
      harness.detectChanges();
      el().querySelector<HTMLButtonElement>('.role-model__save')!.click();
      httpMock.expectOne('/api/worker-roles/implement/model').flush({ role: 'implement', model: 'opus' });
      harness.detectChanges();

      // Act
      const navigated = await TestBed.inject(Router).navigateByUrl('/settings/roles');

      // Assert
      expect(confirm).not.toHaveBeenCalled();
      expect(navigated).toBe(true);
    });
  });
});
