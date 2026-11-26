import { GUARDED_CONSOLE_METHODS, undeclaredConsoleOutput } from './console-guard';

describe('undeclaredConsoleOutput', () => {
  it('passes a test that wrote nothing', () => {
    expect(
      undeclaredConsoleOutput(GUARDED_CONSOLE_METHODS.map((method) => ({ method, calls: [] }))),
    ).toBeNull();
  });

  it('guards error, warn, log, info and debug', () => {
    expect([...GUARDED_CONSOLE_METHODS].sort()).toEqual(['debug', 'error', 'info', 'log', 'warn']);
  });

  it('fails a console.error call, naming the method and its arguments', () => {
    const failure = undeclaredConsoleOutput([
      { method: 'error', calls: [['load failed', new Error('boom')]] },
    ]);

    expect(failure).toContain('console.error: load failed Error: boom');
  });

  it('fails a console.warn call, naming the method and its arguments', () => {
    const failure = undeclaredConsoleOutput([{ method: 'warn', calls: [['NG0956']] }]);

    expect(failure).toContain('console.warn: NG0956');
  });

  it('fails a console.log call, naming the method and its arguments', () => {
    const failure = undeclaredConsoleOutput([{ method: 'log', calls: [['state', { id: 1 }]] }]);

    expect(failure).toContain('console.log: state {"id":1}');
  });

  it('fails a console.info call, naming the method and its arguments', () => {
    const failure = undeclaredConsoleOutput([{ method: 'info', calls: [['connected']] }]);

    expect(failure).toContain('console.info: connected');
  });

  it('fails a console.debug call, naming the method and its arguments', () => {
    const failure = undeclaredConsoleOutput([{ method: 'debug', calls: [['tick', 3]] }]);

    expect(failure).toContain('console.debug: tick 3');
  });

  it('lists every method that has calls', () => {
    const failure = undeclaredConsoleOutput([
      { method: 'error', calls: [['first failure']] },
      { method: 'warn', calls: [] },
      { method: 'log', calls: [['stray log']] },
      { method: 'info', calls: [] },
      { method: 'debug', calls: [['stray debug']] },
    ]);

    expect(failure).toContain('console.error: first failure');
    expect(failure).toContain('console.log: stray log');
    expect(failure).toContain('console.debug: stray debug');
    expect(failure).not.toContain('console.warn');
    expect(failure).not.toContain('console.info');
  });
});
