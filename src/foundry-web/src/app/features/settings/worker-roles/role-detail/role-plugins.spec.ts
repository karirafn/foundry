import { TestBed } from '@angular/core/testing';
import { RolePluginsComponent } from './role-plugins';
import { buildWorkerRole, buildWorkerRoleCatalog } from '../../testing/worker-role-catalog.fixture';
import { WorkerRoleCard, WorkerRoleCatalog } from '../worker-role.service';

function render(
  role: WorkerRoleCard = buildWorkerRole(),
  shared: WorkerRoleCatalog['shared'] = buildWorkerRoleCatalog().shared,
) {
  TestBed.configureTestingModule({ imports: [RolePluginsComponent] });
  const fixture = TestBed.createComponent(RolePluginsComponent);
  fixture.componentRef.setInput('role', role);
  fixture.componentRef.setInput('shared', shared);
  fixture.detectChanges();
  return { fixture, el: fixture.nativeElement as HTMLElement };
}

const NO_SHARED: WorkerRoleCatalog['shared'] = { description: 'Loaded for every role', skills: [], agents: [] };

function text(el: Element | null | undefined): string {
  return el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
}

describe('RolePluginsComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('should list the role skills followed by shared skills labelled as shared', () => {
    // Act
    const { el } = render();

    // Assert
    const skills = Array.from(el.querySelectorAll('.role-plugins__skills details'));
    expect(skills.map((s) => text(s.querySelector('.role-plugins__summary-name')))).toEqual([
      'tdd',
      'commit-style',
    ]);
    expect(skills[0].querySelector('.role-plugins__shared')).toBeNull();
    expect(text(skills[1].querySelector('.role-plugins__shared'))).toBe('Shared');
    expect(text(skills[0].querySelector('.role-plugins__summary-description'))).toBe(
      'Red-green-refactor loop',
    );
  });

  it('should title the block as an h3 with h4 Skills and Agents subgroups', () => {
    // Act
    const { el } = render();

    // Assert
    expect(text(el.querySelector('h3'))).toBe('Skills and agents');
    expect(text(el.querySelector('.role-plugins__skills h4'))).toBe('Skills');
    expect(text(el.querySelector('.role-plugins__agents h4'))).toBe('Agents');
  });

  it('should keep every row collapsed at first', () => {
    // Act
    const { el } = render();

    // Assert
    const rows = Array.from(el.querySelectorAll<HTMLDetailsElement>('details'));
    expect(rows.length).toBeGreaterThan(0);
    expect(rows.some((d) => d.open)).toBe(false);
  });

  it('should show the shared description hint when a shared entry exists', () => {
    // Act
    const { el } = render();

    // Assert
    expect(text(el.querySelector('.role-plugins__shared-hint'))).toBe('Shared: Loaded for every role');
  });

  it('should omit the shared hint when there are no shared entries', () => {
    // Act
    const { el } = render(buildWorkerRole(), NO_SHARED);

    // Assert
    expect(el.querySelector('.role-plugins__shared-hint')).toBeNull();
  });

  it('should show one hint when the role loads no skills or agents', () => {
    // Act
    const { el } = render(buildWorkerRole({ skills: [], agents: [] }), NO_SHARED);

    // Assert
    expect(text(el)).toContain('This role loads no skills or agents.');
    expect(el.querySelector('.role-plugins__skills')).toBeNull();
    expect(el.querySelector('.role-plugins__agents')).toBeNull();
  });

  it('should make skill file content a labelled scroll region', () => {
    // Act
    const { el } = render();

    // Assert
    const pre = el.querySelector('.role-plugins__skills details pre')!;
    expect(pre.getAttribute('role')).toBe('region');
    expect(pre.getAttribute('aria-label')).toBe('tdd file');
    expect(pre.getAttribute('tabindex')).toBe('0');
  });

  it('should expand a skill to its full file content with placeholders highlighted', () => {
    // Arrange
    const { el, fixture } = render();
    const skill = el.querySelector<HTMLDetailsElement>('details.role-plugins__plugin')!;

    // Act
    skill.querySelector<HTMLElement>('summary')!.click();
    fixture.detectChanges();

    // Assert
    expect(skill.open).toBe(true);
    const content = skill.querySelector('pre')!;
    expect(content.textContent).toBe('# TDD\nWrite a failing test for {issueNumber} first.');
    expect(text(content.querySelector('mark'))).toBe('{issueNumber}');
    expect(text(skill.querySelector('.role-plugins__path'))).toBe('skills/tdd/SKILL.md');
  });

  it('should list agents with their expandable content', () => {
    // Act
    const { el } = render();

    // Assert
    const reviewer = Array.from(el.querySelectorAll('.role-plugins__agents details')).find(
      (p) => text(p.querySelector('.role-plugins__summary-name')) === 'reviewer',
    )!;
    expect(reviewer.querySelector('pre')!.textContent).toBe('# Reviewer\nReview the diff.');
  });

  it('should say there are no agents when neither the role nor the shared plugin has any', () => {
    // Act
    const { el } = render(buildWorkerRole({ agents: [] }));

    // Assert
    expect(text(el)).toContain('No agents.');
  });
});
