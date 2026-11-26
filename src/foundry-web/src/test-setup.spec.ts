import { TestContext } from 'vitest';
import { GUARDED_CONSOLE_METHODS } from './testing/console-guard';

const GUARD_FAILURE = 'The test wrote to the console without declaring it.';

// The guard fails a test from the onTestFinished callback that src/test-setup.ts registers, which
// runs after every hook the test can register except onTestFailed. A test expecting that failure
// checks it there and, when it is the expected one, clears it so the test passes.
const expectedFailures = new Set<string>();
const seenFailures = new Set<string>();

function expectGuardFailure(context: TestContext, expected: string, unexpected?: string): void {
  const name = context.task.name;
  expectedFailures.add(name);
  context.onTestFailed(({ task }) => {
    seenFailures.add(name);
    const messages = (task.result?.errors ?? []).map((error) => error.message);
    const isExpected =
      messages.length === 1 &&
      messages[0].startsWith(GUARD_FAILURE) &&
      messages[0].includes(expected) &&
      (unexpected === undefined || !messages[0].includes(unexpected));
    if (!isExpected || task.result === undefined) {
      throw new Error(
        `Expected only the console guard's failure naming "${expected}", got:\n${messages.join('\n')}`,
      );
    }
    task.result.errors = [];
    task.result.state = 'pass';
  });
}

describe('the console guard in test-setup.ts', () => {
  afterAll(() => {
    const missed = [...expectedFailures].filter((name) => !seenFailures.has(name));
    expect(missed, 'tests the console guard did not fail').toEqual([]);
  });

  it.for(GUARDED_CONSOLE_METHODS)('fails an unstubbed console.%s call', (method, context) => {
    expectGuardFailure(context, `console.${method}: stray ${method} output`);

    console[method](`stray ${method} output`);
  });

  it('fails a call made before the method is stubbed with mockImplementation', (context) => {
    expectGuardFailure(context, 'console.error: written before the stub');

    console.error('written before the stub');
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
  });

  it('passes a call made after the method is stubbed with mockImplementation', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);

    console.warn('written after the stub');

    expect(warn).toHaveBeenCalledWith('written after the stub');
  });

  it('fails the second call on a mockImplementationOnce stub', (context) => {
    expectGuardFailure(context, 'console.error: second call', 'first call');
    vi.spyOn(console, 'error').mockImplementationOnce(() => undefined);

    console.error('first call');
    console.error('second call');
  });
});
