// The clock of the world bosses: when the other faction can be on a home map. Kept apart from crowdResolve.ts so the NC match can use it without an import loop.

/**
 * The rifts: every three hours a spacetime rift opens for one hour and the other faction can cross over; everybody is sent home when the hour ends. The schedule
 * follows the LOCAL time of the region, not UTC: in Europe the rifts open at 02:00, 05:00, 08:00, 11:00, 14:00, 17:00, 20:00 and 23:00 Central European Time, so in UTC
 * at 12:00, 15:00 ... in summer time and at 13:00, 16:00 ... in winter time (the data of 25.10.2026, the day the clocks went back: the paired server was on the map
 * 12-13 UTC before it, 13-14 UTC after it; 0.0 % of the players of the paired server in the hour after a rift). NA East and NA West fit the same rule in their own zones (until
 * the clocks go back there on 1.11.2026 that is the same UTC hours as before, which is why it could not be told apart so far); Asia and LATAM have no player of the other faction
 * on the map in any hour (0.00 % in 54 kills), their zones are not known, so they keep the UTC hours of before.
 */
export const RIFT_EVERY_HOURS = 3;
/** A little slack on both sides: the game's clock and the client's are not the same, and a fight can reach over the edge. */
const RIFT_SLACK_MS = 2 * 60_000;

const RIFT_ZONE: Record<string, string> = { Europe: "Europe/Berlin", "NA East": "America/New_York", "NA West": "America/Los_Angeles" };
const formats = new Map<string, Intl.DateTimeFormat>();

/** The hour (0-23) the wall clock of the zone shows at that moment. */
function localHour(at: number, zone: string): number {
  let f = formats.get(zone);
  if (!f) {
    f = new Intl.DateTimeFormat("en-US", { timeZone: zone, hour: "numeric", hourCycle: "h23" });
    formats.set(zone, f);
  }
  return Number(f.formatToParts(new Date(at)).find((p) => p.type === "hour")?.value ?? 0) % 24;
}

/** The region of a server's display name ("Europe - Kaisinel" -> "Europe"), null when it has none. */
export function regionOfServerName(name: string | null | undefined): string | null {
  return /^(.+?) - /.exec(name ?? "")?.[1] ?? null;
}

/** Whether a rift is open (or just about) at the moment of the kill, in the region of the uploader's server. */
export function riftOpen(startedAt: number, endedAt: number, region?: string | null): boolean {
  const zone = region ? RIFT_ZONE[region] : undefined;
  const open = (at: number) => (zone ? localHour(at, zone) % RIFT_EVERY_HOURS === 2 : new Date(at).getUTCHours() % RIFT_EVERY_HOURS === 0);
  return [startedAt - RIFT_SLACK_MS, startedAt, endedAt, endedAt + RIFT_SLACK_MS].some(open);
}
