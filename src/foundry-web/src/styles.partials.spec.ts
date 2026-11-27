import { readdirSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { compile } from 'sass';

// Under emulated encapsulation a @use'd partial that emits rules is copied into every consumer's
// compiled CSS and counts against its style budget, so every partial stays mixin-only.
const srcDir = resolve(process.cwd(), 'src');
const loadPath = resolve(srcDir, 'styles');

// Only global styles.scss uses _motion.scss: its view-transition pseudo-elements cannot be styled
// from emulated-encapsulation component styles.
const emitsCssByDesign = new Set([resolve(srcDir, 'styles/_motion.scss')]);

const partials: string[] = readdirSync(srcDir, { recursive: true, encoding: 'utf8' })
  .filter((path) => /^_.*\.scss$/.test(path.split(/[\\/]/).pop() ?? ''))
  .map((path) => join(srcDir, path))
  .filter((path) => !emitsCssByDesign.has(path));

describe('scss partials', () => {
  it('finds the partials to check', () => {
    expect(partials.length).toBeGreaterThan(5);
  });

  for (const partial of partials) {
    it(`${partial.slice(srcDir.length + 1)} emits no CSS of its own`, () => {
      // Act
      const css = compile(partial, { loadPaths: [loadPath] }).css;

      // Assert
      expect(css).toBe('');
    });
  }
});
