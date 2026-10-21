import { announcementList } from "../announcements.js";
import { getNc } from "../portraitService.js";

// The scheduled game server maintenance, read from NC's own notice ("When: October 21, 2026 at 08h30 CEST ... Duration: 5.5 hours").
// During it the login is closed for players although the servers still accept connections, so the status page must not say "online".

type FetchFn = typeof fetch;

const BASE = "https://api-global-community.plaync.com/aion2_global/board";
const MAX_BYTES = 2 * 1024 * 1024;
const RECHECK_MS = 10 * 60_000; // NC edits the notice when the maintenance is extended
const LOOK_AHEAD_MS = 3 * 86_400_000;

export type MaintenanceState = "scheduled" | "active";

export interface MaintenanceWindow {
  id: string;
  title: string;
  url: string;
  /** Epoch ms. */
  start: number;
  end: number;
}

export interface MaintenanceNotice extends MaintenanceWindow {
  state: MaintenanceState;
}

const MONTHS = ["january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december"];
/** Hours to subtract from the written local time to get UTC. */
const ZONES: Record<string, number> = { CEST: 2, CET: 1, PDT: -7, PST: -8 };

/** Notice HTML -> one line of plain text. */
export function plainText(html: string): string {
  return html
    .replace(/<[^>]+>/g, " ")
    .replace(/&nbsp;/g, " ")
    .replace(/&amp;/g, "&")
    .replace(/[​\s]+/g, " ")
    .trim();
}

/** Start and length of the maintenance in a notice text, or null when it names no date (NC writes "TBD" first, then edits it). */
export function parseWindow(text: string): { start: number; end: number } | null {
  // The CEST/CET date is preferred, the PDT/PST one is the same moment.
  const re = /([A-Z][a-z]+)\s+(\d{1,2}),\s*(\d{4})\s+at\s+(\d{1,2})\s*[h:]\s*(\d{2})\s*(CEST|CET|PDT|PST)/g;
  let best: { start: number; zone: string } | null = null;
  for (const m of text.matchAll(re)) {
    const month = MONTHS.indexOf(m[1].toLowerCase());
    if (month < 0) {
      continue;
    }
    const start = Date.UTC(Number(m[3]), month, Number(m[2]), Number(m[4]) - ZONES[m[6]], Number(m[5]));
    if (!best || (/^CE/.test(m[6]) && !/^CE/.test(best.zone))) {
      best = { start, zone: m[6] };
    }
  }
  const d = /Duration:\s*(?:approx\.?|about|~)?\s*(\d+(?:[.,]\d+)?)\s*(hours?|hrs?|h|minutes?|mins?)/i.exec(text);
  if (!best || !d) {
    return null;
  }
  const n = Number(d[1].replace(",", "."));
  const minutes = /^h/i.test(d[2]) ? n * 60 : n;
  return { start: best.start, end: best.start + Math.round(minutes * 60_000) };
}

/** What the page shows for a window right now; null when it is over or still far away. */
export function stateOf(w: { start: number; end: number }, now: number): MaintenanceState | null {
  if (now >= w.end) {
    return null;
  }
  if (now >= w.start) {
    return "active";
  }
  return w.start - now <= LOOK_AHEAD_MS ? "scheduled" : null;
}

const windows = new Map<string, { checkedAt: number; window: MaintenanceWindow | null }>();

/** Looks at the newest game server maintenance notices and reads the ones that are not completed. Returns how many windows are known. */
export async function refreshMaintenance(fetchFn: FetchFn = fetch, now = Date.now()): Promise<number> {
  const list = announcementList("en")
    .items.filter((x) => x.category === "maintenance" && x.status !== "done" && !/platform/i.test(x.title))
    .slice(0, 3);
  const all = announcementList("en").items;
  for (const [id] of windows) {
    // a notice that got its "(Completed)" ends the window at once
    if (all.some((x) => x.id === id && x.status === "done")) {
      windows.delete(id);
    }
  }
  for (const item of list) {
    const known = windows.get(item.id);
    if (known && now - known.checkedAt < RECHECK_MS) {
      continue;
    }
    try {
      const { body } = await getNc(fetchFn, new URL(`${BASE}/notice_en/article/${encodeURIComponent(item.id)}`), MAX_BYTES);
      const json = JSON.parse(body.toString("utf8")) as { article?: { content?: { content?: unknown } } };
      const html = json?.article?.content?.content;
      const parsed = typeof html === "string" ? parseWindow(plainText(html)) : null;
      windows.set(item.id, { checkedAt: now, window: parsed ? { id: item.id, title: item.title, url: item.url, ...parsed } : null });
    } catch {
      // try again next time
    }
  }
  return windows.size;
}

/** The maintenance that is running or comes next (the notices that are completed are not read at all). */
export function maintenanceNotice(now = Date.now()): MaintenanceNotice | null {
  let pick: MaintenanceNotice | null = null;
  for (const { window } of windows.values()) {
    const state = window ? stateOf(window, now) : null;
    if (window && state && (!pick || window.start < pick.start)) {
      pick = { ...window, state };
    }
  }
  return pick;
}

export function resetMaintenance(): void {
  windows.clear();
}
