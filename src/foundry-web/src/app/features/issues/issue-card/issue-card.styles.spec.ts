import { resolve } from 'node:path';
import { compile } from 'sass';

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

// At phone width the longest state badge plus an outcome badge overflow the meta row unless
// the badge group wraps to its own line and the badges wrap between each other.
describe('issue-card stylesheet badge wrapping', () => {
  const issueCardPath = 'src/app/features/issues/issue-card/issue-card.scss';

  it('wraps the meta row so the badge group can move to its own line', () => {
    // Act
    const body = ruleBody(compiledCss(issueCardPath), '.issue-card__meta');

    // Assert
    expect(body).toContain('flex-wrap: wrap');
    expect(body).toContain('gap: var(--fd-spacing-xs)');
  });

  it('wraps badges between badges within the card width', () => {
    // Act
    const body = ruleBody(compiledCss(issueCardPath), '.issue-card__badge');

    // Assert
    expect(body).toContain('flex-wrap: wrap');
    expect(body).toContain('max-width: 100%');
    expect(body).toContain('gap: var(--fd-spacing-xs)');
  });

  it('keeps the slug as the item that truncates first', () => {
    // Act
    const body = ruleBody(compiledCss(issueCardPath), '.issue-card__slug');

    // Assert
    expect(body).toContain('flex: 1');
    expect(body).toContain('text-overflow: ellipsis');
  });
});
