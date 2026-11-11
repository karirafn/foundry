import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RoleCardGridComponent } from './role-card-grid';
import { buildWorkerRole } from '../../testing/worker-role-catalog.fixture';
import { WorkerRoleCard } from '../worker-role.service';

function render(roles: readonly WorkerRoleCard[]): HTMLElement {
  TestBed.configureTestingModule({
    imports: [RoleCardGridComponent],
    providers: [provideRouter([])],
  });
  const fixture = TestBed.createComponent(RoleCardGridComponent);
  fixture.componentRef.setInput('roles', roles);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('RoleCardGridComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('should render one card per role, in order, as list items', () => {
    // Act
    const el = render([buildWorkerRole(), buildWorkerRole({ name: 'review' })]);

    // Assert
    const list = el.querySelector('ul.role-card-grid')!;
    expect(list.getAttribute('role')).toBe('list');
    const titles = Array.from(list.querySelectorAll(':scope > li > fd-role-card h3'));
    expect(titles.map((h) => h.textContent?.trim())).toEqual(['implement', 'review']);
  });

  it('should still render the grid for a single role', () => {
    // Act
    const el = render([buildWorkerRole()]);

    // Assert
    expect(el.querySelectorAll('.role-card-grid > li')).toHaveLength(1);
  });
});
