import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SettingsWorkerRolesComponent } from './settings-worker-roles';
import { buildWorkerRole, buildWorkerRoleCatalog } from '../testing/worker-role-catalog.fixture';
import { WorkerRoleCatalog, WorkerRoleService } from './worker-role.service';

const API_URL = '/api/worker-roles';
const MODELS_URL = '/api/worker-roles/models';

function setup() {
  TestBed.configureTestingModule({
    imports: [SettingsWorkerRolesComponent],
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });
  const fixture = TestBed.createComponent(SettingsWorkerRolesComponent);
  const httpMock = TestBed.inject(HttpTestingController);
  fixture.detectChanges();
  return { fixture, httpMock, el: fixture.nativeElement as HTMLElement };
}

function setupLoaded(catalog: WorkerRoleCatalog = buildWorkerRoleCatalog()) {
  const ctx = setup();
  ctx.httpMock.expectOne(API_URL).flush(catalog);
  ctx.fixture.detectChanges();
  return ctx;
}

function cards(el: HTMLElement): HTMLElement[] {
  return Array.from(el.querySelectorAll<HTMLElement>('fd-role-card'));
}

function text(el: Element | null | undefined): string {
  return el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
}

describe('SettingsWorkerRolesComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  describe('load states', () => {
    it('should show a loading status while the catalog request is in flight', () => {
      // Arrange / Act
      const { el, httpMock } = setup();

      // Assert
      expect(el.querySelector('[role="status"][aria-label="Loading worker roles"]')).toBeTruthy();
      httpMock.expectOne(API_URL).flush(buildWorkerRoleCatalog());
    });

    it('should render the page heading as an h2', () => {
      // Arrange / Act
      const { el } = setupLoaded();

      // Assert
      expect(text(el.querySelector('h2'))).toBe('Worker roles');
    });

    it('should leave the model list to the details page', () => {
      // Arrange / Act
      const { httpMock } = setupLoaded();

      // Assert
      httpMock.expectNone((req) => req.url === MODELS_URL);
    });

    it('should explain the catalog is not loaded yet when the API returns 503', () => {
      // Arrange
      const { el, fixture, httpMock } = setup();

      // Act
      httpMock
        .expectOne(API_URL)
        .flush({ status: 503 }, { status: 503, statusText: 'Service Unavailable' });
      fixture.detectChanges();

      // Assert
      const status = el.querySelector('[role="status"]');
      expect(text(status)).toContain('Catalog not loaded yet');
      expect(text(status)).toContain('it is read during the worker image build');
      expect(el.querySelector('[role="alert"]')).toBeNull();
      expect(cards(el)).toHaveLength(0);
    });

    it('should retry the request from the 503 state', () => {
      // Arrange
      const { el, fixture, httpMock } = setup();
      httpMock.expectOne(API_URL).flush(null, { status: 503, statusText: 'Service Unavailable' });
      fixture.detectChanges();

      // Act
      el.querySelector<HTMLButtonElement>('.general-settings__retry-btn')!.click();
      httpMock.expectOne(API_URL).flush(buildWorkerRoleCatalog());
      fixture.detectChanges();

      // Assert
      expect(cards(el)).toHaveLength(1);
    });

    it('should show an alert with the error message on other failures', () => {
      // Arrange
      const { el, fixture, httpMock } = setup();
      vi.spyOn(console, 'error').mockImplementation(() => {});

      // Act
      httpMock.expectOne(API_URL).flush(null, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      // Assert
      expect(text(el.querySelector('[role="alert"]'))).toContain('Failed to load worker roles');
      expect(cards(el)).toHaveLength(0);
    });

    it('should keep the cached grid, without a spinner, while the catalog reloads', () => {
      // Arrange
      TestBed.configureTestingModule({
        providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
      });
      const httpMock = TestBed.inject(HttpTestingController);
      TestBed.inject(WorkerRoleService).load();
      httpMock.expectOne(API_URL).flush(buildWorkerRoleCatalog());

      // Act
      const fixture = TestBed.createComponent(SettingsWorkerRolesComponent);
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      expect(cards(el)).toHaveLength(1);
      expect(el.querySelector('[aria-label="Loading worker roles"]')).toBeNull();
      httpMock.expectOne(API_URL).flush(buildWorkerRoleCatalog());
    });
  });

  describe('page copy', () => {
    it('should introduce the roles and point to their details pages', () => {
      // Arrange / Act
      const { el } = setupLoaded();

      // Assert
      expect(text(el.querySelector('.general-settings__section-description'))).toBe(
        'Roles ship in the worker image. Open a role to read its prompts, skills, agents and verdict schema, or to change the model it runs on.',
      );
    });

    it('should show the image and the empty state, with no grid, when the image declares no roles', () => {
      // Arrange / Act
      const { el } = setupLoaded(buildWorkerRoleCatalog({ roles: [] }));

      // Assert
      expect(el.querySelector('fd-role-image')).not.toBeNull();
      expect(el.querySelector('fd-role-card-grid')).toBeNull();
      const empty = el.querySelector('.general-settings__section-description:last-of-type')!;
      expect(text(empty)).toBe('The worker image declares no roles.');
    });
  });

  describe('catalog image', () => {
    it('should show the image once, at page level', () => {
      // Arrange / Act
      const { el } = setupLoaded(
        buildWorkerRoleCatalog({ roles: [buildWorkerRole(), buildWorkerRole({ name: 'review' })] }),
      );

      // Assert
      expect(el.querySelectorAll('fd-role-image')).toHaveLength(1);
      expect(cards(el).some((card) => card.querySelector('fd-role-image') !== null)).toBe(false);
    });

    it('should show the unverified warning once when the image is not verified', () => {
      // Arrange
      const catalog = buildWorkerRoleCatalog({
        roles: [buildWorkerRole(), buildWorkerRole({ name: 'review' })],
      });
      catalog.image = { ...catalog.image, verified: false };

      // Act
      const { el } = setupLoaded(catalog);

      // Assert
      expect(el.querySelectorAll('.role-image__unverified')).toHaveLength(1);
    });
  });

  describe('grid', () => {
    it('should render a card per role in the grid', () => {
      // Arrange / Act
      const { el } = setupLoaded(
        buildWorkerRoleCatalog({
          roles: [buildWorkerRole(), buildWorkerRole({ name: 'review', purpose: 'Reviews PRs.' })],
        }),
      );

      // Assert
      const grid = el.querySelector('fd-role-card-grid')!;
      expect(Array.from(grid.querySelectorAll('fd-role-card h3')).map((h) => text(h))).toEqual([
        'implement',
        'review',
      ]);
    });
  });
});
