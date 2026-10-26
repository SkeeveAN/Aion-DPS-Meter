// The clock of the world bosses: when the other faction can be on a home map. Kept apart from crowdResolve.ts so the NC match can use it without an import loop.

/**
 * The rift hours: a spacetime rift opens every three hours on the game's UTC clock (00:00, 03:00 ... 21:00 UTC - the same in every region, 02:00, 05:00 ... 23:00
 * in summer time in Central Europe) and the other faction can cross over for the hour after it; everybody is sent home when the hour ends.
 */
export const RIFT_EVERY_HOURS = 3;
/** A little slack on both sides: the game's clock and the client's are not the same, and a fight can reach over the edge. */
const RIFT_SLACK_MS = 2 * 60_000;

/** Whether a rift hour is open (or just about) at the moment of the kill. */
export function riftOpen(startedAt: number, endedAt: number): boolean {
  return [startedAt - RIFT_SLACK_MS, startedAt, endedAt, endedAt + RIFT_SLACK_MS].some((at) => new Date(at).getUTCHours() % RIFT_EVERY_HOURS === 0);
}

