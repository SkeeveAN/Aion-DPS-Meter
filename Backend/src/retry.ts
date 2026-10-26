/** An HTTP answer that was not ok, for the retry decision. */
export class HttpFailure extends Error {
  constructor(readonly status: number, message: string) {
    super(message);
  }
}

/**
 * Runs `task` up to `delaysMs.length + 1` times, waiting the given time between tries, as long as `retryable` says the failure is worth another try.
 * The last failure is thrown. The sleep is injectable for tests.
 */
export async function retrying<T>(
  task: (attempt: number) => Promise<T>,
  retryable: (error: unknown) => boolean,
  delaysMs: readonly number[],
  sleep: (ms: number) => Promise<void> = (ms) => new Promise((r) => setTimeout(r, ms)),
): Promise<T> {
  for (let attempt = 1; ; attempt++) {
    try {
      return await task(attempt);
    } catch (error) {
      const wait = delaysMs[attempt - 1];
      if (wait === undefined || !retryable(error)) {
        throw error;
      }
      await sleep(wait);
    }
  }
}

/** GitHub answers that a second try can fix: no answer at all (a timeout, a connection that failed), a server error, "too many", and the "malformed request" it sometimes gives to a big upload. */
export function isTransientGithubFailure(error: unknown): boolean {
  if (error instanceof HttpFailure) {
    return error.status >= 500 || error.status === 429 || (error.status === 400 && /malformed/i.test(error.message));
  }
  return error instanceof TypeError || (error instanceof Error && (error.name === "AbortError" || error.name === "TimeoutError"));
}
