import { TestBed } from '@angular/core/testing';
import { RepositoryAddProgressComponent } from './repository-add-progress';

describe('RepositoryAddProgressComponent', () => {
  function setup() {
    TestBed.configureTestingModule({ imports: [RepositoryAddProgressComponent] });
    const fixture = TestBed.createComponent(RepositoryAddProgressComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('should render a status region with the "Adding…" label', () => {
    // Arrange / Act
    const fixture = setup();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const status = el.querySelector('[role="status"]');
    expect(status).toBeTruthy();
    expect(status?.textContent).toContain('Adding…');
  });

  it('should render an indeterminate bar hidden from assistive technology', () => {
    // Arrange / Act
    const fixture = setup();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const track = el.querySelector('.repository-add-progress__track');
    expect(track?.getAttribute('aria-hidden')).toBe('true');
    expect(el.querySelector('.repository-add-progress__bar')).toBeTruthy();
  });

  it('should not render a spinner', () => {
    // Arrange / Act
    const fixture = setup();

    // Assert
    expect((fixture.nativeElement as HTMLElement).querySelector('fd-spinner')).toBeFalsy();
  });
});
