import { env } from "../env.js";
import { escapeHtml } from "./html.js";

export const SITE_NAME = "Aion DPS";
export const DEFAULT_OG_IMAGE = "/og/default.png";

export interface PageMeta {
  title: string;
  description: string;
  /** Path (with query if it matters) that is the canonical address of this page. */
  canonicalPath: string;
  noindex?: boolean;
  ogImage?: string;
  ogType?: "website" | "article";
  jsonLd?: object[];
}

/** Everything between the SEO:HEAD markers of Web-Frontend/index.html - see seo/shell.ts. */
export function renderHead(meta: PageMeta): string {
  const canonical = env.BASE_URL + meta.canonicalPath;
  const image = env.BASE_URL + (meta.ogImage ?? DEFAULT_OG_IMAGE);
  const lines = [
    `<title>${escapeHtml(meta.title)}</title>`,
    `<meta name="description" content="${escapeHtml(meta.description)}" />`,
    `<link rel="canonical" href="${escapeHtml(canonical)}" />`,
    meta.noindex ? `<meta name="robots" content="noindex, follow" />` : "",
    `<meta property="og:type" content="${meta.ogType ?? "website"}" />`,
    `<meta property="og:site_name" content="${SITE_NAME}" />`,
    `<meta property="og:title" content="${escapeHtml(meta.title)}" />`,
    `<meta property="og:description" content="${escapeHtml(meta.description)}" />`,
    `<meta property="og:url" content="${escapeHtml(canonical)}" />`,
    `<meta property="og:image" content="${escapeHtml(image)}" />`,
    `<meta name="twitter:card" content="summary_large_image" />`,
    `<meta name="twitter:title" content="${escapeHtml(meta.title)}" />`,
    `<meta name="twitter:description" content="${escapeHtml(meta.description)}" />`,
    `<meta name="twitter:image" content="${escapeHtml(image)}" />`,
    ...(meta.jsonLd ?? []).map(jsonLdScript),
  ];
  return lines.filter((l) => l !== "").join("\n  ");
}

// "</script" inside JSON would end the script element early; escaping "<" keeps the JSON valid
// while making that impossible.
function jsonLdScript(data: object): string {
  return `<script type="application/ld+json">${JSON.stringify(data).replace(/</g, "\\u003c")}</script>`;
}

export function breadcrumbJsonLd(items: { name: string; path: string }[]): object {
  return {
    "@context": "https://schema.org",
    "@type": "BreadcrumbList",
    itemListElement: items.map((item, i) => ({
      "@type": "ListItem",
      position: i + 1,
      name: item.name,
      item: env.BASE_URL + item.path,
    })),
  };
}

export function itemListJsonLd(name: string, items: { name: string; path: string }[]): object {
  return {
    "@context": "https://schema.org",
    "@type": "ItemList",
    name,
    numberOfItems: items.length,
    itemListElement: items.map((item, i) => ({
      "@type": "ListItem",
      position: i + 1,
      name: item.name,
      url: env.BASE_URL + item.path,
    })),
  };
}

export function softwareApplicationJsonLd(): object {
  return {
    "@context": "https://schema.org",
    "@type": "SoftwareApplication",
    name: "Aion DPS Meter",
    applicationCategory: "UtilitiesApplication",
    operatingSystem: "Windows",
    offers: { "@type": "Offer", price: "0", priceCurrency: "EUR" },
    url: env.BASE_URL + "/download",
    downloadUrl: "https://github.com/SkeeveAN/Aion-DPS-Meter/releases",
    description:
      "Free open-source DPS/HPS meter for Aion. Reads only the game's own Chat.log, shows live damage and healing per player, and can upload boss fights to community leaderboards.",
  };
}
