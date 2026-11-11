import { TestBed } from '@angular/core/testing';
import { RoleTagComponent } from './role-tag';

function render(role: string) {
  TestBed.configureTestingModule({ imports: [RoleTagComponent] });
  const fixture = TestBed.createComponent(RoleTagComponent);
  fixture.componentRef.setInput('role', role);
  fixture.detectChanges();
  return { fixture, el: fixture.nativeElement as HTMLElement };
}

describe('RoleTagComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('should render the role name as is, lower-case', () => {
    // Act
    const { el } = render('triage');

    // Assert
    const tag = el.querySelector('.role-tag');
    expect(tag?.lastChild?.textContent).toBe('triage');
  });

  it('should prefix the role with a screen-reader-only "Role: " label', () => {
    // Act
    const { el } = render('implement');

    // Assert
    const tag = el.querySelector('.role-tag');
    expect(tag?.firstElementChild?.classList.contains('sr-only')).toBe(true);
    expect(tag?.firstElementChild?.textContent).toBe('Role: ');
    expect(tag?.textContent).toBe('Role: implement');
  });

  it('should not be interactive', () => {
    // Act
    const { el } = render('implement');

    // Assert
    expect(el.querySelector('a, button, [tabindex]')).toBeNull();
  });

  it('should update when the role input changes', () => {
    // Arrange
    const { fixture, el } = render('implement');

    // Act
    fixture.componentRef.setInput('role', 'triage');
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.role-tag')?.textContent).toBe('Role: triage');
  });
});
