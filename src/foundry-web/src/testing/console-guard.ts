/** The console methods a spec must declare before it writes to them. */
export type GuardedConsoleMethod = 'error' | 'warn';

export const GUARDED_CONSOLE_METHODS: readonly GuardedConsoleMethod[] = ['error', 'warn'];

/** What one guarded console method saw during a test. */
export interface ConsoleMethodUsage {
  readonly method: GuardedConsoleMethod;
  /** The calls that reached the console's own implementation. */
  readonly calls: readonly (readonly unknown[])[];
  /** Whether the test replaced the method's implementation, e.g. `vi.spyOn(console, 'error').mockImplementation(...)`. */
  readonly replaced: boolean;
}

/** The failure message for console output a test did not declare, or `null` when there is none. */
export function undeclaredConsoleOutput(usages: readonly ConsoleMethodUsage[]): string | null {
  const undeclared = usages.filter((usage) => usage.calls.length > 0 && !usage.replaced);
  if (undeclared.length === 0) {
    return null;
  }

  const lines = undeclared.flatMap((usage) =>
    usage.calls.map((args) => `console.${usage.method}: ${args.map(describe).join(' ')}`),
  );
  return [
    'The test wrote to the console without declaring it. Spy on the method, replace its implementation and assert the call:',
    ...lines,
  ].join('\n');
}

function describe(arg: unknown): string {
  if (arg instanceof Error) {
    return arg.stack ?? `${arg.name}: ${arg.message}`;
  }
  if (typeof arg === 'string') {
    return arg;
  }
  try {
    return JSON.stringify(arg) ?? String(arg);
  } catch {
    return String(arg);
  }
}
