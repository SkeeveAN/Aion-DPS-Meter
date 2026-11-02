/**
 * Lets the event loop run one turn: waiting timers, sockets and the clean-up of native objects (better-sqlite3 statements, ...) get their turn.
 *
 * Filing one crowded world boss upload (1,300+ players) is a synchronous job of seconds, and several of them in a row only chained by promises
 * never let the loop turn (promise callbacks run before it does). Measured on 2.11.2026 with such a fight: without a turn in between, every filing kept
 * about 85 MB of native memory until the process was killed (5.8 GB), and the site did not answer meanwhile; with one `setImmediate` between two
 * filings the memory stayed flat at 430 MB.
 */
export const yieldToEventLoop = (): Promise<void> => new Promise((resolve) => setImmediate(resolve));
