import { TestBed } from '@angular/core/testing';
import { StateBadgeComponent } from './state-badge';
import { IssueState } from '../../../features/issues/issue.model';

function createComponent(state: IssueState) {
  TestBed.configureTestingModule({
    imports: [StateBadgeComponent],
  });
  const fixture = TestBed.createComponent(StateBadgeComponent);
  fixture.componentRef.setInput('state', state);
  fixture.detectChanges();
  return fixture;
}

describe('StateBadgeComponent', () => {
  // Cycle 1: component creates and renders a pill element
  it('should create the component', () => {
    // Arrange / Act
    const fixture = createComponent('detected');

    // Assert
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render a span without role="status" as it is a static label not a live region', () => {
    // Arrange / Act
    const fixture = createComponent('detected');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const span = el.querySelector('span');
    expect(span).toBeTruthy();
    expect(span?.getAttribute('role')).toBeNull();
  });

  // Cycle 2: label text for each state
  it('should display "DETECTED" for detected state', () => {
    // Arrange / Act
    const fixture = createComponent('detected');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('DETECTED');
  });

  it('should display "QUEUED" for queued state', () => {
    // Arrange / Act
    const fixture = createComponent('queued');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('QUEUED');
  });

  it('should display "BLOCKED" for blocked state', () => {
    // Arrange / Act
    const fixture = createComponent('blocked');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('BLOCKED');
  });

  it('should display "IN PROGRESS" for in_progress state', () => {
    // Arrange / Act
    const fixture = createComponent('in_progress');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('IN PROGRESS');
  });

  it('should display "REVIEW" for review state', () => {
    // Arrange / Act
    const fixture = createComponent('review');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('REVIEW');
  });

  it('should display "UNCHANGED" for unchanged state', () => {
    // Arrange / Act
    const fixture = createComponent('unchanged');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('UNCHANGED');
  });

  it('should display "FAILED" for failed state', () => {
    // Arrange / Act
    const fixture = createComponent('failed');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('FAILED');
  });

  it('should display "COMPLETED" for completed state', () => {
    // Arrange / Act
    const fixture = createComponent('completed');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('COMPLETED');
  });

  it('should display "DISMISSED" for dismissed state', () => {
    // Arrange / Act
    const fixture = createComponent('dismissed');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('DISMISSED');
  });

  it('should display "REV QUEUED" for revision_queued state', () => {
    // Arrange / Act
    const fixture = createComponent('revision_queued');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('REV QUEUED');
  });

  it('should display "REV IN PROGRESS" for revision_in_progress state', () => {
    // Arrange / Act
    const fixture = createComponent('revision_in_progress');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('REV IN PROGRESS');
  });

  it('should display "REV FAILED" for revision_failed state', () => {
    // Arrange / Act
    const fixture = createComponent('revision_failed');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.textContent?.trim()).toBe('REV FAILED');
  });

  // Cycle 3: aria-label is human-readable
  it('should set aria-label with human-readable state description', () => {
    // Arrange / Act
    const fixture = createComponent('in_progress');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.getAttribute('aria-label')).toBe('State: in progress');
  });

  it('should set aria-label for revision_in_progress', () => {
    // Arrange / Act
    const fixture = createComponent('revision_in_progress');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.getAttribute('aria-label')).toBe('State: revision in progress');
  });

  // Cycle 4: CSS class reflects state for color binding
  it('should apply a CSS class based on the current state', () => {
    // Arrange / Act
    const fixture = createComponent('completed');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.classList.contains('badge--completed')).toBe(true);
  });

  it('should apply CSS class for revision_queued state', () => {
    // Arrange / Act
    const fixture = createComponent('revision_queued');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('span')?.classList.contains('badge--revision-queued')).toBe(true);
  });
});
