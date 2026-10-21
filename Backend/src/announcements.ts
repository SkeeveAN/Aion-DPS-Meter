import { existsSync, mkdirSync, readFileSync, renameSync, writeFileSync } from "node:fs";
import path from "node:path";
import { getNc } from "./portraitService.js";

// NC's official AION 2 announcements (public community API, no login), shown on /announcements. The list endpoint returns only the newest 10 per language and the
// paging parameter is unknown, so every refresh is merged into a stored history that grows over time.
// There is no category field at NC: the kind is read from the English title and carried over to the translations by the posting time; the keyword lists
// are only the fallback for translations without an English twin (the translated lists are not identical to the English one).

type FetchFn = typeof fetch;

const BASE = "https://api-global-community.plaync.com/aion2_global/board";
const MAX_BYTES = 2 * 1024 * 1024;
/** The languages NC publishes announcements in; every other site language falls back to English. */
export const NC_LANGS = ["en", "de", "fr", "es", "pt", "ja"] as const;
export type NcLang = (typeof NC_LANGS)[number];
const WEB_LOCALE: Record<NcLang, string> = { en: "en-us", de: "de-de", fr: "fr-fr", es: "es-es", pt: "pt-br", ja: "ja-jp" };

export const CATEGORIES = ["maintenance", "outage", "newServers", "serverTransfer", "compensation", "account", "twitchDrops", "knownIssues", "other"] as const;
export type Category = (typeof CATEGORIES)[number];

export type Announcement = {
  id: string;
  lang: NcLang;
  contentId: number;
  title: string;
  summary: string;
  /** Epoch seconds. */
  posted: number;
  category: Category;
  /** "done" when the title says completed / resolved. */
  status: "done" | null;
  /** Account restrictions only: the number of restricted accounts from the "[Restriction Details]" block of the full text. undefined = not read yet, null = read, no number in it. */
  restricted?: number | null;
};

/** Lower case without accents, so one keyword covers "Compensación" and "compensacion". */
const fold = (s: string) => s.normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase();

// Order matters: "compensation for the outage" is a compensation, "server transfer" is not a "new server".
const KEYWORDS: [Category, string[]][] = [
  ["twitchDrops", ["twitch drops", "drops twitch", "twitch-drops", "ツイッチドロップ", "twitchドロップ"]],
  ["knownIssues", ["known issues", "bekannte probleme", "problemes connus", "erreurs connues", "problemas conocidos", "problemas conhecidos", "既知の"]],
  ["compensation", ["compensation", "entschadigung", "compensacion", "compensacao", "補償"]],
  ["account", ["account restriction", "konto-einschr", "restrictions de compte", "restricciones de cuenta", "restricoes de conta", "利用制限"]],
  ["serverTransfer", ["server transfer", "servertransfer", "transfert de serveur", "transferencia de servidor", "transferencia de servidores", "serveur transfer", "サーバー移動"]],
  ["newServers", ["new server", "neue server", "nouveaux serveurs", "nuevos servidores", "novos servidores", "新サーバー"]],
  ["maintenance", ["maintenance", "wartung", "mantenimiento", "manutencao", "メンテナンス"]],
  ["outage", ["outage", "affected by", "storung", "ausfall", "panne", "interrupcion", "interrupcao", "instabilidade", "接続不安定", "障害"]],
];

export function categoryByKeywords(title: string): Category {
  const folded = fold(title);
  for (const [category, words] of KEYWORDS) {
    if (words.some((w) => folded.includes(fold(w)))) {
      return category;
    }
  }
  return "other";
}

const DONE_WORDS = ["completed", "resolved", "abgeschlossen", "behoben", "terminee", "termine", "resolu", "finalizado", "finalizada", "resuelta", "resuelto", "concluido", "normalizada", "normalizado", "完了", "解決"];

/** "(Completed)" / "(Behoben)" / "(完了)" at the end of the title. */
export function statusOf(title: string): "done" | null {
  const m = /[(（]([^()（）]*)[)）]\s*$/.exec(title.trim());
  if (!m) {
    return null;
  }
  const word = fold(m[1]).replace(/[.\s]+$/g, "");
  return DONE_WORDS.some((w) => word === fold(w) || word.startsWith(fold(w))) ? "done" : null;
}

/** "[Notice] Temporary Maintenance" -> "Temporary Maintenance" (the tag is NC's, in the language of the list, with typos like "[Annouce]"). */
export function cleanTitle(title: string): string {
  return title.replace(/^\s*\[[^\]]{1,24}\]\s*/, "").replace(/\s+/g, " ").trim();
}

const ENTITIES: Record<string, string> = { nbsp: " ", amp: "&", lt: "<", gt: ">", quot: '"', apos: "'", "#39": "'" };

/** NC's summary is flattened HTML text with entities; the line breaks are lost, so it is only a teaser. */
export function cleanSummary(raw: string, max = 280): string {
  const text = raw
    .replace(/&(#x?[0-9a-f]+|[a-z]+);/gi, (m, e: string) => {
      if (e[0] === "#") {
        const code = e[1].toLowerCase() === "x" ? parseInt(e.slice(2), 16) : parseInt(e.slice(1), 10);
        return Number.isInteger(code) && code > 0 && code < 0x110000 ? String.fromCodePoint(code) : " ";
      }
      return ENTITIES[e.toLowerCase()] ?? m;
    })
    .replace(/[​ ]/g, " ")
    .replace(/\s+/g, " ")
    .trim();
  return text.length > max ? `${text.slice(0, max - 1).trimEnd()}…` : text;
}

/** One list response of NC into our items (anything that does not look right is skipped). */
export function parseList(lang: NcLang, json: unknown): Announcement[] {
  const list = (json as { contentList?: unknown })?.contentList;
  const out: Announcement[] = [];
  for (const raw of Array.isArray(list) ? (list as Record<string, any>[]) : []) {
    const id = raw?.id;
    const title = raw?.title;
    const posted = Number(raw?.timestamps?.postedEpoch);
    const contentId = Number(raw?.snow?.contentId);
    if (typeof id !== "string" || !/^[0-9a-f]{24}$/.test(id) || typeof title !== "string" || !title || !Number.isFinite(posted) || posted <= 0) {
      continue;
    }
    out.push({
      id,
      lang,
      contentId: Number.isFinite(contentId) ? contentId : 0,
      title: cleanTitle(title),
      summary: cleanSummary(typeof raw.summary === "string" ? raw.summary : ""),
      posted,
      category: categoryByKeywords(title),
      status: statusOf(title),
    });
  }
  return out;
}

/** The "■ Accounts restricted: [15,380]" line of an account notice (any language: the number is the only thing in square brackets, with "," "." or spaces as thousands marks). */
export function restrictedCount(html: string): number | null {
  const text = html.replace(/<[^>]+>/g, "\n").replace(/&nbsp;/g, " ");
  for (const line of text.split("\n")) {
    const m = /■[^\[\]]*\[\s*(\d[\d.,   ]*)\s*\]/.exec(line);
    const n = m ? Number(m[1].replace(/\D/g, "")) : NaN;
    if (Number.isSafeInteger(n) && n > 0) {
      return n;
    }
  }
  return null;
}

/** NC leaves the English zone abbreviations in the German texts ("7 Oktober (CEST)"); in German they are MESZ / MEZ (PDT and PST are written the same everywhere). */
export function localizeZones(text: string, lang: NcLang): string {
  return lang === "de" ? text.replace(/\bCEST\b/g, "MESZ").replace(/\bCET\b/g, "MEZ") : text;
}

/** Translations are matched to their English twin when it was posted within this many seconds. */
const TWIN_WINDOW = 15 * 60;

/**
 * The category of the English twin (the nearest posting time within TWIN_WINDOW) where there is a clear one, otherwise the item's own keyword result.
 * Not clear = several English announcements at the same nearest time with different kinds (two were posted in the same minute at launch).
 */
export function applyEnglishCategories(items: Announcement[]): void {
  const english = items.filter((x) => x.lang === "en");
  for (const item of items) {
    if (item.lang === "en") {
      continue;
    }
    item.category = categoryByKeywords(item.title);
    let nearest = TWIN_WINDOW + 1;
    let kinds = new Set<Category>();
    for (const e of english) {
      const d = Math.abs(e.posted - item.posted);
      if (d < nearest) {
        nearest = d;
        kinds = new Set([e.category]);
      } else if (d === nearest) {
        kinds.add(e.category);
      }
    }
    if (kinds.size === 1) {
      item.category = [...kinds][0];
    }
  }
}

// --- the stored history ---

export const ANNOUNCEMENTS_FILE = path.resolve(process.env.ANNOUNCEMENTS_FILE ?? "./data/announcements.json");
const KEEP_PER_LANG = 500;

let store = new Map<string, Announcement>();
let checkedAt: number | null = null;
/** Languages whose older history was paged back to the very first announcement (done once, then the newest 10 are enough). */
let backfilled = new Set<string>();
const MAX_PAGES = 30;

const keyOf = (x: { lang: string; id: string }) => `${x.lang}:${x.id}`;

function load(): void {
  try {
    if (existsSync(ANNOUNCEMENTS_FILE)) {
      const data = JSON.parse(readFileSync(ANNOUNCEMENTS_FILE, "utf8")) as { items?: Announcement[]; checkedAt?: number; backfilled?: string[] };
      backfilled = new Set(data.backfilled ?? []);
      store = new Map((data.items ?? []).filter((x) => NC_LANGS.includes(x?.lang) && typeof x?.id === "string").map((x) => [keyOf(x), x]));
      checkedAt = typeof data.checkedAt === "number" ? data.checkedAt : null;
      // the kinds are derived data: a changed rule applies to the stored history right away, not only to what is fetched next
      applyEnglishCategories([...store.values()]);
    }
  } catch {
    store = new Map();
  }
}

function save(): void {
  try {
    mkdirSync(path.dirname(ANNOUNCEMENTS_FILE), { recursive: true });
    const tmp = `${ANNOUNCEMENTS_FILE}.tmp`;
    writeFileSync(tmp, JSON.stringify({ checkedAt, backfilled: [...backfilled], items: [...store.values()] }));
    renameSync(tmp, ANNOUNCEMENTS_FILE);
  } catch {
    // the next refresh writes it again; the memory copy keeps working
  }
}

/** Merges fresh items into the history (a changed title or summary replaces the old one) and trims every language to its newest KEEP_PER_LANG. */
export function mergeItems(fresh: Announcement[]): void {
  for (const x of fresh) {
    const old = store.get(keyOf(x));
    if (old?.restricted !== undefined && x.restricted === undefined && old.title === x.title) {
      x.restricted = old.restricted;
    }
    store.set(keyOf(x), x);
  }
  const all = [...store.values()];
  applyEnglishCategories(all);
  const byLang = new Map<string, Announcement[]>();
  for (const x of all) {
    byLang.set(x.lang, [...(byLang.get(x.lang) ?? []), x]);
  }
  store = new Map();
  for (const list of byLang.values()) {
    list.sort(compare);
    for (const x of list.slice(0, KEEP_PER_LANG)) {
      store.set(keyOf(x), x);
    }
  }
}

const compare = (a: Announcement, b: Announcement) => b.posted - a.posted || b.contentId - a.contentId;

export function resetStore(): void {
  store = new Map();
  checkedAt = null;
  backfilled = new Set();
}

export function ncLangFor(siteLang: string | undefined): NcLang {
  const base = String(siteLang ?? "").toLowerCase().split("-")[0];
  return (NC_LANGS as readonly string[]).includes(base) ? (base as NcLang) : "en";
}

export type AnnouncementView = { id: string; title: string; summary: string; postedAt: string; category: Category; status: "done" | null; url: string; restricted?: number };

/** The list for one site language (English where NC has no board in it), newest first. */
export function announcementList(siteLang: string | undefined): { lang: NcLang; checkedAt: string | null; items: AnnouncementView[] } {
  const lang = ncLangFor(siteLang);
  const items = [...store.values()]
    .filter((x) => x.lang === lang)
    .sort(compare)
    .map((x) => ({
      id: x.id,
      title: localizeZones(x.title, lang),
      summary: localizeZones(x.summary, lang),
      postedAt: new Date(x.posted * 1000).toISOString(),
      category: x.category,
      status: x.status,
      ...(x.category === "account" && typeof x.restricted === "number" ? { restricted: x.restricted } : {}),
      url: `https://aion2.plaync.com/${WEB_LOCALE[lang]}/board/notice/view?articleId=${x.id}`,
    }));
  return { lang, checkedAt: checkedAt === null ? null : new Date(checkedAt).toISOString(), items };
}

/** Posting time (epoch seconds) of the newest announcement in any language, null while nothing is known; this is what the client's "New" badge compares. */
export function latestPosted(): number | null {
  let latest = 0;
  for (const x of store.values()) {
    latest = Math.max(latest, x.posted);
  }
  return latest > 0 ? latest : null;
}

/**
 * The pages before the oldest stored item, found in NC's own web page (network tab): article/search/moreArticle?moreSize=18&moreDirection=BEFORE&previousArticleId=<id>.
 * Eighteen per page is the most it gives; hasMore ends it. Returns false when it stopped on an error (tried again next time).
 */
async function backfill(lang: NcLang, fetchFn: FetchFn, pauseMs: number): Promise<boolean> {
  for (let page = 0; page < MAX_PAGES; page++) {
    const mine = [...store.values()].filter((x) => x.lang === lang).sort(compare);
    const oldest = mine[mine.length - 1];
    if (!oldest) {
      return false;
    }
    let json: { hasMore?: boolean };
    try {
      const url = new URL(`${BASE}/notice_${lang}/article/search/moreArticle`);
      url.searchParams.set("isVote", "true");
      url.searchParams.set("moreSize", "18");
      url.searchParams.set("moreDirection", "BEFORE");
      url.searchParams.set("previousArticleId", oldest.id);
      json = JSON.parse((await getNc(fetchFn, url, MAX_BYTES)).body.toString("utf8"));
    } catch {
      return false;
    }
    const older = parseList(lang, json);
    mergeItems(older);
    if (json.hasMore !== true || older.length === 0) {
      return true;
    }
    if (pauseMs > 0) {
      await new Promise((r) => setTimeout(r, pauseMs));
    }
  }
  return false;
}

/** Fetches the six lists (one request each, with a pause) and merges what came back; a language that fails keeps its stored history. */
export async function refreshAnnouncements(fetchFn: FetchFn = fetch, now = Date.now(), pauseMs = 2_000): Promise<number> {
  const fresh: Announcement[] = [];
  for (const lang of NC_LANGS) {
    try {
      const { body } = await getNc(fetchFn, new URL(`${BASE}/notice_${lang}/article`), MAX_BYTES);
      fresh.push(...parseList(lang, JSON.parse(body.toString("utf8"))));
    } catch {
      // keep the old data of this language
    }
    if (pauseMs > 0) {
      await new Promise((r) => setTimeout(r, pauseMs));
    }
  }
  if (fresh.length > 0) {
    mergeItems(fresh);
    checkedAt = now;
    for (const lang of NC_LANGS) {
      if (!backfilled.has(lang) && fresh.some((x) => x.lang === lang) && (await backfill(lang, fetchFn, pauseMs))) {
        backfilled.add(lang);
      }
    }
    await readRestricted(fetchFn, pauseMs);
    save();
  }
  return fresh.length;
}

/** Reads the restricted-account number from the full text of every account notice that has none yet (a failed request is tried again next time). */
async function readRestricted(fetchFn: FetchFn, pauseMs: number): Promise<void> {
  for (const x of store.values()) {
    if (x.category !== "account" || x.restricted !== undefined) {
      continue;
    }
    try {
      const { body } = await getNc(fetchFn, new URL(`${BASE}/notice_${x.lang}/article/${x.id}`), MAX_BYTES);
      const html = (JSON.parse(body.toString("utf8")) as { article?: { content?: { content?: unknown } } })?.article?.content?.content;
      x.restricted = typeof html === "string" ? restrictedCount(html) : null;
    } catch {
      // later
    }
    if (pauseMs > 0) {
      await new Promise((r) => setTimeout(r, pauseMs));
    }
  }
}

/** Off under tests and with NC_MATCH_OFF=1 (like the other NC jobs). */
export function startAnnouncements(intervalMs = 10 * 60_000): void {
  load();
  if (process.env.NC_MATCH_OFF === "1" || process.env.NODE_TEST_CONTEXT) {
    return;
  }
  const run = () => void refreshAnnouncements().catch(() => undefined);
  setTimeout(run, 20_000).unref();
  setInterval(run, intervalMs).unref();
}
