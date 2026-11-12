import { TestBed } from '@angular/core/testing';
import { PublishFactsComponent } from './publish-facts';
import { PublishFacts } from '../../workers/worker-run.model';

const SHA = '0123456789abcdef0123456789abcdef01234567';

function facts(overrides: Partial<PublishFacts> = {}): PublishFacts {
  return {
    headSha: SHA,
    headCommitUrl: `https://github.com/owner/repo/commit/${SHA}`,
    pullRequestUrl: 'https://github.com/owner/repo/pull/7',
    branchCommitCount: 3,
    ...overrides,
  };
}

function render(value: PublishFacts, providerType = 'github', branchName: string | null = 'feat/42-add-retry') {
  TestBed.configureTestingModule({ imports: [PublishFactsComponent] });
  const fixture = TestBed.createComponent(PublishFactsComponent);
  fixture.componentRef.setInput('facts', value);
  fixture.componentRef.setInput('branchName', branchName);
  fixture.componentRef.setInput('providerType', providerType);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const text = (): string => (el.textContent ?? '').replace(/\s+/g, ' ').trim();
  return { el, text };
}

describe('PublishFactsComponent', () => {
  it('should label the block "Published"', () => {
    // Arrange / Act
    const { el } = render(facts());

    // Assert
    expect(el.querySelector('.publish-facts__key')?.textContent?.trim()).toBe('Published');
  });

  it('should link the 7-character SHA to the head commit, with the full SHA as title and aria-label', () => {
    // Arrange / Act
    const { el } = render(facts());

    // Assert
    const sha = el.querySelector('.publish-facts__sha') as HTMLElement;
    expect(sha.tagName.toLowerCase()).toBe('a');
    expect(sha.textContent?.trim()).toBe('0123456');
    expect(sha.getAttribute('href')).toBe(`https://github.com/owner/repo/commit/${SHA}`);
    expect(sha.getAttribute('title')).toBe(SHA);
    expect(sha.getAttribute('aria-label')).toBe(`Head commit ${SHA}`);
  });

  it('should show the short SHA as plain text when there is no head commit URL', () => {
    // Arrange / Act
    const { el } = render(facts({ headCommitUrl: null }));

    // Assert
    const sha = el.querySelector('.publish-facts__sha') as HTMLElement;
    expect(sha.tagName.toLowerCase()).toBe('span');
    expect(sha.textContent?.trim()).toBe('0123456');
    expect(sha.getAttribute('title')).toBe(SHA);
    expect(sha.getAttribute('aria-label')).toBe(`Head commit ${SHA}`);
  });

  it.each([
    [0, 'No commits'],
    [1, '1 commit'],
    [4, '4 commits'],
  ])('should read %i commits on the branch as "%s"', (count, expected) => {
    // Arrange / Act
    const { el, text } = render(facts({ branchCommitCount: count }));

    // Assert
    expect(text()).toContain('Commits on branch');
    expect(el.querySelector('.publish-facts__commit-count')?.textContent?.trim()).toBe(expected);
  });

  it('should show a "View PR" link in a "Pull request" row for GitHub', () => {
    // Arrange / Act
    const { el, text } = render(facts());

    // Assert
    expect(text()).toContain('Pull request');
    const link = el.querySelector('.publish-facts__pr-link') as HTMLAnchorElement;
    expect(link.textContent?.trim()).toBe('View PR');
    expect(link.getAttribute('href')).toBe('https://github.com/owner/repo/pull/7');
    expect(link.getAttribute('aria-label')).toBe('View pull request for branch feat/42-add-retry');
  });

  it('should use merge request wording for GitLab', () => {
    // Arrange / Act
    const { el, text } = render(
      facts({ pullRequestUrl: 'https://gitlab.com/group/project/-/merge_requests/3' }),
      'gitlab',
    );

    // Assert
    expect(text()).toContain('Merge request');
    expect(el.querySelector('.publish-facts__pr-link')?.textContent?.trim()).toBe('View MR');
  });

  it('should leave the pull request row out when there is no pull request', () => {
    // Arrange / Act
    const { el, text } = render(facts({ pullRequestUrl: null }));

    // Assert
    expect(el.querySelector('.publish-facts__pr-link')).toBeNull();
    expect(text()).not.toContain('Pull request');
  });

  it('should pass links through safeHref', () => {
    // Arrange / Act
    const { el } = render(facts({ headCommitUrl: 'javascript:alert(1)', pullRequestUrl: 'http://example.com/pr/1' }));

    // Assert
    expect(el.querySelector('a')).toBeNull();
  });
});
