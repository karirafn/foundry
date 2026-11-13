import { TestBed } from '@angular/core/testing';
import { IssueRefComponent } from './issue-ref';

function render(slug: string, issueNumber: number) {
  TestBed.configureTestingModule({ imports: [IssueRefComponent] });
  const fixture = TestBed.createComponent(IssueRefComponent);
  fixture.componentRef.setInput('slug', slug);
  fixture.componentRef.setInput('issueNumber', issueNumber);
  fixture.detectChanges();
  return { fixture, el: fixture.nativeElement as HTMLElement };
}

function text(el: Element | null | undefined): string {
  return el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
}

describe('IssueRefComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('shows the repository name, hidden from screen readers, and the number', () => {
    // Act
    const { el } = render('efla/customer-projects/isal/isal-monitoring', 12345);

    // Assert
    const name = el.querySelector('.issue-ref__name')!;
    expect(text(name)).toBe('isal-monitoring');
    expect(name.getAttribute('aria-hidden')).toBe('true');
    expect(text(el.querySelector('.issue-ref__number'))).toBe('#12345');
  });

  it('puts the full slug in its title and in screen-reader text', () => {
    // Act
    const { el } = render('efla/customer-projects/isal/isal-monitoring', 12345);

    // Assert
    expect(el.getAttribute('title')).toBe('efla/customer-projects/isal/isal-monitoring');
    expect(el.querySelector('.sr-only')?.textContent).toBe(
      'efla/customer-projects/isal/isal-monitoring ',
    );
  });

  it('reads the full slug, then the number, to screen readers', () => {
    // Act
    const { el } = render('acme/api', 42);

    // Assert
    const spoken = [...el.children]
      .filter((child) => child.getAttribute('aria-hidden') !== 'true')
      .map((child) => child.textContent)
      .join('');
    expect(spoken).toBe('acme/api #42');
  });

  it('keeps the number outside the truncating name', () => {
    // Act
    const { el } = render('efla/customer-projects/isal/isal-monitoring', 12345);

    // Assert
    const name = el.querySelector('.issue-ref__name')!;
    const number = el.querySelector('.issue-ref__number')!;
    expect(name.contains(number)).toBe(false);
    expect(number.parentElement).toBe(el);
    expect(getComputedStyle(name).textOverflow).toBe('ellipsis');
    expect(getComputedStyle(number).flexShrink).toBe('0');
  });

  it('falls back to the full slug when its last segment is empty', () => {
    // Act
    const { el } = render('acme/api/', 7);

    // Assert
    expect(text(el.querySelector('.issue-ref__name'))).toBe('acme/api/');
  });
});
