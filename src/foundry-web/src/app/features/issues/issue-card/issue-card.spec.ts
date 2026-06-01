import { TestBed } from '@angular/core/testing';
import { IssueCardComponent } from './issue-card';
import { IssueSummary } from '../issue.model';

const mockIssue: IssueSummary = {
  id: 'abc123',
  issueNumber: 42,
  title: 'Enable dark mode for dashboard',
  state: 'in_progress',
  repositorySlug: 'owner/repo',
  detectedAt: '2026-01-01T00:00:00Z',
  url: 'https://github.com/owner/repo/issues/42',
};

function createComponent(issue: IssueSummary = mockIssue, expanded = false) {
  TestBed.configureTestingModule({
    imports: [IssueCardComponent],
  });
  const fixture = TestBed.createComponent(IssueCardComponent);
  fixture.componentRef.setInput('issue', issue);
  fixture.componentRef.setInput('expanded', expanded);
  fixture.detectChanges();
  return fixture;
}

describe('IssueCardComponent', () => {
  // Cycle 1: component creates and renders
  it('should create the component', () => {
    // Arrange / Act
    const fixture = createComponent();

    // Assert
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render a card container with role="button"', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    expect(card).toBeTruthy();
    expect(card.getAttribute('role')).toBe('button');
  });

  // Cycle 2: meta row renders issue number and repo slug
  it('should display the issue number in the meta row', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const number = el.querySelector('.issue-card__number');
    expect(number?.textContent?.trim()).toBe('#42');
  });

  it('should display the repository slug in the meta row', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const slug = el.querySelector('.issue-card__slug');
    expect(slug?.textContent?.trim()).toBe('owner/repo');
  });

  it('should render fd-state-badge with the issue state', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const badge = el.querySelector('fd-state-badge');
    expect(badge).toBeTruthy();
  });

  // Cycle 3: title row
  it('should display the issue title', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const title = el.querySelector('.issue-card__title');
    expect(title?.textContent?.trim()).toBe('Enable dark mode for dashboard');
  });

  it('should set title attribute on the title element for tooltip', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const title = el.querySelector('.issue-card__title') as HTMLElement;
    expect(title?.getAttribute('title')).toBe('Enable dark mode for dashboard');
  });

  // Cycle 4: footer row - timestamp and link
  it('should render a footer with a timestamp', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const timestamp = el.querySelector('.issue-card__timestamp');
    expect(timestamp).toBeTruthy();
  });

  it('should render an external link with target="_blank"', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const link = el.querySelector('.issue-card__link') as HTMLAnchorElement;
    expect(link).toBeTruthy();
    expect(link?.getAttribute('target')).toBe('_blank');
    expect(link?.getAttribute('rel')).toBe('noopener noreferrer');
    expect(link?.getAttribute('href')).toBe('https://github.com/owner/repo/issues/42');
  });

  it('should have aria-label on the external link', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const link = el.querySelector('.issue-card__link') as HTMLAnchorElement;
    expect(link?.getAttribute('aria-label')).toBe('Open issue #42 on GitHub');
  });

  // Cycle 5: toggle output on click
  it('should emit toggle when the card is clicked', () => {
    // Arrange
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;
    const card = el.querySelector('.issue-card') as HTMLElement;
    let emitted = false;
    fixture.componentInstance.toggle.subscribe(() => (emitted = true));

    // Act
    card.click();

    // Assert
    expect(emitted).toBe(true);
  });

  it('should not emit toggle when the external link is clicked', () => {
    // Arrange
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;
    const link = el.querySelector('.issue-card__link') as HTMLAnchorElement;
    let emitted = false;
    fixture.componentInstance.toggle.subscribe(() => (emitted = true));

    // Act
    const event = new MouseEvent('click', { bubbles: true });
    link.dispatchEvent(event);

    // Assert
    expect(emitted).toBe(false);
  });

  // Cycle 6: ARIA - aria-expanded reflects expanded input
  it('should set aria-expanded="false" when not expanded', () => {
    // Arrange / Act
    const fixture = createComponent(mockIssue, false);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    expect(card?.getAttribute('aria-expanded')).toBe('false');
  });

  it('should set aria-expanded="true" when expanded', () => {
    // Arrange / Act
    const fixture = createComponent(mockIssue, true);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    expect(card?.getAttribute('aria-expanded')).toBe('true');
  });

  it('should have tabindex="0" for keyboard accessibility', () => {
    // Arrange / Act
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const card = el.querySelector('.issue-card') as HTMLElement;
    expect(card?.getAttribute('tabindex')).toBe('0');
  });

  // Cycle 7: keyboard interaction
  it('should emit toggle when Enter key is pressed', () => {
    // Arrange
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;
    const card = el.querySelector('.issue-card') as HTMLElement;
    let emitted = false;
    fixture.componentInstance.toggle.subscribe(() => (emitted = true));

    // Act
    card.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));

    // Assert
    expect(emitted).toBe(true);
  });

  it('should emit toggle when Space key is pressed', () => {
    // Arrange
    const fixture = createComponent();
    const el = fixture.nativeElement as HTMLElement;
    const card = el.querySelector('.issue-card') as HTMLElement;
    let emitted = false;
    fixture.componentInstance.toggle.subscribe(() => (emitted = true));

    // Act
    card.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }));

    // Assert
    expect(emitted).toBe(true);
  });
});
