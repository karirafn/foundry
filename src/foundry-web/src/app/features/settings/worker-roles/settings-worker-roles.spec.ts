import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SettingsWorkerRolesComponent } from './settings-worker-roles';
import {
  ROLE_PROMPT_SLOT,
  buildWorkerRole,
  buildWorkerRoleCatalog,
} from '../testing/worker-role-catalog.fixture';
import { WorkerRoleCatalog } from './worker-role.service';

const API_URL = '/api/worker-roles';

function setup() {
  TestBed.configureTestingModule({
    imports: [SettingsWorkerRolesComponent],
    providers: [provideHttpClient(), provideHttpClientTesting()],
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
  return Array.from(el.querySelectorAll<HTMLElement>('fd-worker-role-card'));
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
  });

  describe('role card', () => {
    it('should render one card per role with name and purpose', () => {
      // Arrange / Act
      const { el } = setupLoaded(
        buildWorkerRoleCatalog({
          roles: [buildWorkerRole(), buildWorkerRole({ name: 'review', purpose: 'Reviews PRs.' })],
        }),
      );

      // Assert
      const rendered = cards(el);
      expect(rendered).toHaveLength(2);
      expect(text(rendered[0].querySelector('h3'))).toBe('implement');
      expect(text(rendered[0].querySelector('.role-card__purpose'))).toBe(
        'Implements the issue and opens a pull request.',
      );
      expect(text(rendered[1].querySelector('h3'))).toBe('review');
      const article = rendered[0].querySelector('article')!;
      expect(article.getAttribute('aria-labelledby')).toBe(rendered[0].querySelector('h3')!.id);
    });

    it('should show the image tag, build time and short commit', () => {
      // Arrange / Act
      const { el } = setupLoaded();

      // Assert
      const card = cards(el)[0];
      expect(text(card.querySelector('.role-card__image-tag'))).toBe('foundry-worker:local');
      const builtAt = card.querySelector('time.role-card__built-at')!;
      expect(builtAt.getAttribute('datetime')).toBe('2026-11-01T10:30:00Z');
      expect(text(builtAt)).toContain('2026');
      const commit = card.querySelector('.role-card__commit')!;
      expect(text(commit)).toBe('0123456');
      expect(commit.getAttribute('title')).toBe('0123456789abcdef0123456789abcdef01234567');
    });

    it('should show "unknown" when the image commit is null', () => {
      // Arrange
      const catalog = buildWorkerRoleCatalog();
      catalog.image = { ...catalog.image, commit: null };

      // Act
      const { el } = setupLoaded(catalog);

      // Assert
      expect(text(cards(el)[0].querySelector('.role-card__commit'))).toBe('unknown');
    });

    it('should not show the unverified warning for a verified image', () => {
      // Arrange / Act
      const { el } = setupLoaded();

      // Assert
      expect(el.querySelector('.worker-roles__unverified')).toBeNull();
      expect(el.querySelector('.role-card__unverified')).toBeNull();
    });

    it('should warn that the catalog is unverified when the image is not verified', () => {
      // Arrange
      const catalog = buildWorkerRoleCatalog();
      catalog.image = { ...catalog.image, verified: false };

      // Act
      const { el } = setupLoaded(catalog);

      // Assert
      const warning = el.querySelector('.worker-roles__unverified');
      expect(text(warning)).toContain('Unverified');
      expect(text(warning)).toContain('read from disk');
      expect(text(cards(el)[0].querySelector('.role-card__unverified'))).toBe('Unverified');
    });
  });

  describe('placeholder highlighting', () => {
    it('should highlight {issueNumber} in the system prompt without substituting it', () => {
      // Arrange / Act
      const { el } = setupLoaded();

      // Assert
      const prompt = cards(el)[0].querySelector('.role-card__system-prompt pre')!;
      const marks = Array.from(prompt.querySelectorAll('mark.prompt-text__placeholder')).map(
        (m) => m.textContent,
      );
      expect(marks).toEqual(['{issueNumber}', '{repository}']);
      expect(prompt.textContent).toBe('You are implementing issue #{issueNumber} in {repository}.');
    });

    it('should preserve the task prompt text verbatim, including newlines', () => {
      // Arrange / Act
      const { el } = setupLoaded();

      // Assert
      const prompt = cards(el)[0].querySelector('.role-card__task-prompt pre')!;
      expect(prompt.textContent).toBe('Implement issue #{issueNumber}.\n\n{issueContent}');
      expect(prompt.querySelectorAll('mark')).toHaveLength(2);
    });

    it('should render markup in prompt text as text, never as HTML', () => {
      // Arrange
      const role = buildWorkerRole({
        systemPrompt: '<img src=x onerror="alert(1)"> {issueNumber}',
      });

      // Act
      const { el } = setupLoaded(buildWorkerRoleCatalog({ roles: [role] }));

      // Assert
      const prompt = cards(el)[0].querySelector('.role-card__system-prompt pre')!;
      expect(prompt.querySelector('img')).toBeNull();
      expect(prompt.textContent).toBe('<img src=x onerror="alert(1)"> {issueNumber}');
    });

    it('should not highlight braces that are not placeholder tokens', () => {
      // Arrange
      const role = buildWorkerRole({ systemPrompt: 'JSON {"a": 1} and {} and {9x}' });

      // Act
      const { el } = setupLoaded(buildWorkerRoleCatalog({ roles: [role] }));

      // Assert
      const prompt = cards(el)[0].querySelector('.role-card__system-prompt pre')!;
      expect(prompt.querySelectorAll('mark')).toHaveLength(0);
    });
  });

  describe('prompt sections', () => {
    it('should list every section in order with a human condition label', () => {
      // Arrange / Act
      const { el } = setupLoaded();

      // Assert
      const sections = Array.from(cards(el)[0].querySelectorAll('.role-card__section'));
      expect(sections.map((s) => text(s.querySelector('.role-card__summary-name')))).toEqual([
        'Base instructions',
        'Role prompt',
        '.NET toolchain',
        'Docker',
        'Fresh run',
        'Continuation',
        'Revision',
        'Rebase',
      ]);
      expect(sections.map((s) => text(s.querySelector('.role-card__condition')))).toEqual([
        'Always',
        'Always',
        '.NET image',
        'Docker image',
        'Fresh',
        'Continuation',
        'Revision',
        'Rebase conflict',
      ]);
    });

    it('should render sections as keyboard-accessible details/summary expanders', () => {
      // Arrange / Act
      const { el } = setupLoaded();

      // Assert
      const section = cards(el)[0].querySelector('.role-card__section')!;
      expect(section.tagName.toLowerCase()).toBe('details');
      expect(section.querySelector(':scope > summary')).toBeTruthy();
    });

    it('should replace the role prompt slot marker with a note', () => {
      // Arrange / Act
      const { el } = setupLoaded();

      // Assert
      const slot = cards(el)[0].querySelectorAll('.role-card__section')[1];
      expect(text(slot.querySelector('.role-card__slot-note'))).toContain(
        "The role's system prompt (above) is inserted here.",
      );
      expect(slot.querySelector('pre')).toBeNull();
      expect(slot.textContent).not.toContain(ROLE_PROMPT_SLOT);
    });

    it('should highlight placeholders in section content', () => {
      // Arrange / Act
      const { el } = setupLoaded();

      // Assert
      const base = cards(el)[0].querySelector('.role-card__section')!;
      expect(text(base.querySelector('mark.prompt-text__placeholder'))).toBe('{branchName}');
    });
  });

  describe('skills and agents', () => {
    it('should list the role skills followed by shared skills labelled as shared', () => {
      // Arrange / Act
      const { el } = setupLoaded();

      // Assert
      const skills = Array.from(cards(el)[0].querySelectorAll('.role-card__skills details'));
      expect(skills.map((s) => text(s.querySelector('.role-card__summary-name')))).toEqual([
        'tdd',
        'commit-style',
      ]);
      expect(skills[0].querySelector('.role-card__shared')).toBeNull();
      expect(text(skills[1].querySelector('.role-card__shared'))).toBe('Shared');
      expect(text(skills[0].querySelector('.role-card__summary-description'))).toBe(
        'Red-green-refactor loop',
      );
    });

    it('should expand a skill to its full file content with placeholders highlighted', () => {
      // Arrange
      const { el, fixture } = setupLoaded();
      const skill = cards(el)[0].querySelector<HTMLDetailsElement>('details.role-card__plugin')!;

      // Act
      skill.querySelector<HTMLElement>('summary')!.click();
      fixture.detectChanges();

      // Assert
      expect(skill.open).toBe(true);
      const content = skill.querySelector('pre')!;
      expect(content.textContent).toBe('# TDD\nWrite a failing test for {issueNumber} first.');
      expect(text(content.querySelector('mark'))).toBe('{issueNumber}');
      expect(text(skill.querySelector('.role-card__path'))).toBe('skills/tdd/SKILL.md');
    });

    it('should list agents with their expandable content', () => {
      // Arrange / Act
      const { el } = setupLoaded();

      // Assert
      const plugins = Array.from(cards(el)[0].querySelectorAll('.role-card__agents details'));
      const reviewer = plugins.find(
        (p) => text(p.querySelector('.role-card__summary-name')) === 'reviewer',
      )!;
      expect(reviewer).toBeTruthy();
      expect(reviewer.querySelector('pre')!.textContent).toBe('# Reviewer\nReview the diff.');
    });

    it('should say there are no agents when neither the role nor the shared plugin has any', () => {
      // Arrange
      const role = buildWorkerRole({ agents: [] });

      // Act
      const { el } = setupLoaded(buildWorkerRoleCatalog({ roles: [role] }));

      // Assert
      expect(text(cards(el)[0])).toContain('No agents.');
    });
  });

  describe('verdict schema', () => {
    it('should show the generative-role note when the verdict schema is absent', () => {
      // Arrange / Act
      const { el } = setupLoaded();

      // Assert
      const card = cards(el)[0];
      expect(card.querySelector('.role-card__schema')).toBeNull();
      expect(text(card.querySelector('.role-card__no-schema'))).toBe(
        'No verdict schema (generative role).',
      );
    });

    it('should pretty-print a JSON verdict schema', () => {
      // Arrange
      const role = buildWorkerRole({ verdictSchema: '{"type":"object","required":["verdict"]}' });

      // Act
      const { el } = setupLoaded(buildWorkerRoleCatalog({ roles: [role] }));

      // Assert
      const schema = cards(el)[0].querySelector('.role-card__schema')!;
      expect(schema.textContent).toBe(
        JSON.stringify({ type: 'object', required: ['verdict'] }, null, 2),
      );
      expect(cards(el)[0].querySelector('.role-card__no-schema')).toBeNull();
    });

    it('should show a verdict schema that is not valid JSON verbatim', () => {
      // Arrange
      const role = buildWorkerRole({ verdictSchema: 'not { json' });

      // Act
      const { el } = setupLoaded(buildWorkerRoleCatalog({ roles: [role] }));

      // Assert
      expect(cards(el)[0].querySelector('.role-card__schema')!.textContent).toBe('not { json');
    });
  });
});
