import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { Resvg } from "@resvg/resvg-js";
import { and, eq, ne } from "drizzle-orm";
import { db } from "../db/client.js";
import { bosses, encounterParticipants, encounters, players, servers } from "../db/schema.js";
import { buildProfileView } from "../profile.js";
import { portraitFile } from "../portraitService.js";
import { cachedBuffer } from "./cache.js";
import { classImage } from "./pages.js";
import { parseIdOrSlug } from "./slug.js";

// Link-preview picture of one player profile (Discord, Slack, X ...): the cut-out portrait on the
// obelisk artwork, name, class line, and two stat tiles. Discord shows it ~400 px wide, so the type
// is large. Without a portrait the class emblem takes its place. Wording is English only, like the
// profile page's own meta tags (a preview is fetched once per URL and shown to everybody).

const WIDTH = 1200;
const HEIGHT = 630;
const CACHE_TTL_MS = 3_600_000;

const here = path.dirname(fileURLToPath(import.meta.url));
const FRONTEND_ROOT = path.join(here, "..", "..", "..", "Web-Frontend");
const FONT_FILES = ["DejaVuSans.ttf", "DejaVuSans-Bold.ttf"].map((f) => path.join(here, "..", "..", "assets", "fonts", f));
const HERO = "/og/art/ui/hero-obelisk.jpg";

const GOLD = "#f0c86e";
const MUTED = "#c8c8d7";
/** Accent per class; unknown classes get the Sorcerer purple. */
const CLASS_COLORS: Record<string, string> = {
  Gladiator: "#e0674f",
  Templar: "#e8c15a",
  Ranger: "#6dc07a",
  Assassin: "#4fc3c9",
  Spiritmaster: "#5aa0f0",
  Sorcerer: "#aa6ef0",
  Cleric: "#f0e3a0",
  Chanter: "#f09a4f",
  Brawler: "#d96fa8",
};

const escapeXml = (s: string) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");

/** Rough glyph widths of DejaVu Sans Bold; only used to shrink long names, so it errs wide. */
function textWidth(text: string, size: number): number {
  let units = 0;
  for (const ch of text) {
    units += /[A-ZÄÖÜА-Я0-9]/.test(ch) ? 0.82 : /[ilI.,:;'|!·]/.test(ch) ? 0.36 : ch.charCodeAt(0) > 0x2e80 ? 1.05 : 0.72;
  }
  return units * size;
}

function dataUri(file: string, mime: string): string | null {
  try {
    return `data:${mime};base64,${fs.readFileSync(file).toString("base64")}`;
  } catch {
    return null;
  }
}
const staticUri = (webPath: string) => dataUri(path.join(FRONTEND_ROOT, webPath), webPath.endsWith(".png") ? "image/png" : "image/jpeg");

export interface PlayerCard {
  name: string;
  level: number | null;
  className: string | null;
  faction: string | null;
  guild: string | null;
  serverName: string | null;
  itemLevel: number | null;
  fights: number;
  bestIdps: number | null;
  bestBoss: string | null;
  portrait: string | null;
}

function loadPlayer(key: { id: number } | { slug: string }): PlayerCard | null {
  const player = db
    .select({ id: players.id, name: players.name, guild: players.guild, serverName: servers.displayName, fingerprint: servers.fingerprint })
    .from(players)
    .leftJoin(servers, eq(players.serverId, servers.id))
    .where("id" in key ? eq(players.id, key.id) : eq(players.slug, key.slug))
    .get();
  // Same rule as the profile page: only Aion 2 characters are served.
  if (!player || !(player.fingerprint ?? "").startsWith("aion2:")) return null;
  const runs = db
    .select({ idps: encounterParticipants.idps, className: encounterParticipants.className, faction: encounterParticipants.faction, bossName: bosses.nameEn, bossLocal: bosses.name })
    .from(encounterParticipants)
    .innerJoin(encounters, eq(encounterParticipants.encounterId, encounters.id))
    .innerJoin(bosses, eq(encounters.bossId, bosses.id))
    .where(and(eq(encounterParticipants.playerId, player.id), ne(encounterParticipants.className, "?")))
    .all();
  const profile = buildProfileView(player.id);
  const best = runs.reduce<(typeof runs)[number] | null>((m, r) => (m === null || r.idps > m.idps ? r : m), null);
  return {
    name: player.name,
    level: profile?.level ?? null,
    className: profile?.className ?? runs[0]?.className ?? null,
    faction: profile?.faction ?? runs[0]?.faction ?? null,
    guild: player.guild,
    serverName: player.serverName,
    itemLevel: profile?.averageItemLevel ?? null,
    fights: runs.length,
    bestIdps: best?.idps ?? null,
    bestBoss: best ? (best.bossName ?? best.bossLocal) : null,
    portrait: dataUri(portraitFile(player.id), "image/png"),
  };
}

export function buildSvg(p: PlayerCard): string {
  const fmt = (n: number) => Math.round(n).toLocaleString("en-US");
  const accent = CLASS_COLORS[p.className ?? ""] ?? CLASS_COLORS.Sorcerer;
  const hero = staticUri(HERO);
  const emblem = staticUri(classImage(p.className) ?? "");
  const nameSize = Math.min(96, 590 / Math.max(textWidth(p.name, 1), 1));
  const classLine = [p.level ? `Lv. ${p.level}` : "", p.className ?? "", p.faction ? `· ${p.faction}` : ""].filter(Boolean).join(" ");
  const place = [p.guild, p.serverName?.replace(" - ", " – ")].filter(Boolean).join(" · ");

  let svg = `<defs><linearGradient id="fade" x1="0" x2="1"><stop offset="0" stop-color="#0a0a12" stop-opacity=".78"/><stop offset=".55" stop-color="#0a0a12" stop-opacity=".82"/><stop offset="1" stop-color="#0a0a12" stop-opacity=".94"/></linearGradient>
<linearGradient id="floor" x1="0" y1="0" x2="0" y2="1"><stop offset=".6" stop-color="#0a0a12" stop-opacity="0"/><stop offset="1" stop-color="#0a0a12" stop-opacity=".7"/></linearGradient></defs>
<rect width="${WIDTH}" height="${HEIGHT}" fill="#0a0a12"/>
${hero ? `<image href="${hero}" width="${WIDTH}" height="${HEIGHT}" preserveAspectRatio="xMidYMid slice"/>` : ""}
<rect width="${WIDTH}" height="${HEIGHT}" fill="url(#fade)"/>
<rect width="${WIDTH}" height="${HEIGHT}" fill="url(#floor)"/>`;

  if (p.portrait) {
    svg += `<image href="${p.portrait}" x="690" y="110" width="520" height="520"/>`;
  } else if (emblem) {
    svg += `<clipPath id="disc"><circle cx="960" cy="310" r="200"/></clipPath><image href="${emblem}" x="760" y="110" width="400" height="400" clip-path="url(#disc)" opacity=".92"/>`;
  }

  svg += `<text x="60" y="78" font-size="28" font-weight="700" fill="${GOLD}" letter-spacing="3">AION DPS</text>
<text x="56" y="196" font-size="${nameSize.toFixed(1)}" font-weight="700" fill="#ffffff">${escapeXml(p.name)}</text>
<text x="60" y="256" font-size="36" font-weight="700" fill="${accent}">${escapeXml(classLine)}</text>
<text x="60" y="304" font-size="30" fill="${MUTED}">${escapeXml(place)}</text>`;

  const tiles: [string, string, string][] = [];
  if (p.bestIdps !== null) tiles.push(["Best DPS", fmt(p.bestIdps), GOLD]);
  if (p.itemLevel) tiles.push(["Item level", String(p.itemLevel), "#ffffff"]);
  tiles.forEach(([label, value, color], i) => {
    const x = 60 + i * 270;
    svg += `<rect x="${x}" y="370" width="250" height="130" rx="16" fill="#ffffff" fill-opacity=".1" stroke="#ffffff" stroke-opacity=".2" stroke-width="2"/>
<text x="${x + 22}" y="410" font-size="22" fill="${MUTED}" letter-spacing="1">${label.toUpperCase()}</text>
<text x="${x + 22}" y="473" font-size="52" font-weight="700" fill="${color}">${escapeXml(value)}</text>`;
  });
  if (p.fights > 0) {
    const foot = `${p.fights} boss fight${p.fights === 1 ? "" : "s"} recorded${p.bestBoss ? ` · best vs ${p.bestBoss}` : ""}`;
    svg += `<text x="64" y="552" font-size="26" fill="${MUTED}">${escapeXml(foot)}</text>`;
  }
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${WIDTH}" height="${HEIGHT}" viewBox="0 0 ${WIDTH} ${HEIGHT}" font-family="DejaVu Sans">${svg}</svg>`;
}

/** PNG of one player's preview picture (`idOrSlug` as in /players/:idOrSlug), or null for an unknown / non-Aion-2 player. */
export function playerOgImage(idOrSlug: string): Buffer | null {
  const key = parseIdOrSlug(idOrSlug);
  if (!key) return null;
  return cachedBuffer(`ogp:${"id" in key ? key.id : key.slug}`, CACHE_TTL_MS, () => {
    const card = loadPlayer(key);
    if (!card) return null;
    const resvg = new Resvg(buildSvg(card), {
      font: { fontFiles: FONT_FILES, loadSystemFonts: false, defaultFontFamily: "DejaVu Sans" },
      fitTo: { mode: "width", value: WIDTH },
    });
    return resvg.render().asPng();
  });
}
