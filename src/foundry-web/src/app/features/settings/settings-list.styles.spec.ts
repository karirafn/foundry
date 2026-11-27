import { resolve } from 'node:path';
import { compile } from 'sass';

describe('settings-list partial', () => {
  it('passes the problem block start column through to its grid line', () => {
    // Act
    const css = compile(
      resolve(process.cwd(), 'src/app/features/settings/repositories/repository-list/repository-list.scss'),
      { style: 'compressed' },
    ).css;

    // Assert
    expect(css).toContain('grid-column:3/-1');
  });
});
