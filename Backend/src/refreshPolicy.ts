// When NC's data about a player is fetched again. Nobody can ask for everybody every hour, and most players do not change between two looks,
// so the wait depends on how active the player is, and it doubles while nothing changes.

const HOUR = 3_600_000;
const DAY = 24 * HOUR;

/** A character's data is looked at again after this long at the earliest, by activity (the last time he showed up in an upload). */
export function baseInterval(lastSeenMs: number | null, now: number): number {
  const idle = lastSeenMs === null ? Infinity : Math.max(0, now - lastSeenMs);
  if (idle < DAY) return DAY;
  if (idle < 7 * DAY) return 3 * DAY;
  if (idle < 30 * DAY) return 7 * DAY;
  return 14 * DAY;
}

/**
 * The wait until the next look at a character: the base by activity, doubled for every look that found nothing new, up to a cap (two weeks
 * for players seen this week, a month for the others). The own client's profile is the freshest source for that player, so he waits twice as long.
 */
export function characterInterval(lastSeenMs: number | null, unchanged: number, now: number, ownClient = false): number {
  const idle = lastSeenMs === null ? Infinity : Math.max(0, now - lastSeenMs);
  const cap = idle < 7 * DAY ? 14 * DAY : 30 * DAY;
  const waited = baseInterval(lastSeenMs, now) * 2 ** Math.min(Math.max(0, unchanged), 6) * (ownClient ? 2 : 1);
  return Math.min(waited, Math.max(cap, baseInterval(lastSeenMs, now)));
}

/** The portrait only changes with the looks: weekly for players seen this month, monthly for the rest. */
export function portraitInterval(lastSeenMs: number | null, now: number): number {
  return lastSeenMs !== null && now - lastSeenMs < 30 * DAY ? 7 * DAY : 30 * DAY;
}

/** The server stores times as "2026-10-11 12:00:00" (UTC) or as ISO text. */
export function parseSeen(text: string | null | undefined): number | null {
  if (!text) return null;
  const ms = Date.parse(/[zZ+]|T.*[-+]\d\d:?\d\d$/.test(text) ? text : `${text.replace(" ", "T")}Z`);
  return Number.isFinite(ms) ? ms : null;
}

/** A job that came from an upload or a profile view looks at a player again only when the last look is older than this. */
export const EVENT_STALE_AFTER = { upload: 12 * HOUR, view: 6 * HOUR } as const;

/** Job priority in the queue: the sweep works in the background, an upload that named the player comes first, then somebody looking at the profile. */
export const PRIORITY = { background: 0, upload: 1, view: 2 } as const;
export type Priority = (typeof PRIORITY)[keyof typeof PRIORITY];
