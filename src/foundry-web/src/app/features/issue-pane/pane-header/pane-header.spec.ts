import { TestBed } from '@angular/core/testing';
import { IssueSummary } from '../../issues/issue.model';
import { PaneHeaderComponent } from './pane-header';

const summary: IssueSummary = {
  id: 'f',
  issueNumber: 142,
  title: 'Add retry to the webhook relay',
  state: 'review',
  repositorySlug: 'acme/api',
  detectedAt: '2026-01-01T00:00:00Z',
  url: 'https://github.com/acme/api/issues/142',
  providerType: 'github',
  stage: 'awaiting_review',
  needsAction: true,
  awaitsDispatch: false,
  waitsOnBlockers: false,
  codeStep: null,
  repositoryId: 'repo-1',
  enteredStateAt: '2026-01-01T00:00:00Z',
};

function setup(issue: IssueSummary | null, pullRequestUrl: string | null = null) {
  const fixture = TestBed.createComponent(PaneHeaderComponent);
  fixture.componentRef.setInput('issue', issue);
  fixture.componentRef.setInput('titleId', 'issue-pane-title');
  fixture.componentRef.setInput('pullRequestUrl', pullRequestUrl);
  let closes = 0;
  fixture.componentInstance.closed.subscribe(() => closes++);
  fixture.detectChanges();
  return { el: fixture.nativeElement as HTMLElement, closes: () => closes };
}

describe('PaneHeaderComponent', () => {
  it('heads the pane with the issue number and title, keeping the full title in its title attribute', () => {
    // Act
    const { el } = setup(summary);

    // Assert
    const heading = el.querySelector('h2#issue-pane-title');
    expect(heading?.textContent?.trim()).toBe('#142 Add retry to the webhook relay');
    expect(heading?.getAttribute('title')).toBe('Add retry to the webhook relay');
    expect(heading?.getAttribute('tabindex')).toBe('-1');
  });

  it('shows the repository slug', () => {
    // Act
    const { el } = setup(summary);

    // Assert
    expect(el.querySelector('.pane-header__repo')?.textContent?.trim()).toBe('acme/api');
  });

  it('links the issue and the pull request in new tabs with names that say what they open', () => {
    // Act
    const { el } = setup(summary, 'https://github.com/acme/api/pull/7');

    // Assert
    const links = Array.from(el.querySelectorAll<HTMLAnchorElement>('.pane-header__links a'));
    expect(links.map((a) => a.textContent?.trim())).toEqual(['View issue', 'View PR']);
    expect(links.map((a) => a.getAttribute('aria-label'))).toEqual([
      'View issue #142 on GitHub',
      'View pull request for issue #142',
    ]);
    expect(links.map((a) => a.getAttribute('href'))).toEqual([
      'https://github.com/acme/api/issues/142',
      'https://github.com/acme/api/pull/7',
    ]);
    expect(links.every((a) => a.target === '_blank' && a.rel === 'noopener noreferrer')).toBe(true);
  });

  it('calls a GitLab pull request a merge request', () => {
    // Act
    const { el } = setup({ ...summary, providerType: 'gitlab' }, 'https://gitlab.com/acme/api/-/merge_requests/7');

    // Assert
    const pr = el.querySelectorAll('.pane-header__links a')[1];
    expect(pr.textContent?.trim()).toBe('View MR');
    expect(pr.getAttribute('aria-label')).toBe('View merge request for issue #142');
  });

  it('shows no pull request link without a pull request', () => {
    // Act
    const { el } = setup(summary);

    // Assert
    expect(Array.from(el.querySelectorAll('.pane-header__links a')).map((a) => a.textContent?.trim())).toEqual(['View issue']);
  });

  it('shows no issue link for a URL that is not http(s)', () => {
    // Act
    const { el } = setup({ ...summary, url: 'javascript:alert(1)' });

    // Assert
    expect(el.querySelector('.pane-header__links a')).toBeNull();
  });

  it('shows a busy heading with skeleton bars without a summary', () => {
    // Act
    const { el } = setup(null);

    // Assert
    const heading = el.querySelector('h2#issue-pane-title');
    expect(heading?.getAttribute('aria-busy')).toBe('true');
    expect(heading?.textContent?.trim()).toBe('Loading issue…');
    expect(heading?.querySelectorAll('.pane-header__bar').length).toBe(2);
  });

  it('has a named close button that asks to close the pane', () => {
    // Arrange
    const { el, closes } = setup(summary);

    // Act
    (el.querySelector('button[aria-label="Close issue details"]') as HTMLButtonElement).click();

    // Assert
    expect(closes()).toBe(1);
  });
});
