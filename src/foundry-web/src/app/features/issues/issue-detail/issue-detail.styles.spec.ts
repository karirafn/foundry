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

describe('publish badge styles (#787)', () => {
  const publishStatusPath = 'src/app/features/issues/publish-status/publish-status.scss';
  const stateBadgePath = 'src/app/shared/components/state-badge/state-badge.scss';

  it.each(['.badge--publishing', '.badge--retrying', '.badge--retry-exhausted', '.badge--branch-diverged'])(
    'styles the %s chip in fd-publish-status identically to fd-state-badge',
    (selector) => {
      // Act
      const publishStatusBody = ruleBody(compiledCss(publishStatusPath), selector);
      const stateBadgeBody = ruleBody(compiledCss(stateBadgePath), selector);

      // Assert
      expect(publishStatusBody).toBe(stateBadgeBody);
    },
  );

  it('outlines the publishing chip in the working accent with activity text', () => {
    // Act
    const body = ruleBody(compiledCss(stateBadgePath), '.badge--publishing');

    // Assert
    expect(body).toContain('background: transparent');
    expect(body).toContain('border: 1px solid var(--fd-accent-working)');
    expect(body).toContain('color: var(--fd-text-activity)');
  });

  it('outlines the branch-diverged chip in warning colours', () => {
    // Act
    const body = ruleBody(compiledCss(stateBadgePath), '.badge--branch-diverged');

    // Assert
    expect(body).toContain('border: 1px solid var(--fd-text-warning)');
    expect(body).toContain('color: var(--fd-text-warning)');
  });

  it('fills the publish-failed chip with the failed state colour', () => {
    // Act
    const body = ruleBody(compiledCss(stateBadgePath), '.badge--publish-failed');

    // Assert
    expect(body).toContain('background-color: var(--fd-state-failed)');
  });
});

describe('diff gate badge styles (#788)', () => {
  const stateBadgePath = 'src/app/shared/components/state-badge/state-badge.scss';
  const revertedFilesPath = 'src/app/features/issues/reverted-files/reverted-files.scss';

  it('fills the publish-blocked chip with the failed state colour', () => {
    // Act
    const body = ruleBody(compiledCss(stateBadgePath), '.badge--publish-blocked');

    // Assert
    expect(body).toContain('background-color: var(--fd-state-failed)');
  });

  it('outlines the reverted-oversized chip in warning colours', () => {
    // Act
    const body = ruleBody(compiledCss(stateBadgePath), '.badge--reverted-oversized');

    // Assert
    expect(body).toContain('background: transparent');
    expect(body).toContain('border: 1px solid var(--fd-text-warning)');
    expect(body).toContain('color: var(--fd-text-warning)');
  });

  it('outlines the reverted-binary chip in the secondary text colour', () => {
    // Act
    const body = ruleBody(compiledCss(stateBadgePath), '.badge--reverted-binary');

    // Assert
    expect(body).toContain('background: transparent');
    expect(body).toContain('border: 1px solid var(--fd-text-secondary)');
    expect(body).toContain('color: var(--fd-text-secondary)');
  });

  it.each(['.badge--reverted-oversized', '.badge--reverted-binary'])(
    'styles the %s chip in fd-reverted-files identically to fd-state-badge',
    (selector) => {
      // Act
      const revertedBody = ruleBody(compiledCss(revertedFilesPath), selector);
      const stateBadgeBody = ruleBody(compiledCss(stateBadgePath), selector);

      // Assert
      expect(revertedBody).toBe(stateBadgeBody);
    },
  );
});
