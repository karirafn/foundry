import { resolve } from 'node:path';
import { compile } from 'sass';

// Emulated view encapsulation scopes a component's stylesheet to its own template, so
// the badge classes the issue-detail template uses must be emitted by its own stylesheet.
function compiledCss(relativePath: string): string {
  return compile(resolve(process.cwd(), relativePath)).css;
}

function ruleBody(css: string, selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const match = css.match(new RegExp(`(?:^|\\n|,\\s*)${escaped}\\s*\\{([^}]*)\\}`));
  if (!match) {
    throw new Error(`Rule ${selector} not found in compiled stylesheet`);
  }
  return match[1];
}

describe('issue-detail stylesheet badge chips', () => {
  const issueDetailPath = 'src/app/features/issues/issue-detail/issue-detail.scss';
  const stateBadgePath = 'src/app/shared/components/state-badge/state-badge.scss';

  it('gives the base .badge class its pill shape and typography', () => {
    // Act
    const body = ruleBody(compiledCss(issueDetailPath), '.badge');

    // Assert
    expect(body).toContain('border-radius: 9999px');
    expect(body).toContain('font-size: 11px');
    expect(body).toContain('text-transform: uppercase');
  });

  it.each(['.badge--retrying', '.badge--revision-in-progress', '.badge--retry-exhausted'])(
    'styles the %s chip variant identically to fd-state-badge',
    (selector) => {
      // Act
      const issueDetailBody = ruleBody(compiledCss(issueDetailPath), selector);
      const stateBadgeBody = ruleBody(compiledCss(stateBadgePath), selector);

      // Assert
      expect(issueDetailBody).toBe(stateBadgeBody);
    },
  );
});
