import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { Resvg } from "@resvg/resvg-js";
import { and, desc, eq, ne } from "drizzle-orm";
import { db } from "../db/client.js";
import { bosses, encounterParticipants, encounters, instances, players } from "../db/schema.js";
import type { Game } from "../constants.js";
import { GAME_NAME_TRANSLATIONS, splitConquest } from "../../../Web-Frontend/game-data.js";
import { cachedBuffer } from "./cache.js";
import { bossImage, classImage } from "./pages.js";

// Link-preview picture of one boss fight (Discord, Slack, X ...). Discord shows it about 400 px
// wide, a third of its real size, so it carries only what survives that: the boss, the group's
// iDPS, and per player the class emblem, the name and the damage on a bar, all in large type
// (nothing under ~36 px). Heal and damage-taken bars live on the fight's own page. Everything that
// needs words is kept out on purpose, so one image reads the same for every viewer (a preview is
// fetched once per URL and shown to everybody). Only the boss and instance names and the number
// format follow `lang`.

const WIDTH = 1200;
const HEIGHT = 630;
const MAX_ROWS = 5;
const CACHE_TTL_MS = 3_600_000;

const here = path.dirname(fileURLToPath(import.meta.url));
const FRONTEND_ROOT = path.join(here, "..", "..", "..", "Web-Frontend");
const FONT_FILES = ["DejaVuSans.ttf", "DejaVuSans-Bold.ttf"].map((f) => path.join(here, "..", "..", "assets", "fonts", f));

/** Number formats per site language (same tags as Web-Frontend/i18n.js). */
const LOCALE_TAGS: Record<string, string> = { de: "de-DE", en: "en-US", fr: "fr-FR", es: "es-ES", ru: "ru-RU", zh: "zh-CN", "zh-TW": "zh-TW", pt: "pt-BR", ja: "ja-JP", ko: "ko-KR" };

export function ogLang(value: unknown): string {
  return typeof value === "string" && value in LOCALE_TAGS ? value : "en";
}

const COLORS = {
  damage: "#ffc94a",
  heal: "#5ee59b",
  taken: "#ff6b61",
  shield: "#3aa0ff",
  text: "#f1f3f7",
  muted: "#8d98a8",
  card: "#1b2430",
  track: "#0d121a",
  page: "#0f141b",
};

const dataUris = new Map<string, string | null>();
/** A frontend file as a data: URI (resvg resolves nothing else), read once per process. */
function dataUri(webPath: string | undefined): string | null {
  if (!webPath) return null;
  // resvg cannot decode WebP; the site's WebP pictures have JPEG copies under /og/art/ for this.
  if (webPath.endsWith(".webp")) webPath = "/og/art" + webPath.replace(/^\/images\/aion2/, "").replace(/\.webp$/, ".jpg");
  if (dataUris.has(webPath)) return dataUris.get(webPath) ?? null;
  let uri: string | null = null;
  try {
    const file = path.join(FRONTEND_ROOT, webPath);
    if (file.startsWith(FRONTEND_ROOT)) {
      const mime = webPath.endsWith(".png") ? "image/png" : "image/jpeg";
      uri = `data:${mime};base64,${fs.readFileSync(file).toString("base64")}`;
    }
  } catch {
    uri = null;
  }
  dataUris.set(webPath, uri);
  return uri;
}

/** Glyph advance widths (DejaVu Sans, 19 px) are not available without a font engine; this
 *  estimate only places the header icons and is wide enough for every name in the game data. */
function textWidth(text: string, size: number): number {
  let units = 0;
  for (const ch of text) {
    units += /[A-ZÄÖÜА-Я0-9]/.test(ch) ? 0.72 : /[ilI.,:;'|!·]/.test(ch) ? 0.3 : ch.charCodeAt(0) > 0x2e80 ? 1 : 0.6;
  }
  return units * size;
}

const escapeXml = (s: string) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");

const SHIELD_PATH = "M12 2 L20 5 V11 C20 16 16.5 20 12 22 C7.5 20 4 16 4 11 V5 Z";
const ICONS = {
  damage: (c: string) =>
    `<g transform="rotate(45 12 12)" fill="${c}"><path d="M12 1 L14.2 4 V15 H9.8 V4 Z"/><rect x="6.5" y="15" width="11" height="2.2" rx="1"/><rect x="10.8" y="17" width="2.4" height="4"/><circle cx="12" cy="22" r="1.5"/></g>`,
  heal: (c: string) => `<path fill="${c}" d="M9 3h6v6h6v6h-6v6H9v-6H3V9h6z"/>`,
  // Taken is half red, half blue: the red part got through, the blue one was absorbed by a shield.
  taken: (c: string) =>
    `<clipPath id="shl"><rect x="0" y="0" width="12" height="24"/></clipPath><clipPath id="shr"><rect x="12" y="0" width="12" height="24"/></clipPath><path fill="${c}" clip-path="url(#shl)" d="${SHIELD_PATH}"/><path fill="${COLORS.shield}" clip-path="url(#shr)" d="${SHIELD_PATH}"/>`,
  players: (c: string) =>
    `<circle cx="9" cy="8" r="3.6" fill="${c}"/><path d="M2 20c0-4 3-6.5 7-6.5s7 2.5 7 6.5z" fill="${c}"/><circle cx="17" cy="9" r="2.8" fill="${c}"/><path d="M17 14c3 0 5 2 5 5h-4.5c0-2-.7-3.8-2-5z" fill="${c}"/>`,
  clock: (c: string) =>
    `<circle cx="12" cy="12" r="9" fill="none" stroke="${c}" stroke-width="2.4"/><path d="M12 6.5V12l3.8 2.4" fill="none" stroke="${c}" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"/>`,
};

const icon = (inner: string, x: number, y: number, scale: number) => `<g transform="translate(${x} ${y}) scale(${scale})">${inner}</g>`;

export interface FightRow {
  name: string;
  className: string;
  damage: number;
  dps: number;
  healing: number;
  hps: number;
  taken: number;
  absorbed: number;
}

export interface Fight {
  boss: string;
  /** The fight's difficulty label ("Transzendenz 2") with its colour, null when it has none. */
  step: { text: string; color: string } | null;
  instance: string;
  art: string | undefined;
  durationSeconds: number;
  groupIDps: number;
  playerCount: number;
  rows: FightRow[];
}

/** The label colours of the website (dark theme), per category; a Conquest expedition has its own. */
const STEP_COLORS: Record<string, string> = {
  nightmare: "#c9a2ff",
  conquest: "#ffc857",
  expedition: "#8fd19e",
  transcendence: "#6fc3ff",
  ascension: "#ffab7a",
  hideout: "#5fe0cc",
  sanctuary: "#e6a2f5",
  worldboss: "#ff9ad5",
};

/** The fight's difficulty label as the website words it (German or English): "Albtraum 10", "Eroberung 3", "Aszensusritus Schwer", "Weltboss". "" for none. */
function stepLabel(category: string | null, mode: string, instanceName: string, lang: string): { text: string; color: string } | null {
  const text = stepText(category, mode, instanceName, lang);
  return text ? { text, color: STEP_COLORS[category === "expedition" && splitConquest(instanceName) ? "conquest" : category ?? ""] ?? COLORS.damage } : null;
}

function stepText(category: string | null, mode: string, instanceName: string, lang: string): string {
  const de = lang === "de";
  const stage = /^\d+$/.test(mode) ? mode : "";
  if (category === "nightmare" && stage) return `${de ? "Albtraum" : "Nightmare"} ${stage}`;
  if (category === "transcendence" && stage) return `${de ? "Transzendenz" : "Transcendence"} ${stage}`;
  if (category === "ascension") {
    const names: Record<string, [string, string]> = { easy: ["Easy", "Einfach"], medium: ["Medium", "Mittel"], hard: ["Hard", "Schwer"], extreme: ["Extreme", "Extrem"] };
    const word = names[mode]?.[de ? 1 : 0];
    return word ? `${de ? "Aszensusritus" : "Ascension Rite"} ${word}` : "";
  }
  if (category === "expedition") {
    const conquest = splitConquest(instanceName);
    return conquest ? `${de ? (conquest.hard ? "Eroberung [Schwierig]" : "Eroberung") : conquest.hard ? "Conquest [Hard]" : "Conquest"} ${conquest.stars.length}` : de ? "Erkundung" : "Exploration";
  }
  const words: Record<string, [string, string]> = { hideout: ["Hideout", "Unterschlupf"], sanctuary: ["Sanctuary", "Heiligtum"], worldboss: ["World Boss", "Weltboss"] };
  return words[category ?? ""]?.[de ? 1 : 0] ?? "";
}

function translated(name: string, lang: string): string {
  const conquest = splitConquest(name);
  if (conquest) return translated(conquest.base, lang); // the stage is the label next to the boss name
  const entry = (GAME_NAME_TRANSLATIONS as Record<string, Record<string, string> | undefined>)[name];
  return entry?.[lang] ?? name;
}

function loadFight(game: Game, id: number, lang: string): Fight | null {
  const row = db
    .select({
      durationSeconds: encounters.durationSeconds,
      groupIDps: encounters.groupIDps,
      bossName: bosses.name,
      bossNameEn: bosses.nameEn,
      instanceName: instances.name,
      instanceNameEn: instances.nameEn,
      instanceCategory: instances.category,
      mode: encounters.mode,
      instanceGame: instances.game,
    })
    .from(encounters)
    .innerJoin(bosses, eq(encounters.bossId, bosses.id))
    .innerJoin(instances, eq(bosses.instanceId, instances.id))
    .where(eq(encounters.id, id))
    .get();
  if (!row || row.instanceGame !== game) return null;

  // className "?" rows are pets/summons (see encounterPage in seo/pages.ts).
  const roster = db
    .select({
      name: players.name,
      className: encounterParticipants.className,
      damage: encounterParticipants.totalDamage,
      dps: encounterParticipants.dps,
      healing: encounterParticipants.totalHealing,
      hps: encounterParticipants.hps,
      taken: encounterParticipants.damageTaken,
      absorbed: encounterParticipants.damageAbsorbed,
    })
    .from(encounterParticipants)
    .innerJoin(players, eq(encounterParticipants.playerId, players.id))
    .where(and(eq(encounterParticipants.encounterId, id), ne(encounterParticipants.className, "?")))
    .orderBy(desc(encounterParticipants.totalDamage))
    .all();
  if (roster.length === 0) return null;

  const bossEn = row.bossNameEn ?? row.bossName;
  const instanceEn = row.instanceNameEn ?? row.instanceName;
  return {
    boss: translated(bossEn, lang),
    step: stepLabel(row.instanceCategory, row.mode, row.instanceName, lang),
    instance: translated(instanceEn, lang),
    art: bossImage(row.bossName, row.instanceName) ?? bossImage(bossEn, instanceEn),
    durationSeconds: row.durationSeconds,
    groupIDps: row.groupIDps,
    playerCount: roster.length,
    rows: roster.slice(0, MAX_ROWS),
  };
}

/** The label as the website draws it: a rounded pill in the category colour, its text on the title's baseline. */
function stepPill(step: { text: string; color: string }, x: number, baseline: number, size: number, bold: number): string {
  const pad = size * 0.45;
  const width = textWidth(step.text, size * bold * 0.9) + pad * 2; // the shared estimate runs wide for this size
  const height = size * 1.55;
  const top = baseline - size * 0.36 - height / 2;
  return `<rect x="${x.toFixed(1)}" y="${top.toFixed(1)}" width="${width.toFixed(1)}" height="${height.toFixed(1)}" rx="${(height / 2).toFixed(1)}" fill="${step.color}" fill-opacity=".18" stroke="${step.color}" stroke-opacity=".6" stroke-width="2"/>
<text x="${(x + pad).toFixed(1)}" y="${baseline}" font-size="${size.toFixed(1)}" font-weight="700" fill="${step.color}">${escapeXml(step.text)}</text>`;
}

export function buildSvg(fight: Fight, lang: string): string {
  const fmt = (n: number) => Math.round(n).toLocaleString(LOCALE_TAGS[lang]);
  const C = COLORS;
  const rows = fight.rows;
  const maxDamage = Math.max(...rows.map((r) => r.damage), 1);

  // Boss name: as large as fits next to the iDPS block on the right.
  // The label ("Transcendence 2") sits behind the name at 55 % of its size and shares its width budget.
  const stepRatio = 0.55;
  // textWidth() is made for regular weight; the bold title is about 15 % wider.
  const bold = 1.15;
  const budget = 700 / Math.max(textWidth(fight.boss, bold) + (fight.step ? 0.4 + 0.9 * stepRatio + textWidth(fight.step.text, stepRatio * bold) : 0), 1);
  const bossSize = Math.min(64, budget);
  const duration = `${Math.floor(fight.durationSeconds / 60)}:${String(Math.round(fight.durationSeconds % 60)).padStart(2, "0")}`;

  const art = dataUri(fight.art);
  let svg = `<rect width="${WIDTH}" height="${HEIGHT}" fill="${C.page}"/>
${art ? `<image href="${art}" width="${WIDTH}" height="${HEIGHT}" preserveAspectRatio="xMidYMid slice" opacity=".4"/>` : ""}
<rect width="${WIDTH}" height="${HEIGHT}" fill="${C.page}" opacity=".6"/>
<text x="48" y="76" font-size="${bossSize.toFixed(1)}" font-weight="700" fill="${C.text}">${escapeXml(fight.boss)}</text>
${fight.step ? stepPill(fight.step, 48 + textWidth(fight.boss, bossSize * bold) + bossSize * 0.4, 76, bossSize * stepRatio, bold) : ""}
<text x="1152" y="76" font-size="40" font-weight="700" fill="${C.damage}" text-anchor="end">${escapeXml(fmt(fight.groupIDps))}/s</text>
<text x="1152" y="124" font-size="26" fill="${C.muted}" text-anchor="end">AION DPS · aiondps.com</text>`;

  // Second line: instance, then player count and duration as icon + number.
  let x = 48;
  svg += `<text x="${x}" y="124" font-size="32" fill="${C.muted}">${escapeXml(fight.instance)}</text>`;
  x += textWidth(fight.instance, 32) + 22;
  const facts: [string, string][] = [
    [ICONS.players(C.muted), String(fight.playerCount)],
    [ICONS.clock(C.muted), duration],
  ];
  for (const [glyph, text] of facts) {
    svg += icon(glyph, x, 94, 1.3);
    x += 40;
    svg += `<text x="${x}" y="124" font-size="32" fill="${C.muted}">${escapeXml(text)}</text>`;
    x += textWidth(text, 32) + 26;
  }

  const top0 = 150;
  const gap = 10;
  const rowH = Math.min(86, (HEIGHT - top0 - 20 - gap * (rows.length - 1)) / rows.length);
  const barX = 520;
  const barW = 350;
  const barH = Math.min(30, rowH - 30);
  rows.forEach((r, i) => {
    const top = top0 + i * (rowH + gap);
    const mid = top + rowH / 2;
    svg += `<rect x="40" y="${top}" width="1120" height="${rowH}" rx="14" fill="${C.card}" opacity=".92"/>`;
    const emblem = dataUri(classImage(r.className));
    const iconSize = rowH - 20;
    if (emblem) svg += `<image href="${emblem}" x="56" y="${top + 10}" width="${iconSize}" height="${iconSize}"/>`;
    // Names get ~330 px; a long one shrinks (down to 28) instead of running into the bar.
    const nameSize = Math.max(28, Math.min(40, 330 / Math.max(textWidth(r.name, 1), 1)));
    svg += `<text x="${56 + iconSize + 18}" y="${mid + nameSize * 0.36}" font-size="${nameSize.toFixed(1)}" font-weight="700" fill="${C.text}">${escapeXml(r.name)}</text>`;
    svg += `<rect x="${barX}" y="${mid - barH / 2}" width="${barW}" height="${barH}" rx="${barH / 2}" fill="${C.track}"/>`;
    if (r.damage > 0) {
      svg += `<rect x="${barX}" y="${mid - barH / 2}" width="${Math.max(barH, (barW * r.damage) / maxDamage)}" height="${barH}" rx="${barH / 2}" fill="${C.damage}"/>`;
    }
    svg += `<text x="1138" y="${mid + 14}" font-size="42" font-weight="700" fill="${C.text}" text-anchor="end">${escapeXml(fmt(r.damage))}</text>`;
  });

  return `<svg xmlns="http://www.w3.org/2000/svg" width="${WIDTH}" height="${HEIGHT}" viewBox="0 0 ${WIDTH} ${HEIGHT}" font-family="DejaVu Sans">${svg}</svg>`;
}

/** PNG of one fight's preview picture, or null when the fight is unknown / of another game / has no players. */
export function encounterOgImage(game: Game, id: string, langParam?: unknown): Buffer | null {
  const encounterId = Number(id);
  if (!Number.isInteger(encounterId)) return null;
  const lang = ogLang(langParam);
  return cachedBuffer(`og:${game}:${encounterId}:${lang}`, CACHE_TTL_MS, () => {
    const fight = loadFight(game, encounterId, lang);
    if (!fight) return null;
    const resvg = new Resvg(buildSvg(fight, lang), {
      font: { fontFiles: FONT_FILES, loadSystemFonts: false, defaultFontFamily: "DejaVu Sans" },
      fitTo: { mode: "width", value: WIDTH },
    });
    return resvg.render().asPng();
  });
}
