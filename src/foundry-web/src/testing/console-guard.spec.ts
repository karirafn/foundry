import { undeclaredConsoleOutput } from './console-guard';

describe('undeclaredConsoleOutput', () => {
  it('passes a test that wrote nothing', () => {
    expect(
      undeclaredConsoleOutput([
        { method: 'error', calls: [], replaced: false },
        { method: 'warn', calls: [], replaced: false },
      ]),
    ).toBeNull();
  });

  it('fails a console.error call on a method the test did not replace', () => {
    const failure = undeclaredConsoleOutput([
      { method: 'error', calls: [['load failed', new Error('boom')]], replaced: false },
    ]);

    expect(failure).toContain('console.error: load failed Error: boom');
  });

  it('fails a console.warn call on a method the test did not replace', () => {
    const failure = undeclaredConsoleOutput([{ method: 'warn', calls: [['NG0956']], replaced: false }]);

    expect(failure).toContain('console.warn: NG0956');
  });

  it('passes calls on a method the test replaced', () => {
    expect(
      undeclaredConsoleOutput([
        { method: 'error', calls: [['expected failure']], replaced: true },
        { method: 'warn', calls: [['expected warning']], replaced: true },
      ]),
    ).toBeNull();
  });

  it('fails only the method the test did not replace', () => {
    const failure = undeclaredConsoleOutput([
      { method: 'error', calls: [['expected failure']], replaced: true },
      { method: 'warn', calls: [['stray warning']], replaced: false },
    ]);

    expect(failure).toContain('console.warn: stray warning');
    expect(failure).not.toContain('expected failure');
  });
});
