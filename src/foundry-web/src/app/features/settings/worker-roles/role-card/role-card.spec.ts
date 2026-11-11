import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RoleCardComponent } from './role-card';
import { buildWorkerRole } from '../../testing/worker-role-catalog.fixture';
import { WorkerRoleCard } from '../worker-role.service';

function render(overrides: Partial<WorkerRoleCard> = {}): HTMLElement {
  TestBed.configureTestingModule({
    imports: [RoleCardComponent],
    providers: [provideRouter([])],
  });
  const fixture = TestBed.createComponent(RoleCardComponent);
  fixture.componentRef.setInput('role', buildWorkerRole(overrides));
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

function text(el: Element | null | undefined): string {
  return el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
}

describe('RoleCardComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('should title the card with the role name, labelling the article', () => {
    // Act
    const el = render();

    // Assert
    const heading = el.querySelector('h3.role-card__title')!;
    expect(text(heading)).toBe('implement');
    expect(el.querySelector('article')!.getAttribute('aria-labelledby')).toBe(heading.id);
  });

  it('should make the role name the one link to its details page', () => {
    // Act
    const el = render({ name: 'triage' });

    // Assert
    const links = el.querySelectorAll('a');
    expect(links).toHaveLength(1);
    expect(links[0].getAttribute('href')).toBe('/settings/roles/triage');
    expect(text(links[0])).toBe('triage');
  });

  it('should show the capability badge in the title row', () => {
    // Act
    const el = render({ capability: 'restricted' });

    // Assert
    expect(el.querySelector('.role-card__title-row fd-role-capability')).not.toBeNull();
    expect(text(el.querySelector('.role-capability'))).toContain('Restricted');
  });

  it('should show the purpose', () => {
    // Act
    const el = render();

    // Assert
    expect(text(el.querySelector('.role-card__purpose'))).toBe(
      'Implements the issue and opens a pull request.',
    );
  });

  it('should omit the purpose line when the role has no purpose', () => {
    // Act
    const el = render({ purpose: null });

    // Assert
    expect(el.querySelector('.role-card__purpose')).toBeNull();
  });

  it('should show "Default" when the role has no saved model', () => {
    // Act
    const el = render({ model: '' });

    // Assert
    expect(text(el.querySelector('.role-card__model'))).toBe('Default');
  });

  it('should show the saved model as it is', () => {
    // Act
    const el = render({ model: 'claude-opus-4-1-20250805' });

    // Assert
    expect(text(el.querySelector('.role-card__model'))).toBe('claude-opus-4-1-20250805');
  });

  it('should count only the role own skills and agents, not the shared ones', () => {
    // Arrange
    const role = buildWorkerRole();

    // Act
    const el = render({ skills: [...role.skills, ...role.skills], agents: role.agents });

    // Assert
    expect(text(el.querySelector('.role-card__skills'))).toBe('2');
    expect(text(el.querySelector('.role-card__agents'))).toBe('1');
  });

  it('should show Verdict "No" when the role has no verdict schema', () => {
    // Act
    const el = render({ verdictSchema: null });

    // Assert
    expect(text(el.querySelector('.role-card__verdict'))).toBe('No');
  });

  it('should show Verdict "Yes" when the role has a verdict schema', () => {
    // Act
    const el = render({ verdictSchema: '{"type":"object"}' });

    // Assert
    expect(text(el.querySelector('.role-card__verdict'))).toBe('Yes');
  });
});
