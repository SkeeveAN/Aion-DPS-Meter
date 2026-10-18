import { existsSync, mkdirSync, readFileSync, renameSync, writeFileSync } from "node:fs";
import path from "node:path";
import { announcementList } from "./announcements.js";
import { getNc } from "./portraitService.js";
import { env } from "./env.js";

// Twitch Drops of AION 2. Twitch has no public list of drop campaigns (the drops endpoint answers only for the game's publisher, and the
// site's own GraphQL refuses servers with "failed integrity check"), so the campaigns are read from NC's announcements instead: NC publishes
// each campaign as a notice with a table of watch time, reward and quantity plus the periods. The announcement store already finds those
// notices (category "twitchDrops"); this module fetches their full text, parses the tables and keeps the result in a file, so a campaign stays
// listed after NC drops the notice from its list. What is live right now comes from Twitch's public streams list ("Drops Enabled" tag).

type FetchFn = typeof fetch;

const BASE = "https://api-global-community.plaync.com/aion2_global/board";
const MAX_BYTES = 2 * 1024 * 1024;
const AION2_GAME_ID = "771448419";
export const TWITCH_DROPS_FILE = path.resolve(process.env.TWITCH_DROPS_FILE ?? "./data/twitch-drops.json");

export type DropReward = { minutes: number; name: string; qty: number };
export type DropCampaign = {
  id: string;
  noticeId: string;
  /** "War For Atreia", "Advanced Access Rewards" ... as NC titles the table. */
  name: string;
  /** 1-based position when NC splits one event into several periods ("1. [War For Atreia] ..."), else null. */
  part: number | null;
  /** UTC. Null while NC still says "TBD". With only a date range in the notice these are the start and end of those days in Pacific time. */
  startAt: string | null;
  endAt: string | null;
  /** True when the notice names days but no clock times. */
  dateOnly: boolean;
  rewards: DropReward[];
};
type Notice = { id: string; title: string; postedAt: string; checkedAt: number; claimBy: string | null; campaigns: DropCampaign[] };

const MONTHS = ["january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december"];
const monthOf = (name: string): number => MONTHS.findIndex((m) => m.startsWith(name.toLowerCase().slice(0, 3)));

/** Pacific time to UTC: NC writes PDT / PST (summer / winter). */
function pacificToIso(y: number, month: number, d: number, h: number, min: number, zone = "PDT"): string {
  const offset = /PST/i.test(zone) ? 8 : 7;
  return new Date(Date.UTC(y, month, d, h + offset, min)).toISOString();
}

/** The notice's HTML as plain text: tags gone, block ends become line breaks, zero-width characters out. */
export function noticeText(html: string): string {
  return html
    .replace(/<\/(p|li|tr|h\d|div)>|<br\s*\/?>/gi, "\n")
    .replace(/<[^>]+>/g, " ")
    .replace(/&nbsp;/g, " ")
    .replace(/&amp;/g, "&")
    .replace(/&lt;/g, "<")
    .replace(/&gt;/g, ">")
    .replace(/&#39;|&apos;/g, "'")
    .replace(/&quot;/g, '"')
    .replace(/[​-‍﻿]/g, "")
    .replace(/[ \t]+/g, " ");
}

/** "October 2, 2026 at 12:00 PDT" / "October 7, 2026 at 10:00 AM PDT" -> ISO (UTC), or null (also for "TBD"). */
function parseDateTime(text: string): string | null {
  const m = /([A-Za-z]+)\s+(\d{1,2}),\s*(\d{4})\s+at\s+(\d{1,2}):(\d{2})\s*(AM|PM)?\s*(P[DS]T)/i.exec(text);
  if (!m || monthOf(m[1]) < 0) {
    return null;
  }
  let hour = Number(m[4]);
  if (m[6]?.toUpperCase() === "PM" && hour < 12) {
    hour += 12;
  }
  return pacificToIso(Number(m[3]), monthOf(m[1]), Number(m[2]), hour, Number(m[5]), m[7]);
}

/** "September 30 - October 4", "October 7 - 9" -> first and last day, in the notice's year. */
function parseDayRange(text: string, year: number): { from: [number, number]; to: [number, number] } | null {
  const m = /([A-Za-z]+)\s+(\d{1,2})\s*[-–]\s*(?:([A-Za-z]+)\s+)?(\d{1,2})(?!\d|:)/.exec(text);
  if (!m || monthOf(m[1]) < 0) {
    return null;
  }
  const m2 = m[3] ? monthOf(m[3]) : monthOf(m[1]);
  if (m2 < 0) {
    return null;
  }
  void year;
  return { from: [monthOf(m[1]), Number(m[2])], to: [m2, Number(m[4])] };
}

const ROW = /(\d+)\s*(minutes?|mins?|hours?|hrs?)\s+(.+?)\s+x\s?([\d,]+)(?!\d)(?!\s*\()/gi;
const HEADER = /(?:\d+\s*\.\s*)?(?:\[[^\]]+\]\s*)?(?:All\s+)?Channels\s*:/gi;

/** All campaigns in one notice. The tables are free text, so this is strict about what counts as a row and returns nothing rather than guesses. */
export function parseDropsNotice(html: string, noticeId: string, postedAt: string): { claimBy: string | null; campaigns: DropCampaign[] } {
  const flat = noticeText(html).replace(/\s+/g, " ");
  const year = new Date(postedAt).getUTCFullYear();
  const claim = /before\s+(?:\([A-Za-z]+\)\s*)?([A-Za-z]+\s+\d{1,2},\s*\d{4}\s+at\s+\d{1,2}:\d{2}\s*(?:AM|PM)?\s*P[DS]T)/i.exec(flat);
  const claimBy = claim ? parseDateTime(claim[1]) : null;

  const starts: number[] = [];
  for (const m of flat.matchAll(HEADER)) {
    starts.push(m.index!);
  }
  const campaigns: DropCampaign[] = [];
  starts.forEach((start, i) => {
    const end = i + 1 < starts.length ? starts[i + 1] : flat.length;
    const block = flat.slice(start, end);
    const tableAt = block.search(/Watch Time/i);
    if (tableAt < 0) {
      return;
    }
    const header = block.slice(0, tableAt).trim();
    const rows: DropReward[] = [];
    for (const r of block.slice(tableAt).matchAll(ROW)) {
      const unit = r[2].toLowerCase();
      rows.push({ minutes: Number(r[1]) * (unit.startsWith("h") ? 60 : 1), name: r[3].trim(), qty: Number(r[4].replace(/,/g, "")) });
    }
    if (rows.length === 0) {
      return;
    }
    const part = /^(\d+)\s*\./.exec(header)?.[1];
    const label = /\[([^\]]+)\]/.exec(header)?.[1] ?? /Channels\s*:\s*(.*?)(?:\(|Start:|$)/i.exec(header)?.[1]?.trim() ?? "Twitch Drops";
    const startMatch = /Start:\s*(.*?)\s*End:/i.exec(header);
    const endMatch = /End:\s*(.*)$/i.exec(header);
    let startAt = startMatch ? parseDateTime(startMatch[1]) : null;
    let endAt = endMatch ? parseDateTime(endMatch[1]) : null;
    let dateOnly = false;
    if (!startMatch && !endMatch) {
      const range = parseDayRange(header, year);
      if (range) {
        startAt = pacificToIso(year, range.from[0], range.from[1], 0, 0);
        endAt = pacificToIso(year, range.to[0], range.to[1], 23, 59);
        dateOnly = true;
      }
    } else if (startMatch && endMatch && !startAt && !endAt) {
      // "Start: TBD End: TBD": the days in the heading are all that is known
      const range = parseDayRange(header.slice(0, header.search(/Start:/i)), year);
      if (range) {
        startAt = pacificToIso(year, range.from[0], range.from[1], 0, 0);
        endAt = pacificToIso(year, range.to[0], range.to[1], 23, 59);
        dateOnly = true;
      }
    }
    campaigns.push({
      id: `${noticeId}-${campaigns.length + 1}`,
      noticeId,
      name: label.replace(/\s+/g, " ").trim(),
      part: part ? Number(part) : null,
      startAt,
      endAt,
      dateOnly,
      rewards: rows,
    });
  });
  return { claimBy, campaigns };
}

let notices = new Map<string, Notice>();
let checkedAt: number | null = null;

function load(): void {
  try {
    if (existsSync(TWITCH_DROPS_FILE)) {
      const data = JSON.parse(readFileSync(TWITCH_DROPS_FILE, "utf8")) as { checkedAt?: number; notices?: Notice[] };
      notices = new Map((data.notices ?? []).filter((n) => typeof n?.id === "string" && Array.isArray(n.campaigns)).map((n) => [n.id, n]));
      checkedAt = typeof data.checkedAt === "number" ? data.checkedAt : null;
    }
  } catch {
    notices = new Map();
  }
}
function save(): void {
  try {
    mkdirSync(path.dirname(TWITCH_DROPS_FILE), { recursive: true });
    const tmp = `${TWITCH_DROPS_FILE}.tmp`;
    writeFileSync(tmp, JSON.stringify({ checkedAt, notices: [...notices.values()] }));
    renameSync(tmp, TWITCH_DROPS_FILE);
  } catch {
    // the next refresh writes it again
  }
}
export function resetTwitchDrops(): void {
  notices = new Map();
  checkedAt = null;
}

const RECHECK_MS = 6 * 3600_000; // NC edits a notice (a "TBD" gets its date): look at the recent ones again

/** Fetches the drops notices the announcement store knows and parses new or recently changed ones. Returns how many notices were parsed. */
export async function refreshTwitchDrops(fetchFn: FetchFn = fetch, now = Date.now(), pauseMs = 1_000): Promise<number> {
  const list = announcementList("en").items.filter((x) => x.category === "twitchDrops");
  let parsed = 0;
  for (const item of list) {
    const known = notices.get(item.id);
    const recent = now - Date.parse(item.postedAt) < 45 * 86400_000;
    if (known && (!recent || now - known.checkedAt < RECHECK_MS)) {
      continue;
    }
    try {
      const { body } = await getNc(fetchFn, new URL(`${BASE}/notice_en/article/${encodeURIComponent(item.id)}`), MAX_BYTES);
      const json = JSON.parse(body.toString("utf8")) as { article?: { content?: { content?: unknown } } };
      const html = json?.article?.content?.content;
      if (typeof html === "string") {
        const { claimBy, campaigns } = parseDropsNotice(html, item.id, item.postedAt);
        // a notice without tables (a reminder) keeps its place so it is not fetched again and again; a parse that found nothing in a known notice keeps the old result
        if (campaigns.length > 0 || !known) {
          notices.set(item.id, { id: item.id, title: item.title, postedAt: item.postedAt, checkedAt: now, claimBy, campaigns });
        } else {
          known.checkedAt = now;
        }
        parsed++;
      }
    } catch {
      // try again next time
    }
    if (pauseMs > 0) {
      await new Promise((r) => setTimeout(r, pauseMs));
    }
  }
  if (list.length > 0) {
    checkedAt = now;
    save();
  }
  return parsed;
}

// --- Live signal: streams in the AION 2 category that carry Twitch's "Drops Enabled" tag (localised: DropsEnabled, DropsВключены, Drops有効 ...) ---

export type LiveChannel = { login: string; name: string; viewers: number; title: string; language: string };
export type LiveDrops = { checkedAt: string; streams: number; viewers: number; channels: LiveChannel[] };
let liveCache: { at: number; data: LiveDrops } | null = null;
let appToken: { value: string; expires: number } | null = null;
const LIVE_TTL_MS = 5 * 60_000;
const LIVE_RETRY_MS = 60_000;
let liveFailedAt: number | null = null;
let liveInflight: Promise<LiveDrops | null> | null = null;

async function twitchToken(fetchFn: FetchFn, now: number): Promise<string | null> {
  if (!env.TWITCH_CLIENT_ID || !env.TWITCH_CLIENT_SECRET) {
    return null;
  }
  if (appToken && appToken.expires - 3600_000 > now) {
    return appToken.value;
  }
  const res = await fetchFn("https://id.twitch.tv/oauth2/token", {
    method: "POST",
    headers: { "content-type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ client_id: env.TWITCH_CLIENT_ID, client_secret: env.TWITCH_CLIENT_SECRET, grant_type: "client_credentials" }),
    signal: AbortSignal.timeout(10_000),
  });
  if (!res.ok) {
    throw new Error(`twitch token HTTP ${res.status}`);
  }
  const json = (await res.json()) as { access_token?: string; expires_in?: number };
  if (!json.access_token) {
    throw new Error("twitch token missing");
  }
  appToken = { value: json.access_token, expires: now + (json.expires_in ?? 3600) * 1000 };
  return appToken.value;
}

/** Cached for five minutes; null without Twitch credentials or while Twitch does not answer. */
export async function liveDrops(fetchFn: FetchFn = fetch, now = Date.now()): Promise<LiveDrops | null> {
  if (liveCache && now - liveCache.at < LIVE_TTL_MS) {
    return liveCache.data;
  }
  // While Twitch does not answer, the page serves the last data (or none) for a minute instead of waiting for the timeouts on every visit.
  if (liveFailedAt !== null && now - liveFailedAt < LIVE_RETRY_MS) {
    return liveCache?.data ?? null;
  }
  // One request towards Twitch at a time, however many visitors arrive while it runs.
  liveInflight ??= fetchLiveDrops(fetchFn, now).finally(() => {
    liveInflight = null;
  });
  return liveInflight;
}

async function fetchLiveDrops(fetchFn: FetchFn, now: number): Promise<LiveDrops | null> {
  try {
    for (let attempt = 0; attempt < 2; attempt++) {
      const token = await twitchToken(fetchFn, now);
      if (!token) {
        return null;
      }
      const res = await fetchFn(`https://api.twitch.tv/helix/streams?game_id=${AION2_GAME_ID}&first=100`, {
        headers: { Authorization: `Bearer ${token}`, "Client-Id": env.TWITCH_CLIENT_ID },
        signal: AbortSignal.timeout(10_000),
      });
      if (res.status === 401) {
        appToken = null; // revoked or expired early: fetch a new one once
        continue;
      }
      if (!res.ok) {
        throw new Error(`twitch streams HTTP ${res.status}`);
      }
      const rows = ((await res.json()) as { data?: { user_login: string; user_name: string; viewer_count: number; title: string; language: string; tags?: string[] }[] }).data ?? [];
      const drops = rows.filter((s) => (s.tags ?? []).some((t) => /^drops/i.test(t))).sort((a, b) => b.viewer_count - a.viewer_count);
      const data: LiveDrops = {
        checkedAt: new Date(now).toISOString(),
        streams: drops.length,
        viewers: drops.reduce((n, s) => n + s.viewer_count, 0),
        channels: drops.slice(0, 8).map((s) => ({ login: s.user_login, name: s.user_name, viewers: s.viewer_count, title: s.title.slice(0, 120), language: s.language })),
      };
      liveCache = { at: now, data };
      liveFailedAt = null;
      return data;
    }
  } catch {
    liveFailedAt = now;
    return liveCache?.data ?? null;
  }
  return null;
}

export type TwitchDropsView = { checkedAt: string | null; claimBy: string | null; campaigns: DropCampaign[]; live: LiveDrops | null };

/** One list of campaigns from all notices: NC repeats a campaign's table in later reminders, so equal campaigns (same name and rewards) count once, taking the version with clock times, else the newest. */
export function mergeCampaigns(all: Pick<Notice, "postedAt" | "campaigns">[]): DropCampaign[] {
  const best = new Map<string, { c: DropCampaign; at: string }>();
  for (const n of all) {
    for (const c of n.campaigns) {
      const key = `${c.name.toLowerCase()}|${c.rewards.map((r) => `${r.minutes}:${r.name.toLowerCase()}:${r.qty}`).join(",")}`;
      const cur = best.get(key);
      if (!cur || (cur.c.dateOnly && !c.dateOnly) || (cur.c.dateOnly === c.dateOnly && n.postedAt > cur.at)) {
        best.set(key, { c: { ...c, part: c.part ?? cur?.c.part ?? null, startAt: c.startAt ?? cur?.c.startAt ?? null, endAt: c.endAt ?? cur?.c.endAt ?? null }, at: n.postedAt });
      }
    }
  }
  return [...best.values()].map((x) => x.c).sort((a, b) => (a.startAt ?? "9").localeCompare(b.startAt ?? "9") || (a.part ?? 0) - (b.part ?? 0));
}

export async function twitchDropsView(): Promise<TwitchDropsView> {
  const all = [...notices.values()];
  const claimBy = all.map((n) => n.claimBy).filter((x): x is string => !!x).sort().pop() ?? null;
  return { checkedAt: checkedAt ? new Date(checkedAt).toISOString() : null, claimBy, campaigns: mergeCampaigns(all), live: await liveDrops() };
}

/** What the meter's Twitch icon needs: is a campaign running, and when did the newest started one begin (epoch seconds; a new campaign makes it grow, which lights the "New" badge). */
export function statusOf(campaigns: DropCampaign[], now: number): { running: boolean; latest: number | null; endsAt: string | null } {
  const started = campaigns.filter((c) => c.startAt !== null && Date.parse(c.startAt) <= now);
  const live = started.filter((c) => c.endAt !== null && Date.parse(c.endAt) > now);
  const latest = started.length ? Math.max(...started.map((c) => Math.floor(Date.parse(c.startAt!) / 1000))) : null;
  const endsAt = live.length ? live.map((c) => c.endAt!).sort().pop()! : null;
  return { running: live.length > 0, latest, endsAt };
}

export function twitchDropsStatus(now = Date.now()) {
  return statusOf(mergeCampaigns([...notices.values()]), now);
}

/** Off under tests and with NC_MATCH_OFF=1 (like the other NC jobs). Starts after the announcement store has had its first refresh. */
export function startTwitchDrops(intervalMs = 30 * 60_000): void {
  load();
  if (process.env.NC_MATCH_OFF === "1" || process.env.NODE_TEST_CONTEXT) {
    return;
  }
  const run = () => void refreshTwitchDrops().catch(() => undefined);
  setTimeout(run, 90_000).unref();
  setInterval(run, intervalMs).unref();
}
