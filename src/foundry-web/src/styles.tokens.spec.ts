// Per CSS Custom Properties Level 1 §3.1, a var() of an undefined property makes the
// declaration "invalid at computed-value time": an inherited property such as `color`
// silently takes its inherited value, and a reference with a fallback silently keeps
// the fallback. So every var(--fd-…) must name a property declared under src.
import { readdirSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

function sourceFiles(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name);
    return entry.isDirectory() ? sourceFiles(path) : [path];
  });
}

describe('--fd-* custom properties', () => {
  const files = sourceFiles(resolve(process.cwd(), 'src'));
  const declared = new Set<string>();
  for (const file of files.filter((f) => /\.(scss|ts|html)$/.test(f))) {
    for (const m of readFileSync(file, 'utf-8').matchAll(/(--fd-[\w-]+)\s*:/g)) {
      declared.add(m[1]);
    }
  }

  it('every var(--fd-…) reference names a declared property', () => {
    const undefinedRefs: string[] = [];
    const referencing = files.filter((f) => /\.(scss|ts|html)$/.test(f) && !/\.spec\.ts$/.test(f));
    for (const file of referencing) {
      for (const m of readFileSync(file, 'utf-8').matchAll(/var\(\s*(--fd-[\w-]+)/g)) {
        if (!declared.has(m[1])) {
          undefinedRefs.push(`${file}: ${m[1]}`);
        }
      }
    }

    expect(undefinedRefs).toEqual([]);
  });
});
