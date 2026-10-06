import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { Resvg } from "@resvg/resvg-js";
import { and, desc, eq, ne } from "drizzle-orm";
import { db } from "../db/client.js";
import { bosses, encounterParticipants, encounters, instances, players } from "../db/schema.js";
import type { Game } from "../constants.js";
import { GAME_NAME_TRANSLATIONS } from "../../../Web-Frontend/game-data.js";
import { cachedBuffer } from "./cache.js";
import { bossImage, classImage } from "./pages.js";

// Link-preview picture of one boss fight (Discord, Slack, X ...): boss art on the left, then per
// player a damage, a healing and a damage-taken bar. Everything that needs words is kept out on
// purpose - the rows are told apart by icons and colours, class by its emblem - so one image reads
// the same for every viewer (a preview is fetched once per URL and shown to everybody, there is no
// per-viewer language). Only the boss and instance names and the number format follow `lang`.

const WIDTH = 1200;
const HEIGHT = 630;
const MAX_ROWS = 6;
const CACHE_TTL_MS = 3_600_000;

const here = path.dirname(fileURLToPath(import.meta.url));
const FRONTEND_ROOT = path.join(here, "..", "..", "..", "Web-Frontend");
const FONT_FILES = ["DejaVuSans.ttf", "DejaVuSans-Bold.ttf"].map((f) => path.join(here, "..", "..", "assets", "fonts", f));

/** Number formats per site language (same tags as Web-Frontend/i18n.js). */
const LOCALE_TAGS: Record<string, string> = { de: "de-DE", en: "en-US", fr: "fr-FR", es: "es-ES", ru: "ru-RU", pl: "pl-PL", tr: "tr-TR", zh: "zh-CN" };

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

interface FightRow {
  name: string;
  className: string;
  damage: number;
  dps: number;
  healing: number;
  hps: number;
  taken: number;
  absorbed: number;
}

interface Fight {
  boss: string;
  instance: string;
  art: string | undefined;
  durationSeconds: number;
  groupIDps: number;
  playerCount: number;
  rows: FightRow[];
}

function translated(name: string, lang: string): string {
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
    instance: translated(instanceEn, lang),
    art: bossImage(row.bossName, row.instanceName) ?? bossImage(bossEn, instanceEn),
    durationSeconds: row.durationSeconds,
    groupIDps: row.groupIDps,
    playerCount: roster.length,
    rows: roster.slice(0, MAX_ROWS),
  };
}

function buildSvg(fight: Fight, lang: string): string {
  const fmt = (n: number) => Math.round(n).toLocaleString(LOCALE_TAGS[lang]);
  const C = COLORS;
  const rows = fight.rows;
  const maxOf = (pick: (r: FightRow) => number) => Math.max(...rows.map(pick), 1);
  const maxDamage = maxOf((r) => r.damage);
  const maxHeal = maxOf((r) => r.healing);
  const maxTaken = maxOf((r) => r.taken + r.absorbed);

  const bossSize = Math.min(32, 560 / Math.max(textWidth(fight.boss, 1), 1));
  const duration = `${Math.floor(fight.durationSeconds / 60)}:${String(Math.round(fight.durationSeconds % 60)).padStart(2, "0")}`;

  const art = dataUri(fight.art);
  let svg = `<defs><pattern id="hatch" width="9" height="9" patternUnits="userSpaceOnUse" patternTransform="rotate(45)"><rect width="9" height="9" fill="${C.shield}"/><rect width="3.5" height="9" fill="#9fcdff"/></pattern>
<clipPath id="art"><rect x="30" y="108" width="202" height="512" rx="12"/></clipPath></defs>
<rect width="${WIDTH}" height="${HEIGHT}" fill="${C.page}"/>
${art ? `<image href="${art}" width="${WIDTH}" height="${HEIGHT}" preserveAspectRatio="xMidYMid slice" opacity=".35"/>` : ""}
<rect width="${WIDTH}" height="${HEIGHT}" fill="${C.page}" opacity=".62"/>
<text x="40" y="56" font-size="${bossSize.toFixed(1)}" font-weight="700" fill="${C.text}">${escapeXml(fight.boss)}</text>
<text x="1160" y="58" font-size="28" font-weight="700" fill="${C.damage}" text-anchor="end">AION DPS</text>
<text x="1160" y="88" font-size="17" fill="${C.muted}" text-anchor="end">aiondps.com</text>`;

  // Header line: instance name, then player count, duration and group iDPS as icon + number.
  let x = 40;
  svg += `<text x="${x}" y="90" font-size="19" fill="${C.muted}">${escapeXml(fight.instance)}</text>`;
  x += textWidth(fight.instance, 19) + 16;
  const facts: [string, string, string][] = [
    [ICONS.players(C.muted), String(fight.playerCount), C.muted],
    [ICONS.clock(C.muted), duration, C.muted],
    [ICONS.damage(C.damage), `${fmt(fight.groupIDps)}/s`, C.muted],
  ];
  for (const [glyph, text, color] of facts) {
    svg += `<text x="${x}" y="90" font-size="19" fill="${C.muted}">·</text>`;
    x += 14;
    svg += icon(glyph, x, 73.5, 0.8);
    x += 26;
    svg += `<text x="${x}" y="90" font-size="19" fill="${color}">${escapeXml(text)}</text>`;
    x += textWidth(text, 19) + 16;
  }

  const gap = 8;
  const rowH = Math.min(96, (512 - gap * (rows.length - 1)) / rows.length);
  const pitch = Math.min(24, (rowH - 14) / 3);
  const barH = Math.min(15, pitch - 9);
  const barX = 512;
  const barW = 368;
  rows.forEach((r, i) => {
    const top = 108 + i * (rowH + gap);
    const first = top + (rowH - 3 * pitch) / 2; // top of the first text line
    const emblem = dataUri(classImage(r.className));
    const iconSize = Math.min(46, rowH - 22);
    svg += `<rect x="248" y="${top}" width="922" height="${rowH}" rx="12" fill="${C.card}" opacity=".9"/>`;
    if (emblem) svg += `<image href="${emblem}" x="262" y="${top + (rowH - iconSize) / 2}" width="${iconSize}" height="${iconSize}"/>`;
    // Names get 150 px; long ones shrink instead of running into the icons.
    const nameSize = Math.max(15, Math.min(24, 142 / (textWidth(r.name, 1) * 1.2)));
    svg += `<text x="320" y="${top + rowH / 2 + nameSize / 3}" font-size="${nameSize.toFixed(1)}" font-weight="700" fill="${C.text}">${escapeXml(r.name)}</text>`;

    const line = (n: number, glyph: string, value: number, max: number, color: string, label: string, labelColor: string, extra: string) => {
      const y = first + n * pitch;
      const mid = y + pitch / 2;
      const bar = `<rect x="${barX}" y="${mid - barH / 2}" width="${barW}" height="${barH}" rx="${barH / 2}" fill="${C.track}"/>`;
      const fill = value > 0 ? `<rect x="${barX}" y="${mid - barH / 2}" width="${Math.max(barH, (barW * value) / max)}" height="${barH}" rx="${barH / 2}" fill="${color}"/>` : "";
      return `${icon(glyph, 474, mid - 9, 0.76)}${bar}${fill}<text x="1015" y="${mid + 7}" font-size="20" ${n === 0 ? 'font-weight="700"' : ""} fill="${labelColor}" text-anchor="end">${escapeXml(label)}</text>${extra}`;
    };
    const side = (n: number, text: string, color: string) => {
      const mid = first + n * pitch + pitch / 2;
      return `<text x="1155" y="${mid + 6}" font-size="15" fill="${color}" text-anchor="end">${escapeXml(text)}</text>`;
    };

    svg += line(0, ICONS.damage(C.damage), r.damage, maxDamage, C.damage, fmt(r.damage), C.text, side(0, `${fmt(r.dps)}/s`, C.muted));
    svg += line(1, ICONS.heal(C.heal), r.healing, maxHeal, C.heal, fmt(r.healing), C.text, side(1, `${fmt(r.hps)}/s`, C.muted));
    // Taken bar: the part that got through in red, what a shield soaked up as blue hatching behind it.
    const tMid = first + 2 * pitch + pitch / 2;
    const tTop = tMid - barH / 2;
    let taken = line(2, ICONS.taken(C.taken), 0, 1, C.taken, fmt(r.taken), C.taken, r.absorbed > 0 ? side(2, `+${fmt(r.absorbed)}`, C.shield) : "");
    if (r.taken > 0) taken += `<rect x="${barX}" y="${tTop}" width="${Math.max(barH, (barW * r.taken) / maxTaken)}" height="${barH}" rx="${barH / 2}" fill="${C.taken}"/>`;
    if (r.absorbed > 0) taken += `<rect x="${barX + (barW * r.taken) / maxTaken}" y="${tTop}" width="${(barW * r.absorbed) / maxTaken}" height="${barH}" rx="${r.taken > 0 ? 0 : barH / 2}" fill="url(#hatch)"/>`;
    svg += taken;
  });

  if (art) {
    svg += `<image href="${art}" x="-40" y="108" width="460" height="512" preserveAspectRatio="xMidYMid slice" clip-path="url(#art)"/>`;
  } else {
    // No photo for this boss or instance: the site logo on a quiet panel instead of an empty frame.
    const logo = dataUri("/logo.png");
    svg += `<rect x="30" y="108" width="202" height="512" rx="12" fill="${C.card}" opacity=".9"/>${logo ? `<clipPath id="logo"><rect x="56" y="289" width="150" height="150" rx="30"/></clipPath><image href="${logo}" x="56" y="289" width="150" height="150" clip-path="url(#logo)"/>` : ""}`;
  }
  svg += `<rect x="30" y="108" width="202" height="512" rx="12" fill="none" stroke="#2a3544" stroke-width="2"/>`;

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
