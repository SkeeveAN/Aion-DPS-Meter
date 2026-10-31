import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { Resvg } from "@resvg/resvg-js";
import { cachedBuffer } from "./cache.js";

// Link-preview pictures of the main pages (og:image of home, features, download ...). They are drawn
// per request host, because the website answers under three addresses and the picture names the one
// the link was posted with. The text sits in the middle, since WhatsApp cuts a square out of there.

const WIDTH = 1200;
const HEIGHT = 630;
const CACHE_TTL_MS = 86_400_000;

const here = path.dirname(fileURLToPath(import.meta.url));
const FRONTEND_ROOT = path.join(here, "..", "..", "..", "Web-Frontend");
const FONT_FILES = ["DejaVuSans.ttf", "DejaVuSans-Bold.ttf"].map((f) => path.join(here, "..", "..", "assets", "fonts", f));

const GREEN = "#49de5c";
const MUTED = "#d6d6e2";

/** The addresses the website answers under; aiondps.com is the one search engines list. */
export const SITE_HOSTS = ["aiondps.com", "daevasuite.com", "daevaspowersuite.com"] as const;

/** Headline (one or two lines) and sub line of each card. */
const CARDS: Record<string, { title: string[]; sub: string }> = {
  home: { title: ["More than a", "damage meter."], sub: "Daevas Power Suite – everything for AION 2 in one free tool" },
  guide: { title: ["AION 2", "DPS Meter"], sub: "Features · Installation · Comparison" },
  features: { title: ["Features"], sub: "Everything the Daevas Power Suite does for AION 2" },
  download: { title: ["Download"], sub: "Daevas Power Suite – free for Windows" },
  "how-it-works": { title: ["How it works"], sub: "Passive packet capture, no memory reading" },
  changelog: { title: ["Changelog"], sub: "Every release of the Daevas Power Suite" },
  legions: { title: ["Legions"], sub: "Legion rankings of AION 2" },
  stats: { title: ["Statistics"], sub: "Boss leaderboards and class rankings" },
  status: { title: ["Server status"], sub: "Is AION 2 online right now?" },
  announcements: { title: ["Announcements"], sub: "The latest news from AION 2" },
  patchnotes: { title: ["Patch notes"], sub: "What changed in AION 2" },
  "twitch-drops": { title: ["Twitch Drops"], sub: "AION 2 campaigns and rewards" },
};

/** The website address a request came in on (without "www."), falling back to the main address for anything unknown. */
export function siteHost(hostname: string): (typeof SITE_HOSTS)[number] {
  const host = hostname.toLowerCase().replace(/^www\./, "");
  return SITE_HOSTS.find((h) => h === host) ?? SITE_HOSTS[0];
}

const escapeXml = (s: string) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");

function dataUri(webPath: string, mime: string): string {
  try {
    return `data:${mime};base64,${fs.readFileSync(path.join(FRONTEND_ROOT, webPath)).toString("base64")}`;
  } catch {
    return "";
  }
}

export function pageCardSvg(slug: string, host: string): string | null {
  const card = CARDS[slug];
  if (!card) return null;
  const hero = dataUri("/og/art/ui/hero-obelisk.jpg", "image/jpeg");
  const logo = dataUri("/images/ui/aion-dps-wordmark.png", "image/png");
  const titleSize = card.title.length > 1 ? 80 : 92;
  const firstBaseline = card.title.length > 1 ? 318 : 360;
  const titleLines = card.title.map((line, i) => `<text x="600" y="${firstBaseline + i * 88}" font-size="${titleSize}" font-weight="700" fill="#ffffff" text-anchor="middle">${escapeXml(line)}</text>`).join("");
  const subY = firstBaseline + (card.title.length - 1) * 88 + 58;
  const svg = `<defs><linearGradient id="fade" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#0a0a12" stop-opacity=".55"/><stop offset=".5" stop-color="#0a0a12" stop-opacity=".78"/><stop offset="1" stop-color="#0a0a12" stop-opacity=".7"/></linearGradient></defs>
<rect width="${WIDTH}" height="${HEIGHT}" fill="#0a0a12"/>
${hero ? `<image href="${hero}" width="${WIDTH}" height="${HEIGHT}" preserveAspectRatio="xMidYMid slice"/>` : ""}
<rect width="${WIDTH}" height="${HEIGHT}" fill="url(#fade)"/>
<rect width="14" height="${HEIGHT}" fill="${GREEN}"/>
${logo ? `<image href="${logo}" x="470" y="70" width="260" height="134"/>` : ""}
${titleLines}
<text x="600" y="${subY}" font-size="30" fill="${MUTED}" text-anchor="middle">${escapeXml(card.sub)}</text>
<text x="600" y="${subY + 82}" font-size="34" font-weight="700" fill="${GREEN}" text-anchor="middle">${escapeXml(host)}</text>`;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${WIDTH}" height="${HEIGHT}" viewBox="0 0 ${WIDTH} ${HEIGHT}" font-family="DejaVu Sans">${svg}</svg>`;
}

/** PNG of one main page's preview picture for one of the website's addresses, or null for an unknown page. */
export function pageOgImage(slug: string, host: string): Buffer | null {
  return cachedBuffer(`ogpage:${slug}:${host}`, CACHE_TTL_MS, () => {
    const svg = pageCardSvg(slug, host);
    if (!svg) return null;
    const resvg = new Resvg(svg, {
      font: { fontFiles: FONT_FILES, loadSystemFonts: false, defaultFontFamily: "DejaVu Sans" },
      fitTo: { mode: "width", value: WIDTH },
    });
    return resvg.render().asPng();
  });
}
