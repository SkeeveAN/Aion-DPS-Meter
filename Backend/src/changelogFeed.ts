import { readFileSync, statSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

type Release = { version: string; date: string; items: { text: string; areas?: string[] }[] };
type ShortTexts = Record<string, { en?: string; de?: string }>;

export type ChangelogFeedVersion = { version: string; date: string; items: string[] };

const WEB_FRONTEND = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "..", "Web-Frontend");
const MAX_VERSIONS = 30;
const FALLBACK_LENGTH = 110;

function parseVersion(version: string): number[] | undefined {
  const m = /^(\d{1,4})\.(\d{1,4})\.(\d{1,4})$/.exec(version);
  return m ? [Number(m[1]), Number(m[2]), Number(m[3])] : undefined;
}

function compareVersions(a: number[], b: number[]): number {
  return a[0] - b[0] || a[1] - b[1] || a[2] - b[2];
}

export function isVersion(value: unknown): value is string {
  return typeof value === "string" && parseVersion(value) !== undefined;
}

/** The line of an item that has no short text of its own: the first clause (before a bracket or a semicolon), cut at a word when it is still long. */
export function shortenText(text: string): string {
  const clause = text.split(/ \(|; /)[0].trim();
  const line = clause.length > FALLBACK_LENGTH ? `${clause.slice(0, FALLBACK_LENGTH).replace(/\s+\S*$/, "")}…` : clause;
  return line.charAt(0).toUpperCase() + line.slice(1);
}

/**
 * What the client shows in its update window: the client changes of every release after `from` up to and including `to`, newest first, one short
 * line per change. A release's last client entry is the release line that sums the others up, so it is left out when there are others; a change
 * with no short text in changelog-short.json gets its first clause. German when `lang` is "de" (the German text of the full entry for the fallback).
 */
export function buildChangelogFeed(releases: Release[], german: Record<string, string>, shorts: ShortTexts, from: string, to: string | undefined, lang: string): ChangelogFeedVersion[] {
  const low = parseVersion(from);
  const high = to === undefined ? undefined : parseVersion(to);
  if (!low) {
    return [];
  }

  const de = lang === "de";
  const feed: ChangelogFeedVersion[] = [];
  for (const release of releases) {
    const version = parseVersion(release.version);
    if (!version || compareVersions(version, low) <= 0 || (high && compareVersions(version, high) > 0)) {
      continue;
    }

    let items = release.items.filter((item) => item.areas?.includes("Client"));
    if (items.length > 1) {
      items = items.slice(0, -1);
    }

    if (items.length > 0) {
      feed.push({
        version: release.version,
        date: release.date,
        items: items.map((item) => (de ? shorts[item.text]?.de : shorts[item.text]?.en) ?? shortenText(de ? (german[item.text] ?? item.text) : item.text)),
      });
    }
  }

  return feed.sort((a, b) => compareVersions(parseVersion(b.version)!, parseVersion(a.version)!)).slice(0, MAX_VERSIONS);
}

type Loaded<T> = { mtime: number; value: T };
const cache = new Map<string, Loaded<unknown>>();

function loadJson<T>(file: string, fallback: T): T {
  const full = path.join(WEB_FRONTEND, file);
  try {
    const mtime = statSync(full).mtimeMs;
    const hit = cache.get(full);
    if (hit && hit.mtime === mtime) {
      return hit.value as T;
    }

    const value = JSON.parse(readFileSync(full, "utf8")) as T;
    cache.set(full, { mtime, value });
    return value;
  } catch {
    return fallback;
  }
}

/** The feed from the site's own files (changelog.json, changelog.de.json, changelog-short.json), read again when one of them changes. */
export function changelogFeed(from: string, to: string | undefined, lang: string): ChangelogFeedVersion[] {
  return buildChangelogFeed(
    loadJson<Release[]>("changelog.json", []),
    lang === "de" ? loadJson<Record<string, string>>("changelog.de.json", {}) : {},
    loadJson<ShortTexts>("changelog-short.json", {}),
    from,
    to,
    lang,
  );
}
