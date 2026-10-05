import { env } from "../env.js";
import { escapeHtml } from "./html.js";

export const SITE_NAME = "Aion DPS";
// ?v= busts Discord/Slack/etc link-preview caches, which key on the exact image URL and otherwise
// keep showing a stale image indefinitely. Bump it whenever og/default.png changes.
export const DEFAULT_OG_IMAGE = "/og/default.png?v=20260926";

export interface PageMeta {
  title: string;
  description: string;
  /** Path (with query if it matters) that is the canonical address of this page. */
  canonicalPath: string;
  noindex?: boolean;
  /** Path of the preview image (Discord, Slack, X ...); the site-wide default when absent. */
  ogImage?: string;
  ogImageAlt?: string;
  /** "summary" shows a small square image next to the text (class emblems), the default a wide banner. */
  twitterCard?: "summary" | "summary_large_image";
  ogType?: "website" | "article" | "profile";
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
    `<meta property="og:locale" content="en_US" />`,
    `<meta property="og:image" content="${escapeHtml(image)}" />`,
    meta.ogImageAlt ? `<meta property="og:image:alt" content="${escapeHtml(meta.ogImageAlt)}" />` : "",
    `<meta name="twitter:card" content="${meta.twitterCard ?? "summary_large_image"}" />`,
    `<meta name="twitter:title" content="${escapeHtml(meta.title)}" />`,
    `<meta name="twitter:description" content="${escapeHtml(meta.description)}" />`,
    `<meta name="twitter:image" content="${escapeHtml(image)}" />`,
    meta.ogImageAlt ? `<meta name="twitter:image:alt" content="${escapeHtml(meta.ogImageAlt)}" />` : "",
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
      "Free open-source DPS/HPS meter for Aion 2. Reads the game's network traffic passively, shows live damage and healing per player, and can upload boss fights to community leaderboards.",
  };
}

export function websiteJsonLd(): object {
  return {
    "@context": "https://schema.org",
    "@type": "WebSite",
    name: SITE_NAME,
    url: env.BASE_URL + "/",
    inLanguage: ["en", "de", "fr", "es", "ru", "pl", "tr", "zh"],
    potentialAction: { "@type": "SearchAction", target: env.BASE_URL + "/search?q={search_term_string}", "query-input": "required name=search_term_string" },
  };
}

export function profileJsonLd(profile: { name: string; path: string; description: string; image: string; guild?: string | null; server?: string | null }): object {
  return {
    "@context": "https://schema.org",
    "@type": "ProfilePage",
    url: env.BASE_URL + profile.path,
    mainEntity: {
      "@type": "Person",
      name: profile.name,
      description: profile.description,
      image: env.BASE_URL + profile.image,
      ...(profile.guild ? { memberOf: { "@type": "Organization", name: profile.guild } } : {}),
      ...(profile.server ? { homeLocation: { "@type": "Place", name: profile.server } } : {}),
    },
  };
}
