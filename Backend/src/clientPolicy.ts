import { readFileSync } from "node:fs";
import type { FastifyReply, FastifyRequest } from "fastify";

/**
 * Update obligation: only the current client may upload.
 *
 * "Current" is decided by release TIME, not by the number (the version numbers are no order: 0.9.57 came after 0.15.x). The newest release is
 * always allowed. An older release is allowed only for the grace period after its successor came out (CLIENT_UPDATE_GRACE_HOURS, default 0:
 * the moment a newer release is known, the older ones are blocked). A version the server does not know and that is numerically above the
 * newest release is a release the server has not heard of yet and is allowed; every other unknown version (a fork, an old line, a dev build)
 * is blocked.
 *
 * The release times come from Web-Frontend/releases.json (written from the git tags by Tools/changelog/gen.mjs and deployed with the server).
 * CLIENT_UPDATE_ENFORCE=off switches the rule off.
 */

export type Release = { version: string; at: number };

const RELEASES_URL = new URL("../../Web-Frontend/releases.json", import.meta.url);

/** The released versions, oldest first. */
export function loadReleases(): Release[] {
  try {
    const raw = JSON.parse(readFileSync(RELEASES_URL, "utf8")) as Record<string, string>;
    return Object.entries(raw)
      .map(([version, iso]) => ({ version, at: Date.parse(iso) }))
      .filter((r) => Number.isFinite(r.at))
      .sort((a, b) => a.at - b.at);
  } catch {
    return [];
  }
}

/** "0.29.0" -> [0, 29, 0]; null for anything else (a leading v and a build suffix like "+abc" are ignored). */
export function parseVersion(text: unknown): [number, number, number] | null {
  if (typeof text !== "string") {
    return null;
  }
  const m = /^v?(\d{1,4})\.(\d{1,4})\.(\d{1,4})(?:[-+].*)?$/.exec(text.trim());
  return m ? [Number(m[1]), Number(m[2]), Number(m[3])] : null;
}

/** Numeric comparison of two versions: negative when a < b. */
export function compareVersions(a: [number, number, number], b: [number, number, number]): number {
  return a[0] - b[0] || a[1] - b[1] || a[2] - b[2];
}

/** On by default; CLIENT_UPDATE_ENFORCE=off switches it off. Under `node --test` it is off unless a test sets CLIENT_UPDATE_ENFORCE=on itself. */
export const enforcementOn = () => (process.env.CLIENT_UPDATE_ENFORCE ?? (process.env.NODE_TEST_CONTEXT ? "off" : "on")).toLowerCase() !== "off";
const graceMs = () => Math.max(0, Number(process.env.CLIENT_UPDATE_GRACE_HOURS ?? 0) || 0) * 3_600_000;

export type Verdict = { allowed: true } | { allowed: false; requiredVersion: string };

/** May a client of this version upload now? Pure: the releases and the clock are handed in. */
export function judgeClient(version: unknown, releases: Release[], now: number, grace = 0): Verdict {
  const latest = releases[releases.length - 1];
  if (!latest) {
    return { allowed: true }; // no release list: never lock everybody out
  }
  const parsed = parseVersion(version);
  const clean = parsed ? parsed.join(".") : null;
  if (!clean) {
    return { allowed: false, requiredVersion: latest.version };
  }
  const index = releases.findIndex((r) => r.version === clean);
  if (index === releases.length - 1) {
    return { allowed: true };
  }
  if (index >= 0) {
    return releases[index + 1].at + grace > now ? { allowed: true } : { allowed: false, requiredVersion: latest.version };
  }
  const known = parseVersion(latest.version);
  return known && compareVersions(parsed!, known) > 0 ? { allowed: true } : { allowed: false, requiredVersion: latest.version };
}

export const OUTDATED_MESSAGE =
  "You have an old client version, please update. No uploads are possible until you do. Download: https://aiondps.com/";

/** The newest release and what is required right now; with the client's version also whether it may upload (for the client's start-up check). */
export function clientPolicy(now = Date.now(), version?: unknown) {
  const releases = loadReleases();
  const latest = releases[releases.length - 1]?.version ?? null;
  const verdict = version === undefined ? null : enforcementOn() ? judgeClient(version, releases, now, graceMs()) : ({ allowed: true } as Verdict);
  return {
    enforce: enforcementOn(),
    latestVersion: latest,
    requiredVersion: latest,
    graceHours: graceMs() / 3_600_000,
    ...(verdict ? { allowed: verdict.allowed } : {}),
    message: OUTDATED_MESSAGE,
    checkedAt: new Date(now).toISOString(),
  };
}

/**
 * Fastify preHandler for the upload routes: answers 426 Upgrade Required to an outdated client, before the payload is even looked at (an old
 * client may send a payload this server would reject as invalid, and it must still learn why it cannot upload). The message comes first in the
 * body, because old clients show only the start of it.
 */
export async function requireCurrentClient(request: FastifyRequest, reply: FastifyReply): Promise<void> {
  if (!enforcementOn()) {
    return;
  }
  const version = (request.body as { clientVersion?: unknown } | null | undefined)?.clientVersion;
  const verdict = judgeClient(version, loadReleases(), Date.now(), graceMs());
  if (verdict.allowed) {
    return;
  }
  request.log.warn({ clientVersion: typeof version === "string" ? version.slice(0, 20) : null, requiredVersion: verdict.requiredVersion }, "upload refused: outdated client");
  await reply.status(426).send({
    message: OUTDATED_MESSAGE,
    error: "client_outdated",
    requiredVersion: verdict.requiredVersion,
    yourVersion: typeof version === "string" ? version.slice(0, 20) : null,
  });
}
