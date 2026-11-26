import { beforeEach } from 'vitest';
import {
  ConsoleMethodUsage,
  GUARDED_CONSOLE_METHODS,
  GuardedConsoleMethod,
  undeclaredConsoleOutput,
} from './testing/console-guard';

// Each test starts on the recorders, below any spy it creates, so a stub that replaces the
// method's implementation takes its calls before they reach them, a call made before the stub or
// past a mockImplementationOnce still does, and a spy an earlier test left behind does not
// silence a later one.
const recorded: Record<GuardedConsoleMethod, unknown[][]> = { error: [], warn: [], log: [], info: [], debug: [] };
const recorders = {} as Record<GuardedConsoleMethod, (...args: unknown[]) => void>;

for (const method of GUARDED_CONSOLE_METHODS) {
  const original = console[method].bind(console);
  const recorder = (...args: unknown[]): void => {
    recorded[method].push(args);
    original(...args);
  };
  recorders[method] = recorder;
  console[method] = recorder;
}

// Checked once every hook has run: a failing afterEach would skip the hooks after it, such as
// the TestBed teardown, and fail every later test in the file.
beforeEach(({ onTestFinished }) => {
  for (const method of GUARDED_CONSOLE_METHODS) {
    recorded[method] = [];
    console[method] = recorders[method];
  }
  onTestFinished(() => {
    const usages: ConsoleMethodUsage[] = GUARDED_CONSOLE_METHODS.map((method) => ({
      method,
      calls: recorded[method],
    }));
    const failure = undeclaredConsoleOutput(usages);
    if (failure !== null) {
      throw new Error(failure);
    }
  });
});
