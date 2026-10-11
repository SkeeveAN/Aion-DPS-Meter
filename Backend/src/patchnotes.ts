import { existsSync, mkdirSync, readFileSync, renameSync, writeFileSync } from "node:fs";
import path from "node:path";
import { NC_LANGS, cleanTitle, localizeZones, ncLangFor, type NcLang } from "./announcements.js";
import { getNc } from "./portraitService.js";

// NC's AION 2 patch notes (board update_xx): the one place where the full text is shown, as a small tree built from NC's editor HTML.
// Only a fixed set of tags survives (paragraphs, bold, italic, lists, line breaks, https links); everything else, pictures included, is dropped,
// so the frontend can build the page with createElement and never touches NC's markup.

type FetchFn = typeof fetch;

const BASE = "https://api-global-community.plaync.com/aion2_global/board";
const MAX_BYTES = 2 * 1024 * 1024;
const MAX_HTML = 300_000;
const WEB_LOCALE: Record<NcLang, string> = { en: "en-us", de: "de-de", fr: "fr-fr", es: "es-es", pt: "pt-br", ja: "ja-jp" };

export type BodyNode = string | { tag: "p" | "strong" | "em" | "u" | "ul" | "ol" | "li" | "br" | "h" | "a"; href?: string; children: BodyNode[] };

export type Patchnote = { id: string; lang: NcLang; contentId: number; title: string; posted: number; updated: number; body: BodyNode[] };

const TAGS: Record<string, "p" | "strong" | "em" | "u" | "ul" | "ol" | "li" | "br" | "h" | "a"> = {
  div: "p", p: "p", strong: "strong", b: "strong", em: "em", i: "em", u: "u", ul: "ul", ol: "ol", li: "li", br: "br", h1: "h", h2: "h", h3: "h", h4: "h", a: "a",
};
const DROP_WITH_CONTENT = new Set(["script", "style", "head", "title"]);
const VOID = new Set(["br", "img", "hr", "input", "meta", "link"]);
const ENTITIES: Record<string, string> = { nbsp: " ", amp: "&", lt: "<", gt: ">", quot: '"', apos: "'" };

function decode(text: string): string {
  return text.replace(/&(#x?[0-9a-f]+|[a-z]+);/gi, (m, e: string) => {
    if (e[0] === "#") {
      const code = e[1].toLowerCase() === "x" ? parseInt(e.slice(2), 16) : parseInt(e.slice(1), 10);
      return Number.isInteger(code) && code > 0 && code < 0x110000 ? String.fromCodePoint(code) : " ";
    }
    return ENTITIES[e.toLowerCase()] ?? m;
  });
}

type Open = { tag: "p" | "strong" | "em" | "u" | "ul" | "ol" | "li" | "br" | "h" | "a"; href?: string; children: BodyNode[]; source: string };

/** NC's editor HTML into the safe tree. */
export function htmlToTree(html: string): BodyNode[] {
  const root: Open = { tag: "p", children: [], source: "" };
  const stack: Open[] = [root];
  let skip = 0;
  const skipName: string[] = [];
  const re = /<!--[\s\S]*?-->|<(\/?)([a-zA-Z][a-zA-Z0-9]*)([^>]*)>|([^<]+)/g;
  for (const m of html.slice(0, MAX_HTML).matchAll(re)) {
    const top = stack[stack.length - 1];
    if (m[4] !== undefined) {
      if (skip === 0) {
        top.children.push(decode(m[4]));
      }
      continue;
    }
    if (m[2] === undefined) {
      continue;
    }
    const closing = m[1] === "/";
    const name = m[2].toLowerCase();
    if (DROP_WITH_CONTENT.has(name)) {
      if (!closing) {
        skip++;
        skipName.push(name);
      } else if (skipName[skipName.length - 1] === name) {
        skip--;
        skipName.pop();
      }
      continue;
    }
    if (skip > 0) {
      continue;
    }
    const tag = TAGS[name];
    if (closing) {
      for (let i = stack.length - 1; i > 0; i--) {
        if (stack[i].source === name) {
          stack.length = i;
          break;
        }
      }
      continue;
    }
    if (!tag) {
      continue; // unknown or dropped tag (img, span, ...): its text stays, it is just not wrapped
    }
    const node: Open = { tag, children: [], source: name };
    if (tag === "a") {
      const href = /href\s*=\s*"([^"]*)"/i.exec(m[3])?.[1];
      const url = href ? decode(href) : "";
      if (/^https:\/\/[^\s"'<>]{1,500}$/.test(url)) {
        node.href = url;
      }
    }
    top.children.push(node);
    if (!VOID.has(name) && !/\/\s*$/.test(m[3]) && stack.length < 14) {
      stack.push(node);
    }
  }
  return prune(root.children).map(strip);
}

/** The bookkeeping field out of the finished tree. */
function strip(n: BodyNode): BodyNode {
  if (typeof n === "string") {
    return n;
  }
  const out: BodyNode = { tag: n.tag, children: n.children.map(strip) };
  if (n.href) {
    out.href = n.href;
  }
  return out;
}

const plain = (nodes: BodyNode[]): string => nodes.map((n) => (typeof n === "string" ? n : n.tag === "br" ? " " : plain(n.children))).join("");

/** Zero-width characters out, whitespace collapsed, empty paragraphs dropped, a link without an address becomes plain content. */
function prune(nodes: BodyNode[]): BodyNode[] {
  const out: BodyNode[] = [];
  for (const n of nodes) {
    if (typeof n === "string") {
      const text = n.replace(/[​ ﻿]/g, " ").replace(/\s+/g, " ");
      if (text.trim() !== "" || out.length > 0) {
        out.push(text);
      }
      continue;
    }
    n.children = prune(n.children);
    if (n.tag === "a" && !n.href) {
      out.push(...n.children);
    } else if (n.tag === "br" || plain(n.children).trim() !== "" || (n.tag !== "p" && n.tag !== "li" && n.children.some((c) => typeof c !== "string"))) {
      out.push(n);
    }
  }
  // a text node that only follows a block carries nothing
  return out.filter((n, i) => !(typeof n === "string" && n.trim() === "" && (i === 0 || i === out.length - 1)));
}

export function parseDetail(lang: NcLang, listItem: Record<string, any>, detail: unknown): Patchnote | null {
  const id = listItem?.id;
  const posted = Number(listItem?.timestamps?.postedEpoch);
  const updated = Number(listItem?.timestamps?.updatedEpoch);
  const html = (detail as { article?: { content?: { content?: unknown } } })?.article?.content?.content;
  if (typeof id !== "string" || !/^[0-9a-f]{24}$/.test(id) || typeof listItem.title !== "string" || !Number.isFinite(posted) || posted <= 0 || typeof html !== "string") {
    return null;
  }
  return {
    id,
    lang,
    contentId: Number(listItem?.snow?.contentId) || 0,
    title: cleanTitle(listItem.title),
    posted,
    updated: Number.isFinite(updated) ? updated : posted,
    body: htmlToTree(html),
  };
}

// --- the stored history ---

export const PATCHNOTES_FILE = path.resolve(process.env.PATCHNOTES_FILE ?? "./data/patchnotes.json");
let store = new Map<string, Patchnote>();
let checkedAt: number | null = null;
const keyOf = (x: { lang: string; id: string }) => `${x.lang}:${x.id}`;
const compare = (a: Patchnote, b: Patchnote) => b.posted - a.posted || b.contentId - a.contentId;

function load(): void {
  try {
    if (existsSync(PATCHNOTES_FILE)) {
      const data = JSON.parse(readFileSync(PATCHNOTES_FILE, "utf8")) as { items?: Patchnote[]; checkedAt?: number };
      store = new Map((data.items ?? []).filter((x) => NC_LANGS.includes(x?.lang) && typeof x?.id === "string").map((x) => [keyOf(x), x]));
      checkedAt = typeof data.checkedAt === "number" ? data.checkedAt : null;
    }
  } catch {
    store = new Map();
  }
}

function save(): void {
  try {
    mkdirSync(path.dirname(PATCHNOTES_FILE), { recursive: true });
    const tmp = `${PATCHNOTES_FILE}.tmp`;
    writeFileSync(tmp, JSON.stringify({ checkedAt, items: [...store.values()] }));
    renameSync(tmp, PATCHNOTES_FILE);
  } catch {
    // the next refresh writes it again
  }
}

export function resetPatchnotes(): void {
  store = new Map();
  checkedAt = null;
}

export function putPatchnote(x: Patchnote): void {
  store.set(keyOf(x), x);
}

/** The zone abbreviations of the German text, in every text node of the tree (see localizeZones). */
function localizeTree(nodes: BodyNode[], lang: NcLang): BodyNode[] {
  return lang !== "de" ? nodes : nodes.map((n): BodyNode => (typeof n === "string" ? localizeZones(n, lang) : { ...n, children: localizeTree(n.children, lang) }));
}

export type PatchnoteView = { id: string; title: string; postedAt: string; body: BodyNode[]; url: string };

export function patchnoteList(siteLang: string | undefined): { lang: NcLang; checkedAt: string | null; items: PatchnoteView[] } {
  const lang = ncLangFor(siteLang);
  const items = [...store.values()]
    .filter((x) => x.lang === lang)
    .sort(compare)
    .map((x) => ({
      id: x.id,
      title: localizeZones(x.title, lang),
      postedAt: new Date(x.posted * 1000).toISOString(),
      body: localizeTree(x.body, lang),
      url: `https://aion2.plaync.com/${WEB_LOCALE[lang]}/board/update/view?articleId=${x.id}`,
    }));
  return { lang, checkedAt: checkedAt === null ? null : new Date(checkedAt).toISOString(), items };
}

/** Lists of the six languages (the list answer is only the newest page; older pages come from moreArticle until hasMore ends), full text only for what is new or edited. */
export async function refreshPatchnotes(fetchFn: FetchFn = fetch, now = Date.now(), pauseMs = 2_000): Promise<number> {
  const pause = () => (pauseMs > 0 ? new Promise((r) => setTimeout(r, pauseMs)) : Promise.resolve());
  let changed = 0;
  for (const lang of NC_LANGS) {
    try {
      const rows: Record<string, any>[] = [];
      let url = new URL(`${BASE}/update_${lang}/article`);
      for (let page = 0; page < 20; page++) {
        const json = JSON.parse((await getNc(fetchFn, url, MAX_BYTES)).body.toString("utf8")) as { contentList?: Record<string, any>[]; hasMore?: boolean };
        const list = Array.isArray(json.contentList) ? json.contentList : [];
        rows.push(...list);
        if (json.hasMore !== true || list.length === 0) {
          break;
        }
        url = new URL(`${BASE}/update_${lang}/article/search/moreArticle`);
        url.searchParams.set("isVote", "true");
        url.searchParams.set("moreSize", "18");
        url.searchParams.set("moreDirection", "BEFORE");
        url.searchParams.set("previousArticleId", String(list[list.length - 1].id));
        await pause();
      }
      for (const row of rows) {
        const have = store.get(keyOf({ lang, id: String(row?.id) }));
        const updated = Number(row?.timestamps?.updatedEpoch);
        if (have && Number.isFinite(updated) && have.updated >= updated) {
          continue;
        }
        const detail = JSON.parse((await getNc(fetchFn, new URL(`${BASE}/update_${lang}/article/${encodeURIComponent(String(row.id))}`), MAX_BYTES)).body.toString("utf8"));
        const note = parseDetail(lang, row, detail);
        if (note) {
          putPatchnote(note);
          changed++;
        }
        await pause();
      }
    } catch {
      // keep what is stored for this language
    }
    await pause();
  }
  checkedAt = now;
  if (changed > 0 || store.size > 0) {
    save();
  }
  return changed;
}

/** Off under tests and with NC_MATCH_OFF=1 (like the other NC jobs). */
export function startPatchnotes(intervalMs = 30 * 60_000): void {
  load();
  if (process.env.NC_MATCH_OFF === "1" || process.env.NODE_TEST_CONTEXT) {
    return;
  }
  const run = () => void refreshPatchnotes().catch(() => undefined);
  setTimeout(run, 60_000).unref();
  setInterval(run, intervalMs).unref();
}
