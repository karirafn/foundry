import { resolve } from 'node:path';
import { compile, compileString } from 'sass';

// Under emulated encapsulation a @use'd partial that emits rules is copied into every
// consumer's compiled CSS and counts against its style budget, so the partial stays mixin-only.
describe('settings-list partial', () => {
  it('emits no CSS of its own', () => {
    // Act
    const css = compileString('@use "settings-list";', { loadPaths: [resolve(process.cwd(), 'src/styles')] }).css;

    // Assert
    expect(css).toBe('');
  });

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
