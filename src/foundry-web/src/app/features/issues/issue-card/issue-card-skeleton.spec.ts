import { TestBed } from '@angular/core/testing';
import { IssueCardSkeletonComponent } from './issue-card-skeleton';

function createComponent() {
  TestBed.configureTestingModule({
    imports: [IssueCardSkeletonComponent],
  });
  const fixture = TestBed.createComponent(IssueCardSkeletonComponent);
  fixture.detectChanges();
  return fixture;
}

describe('IssueCardSkeletonComponent', () => {
  // Cycle 1: component creates and renders root element
  it('should create the component', () => {
    // Arrange / Act
    const fixture = createComponent();

    // Assert
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render the root skeleton element', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const root = el.querySelector('.issue-card-skeleton');
    expect(root).toBeTruthy();
  });

  // Cycle 2: root element has aria-hidden="true"
  it('should have aria-hidden="true" on the root element', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const root = el.querySelector('.issue-card-skeleton') as HTMLElement;
    expect(root?.getAttribute('aria-hidden')).toBe('true');
  });
});
