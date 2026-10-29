import { LOCALES, getLocale, setLocale, t, formatNumber, formatDate, translateGameName, intlTag } from "./i18n.js";
import { smallPhoto, INSTANCE_IMAGES, INSTANCE_FOCUS, BOSS_IMAGES, BOSS_CARDS, INSTANCE_MIN_LEVEL, INSTANCE_FACTS, SERVER_NAMES, splitConquest } from "./game-data.js";
import { initThemeSwitcher } from "./theme.js";
import { BOSS_CARDS_V2 } from "./game-data-v2.js";
import { buildBossStats, buildGearFilters, buildInstanceStats } from "./boss-stats.js";
import { buildBossDebuffs, buildBuffsPerSkill, buildFocus, buildGroupTimeline } from "./encounter-timeline.js";
import { ARTICLE, ARTICLE_DE, articleSegments, flowStep } from "./article-data.js";
import { GUIDE_ASOF, GUIDE_ROWS, GUIDE_TABLES, GUIDE_WHAT, GUIDE_WEAK, GUIDE_STRONG, GUIDE_OSS, GUIDE_FAQ } from "./guide-data.js";

// Preview switch for the v2 boss cards: ?v2=1 turns it on for this tab, ?v2=0 off. Without it nothing changes.
const V2 = (() => {
  if (window.__V2) return true;
  try {
    const q = new URLSearchParams(location.search).get("v2");
    if (q === "1") sessionStorage.setItem("v2", "1");
    if (q === "0") sessionStorage.removeItem("v2");
    return sessionStorage.getItem("v2") === "1";
  } catch { return false; }
})();
const bossCards = V2 ? { ...BOSS_CARDS, ...BOSS_CARDS_V2 } : BOSS_CARDS;

const app = document.getElementById("app");
const breadcrumb = document.getElementById("breadcrumb");

// Real path URLs (/aion2/bosses/enhanced-harcon) - one address per page, so search engines and
// Discord previews see distinct pages. The first path segment names the game (only Aion 2 exists
// now; the segment stays so every shared link keeps working).
const APP_SECTIONS = ["download", "features", "aion-2-dps-meter", "how-it-works", "changelog", "twitch-drops", "stats", "status", "announcements", "patchnotes", "legions", "guilds", "feedback", "privacy", "terms", "instances", "worldbosses", "bosses", "players", "encounters", "participants", "compare", "search"];
const DEFAULT_GAME = "aion2";
let currentGame = DEFAULT_GAME;

// Links shared before the URL change (#/bosses/12, #/download, …) still land where they used to:
// the hash is translated to the new path once, and the server then 301s numeric ids to slugs.
(function redirectLegacyHash() {
  const match = location.hash.match(/^#\/?(.*)$/);
  if (!match) {
    return;
  }
  const [section, param] = match[1].split("/");
  let target = null;
  if (!section) {
    target = "/instances";
  } else if (section === "download") {
    target = "/download";
  } else if (["instances", "bosses", "players", "encounters", "participants"].includes(section) && param) {
    target = `/${section}/${param}`;
  } else if (section === "search" && param) {
    target = `/search?q=${param}`;
  }
  if (target) {
    location.replace(target);
  }
})();

// The game is no longer part of the address; every page lives directly under /.
function gp(path) {
  return path;
}

function gameLabel(game) {
  return t(`game.${game}`);
}

async function fetchJson(url) {
  const res = await fetch(url);
  if (!res.ok) {
    throw new Error(`${res.status} ${res.statusText}`);
  }
  return res.json();
}

function el(tag, props = {}, children = []) {
  const node = document.createElement(tag);
  for (const [key, value] of Object.entries(props)) {
    // ARIA attributes have no DOM property to assign to.
    if (key.startsWith("aria-") || key === "role") {
      node.setAttribute(key, String(value));
    } else {
      node[key] = value;
    }
  }
  for (const child of children) {
    node.append(child);
  }
  return node;
}

function link(text, href, className) {
  return el("a", { href, textContent: text, ...(className ? { className } : {}) });
}

// Icons are best-effort - a class/faction/skill name with no matching file (an unmapped class,
// an unknown faction, a skill the collected dataset doesn't cover) just renders without one,
// mirroring the desktop client's own Convert() returning null for a missing asset rather than
// erroring.
function icon(src, className) {
  // Lazy by default (aiondps_design_pack_v1 section 16: "Lazy Loading für Kartenbilder") - covers
  // every card photo (via posterCard) and every small class/faction/skill icon alike; a hero image
  // is never built through this helper (see instanceHero/bossHero), so it keeps the browser's
  // default eager loading instead.
  const img = el("img", { src, alt: "", className, loading: "lazy" });
  img.addEventListener("error", () => img.remove(), { once: true });
  return img;
}

// Gladiator (tank or DD) and Chanter (DD or healer) switch roles in Aion 2, so the class alone does
// not say which one a player played. From the encounter itself: a Chanter is a DD only when a Cleric
// covers the healing AND his damage is well above his healing (CHANTER_DD_RATIO times); alone, he is
// the healer; a Gladiator is the tank when no Templar is in the group (with several, the one
// who took the most damage), otherwise a DD. Sets p.role on every roster entry.
const CHANTER_DD_RATIO = 2;

function assignRoles(roster) {
  const real = roster.filter((p) => p.className !== "?");
  const hasCleric = real.some((p) => p.className === "Cleric");
  const hasTemplar = real.some((p) => p.className === "Templar");
  const gladiators = real.filter((p) => p.className === "Gladiator");
  const tankGladiator = hasTemplar || gladiators.length === 0 ? null : gladiators.reduce((a, b) => ((b.damageTaken ?? 0) > (a.damageTaken ?? 0) ? b : a));
  for (const p of roster) {
    const base = classMeta(p.className).role;
    if (p.className === "Chanter") {
      p.role = hasCleric && (p.totalDamage ?? 0) > (p.totalHealing ?? 0) * CHANTER_DD_RATIO ? "dd" : "healer";
    } else if (p.className === "Gladiator") {
      p.role = p === tankGladiator ? "tank" : "dd";
    } else {
      p.role = base;
    }
  }
}

// Aion 2's nine classes have no icon files yet - a short text badge stands in until we have our
// own artwork (mirrors src/data/aion2/classes.json).
const AION2_CLASS_ABBREVIATIONS = {
  Assassin: "ASN",
  Chanter: "CHA",
  Cleric: "CLR",
  Spiritmaster: "SM",
  Brawler: "BRW",
  Gladiator: "GLA",
  Ranger: "RNG",
  Sorcerer: "SOR",
  Templar: "TPL",
};

// Per-class accent color + trinity role for the encounter page's meter bars (see meterRow below). A pet
// ("?" className) gets its own PET_META; any OTHER unmapped className gets UNKNOWN_CLASS_META instead of
// silently being mislabeled a pet/companion - see classMeta and meterRow's role-badge check.
const CLASS_META = {
  Cleric: { color: "#ffe27a", role: "healer" },
  Chanter: { color: "#e0a020", role: "healer" },
  Templar: { color: "#2f5fd0", role: "tank" },
  Gladiator: { color: "#5aa9ff", role: "dd" },
  Brawler: { color: "#e07a5f", role: "dd" },
  Assassin: { color: "#7ee08f", role: "dd" },
  Ranger: { color: "#2f9e57", role: "dd" },
  Sorcerer: { color: "#b98af5", role: "dd" },
  Spiritmaster: { color: "#8a4fd8", role: "dd" },
};
const PET_META = { color: "#8a99a3", role: "companion" };
// role: null - a real class we just don't have a confident trinity role for yet, never asserted in the UI
// (meterRow skips the role badge entirely when role is falsy).
const UNKNOWN_CLASS_META = { color: "#9fb3c8", role: null };

function classMeta(className) {
  if (className === "?") {
    return PET_META;
  }
  return CLASS_META[className] ?? UNKNOWN_CLASS_META;
}

// A class's name in the reader's language, as the game client spells it (class.<English name> in locales/); the English name
// stays the key everywhere else (icons, colors, uploads).
function classLabel(className) {
  const key = `class.${className}`;
  const text = t(key);
  return text === key ? className : text;
}

// Aion 2's nine classes have no icon files - a short text badge stands in.
function classIcon(className) {
  const file = CLASS_EMBLEM[className];
  if (file) {
    return el("img", { className: "class-emblem", src: `/images/aion2/classes/badge/${file}.webp`, alt: classLabel(className), title: classLabel(className), width: 128, height: 128, loading: "lazy" });
  }
  return el("span", { className: "class-badge", title: classLabel(className), textContent: AION2_CLASS_ABBREVIATIONS[className] ?? className.slice(0, 3).toUpperCase() });
}

function factionIcon(faction) {
  return faction ? icon(`/icons/races/${encodeURIComponent(faction)}.png`, "faction-icon") : null;
}

function iconLabel(iconEl, text) {
  return el("span", { className: "icon-label" }, [iconEl, text].filter((x) => x != null));
}

const SITE_TITLE = "Aion DPS";
const HOME_TITLE = "Aion DPS – Daevas Power Suite | AION 2 Companion App & DPS Meter";

function setBreadcrumb(parts) {
  breadcrumb.replaceChildren();
  parts.forEach((part, i) => {
    if (i > 0) {
      breadcrumb.append(" › ");
    }
    breadcrumb.append(part);
  });
  // The trailing crumb is always the most specific thing on screen (boss, player, instance…), which
  // makes it the right tab title / bookmark label / Discord preview text.
  const last = parts[parts.length - 1];
  const text = typeof last === "string" ? last : last?.textContent;
  document.title = text ? `${text} – ${SITE_TITLE}` : HOME_TITLE;
}

/** Root crumbs every game-scoped page shares: start page › this game's instance list. */
function gameCrumbs() {
  return [link(t("breadcrumb.home"), "/"), link(t("breadcrumb.instances"), gp("/instances"))];
}

// The server may already have rendered this exact page into <main> (see Backend/src/seo/pages.ts):
// a crawler or link preview sees real content without JavaScript. When that pre-rendered fragment
// matches the current address, the "Loading…" placeholder is skipped so the page doesn't flash
// empty before the full interactive version replaces it.
let hydrating = app.firstElementChild?.dataset?.ssr === location.pathname + location.search;

function showLoading(text) {
  if (!hydrating) {
    app.replaceChildren(el("p", { textContent: text }));
  }
}

// Language switcher in the header - persists via i18n.setLocale(), then re-renders the static
// header text and the current route so everything reflects the new language immediately.
function setupLanguageSwitcher() {
  const select = document.getElementById("lang-switcher");
  select.replaceChildren(
    ...LOCALES.map((l) => el("option", { value: l.code, textContent: `${l.flag} ${l.label}` })),
  );
  select.value = getLocale();
  select.addEventListener("change", async () => {
    await setLocale(select.value);
    applyStaticTranslations();
    initThemeSwitcher(t);
    route();
  });
}

// The tab of the current page gets .active (the footer-less main navigation in index.html).
const NAV_PAGES = { "nav-features": "/features", "nav-guilds": "/legions", "nav-stats": "/stats", "nav-twitch-drops": "/twitch-drops", "nav-status": "/status", "nav-announcements": "/announcements", "nav-patchnotes": "/patchnotes" };
function markActiveNav() {
  const path = location.pathname.replace(/\/+$/, "") || "/";
  for (const [id, base] of Object.entries(NAV_PAGES)) {
    const link = document.getElementById(id);
    const on = path === base || path.startsWith(`${base}/`);
    link?.classList.toggle("active", on);
    if (on) {
      link?.setAttribute("aria-current", "page");
    } else {
      link?.removeAttribute("aria-current");
    }
  }
}

function applyStaticTranslations() {
  document.getElementById("search-input").placeholder = t("nav.searchPlaceholder");
  document.getElementById("search-button").textContent = t("nav.searchButton");
  document.getElementById("feedback-link").textContent = t("nav.feedback");
  document.getElementById("nav-features").textContent = t("nav.features");
  document.getElementById("nav-guilds").textContent = t("guild.listTitle");
  // "Statistics" and "Changelog" are English only, as in the footer.
  document.getElementById("nav-stats").textContent = "Statistics";
  document.getElementById("nav-twitch-drops").textContent = "Twitch Drops";
  document.getElementById("nav-status").textContent = t("status.title");
  document.getElementById("nav-announcements").textContent = t("ann.title");
  document.getElementById("nav-patchnotes").textContent = t("pn.title");
  document.getElementById("nav-toggle").setAttribute("aria-label", t("nav.menu"));
}

// Mobile hamburger/drawer (aiondps_design_pack_v1 section 15) - see index.html's #nav-toggle and
// style.css's 720px breakpoint. Above that breakpoint #header-controls is always visible and this
// button is hidden, so the .open class simply never matters there.
function setupNavToggle() {
  const toggle = document.getElementById("nav-toggle");
  const controls = document.getElementById("header-controls");
  toggle.addEventListener("click", () => {
    const isOpen = controls.classList.toggle("open");
    toggle.setAttribute("aria-expanded", String(isOpen));
  });
  // A link inside the drawer (game switch, download, server picker) navigates the page - the
  // drawer should close rather than stay open over the new page underneath it.
  controls.addEventListener("click", (e) => {
    if (e.target.closest("a")) {
      controls.classList.remove("open");
      toggle.setAttribute("aria-expanded", "false");
    }
  });
}

// Download arrow inside a circle: the icon alone says "download", so the buttons need no extra word for it.
const DOWNLOAD_CIRCLE_SVG = '<svg viewBox="0 0 24 24" width="26" height="26" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M12 7v9M8 12.5l4 4 4-4"/></svg>';

const GITHUB_REPO = "SkeeveAN/Aion-DPS-Meter";

/**
 * Homepage (aiondps_design_pack_v1 startseite brief) - a fast way in to the client download and
 * the real Aion/Aion 2 data, not a marketing funnel. Everything below the hero is real data or
 * omitted entirely: the live stats bar only appears once at least one real number is non-zero
 * (Backend/src/routes/stats.ts already returns honest zeros for an empty game), and the top
 * players/recent activity columns only draw from whichever games actually have real rows.
 */
const BANNER_WIN_SIZE = {
  meter: [1040, 840], "map-crop": [878, 570], "overlay-chips": [640, 320], bossfight: [956, 700], analyzer: [1760, 820],
  craft: [1960, 1280], "craft-overlay": [544, 284], "character-gear": [2360, 1800], "character-skills": [2360, 1280], "timetable-hero": [545, 500],
};
// The pictures of the banner slider: background, the client windows (name, height in px, how it looks) and where "More about this" leads.
// "dark" puts a dark fill behind windows with see-through areas, so that the gold frame gradient does not shine through them;
// "bare" is a window that already carries its own frame and fade (timetable-hero), "over" lays the second window over the first.
const HOME_SLIDES = [
  { key: "suite", bg: "/images/ui/banner-bg-1920.webp", wins: [["meter", 250], ["map-crop", 220]], link: "/features" },
  { key: "stats", bg: "/images/ui/hero/colosseum.webp", pos: "-470px 18%", size: "125% auto", wins: [["meter", 250], ["overlay-chips", 140]], link: "/features#feature-meter" },
  { key: "boss", bg: "/images/features/scene-bossfight.webp", wins: [["bossfight", 250, "dark"], ["analyzer", 205]], link: "/features#feature-bossfight" },
  { key: "craft", bg: "/images/features/scene-craft.webp", wins: [["craft", 210], ["craft-overlay", 175, "dark over"]], link: "/features#feature-craft" },
  { key: "character", bg: "/images/features/scene-character.webp", wins: [["character-gear", 250], ["character-skills", 210]], link: "/features#feature-character" },
  { key: "event", bg: "/images/ui/hero/sundial.webp", pos: "-497px 18%", size: "110% auto", wins: [["timetable-hero", 250, "bare"]], link: "/features#feature-timetable" },
];
const HOME_SLIDE_SECONDS = 7;

// The banner's three ways in: big buttons (icon, title, one line of what is behind them, an arrow) so that they read as links, not as labels.
const bannerIcon = (paths) => `<svg viewBox="0 0 24 24" width="30" height="30" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">${paths}</svg>`;
const BANNER_ICON_TOOL = bannerIcon('<rect x="3" y="4" width="18" height="14" rx="2"/><path d="M7 14l3-4 3 3 4-6M8 21h8"/>');
const BANNER_ICON_RANK = bannerIcon('<path d="M8 21h8M12 17v4M7 4h10v5a5 5 0 0 1-10 0zM7 6H4v2a3 3 0 0 0 3 3M17 6h3v2a3 3 0 0 1-3 3"/>');
const BANNER_ICON_BOSS = bannerIcon('<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2M12 3v2M21 12h-2M12 21v-2M3 12h2"/>');
const BANNER_ARROW = '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14M13 6l6 6-6 6"/></svg>';
function bannerCta(href, icon, title, sub, main = false) {
  return el("a", { className: `home-cta${main ? " home-cta-main" : ""}`, href }, [
    el("span", { className: "home-cta-ico", innerHTML: icon, "aria-hidden": "true" }),
    el("span", { className: "home-cta-text" }, [el("b", { textContent: title }), el("small", { textContent: sub })]),
    el("span", { className: "home-cta-arrow", innerHTML: BANNER_ARROW, "aria-hidden": "true" }),
  ]);
}
const HOME_NAME_PARTS = [["D", "aevas "], ["P", "ower "], ["S", "uite"]];
const HOME_FAQ_KEYS = ["what", "stand", "safe", "install", "free"];
// FAQ row under the two ranking cards: the same text the server renders for search engines (Backend/src/seo/meta.ts HOME_FAQ), so visitors and crawlers read the same thing.
function buildHomeFaq() {
  return el("section", { className: "home-section-card home-faq" }, [
    el("div", { className: "home-section-head" }, [el("h2", { textContent: t("home.faqHeading") })]),
    el("div", { className: "home-faq-grid" }, HOME_FAQ_KEYS.map((k) => el("div", { className: "home-faq-item", id: `faq-${k}` }, [el("b", { textContent: t(`home.faq.${k}.q`) }), el("p", { textContent: t(`home.faq.${k}.a`) })]))),
  ]);
}
let homeSliderTimer = null;
// The banner slider: one picture every few seconds; small squares jump to a picture, arrows go to the previous or next one.
// It stops while the mouse is over the banner and does not run by itself when the system asks for less motion.
function mountHomeSlider(hero, parts, bannerWin) {
  clearInterval(homeSliderTimer);
  const layers = new Map(); // slide index -> background layer, made when first needed
  let current = -1;
  let hovering = false;
  const reduceMotion = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;

  const slideText = (slide) =>
    slide.key === "suite"
      ? { tag: t("home.heroTagline"), text: t("home.bannerIntro"), more: t("home.slideAll") }
      : { title: t(`home.slide.${slide.key}.title`), tag: t(`home.slide.${slide.key}.tag`), text: t(`home.slide.${slide.key}.text`), more: t("home.slideMore") };

  const bgLayer = (i) => {
    if (!layers.has(i)) {
      const slide = HOME_SLIDES[i];
      const layer = el("div", { className: "home-banner-bgimg" });
      layer.style.backgroundImage = `url(${slide.bg})`;
      if (slide.size) layer.style.backgroundSize = slide.size;
      if (slide.pos) layer.style.backgroundPosition = slide.pos;
      parts.bg.append(layer);
      layers.set(i, layer);
    }
    return layers.get(i);
  };

  const dots = HOME_SLIDES.map((slide, i) =>
    el("button", { type: "button", className: "home-slider-dot", "aria-label": t("home.slideGo", { n: i + 1 }), onclick: () => show(i, true) }),
  );
  const arrow = (dir) =>
    el("button", {
      type: "button",
      className: `home-slider-arrow home-slider-arrow-${dir < 0 ? "prev" : "next"}`,
      "aria-label": t(dir < 0 ? "home.slidePrev" : "home.slideNext"),
      innerHTML: `<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="${dir < 0 ? "M15 5l-7 7 7 7" : "M9 5l7 7-7 7"}"/></svg>`,
      onclick: () => show((current + dir + HOME_SLIDES.length) % HOME_SLIDES.length, true),
    });
  hero.append(arrow(-1), arrow(1), el("div", { className: "home-slider-dots", role: "group" }, dots));

  const fill = (i) => {
    const slide = HOME_SLIDES[i];
    const text = slideText(slide);
    parts.stage.replaceChildren(...slide.wins.map(([name, h, look]) => bannerWin(name, h, look)));
    const title =
      slide.key === "suite"
        ? el("h1", { className: "home-banner-name" }, HOME_NAME_PARTS.map(([initial, rest]) => el("span", {}, [el("b", { textContent: initial }), rest])))
        : el("h1", { textContent: text.title });
    parts.copy.replaceChildren(
      title,
      el("p", { className: "home-banner-tag", textContent: text.tag }),
      el("p", { className: "home-banner-intro", textContent: text.text }),
      el("a", { className: "home-slide-more", href: slide.link, textContent: `${text.more} \u2192` }),
    );
  };

  const show = (i, byUser = false) => {
    if (i === current) return;
    const first = current < 0;
    const swap = () => {
      for (const [k, layer] of layers) layer.classList.toggle("is-on", k === i);
      bgLayer(i).classList.add("is-on");
      fill(i);
      dots.forEach((d, k) => { d.classList.toggle("is-on", k === i); d.setAttribute("aria-current", k === i ? "true" : "false"); });
      parts.stage.classList.remove("is-out"); parts.copy.classList.remove("is-out");
    };
    current = i;
    if (first) { swap(); return; }
    parts.stage.classList.add("is-out"); parts.copy.classList.add("is-out");
    setTimeout(swap, 180);
    if (byUser) restart();
  };

  const restart = () => {
    clearInterval(homeSliderTimer);
    if (reduceMotion) return;
    homeSliderTimer = setInterval(() => { if (!hovering && document.hidden !== true && hero.isConnected) show((current + 1) % HOME_SLIDES.length); else if (!hero.isConnected) clearInterval(homeSliderTimer); }, HOME_SLIDE_SECONDS * 1000);
  };

  hero.addEventListener("mouseenter", () => { hovering = true; });
  hero.addEventListener("mouseleave", () => { hovering = false; });
  hero.addEventListener("keydown", (e) => {
    if (e.key === "ArrowLeft") show((current - 1 + HOME_SLIDES.length) % HOME_SLIDES.length, true);
    else if (e.key === "ArrowRight") show((current + 1) % HOME_SLIDES.length, true);
  });
  show(0);
  restart();
  // The other pictures load in the background once the page is quiet, so that the first change does not wait for them.
  setTimeout(() => HOME_SLIDES.slice(1).forEach((s) => { const img = new Image(); img.src = s.bg; }), 3000);
}

async function renderHome() {
  setBreadcrumb([]);
  document.title = HOME_TITLE;
  showLoading(t("loading.instances"));

  // Slim banner = the Features hero (same look as the tour's scenes): it points at the three entry areas (Features, Instances, World Bosses)
  // but stays small, so Top players and Recent activity below remain the main content.
  const bannerWin = (name, h, look = "") =>
    el("img", { className: `home-banner-win${look ? ` ${look.split(" ").map((c) => `is-${c}`).join(" ")}` : ""}`, width: BANNER_WIN_SIZE[name][0], height: BANNER_WIN_SIZE[name][1], src: `/images/features/${name}@2x.webp?v=${FEATURE_VERSION}`, alt: "", style: `--h:${h}px` });
  const bannerBg = el("div", { className: "home-banner-bg" });
  const bannerStage = el("div", { className: "home-banner-stage" });
  const bannerCopy = el("div", { className: "home-banner-copy" });
  const heroRow = el("section", { className: "home-banner", tabIndex: 0, "aria-roledescription": "carousel" }, [
    bannerBg,
    bannerStage,
    bannerCopy,
    el("div", { className: "home-banner-ctas" }, [
      bannerCta("/features", BANNER_ICON_TOOL, t("home.bannerFeatures"), t("home.bannerFeaturesSub"), true),
      bannerCta("/instances", BANNER_ICON_RANK, t("home.bannerInstances"), t("home.bannerInstancesSub")),
      bannerCta("/worldbosses", BANNER_ICON_BOSS, t("home.bannerWorldbosses"), t("home.bannerWorldbossesSub")),
    ]),
  ]);

  mountHomeSlider(heroRow, { bg: bannerBg, stage: bannerStage, copy: bannerCopy }, bannerWin);

  // The three requests do not depend on each other: started together, the page waits for the slowest instead of the sum.
  const aion2StatsRequest = fetchJson("/api/stats/summary?game=aion2").catch(() => null);
  const topPlayersRequest = fetchJson("/api/players/top?game=aion2&limit=5").catch(() => []);
  const recentActivityRequest = fetchJson("/api/activity/recent?game=aion2&limit=5").catch(() => []);

  const aion2Stats = await aion2StatsRequest;
  const totals = {
    encounterCount: aion2Stats?.encounterCount ?? 0,
    parseCount: aion2Stats?.parseCount ?? 0,
    playerCount: aion2Stats?.playerCount ?? 0,
  };
  const statsBar =
    totals.encounterCount + totals.parseCount + totals.playerCount > 0
      ? el("div", { className: "home-stats-card" }, [
          homeStatItem(ICON_STAT_ENCOUNTERS, formatNumber(totals.encounterCount), t("home.statEncounters")),
          homeStatItem(ICON_STAT_PARSES, formatNumber(totals.parseCount), t("home.statParses")),
          homeStatItem(ICON_STAT_PLAYERS, formatNumber(totals.playerCount), t("home.statPlayers")),
        ])
      : null;

  // Aion 2's official servers are one comparable standard, so the ranking runs combined across them.
  const topPlayers = await topPlayersRequest;
  const topPlayersGame = "aion2";
  const topPlayersSection = topPlayers.length > 0 ? buildTopPlayersSection(topPlayers, topPlayersGame) : null;

  const recentActivity = (await recentActivityRequest)
    .map((r) => ({ ...r, game: "aion2" }))
    .sort((a, b) => parseServerTime(b.createdAt) - parseServerTime(a.createdAt));
  const recentActivitySectionEl = recentActivity.length > 0 ? buildRecentActivitySection(recentActivity) : null;

  // Leaderboard + recent activity side by side 50/50 (per the user, 2026-09-24). The homepage
  // otherwise never lists individual instances - a picked classic-Aion server plus Aion 2's own
  // unscoped catalog made "Featured instances" here look mixed/inconsistent (per the user), so it
  // was dropped rather than fixed again; /aion/instances and /aion2/instances are the real lists.
  // With only one of the two present (no server picked yet, say), that one takes the full row
  // rather than leaving an empty half beside it - the cards float over the hero artwork, so an
  // empty slot there would read as a hole, not as whitespace.
  const sections = [topPlayersSection, recentActivitySectionEl].filter((s) => s != null);
  const contentGrid = el(
    "div",
    { className: "home-content-grid" + (sections.length === 1 ? " home-content-grid-single" : "") },
    sections.map((s) => el("div", { className: "home-content-col" }, [s])),
  );

  // Footer itself is appended centrally by route() (buildSiteFooter), not here - see its own
  // remarks for why this used to be the ONE page that had it.
  // One wrapper carries the 190px pull-up into the hero, so the stats card can sit at a fixed
  // offset above the cards' top edge (see .home-below-hero / .home-stats-card) exactly as in the
  // prototype, instead of being measured down from the hero's bottom.
  app.replaceChildren(heroRow, el("div", { className: "home-below-hero" }, [...(statsBar ? [statsBar] : []), contentGrid, buildHomeFaq()]));
  scrollToHash();
}

// 3-column footer (aiondps_claude_design_pack prototype comparison, index-3.html): logo left, CTA
// centered, legal + real social links right - the prototype only had the center CTA wired up
// before; these are the same real destinations that comparison page used. Used to be built only
// inside renderHome, so every other page (encounters, instances, bosses, leaderboard, participant/
// player profiles, search, legal pages, download, 404...) silently had no footer at all - reported
// by the user for the encounter page specifically, but it was really every non-home route. Now a
// standalone builder appended once, centrally, by route() itself (see its own remarks) rather than
// something every individual render function has to remember to add.
const ICON_STAR = '<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor" aria-hidden="true"><path d="M12 2l3.1 6.3 6.9 1-5 4.9 1.2 6.8-6.2-3.3-6.2 3.3 1.2-6.8-5-4.9 6.9-1z"/></svg>';
let starCountPromise;
// The repository's star count, read once per page load from GitHub (unauthenticated, so it can be rate limited: null then).
function fetchStarCount() {
  starCountPromise ??= fetchJson(`https://api.github.com/repos/${GITHUB_REPO}`).then((r) => (Number.isFinite(r?.stargazers_count) ? r.stargazers_count : null), () => null);
  return starCountPromise;
}
// "Star on GitHub" with the live count: an invitation, not a button that stars for you (that needs the visitor's own GitHub login).
function buildStarLink() {
  const count = el("span", { className: "star-count" });
  const a = el("a", { className: "star-link", href: `https://github.com/${GITHUB_REPO}`, target: "_blank", rel: "noopener", title: t("star.hint") }, [
    el("span", { className: "star-icon", innerHTML: ICON_STAR }),
    el("span", { textContent: t("star.label") }),
    count,
  ]);
  fetchStarCount().then((n) => (n === null ? count.remove() : (count.textContent = n.toLocaleString())));
  return a;
}

function buildSiteFooter() {
  const cta = el("div", { className: "home-footer-cta" }, [
    el("div", { className: "home-footer-logo" }, [
      el("img", { src: "/images/ui/emblem.webp", alt: "", className: "home-footer-emblem", loading: "lazy" }),
      el("img", { src: "/images/ui/aion-dps-wordmark.webp", alt: "Aion DPS", className: "wordmark", width: 186, height: 96, loading: "lazy" }),
      el("div", { className: "home-footer-legal home-footer-legal-logo" }, [
        link(t("legal.privacyTitle"), "/privacy"),
        link(t("legal.termsTitle"), "/terms"),
      ]),
    ]),
    el("div", { className: "home-footer-center" }, [
      el("h2", { textContent: t("home.footerCtaHeading") }),
      el("a", { className: "btn btn-gold", href: "/download" }, [
        el("span", { className: "btn-icon btn-icon-circle", innerHTML: DOWNLOAD_CIRCLE_SVG, "aria-hidden": "true" }),
        el("span", { textContent: "Aion DPS" }),
      ]),
    ]),
    el("div", { className: "home-footer-links" }, [
      el("div", { className: "home-footer-legal" }, [
        link("Changelog", "/changelog"),
        link(t("footer.howItWorks"), "/how-it-works"),
        buildStarLink(),
      ]),
      el("div", { className: "home-footer-social" }, [
        el("a", { href: "https://aiondps.com/twitch", target: "_blank", rel: "noopener", title: "Twitch", innerHTML: ICON_SOCIAL_TWITCH }),
        el("a", { href: "https://aiondps.com/discord", target: "_blank", rel: "noopener", title: "Discord", innerHTML: ICON_SOCIAL_DISCORD }),
        el("a", { href: "https://aiondps.com/steam_aion2", target: "_blank", rel: "noopener", title: "Steam", innerHTML: ICON_SOCIAL_STEAM }),
      ]),
    ]),
  ]);
  return el("div", { className: "site-footer" }, [cta, el("p", { className: "site-disclaimer", textContent: t("footer.disclaimer") })]);
}

function buildTopPlayersSection(rows, game) {
  const best = Math.max(...rows.map((r) => r.idps), 1);
  const list = rows.map((r, i) =>
    el("a", { className: "hp-prow", href: gp(`/encounters/${r.encounterId}`), style: `--cc:${classMeta(r.className).color};${i < 3 ? `--medal:${MEDAL_COLORS[i]}` : ""}` }, [
      el("span", { className: "hp-rank", textContent: String(i + 1) }),
      classEmblem(r.className, false, true),
      el("span", { className: "hp-who" }, [
        el("b", { textContent: r.playerName }),
        el("span", {}, [displayName({ name: r.bossName, nameEn: r.bossNameEn }), " ", diffPill(r.instanceCategory, r.mode, r.instanceName), serverLabel(r.serverName) ? ` · ${serverLabel(r.serverName)}` : ""].filter((x) => x != null)),
      ]),
      el("span", { className: "hp-score" }, [el("b", { textContent: formatNumber(r.idps) }), el("span", { textContent: t("leaderboard.idpsShort") })]),
      el("i", { className: "hp-fill", style: `width:${(r.idps / best) * 100}%` }),
    ]),
  );
  // Which scope this ranking covers, right next to the heading: combined across the servers.
  const scopeTag = gameLabel(game);
  const head = el("div", { className: "home-section-head" }, [
    el("h2", { textContent: t("home.topPlayersHeading") }),
    ...(scopeTag ? [el("span", { className: "home-section-tag", textContent: scopeTag })] : []),
  ]);
  return el("section", { className: "home-section-card" }, [head, el("div", { className: "hp-list" }, list)]);
}

function buildRecentActivitySection(rows) {
  const cards = rows.map((r) => {
    const photo = bossCards[r.bossNameEn] ?? bossCards[r.bossName] ?? BOSS_IMAGES[r.bossNameEn] ?? BOSS_IMAGES[r.bossName] ?? INSTANCE_IMAGES[r.instanceNameEn] ?? INSTANCE_IMAGES[r.instanceName];
    return el("a", { className: "hp-run", href: gp(`/encounters/${r.encounterId}`) }, [
      el("span", { className: "hp-thumb", style: photo ? `background-image:url('${smallPhoto(photo)}')` : "" }),
      el("span", { className: "hp-run-mid" }, [
        el("b", {}, [
          displayName({ name: r.bossName, nameEn: r.bossNameEn }),
          ...[diffPill(r.instanceCategory, r.mode, r.instanceName), partialBadge(r)].filter((x) => x != null).flatMap((p) => [" ", p]),
        ]),
        el("span", { className: "hp-run-sub" }, [
          `${plainInstanceName(r)} · ${formatDuration(r.durationSeconds)} · ${formatRelativeTime(r.createdAt)}`,
          ...(r.topPlayerName ? [" · ", classEmblem(r.topPlayerClassName, true), r.topPlayerName] : []),
        ]),
      ]),
      el("span", { className: "hp-run-score" }, [el("b", { textContent: formatNumber(r.groupIDps) }), el("span", { textContent: t("leaderboard.groupIdps") })]),
    ]);
  });
  // Per the user: this heading sits flush RIGHT (mirroring Top players' flush-left one), with the
  // live marker directly beside it.
  const head = el("div", { className: "home-section-head home-section-head-right" }, [
    el("h2", { textContent: t("home.recentActivityHeading") }),
    el("span", { className: "home-section-tag" }, [el("span", { className: "home-live-dot" }), t("home.liveTag")]),
  ]);
  return el("section", { className: "home-section-card" }, [head, el("div", { className: "hp-list" }, cards)]);
}

/**
 * Per the user: the Aion DPS client itself should be offered for download right here, with an
 * explanation - reachable without picking a server first, since the client works the same
 * regardless of which server it's pointed at.
 *
 * The download link/version comes from GitHub's own releases list, fetched client-side (GitHub's
 * API sends CORS headers for this, no backend proxy needed) - NOT /releases/latest, which 404s for
 * this repo because every release here is marked prerelease (same trap the client's own
 * Update/UpdateService.cs works around); releases[0] is the newest one regardless of that flag.
 */
/** Shared by renderPrivacy/renderTerms below - both are just a title, an intro paragraph, and a
 * flat run of numbered i18n sections ("legal.<prefix>Section<N>Heading/Body"), stopping at the
 * first missing key. */
/**
 * A section's text as paragraphs and bullet lists: a blank line separates paragraphs, lines that start with "• " are the items of a list (their further lines sit indented
 * under the first word, the way a list is set). A text without line breaks is one paragraph.
 */
function legalBody(text) {
  const blocks = [];
  let paragraph = [];
  let list = null;
  const flush = () => {
    if (paragraph.length > 0) {
      blocks.push(el("p", {}, articleSegments(paragraph.join(" ")).map((s) => (s.href ? link(s.text, s.href) : s.bold ? el("strong", { textContent: s.text }) : document.createTextNode(s.text)))));
      paragraph = [];
    }
    list = null;
  };
  for (const raw of text.split("\n")) {
    const line = raw.trim();
    if (line === "") {
      flush();
    } else if (line.startsWith("• ")) {
      if (paragraph.length > 0) {
        flush();
      }
      if (list === null) {
        list = el("ul", { className: "legal-list" });
        blocks.push(list);
      }
      list.append(el("li", { textContent: line.slice(2) }));
    } else {
      list = null;
      paragraph.push(line);
    }
  }
  flush();
  return blocks;
}

// Anchors for linking to one section from outside (e.g. /terms#game-rules); the numbers alone would break when a section moves.
const LEGAL_SECTION_IDS = { terms: ["accept", "fan-project", "no-warranty", "game-rules", "uploads", "fair-use", "availability", "open-source", "liability", "law", "contact"] };
/** After a page is built: jump to the element the URL's #anchor names (the browser tried before the page existed). */
function scrollToHash() {
  const id = decodeURIComponent(location.hash.slice(1));
  if (id) document.getElementById(id)?.scrollIntoView();
}

function renderLegalPage(prefix, titleKey, introKey) {
  setBreadcrumb([link(t("breadcrumb.home"), "/"), t(titleKey)]);
  document.title = `${t(titleKey)} – ${SITE_TITLE}`;
  const sections = [];
  for (let i = 1; ; i++) {
    const headingKey = `legal.${prefix}Section${i}Heading`;
    const bodyKey = `legal.${prefix}Section${i}Body`;
    const heading = t(headingKey);
    if (heading === headingKey) {
      break;
    }
    sections.push(el("section", { className: "legal-section", id: LEGAL_SECTION_IDS[prefix]?.[i - 1] ?? `section-${i}` }, [el("h2", { textContent: heading }), ...legalBody(t(bodyKey))]));
  }
  app.replaceChildren(
    el("div", { className: "legal-page" }, [el("h1", { textContent: t(titleKey) }), el("p", { className: "legal-intro", textContent: t(introKey) }), ...sections]),
  );
  scrollToHash();
}

/** "Problem / Improvement" form: POSTs to /api/feedback, which opens a GitHub issue. */
function renderFeedback() {
  setBreadcrumb([link(t("breadcrumb.home"), "/"), t("feedback.title")]);
  document.title = `${t("feedback.title")} – ${SITE_TITLE}`;

  const field = (labelKey, control, hintKey, tag = "label") =>
    el(tag, { className: "feedback-field" }, [
      el("span", { className: "feedback-label", textContent: t(labelKey) }),
      control,
      ...(hintKey ? [el("small", { textContent: t(hintKey) })] : []),
    ]);
  // A real <select> opens the OS's own popup, which ignores the theme (dark text on a grey sheet,
  // highlight narrower than the list), so the type picker is a small listbox of its own.
  const typeOptions = [
    ["issue", t("feedback.typeIssue")],
    ["feature", t("feedback.typeFeature")],
    ["improvement", t("feedback.typeImprovement")],
  ];
  const type = { value: "issue" };
  const typeButton = el("button", { type: "button", className: "dropdown-button" });
  typeButton.setAttribute("aria-haspopup", "listbox");
  typeButton.setAttribute("aria-expanded", "false");
  const typeList = el("ul", { className: "dropdown-list", role: "listbox", hidden: true });
  const typeItems = typeOptions.map(([value, label]) => {
    const item = el("li", { role: "option", tabIndex: -1, textContent: label });
    item.addEventListener("click", () => choose(value));
    item.addEventListener("keydown", (event) => {
      if (event.key === "Enter" || event.key === " ") {
        event.preventDefault();
        choose(value);
      } else if (event.key === "ArrowDown") {
        event.preventDefault();
        (item.nextElementSibling ?? typeList.firstElementChild).focus();
      } else if (event.key === "ArrowUp") {
        event.preventDefault();
        (item.previousElementSibling ?? typeList.lastElementChild).focus();
      }
    });
    typeList.appendChild(item);
    return { value, item };
  });
  function setOpen(open) {
    typeList.hidden = !open;
    typeButton.setAttribute("aria-expanded", String(open));
    if (open) {
      typeItems.find((o) => o.value === type.value).item.focus();
    }
  }
  function choose(value) {
    type.value = value;
    typeButton.textContent = typeOptions.find(([v]) => v === value)[1];
    typeItems.forEach((o) => o.item.setAttribute("aria-selected", String(o.value === value)));
    setOpen(false);
    typeButton.focus();
  }
  typeButton.addEventListener("click", () => setOpen(typeList.hidden));
  typeList.addEventListener("keydown", (event) => {
    if (event.key === "Escape") {
      setOpen(false);
      typeButton.focus();
    }
  });
  document.addEventListener("click", (event) => {
    if (!typeList.hidden && !typeList.parentElement?.contains(event.target)) {
      setOpen(false);
    }
  });
  choose("issue");
  const typeControl = el("div", { className: "dropdown" }, [typeButton, typeList]);
  const name = el("input", { type: "text", name: "name", maxLength: 80, required: true, autocomplete: "name" });
  const email = el("input", { type: "email", name: "email", maxLength: 200, autocomplete: "email" });
  const message = el("textarea", { name: "message", rows: 8, maxLength: 5000, required: true, minLength: 10 });
  // Honeypot: hidden from people, tempting to form-filling bots.
  const trap = el("input", { type: "text", name: "website", tabIndex: -1, autocomplete: "off", className: "feedback-trap" });
  const status = el("p", { className: "feedback-status", role: "status" });
  const send = el("button", { type: "submit", className: "feedback-send", textContent: t("feedback.send") });

  const form = el("form", { className: "feedback-form" }, [
    field("feedback.type", typeControl, undefined, "div"),
    field("feedback.name", name),
    field("feedback.email", email, "feedback.emailHint"),
    field("feedback.message", message),
    trap,
    el("div", { className: "feedback-actions" }, [send, status]),
  ]);
  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    send.disabled = true;
    status.className = "feedback-status";
    status.textContent = t("feedback.sending");
    try {
      const response = await fetch("/api/feedback", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          type: type.value,
          name: name.value.trim(),
          email: email.value.trim(),
          message: message.value.trim(),
          source: "web",
          lang: getLocale(),
          website: trap.value,
        }),
      });
      if (response.status === 400) {
        status.className = "feedback-status error";
        status.textContent = t("feedback.invalid");
      } else if (!response.ok) {
        throw new Error(String(response.status));
      } else {
        const result = await response.json();
        form.replaceChildren(
          el("p", { className: "feedback-status ok", textContent: t("feedback.sent") }),
          ...(result.issueUrl ? [el("p", {}, [el("a", { href: result.issueUrl, target: "_blank", rel: "noopener", textContent: t("feedback.sentLink") })])] : []),
        );
        return;
      }
    } catch (err) {
      status.className = "feedback-status error";
      status.textContent = t("feedback.failed", { msg: err.message });
    }
    send.disabled = false;
  });

  app.replaceChildren(el("div", { className: "feedback-page" }, [el("h1", { textContent: t("feedback.title") }), el("p", { className: "legal-intro", textContent: t("feedback.intro") }), form]));
}

async function renderPrivacy() {
  renderLegalPage("privacy", "legal.privacyTitle", "legal.privacyIntro");
}

async function renderTerms() {
  renderLegalPage("terms", "legal.termsTitle", "legal.termsIntro");
}

// /how-it-works: the technical article (article-data.js), English, German and the other site languages (article-i18n/).
async function renderArticle() {
  const locale = getLocale();
  let ARTICLE_TEXT = locale === "de" ? ARTICLE_DE : ARTICLE;
  if (locale !== "de" && locale !== "en") {
    // the other languages sit in a file each (article-i18n/), loaded only when that language is open; a missing one stays English
    const translated = await import(`./article-i18n/${locale}.js`).then((m) => m.default, () => null);
    if (translated) ARTICLE_TEXT = { ...ARTICLE, ...translated };
  }
  setBreadcrumb([link(t("breadcrumb.home"), "/"), t("footer.howItWorks")]);
  document.title = `${ARTICLE_TEXT.title} – ${SITE_TITLE}`;
  const inline = (text) => articleSegments(text).map((s) => (s.href ? link(s.text, s.href) : s.bold ? el("strong", { textContent: s.text }) : document.createTextNode(s.text)));
  const block = ([type, value]) => {
    if (type === "h2") return el("h2", { textContent: value });
    if (type === "ul" || type === "ol") return el(type, { className: "guide-list" }, value.map((item) => el("li", {}, inline(item))));
    if (type === "flow") {
      // the arrow belongs to the step before it, so a line never starts with one when the row wraps
      const steps = value.steps.map(flowStep).map((step, i, all) => el("div", { className: "article-flow-item", role: "listitem" }, [
        el("div", { className: `article-flow-step ${step.kind}`, textContent: step.text }),
        ...(i < all.length - 1 ? [el("span", { className: "article-flow-arrow", textContent: "→", "aria-hidden": "true" })] : []),
      ]));
      return el("figure", { className: "article-flow" }, [el("div", { className: "article-flow-steps", role: "list" }, steps), el("figcaption", { textContent: value.caption })]);
    }
    return el("p", { className: type === "lead" ? "guide-intro" : "" }, inline(value));
  };
  const [lead, ...rest] = ARTICLE_TEXT.blocks;
  app.replaceChildren(el("div", { className: "guide-page article-page" }, [el("h1", { textContent: ARTICLE_TEXT.title }), block(lead), el("div", { className: "article-columns" }, rest.map(block))]));
}

// /aion-2-dps-meter: what the meter does, how to install it and a comparison with other AION 2 meters. The tables come from
// guide-data.js, the same data the server writes into the page for crawlers.
function renderGuide() {
  setBreadcrumb([link(t("breadcrumb.home"), "/"), t("guide.breadcrumb")]);
  document.title = `${t("guide.title")} – ${SITE_TITLE}`;
  const cellText = (value) => (value.startsWith("$") ? t(`guide.c.${value.slice(1)}`) : value);
  const cellClass = (value) => (value === "$yes" ? "guide-yes" : value === "$no" ? "guide-no" : value === "$na" ? "guide-na" : "");
  const list = (keys, prefix) => el("ul", { className: "guide-list" }, keys.map((k) => el("li", { textContent: t(`${prefix}.${k}`) })));
  const table = (spec) => el("div", { className: "guide-table-wrap" }, [
    el("h3", { textContent: t(`guide.t.${spec.id}`) }),
    el("table", { className: "guide-table" }, [
      el("thead", {}, [el("tr", {}, [
        el("th", { scope: "col", textContent: t("guide.rowHead") }),
        ...spec.columns.map((c) => el("th", { scope: "col", className: c.ours ? "guide-ours" : "" }, [
          c.url ? el("a", { href: c.url, target: "_blank", rel: "noopener", textContent: c.name }) : document.createTextNode(c.name),
        ])),
      ])]),
      el("tbody", {}, GUIDE_ROWS.map((row) => el("tr", {}, [
        el("th", { scope: "row", textContent: t(`guide.r.${row}`) }),
        ...spec.columns.map((c) => el("td", { className: `${c.ours ? "guide-ours " : ""}${cellClass(c.cells[row])}`.trim(), textContent: cellText(c.cells[row]) })),
      ]))),
    ]),
  ]);
  app.replaceChildren(el("div", { className: "guide-page" }, [
    el("h1", { textContent: t("guide.title") }),
    el("p", { className: "guide-intro", textContent: t("guide.intro") }),
    el("p", {}, [link(t("guide.download"), "/download", "btn btn-gold"), " ", link(t("guide.allFeatures"), "/features", "btn btn-ghost")]),
    el("h2", { textContent: t("guide.h.what") }), list(GUIDE_WHAT, "guide.what"),
    el("h2", { textContent: t("guide.h.install") }),
    el("p", {}, [`${t("guide.installShort")} `, link(t("guide.download"), "/download")]),
    el("p", { textContent: t("guide.installNote") }),
    el("h2", { textContent: t("guide.h.measure") }),
    ...["p1", "p2", "p3"].map((k) => el("p", { textContent: t(`guide.measure.${k}`) })),
    el("h2", { textContent: t("guide.h.compare") }),
    el("p", { className: "guide-intro", textContent: t("guide.compare.intro", { date: GUIDE_ASOF }) }),
    ...GUIDE_TABLES.map(table),
    el("p", { textContent: t("guide.lago") }),
    el("p", { textContent: t("guide.rdpsWhy") }),
    el("p", { textContent: t("guide.also") }),
    el("h2", { textContent: t("guide.h.weak") }), list(GUIDE_WEAK, "guide.weak"),
    el("h2", { textContent: t("guide.h.strong") }), list(GUIDE_STRONG, "guide.strong"),
    el("h2", { textContent: t("guide.h.oss") }), list(GUIDE_OSS, "guide.oss"),
    el("h2", { textContent: t("guide.h.faq") }),
    ...GUIDE_FAQ.flatMap((k) => [el("h3", { textContent: t(`guide.faq.${k}.q`) }), el("p", { textContent: t(`guide.faq.${k}.a`) })]),
  ]));
}

// Feature tour: one scene per feature - a generated backdrop, the text on one side and the real client windows (rendered from the client itself,
// images/features/, see the client's render-* modes) on the other. [file, width, height] of each @2x picture; the shown height is set in style.css.
const FEATURE_BLOCKS = [
  { id: "meter", images: [["meter", 1040, 840]] },
  { id: "modes", images: [["mode-all", 1040, 840], ["mode-all-compact", 1040, 840]] },
  { id: "overlay", images: [["overlay", 780, 338], ["overlay-chips", 780, 330]] },
  { id: "bossfight", images: [["bossfight", 956, 700, { flat: true }]] },
  { id: "assist", badge: "assist.badge", images: [["assist", 840, 1236]] },
  { id: "details", images: [["analyzer", 1760, 820]] },
  { id: "character", images: [["character-gear", 2360, 1800], ["character-skills", 2360, 1280]] },
  { id: "checklist", images: [["checklist", 960, 1274]] },
  // the sides of the scenes are tuned one by one in style.css, so they are fixed here and not alternated by position
  { id: "map", images: [["map", 1448, 892], ["gathering", 1240, 1480]] },
  { id: "petlevel", images: [["petlevel", 382, 122, { h: 150 }]] },
  { id: "timetable", images: [["timetable", 1240, 1480]] },
  { id: "worldboss", images: [["worldboss", 1240, 1480]] },
  { id: "network", images: [["network", 1240, 1080, { h: 400 }], ["network-overlay", 770, 414, { h: 200 }]] },
  { id: "hotkeys", images: [["hotkeys", 1240, 1480], ["settings", 1240, 1480]] },
  // the second picture of these two is the head of the client window with the two buttons that lead here (no frame, fading out at the bottom)
  { id: "announcements", images: [["announcements", 1400, 860, { h: 440 }], ["client-head", 1040, 300, { h: 127, head: true }]] },
  { id: "twitchdrops", images: [["twitchdrops", 1400, 860, { h: 440 }], ["client-head", 1040, 300, { h: 127, head: true }]] },
  // the last scene keeps the alternation of the sides (the one before it is flipped); the window and the overlay are rendered by the client (render-craft)
  { id: "craft", images: [["craft", 1960, 1280, { h: 400 }], ["craft-overlay", 544, 284, { h: 170 }]] },
];
// pictures offered in the designer as alternative backgrounds of a scene (images/features/alt/); the choice is saved as "bg" in feature-layout.json
const FEATURE_BG_ALTERNATIVES = { announcements: ["announcements-1", "announcements-2", "announcements-3"], twitchdrops: ["twitchdrops-1", "twitchdrops-2", "twitchdrops-3"] };
const FEATURE_FLIPPED = new Set(["modes", "bossfight", "details", "checklist", "petlevel", "worldboss", "hotkeys", "twitchdrops"]);
const FEATURE_VERSION = "20261021c";
// scenes whose generated backdrop is not made yet: no request for a picture that is not there
const FEATURE_NO_BACKDROP = new Set();

const SVG = (d, size = 30) => `<svg viewBox="0 0 24 24" width="${size}" height="${size}" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${d}</svg>`;
const FEATURE_CARDS = [
  ["meter", '<path d="M5 20V10M12 20V4M19 20v-7"/>'],
  ["modes", '<rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/>'],
  ["overlay", '<path d="m12 3 9 5-9 5-9-5zM3 13l9 5 9-5"/>'],
  ["bossfight", '<path d="M12 3 4 7v5c0 4.500 3.200 7.500 8 9 4.800-1.500 8-4.500 8-9V7z"/><path d="M12 8v4M12 15h.01"/>'],
  ["assist", '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3.500 2"/><path d="M12 3v2M3 12h2"/>'],
  ["details", '<path d="M12 3v9h9"/><path d="M20.5 15A9 9 0 1 1 9 3.5"/>'],
  ["character", '<circle cx="12" cy="8" r="4"/><path d="M4 21c1-4 4-6 8-6s7 2 8 6"/>'],
  ["checklist", '<rect x="4" y="3" width="16" height="18" rx="2"/><path d="m8 9 1.500 1.500L12 8M8 15l1.500 1.500L12 14M14 9h3M14 15h3"/>'],
  ["map", '<path d="m3 6 6-2 6 2 6-2v14l-6 2-6-2-6 2zM9 4v14M15 6v14"/>'],
  ["petlevel", '<circle cx="7" cy="9" r="2"/><circle cx="12" cy="6" r="2"/><circle cx="17" cy="9" r="2"/><path d="M12 21c-3 0-5-2-5-4.500 0-2 2-4 5-4s5 2 5 4c0 2.500-2 4.500-5 4.500z"/>'],
  ["timetable", '<rect x="3" y="5" width="18" height="16" rx="2"/><path d="M8 3v4M16 3v4M3 10h18"/>'],
  ["worldboss", '<path d="M12 3c-3 0-6 2.5-6 6v4l-2 4h5a3 3 0 0 0 6 0h5l-2-4V9c0-3.500-3-6-6-6z"/>'],
  ["network", '<path d="M2 9a15 15 0 0 1 20 0M5 12.5a10 10 0 0 1 14 0M8.5 16a5 5 0 0 1 7 0"/><path d="M12 19.5h.01"/>'],
  ["hotkeys", '<rect x="2" y="6" width="20" height="12" rx="2"/><path d="M6 10h.01M10 10h.01M14 10h.01M18 10h.01M7 14h10"/>'],
  ["announcements", '<path d="M3 11v3a1 1 0 0 0 1 1h2l4 4V6L6 10H4a1 1 0 0 0-1 1z"/><path d="M15 9a4 4 0 0 1 0 6M18 6a8 8 0 0 1 0 12"/>'],
  ["twitchdrops", '<rect x="3" y="8" width="18" height="4" rx="1"/><path d="M12 8v13M5 12v7a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-7M7.5 8a2.5 2.5 0 0 1 0-5C10 3 12 8 12 8s2-5 4.5-5a2.5 2.5 0 0 1 0 5"/>'],
  ["craft", '<path d="m14 4 6 6-3 3-6-6zM11.500 7.500 4 15l-1 5 5-1 7.500-7.500"/>'],
];
const FEATURE_TINTS = { meter: "#ff8a3d", modes: "#4fb4ff", overlay: "#ffc55a", details: "#34d3c0", sessions: "#e0a04a", character: "#ffc55a", map: "#3ee0b0", timetable: "#ffb347", worldboss: "#ff5a4f", hotkeys: "#b08cff", announcements: "#4fd1c5", twitchdrops: "#9146ff", network: "#4fb4ff", bossfight: "#ff5a4f", assist: "#6fb8ff", checklist: "#ffc55a", petlevel: "#7bd88f", craft: "#ff9a55" };
// Icons of the nine "And more" cards (shield, upload, clock, refresh, globe, code, target, broadcast, lock).
const FEATURE_MORE_ICONS = [
  '<path d="M12 3 4 6v6c0 5 3.5 8 8 9 4.5-1 8-4 8-9V6z"/><path d="m9 12 2 2 4-4"/>',
  '<path d="M12 16V4M7 9l5-5 5 5"/><path d="M4 20h16"/>',
  '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
  '<path d="M20 12a8 8 0 1 1-2.3-5.6"/><path d="M20 4v5h-5"/>',
  '<circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3c3 3 3 15 0 18M12 3c-3 3-3 15 0 18"/>',
  '<path d="m8 8-4 4 4 4M16 8l4 4-4 4M14 5l-4 14"/>',
  '<circle cx="12" cy="12" r="8"/><circle cx="12" cy="12" r="3"/><path d="M12 2v3M12 19v3M2 12h3M19 12h3"/>',
  '<rect x="3" y="5" width="18" height="11" rx="2"/><path d="M8 20h8M12 16v4"/>',
  '<rect x="5" y="11" width="14" height="9" rx="2"/><path d="M8 11V8a4 4 0 0 1 8 0v3"/>',
  '<rect x="4" y="3" width="16" height="18" rx="2"/><path d="m8 9 1.5 1.5L12 8M8 15l1.5 1.5L12 14M14 9h3M14 15h3"/>',
  '<circle cx="12" cy="12" r="9"/><circle cx="8.5" cy="10" r="1.3"/><circle cx="12" cy="7.5" r="1.3"/><circle cx="15.5" cy="10" r="1.3"/><path d="M12 21c-1.5 0-2-1.5-1-2.5s0-2.5-2-2.5"/>',
  '<rect x="2" y="6" width="20" height="12" rx="2" stroke-dasharray="3 2"/><path d="M6 10h.01M10 10h.01M14 10h.01M7 14h10"/>',
  '<path d="m12 3 8 4.500v9L12 21l-8-4.500v-9z"/><path d="M12 12v9M12 12 4 7.500M12 12l8-4.500"/>',
  '<rect x="5" y="3" width="14" height="18" rx="2"/><path d="M8 7h8M8 12h2M12 12h2M16 12h.01M8 16h2M12 16h2M16 16h.01"/>',
  '<path d="M3 17l5-6 4 3 5-8 4 5"/><path d="M3 21h18"/>',
];

// Lead sentence big, the remaining sentences as a check list (no extra translations needed).
function featureText(block) {
  const [lead, ...rest] = t(`features.${block.id}.text`).split(/(?<=[。！？])|(?<=[.!?])\s+/).map((x) => x.trim()).filter(Boolean);
  return el("div", { className: "fx-copy" }, [
    block.badge ? el("span", { className: "fx-badge", textContent: t(`features.${block.badge}`) }) : null,
    el("h2", { textContent: t(`features.${block.id}.title`) }),
    el("p", { className: "lead", textContent: lead }),
    rest.length ? el("ul", {}, rest.map((x) => el("li", { textContent: x }))) : null,
  ].filter(Boolean));
}

// Set by renderFeatures, run by route() when the page is left.
let featuresCleanup = null;

function renderFeatures() {
  setBreadcrumb([link(t("breadcrumb.home"), "/"), t("breadcrumb.features")]);
  document.title = `${t("features.title")} – ${SITE_TITLE}`;
  const scenes = FEATURE_BLOCKS.map((block, index) =>
    el("section", {
      id: `feature-${block.id}`,
      className: `fx-scene${FEATURE_FLIPPED.has(block.id) ? " flip" : ""}`,
      // The backdrop (--bg, read by .fx-bg) is set once the scene comes near the viewport, see below: all of them together are 1.6 MB.
      style: `--tint:${FEATURE_TINTS[block.id]}`,
    }, [
      el("div", { className: "fx-bg" }),
      el("div", { className: "fx-inner" }, [
        featureText(block),
        el("div", { className: "fx-stage" }, block.images.map(([name, width, height, opt], n) =>
          el("img", {
            className: `fx-win${opt?.head ? " fx-head" : ""}${opt?.flat ? " fx-flat" : ""}`,
            src: `/images/features/${name}@2x.webp?v=${FEATURE_VERSION}`,
            width, height,
            loading: index === 0 ? "eager" : "lazy",
            alt: n === 0 ? t(`features.${block.id}.title`) : "",
            // Shown height per role: the main window and the second one next to it (flat overlay pictures are smaller); style.css fine-tunes each scene.
            style: `--h:${opt?.h ?? (block.images.length === 1 ? 440 : n === 0 ? 400 : 300)}px;--r:${(width / height).toFixed(4)};--w:${Math.round(((opt?.h ?? (block.images.length === 1 ? 440 : n === 0 ? 400 : 300)) * width) / height * 1.5)}px`,
          }),
        )),
      ]),
    ]),
  );
  // Jump bar: stays at the top while scrolling; the entry of the scene in view lights up (at the very end always the last one).
  const jump = el("nav", { className: "fx-jump" }, FEATURE_CARDS.map(([id, icon]) => {
    const a = el("a", { href: `#feature-${id}` }, [el("span", { className: "fx-ico", innerHTML: SVG(icon, 16) }), t(`features.card.${id}.title`)]);
    a.dataset.id = id;
    return a;
  }));
  const links = [...jump.querySelectorAll("a")];
  const spy = () => {
    const line = window.innerHeight * 0.35;
    let current = -1;
    scenes.forEach((s, i) => { if (s.getBoundingClientRect().top <= line) current = i; });
    if (window.innerHeight + window.scrollY >= document.documentElement.scrollHeight - 8) current = scenes.length - 1;
    links.forEach((a, i) => a.classList.toggle("on", i === current));
  };
  // At most one run per frame, however many scroll events the browser sends.
  let spyFrame = 0;
  const spyLater = () => {
    if (!spyFrame) {
      spyFrame = requestAnimationFrame(() => {
        spyFrame = 0;
        spy();
      });
    }
  };
  window.addEventListener("scroll", spyLater, { passive: true });
  window.addEventListener("resize", spyLater);
  // Backdrops load when a scene is within a few screens of the viewport; without IntersectionObserver all load at once, as before.
  const loadBackdrop = (scene) => !FEATURE_NO_BACKDROP.has(scene.id.slice("feature-".length)) && scene.style.setProperty("--bg", `url(/images/features/scene-${scene.id.slice("feature-".length)}.webp?v=${FEATURE_VERSION})`);
  let backdrops = null;
  if ("IntersectionObserver" in window) {
    backdrops = new IntersectionObserver((entries) => {
      for (const entry of entries) {
        if (entry.isIntersecting) {
          loadBackdrop(entry.target);
          backdrops.unobserve(entry.target);
        }
      }
    }, { rootMargin: "1500px 0px" });
    scenes.forEach((s) => backdrops.observe(s));
  } else {
    scenes.forEach(loadBackdrop);
  }
  // The next route() calls this: the page's window listeners must not outlive it (each visit would add a pair).
  featuresCleanup = () => {
    window.removeEventListener("scroll", spyLater);
    window.removeEventListener("resize", spyLater);
    if (spyFrame) cancelAnimationFrame(spyFrame);
    backdrops?.disconnect();
  };
  jump.addEventListener("click", (e) => {
    const a = e.target.closest("a");
    if (!a) return;
    e.preventDefault();
    document.getElementById(`feature-${a.dataset.id}`)?.scrollIntoView({ behavior: "smooth", block: "start" });
    history.replaceState(history.state, "", `${location.pathname}${location.search}#feature-${a.dataset.id}`);
  });
  const more = el("div", { className: "fx-mores" }, [1, 2, 3, 4, 5, 6, 8, 9, 11, 12, 13, 14, 15].map((n) => // 7 (boss fight overlay) and 10 (checklist) have scenes of their own now
    el("div", { className: "fx-more-card" }, [
      el("span", { className: "fx-more-ico", innerHTML: SVG(FEATURE_MORE_ICONS[n - 1]) }),
      el("div", {}, [el("h3", { textContent: t(`features.more${n}.title`) }), el("p", { textContent: t(`features.more${n}.text`) })]),
    ]),
  ));
  app.replaceChildren(
    el("div", { className: "fx-page" }, [
      // The page's one heading is for search engines and screen readers; the scenes below carry the visible headings.
      el("h1", { className: "fx-sr", textContent: t("features.title") }),
      jump,
      ...scenes,
      el("section", { className: "fx-more" }, [el("h2", { textContent: t("features.more.heading") }), more]),
    ]),
  );
  spy();
  if (location.hash.startsWith("#feature-")) document.getElementById(location.hash.slice(1))?.scrollIntoView();
  // Layout designer (localhost only): /features?design drags the texts and pictures and saves them to feature-layout.json
  applyFeatureLayout(scenes).then(() => {
    if (/[?&]design\b/.test(location.search) && ["127.0.0.1", "localhost"].includes(location.hostname)) import("/feature-designer.js").then((m) => m.startDesigner(scenes));
  });
}

// feature-layout.json holds the hand-placed offsets (px at 1920 width) of each scene's text ("copy") and pictures ("win0", "win1"), written by the designer.
function setFeatureOffset(node, pos) {
  node.style.setProperty("--dx", `${pos?.[0] ?? 0}px`);
  node.style.setProperty("--dy", `${pos?.[1] ?? 0}px`);
}
async function applyFeatureLayout(scenes) {
  let layout = {};
  try {
    const res = await fetch("/feature-layout.json", { cache: "no-store" });
    if (res.ok) layout = await res.json();
  } catch { /* no saved layout yet */ }
  window.fxLayout = layout;
  window.fxBgAlternatives = FEATURE_BG_ALTERNATIVES;
  window.fxVersion = FEATURE_VERSION;
  for (const scene of scenes) {
    const saved = layout[scene.id.replace("feature-", "")];
    if (!saved) continue;
    if (saved.bg && FEATURE_BG_ALTERNATIVES[scene.id.replace("feature-", "")]?.includes(saved.bg)) scene.querySelector(".fx-bg").style.background = `url(/images/features/alt/${saved.bg}.webp?v=${FEATURE_VERSION}) center / cover no-repeat`;
    const copy = scene.querySelector(".fx-copy");
    if (copy && saved.copy) setFeatureOffset(copy, saved.copy);
    scene.querySelectorAll(".fx-win").forEach((win, n) => { if (saved[`win${n}`]) setFeatureOffset(win, saved[`win${n}`]); });
  }
}

// The download page (2026-10-26 redesign): download card, screenshot viewer and latest client changes side by side, then the five install steps and the FAQ.
// The page's own marker class dl-page is what makes the header link and the footer button step back (style.css), so only this page's button stands out.
const DOWNLOAD_VIEWS = [
  ["tabMeter", "meter"],
  ["tabOverlay", "overlay"],
  ["tabAnalysis", "analyzer"],
  ["tabSettings", "settings"],
  ["tabSessions", "sessions"],
];
const DOWNLOAD_FAQ_ICONS = [
  '<path d="M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z"/><path d="M8.5 12l2.5 2.5L15.5 10"/>',
  '<circle cx="12" cy="12" r="9"/><path d="M12 7v10M9.5 9.5c0-1 1-1.7 2.5-1.7s2.5.7 2.5 1.7-1 1.5-2.5 1.8-2.5.8-2.5 1.9 1 1.7 2.5 1.7 2.5-.7 2.5-1.7"/>',
  '<circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3c3 3 3 15 0 18M12 3c-3 3-3 15 0 18"/>',
  '<circle cx="8" cy="14" r="4"/><path d="M11 12l9-9M16 7l3 3M14 9l2 2"/>',
  '<path d="M7 18a4.5 4.5 0 010-9 6 6 0 0111.5 1.5A3.5 3.5 0 0117 18z"/><path d="M4 4l16 16"/>',
];

function svgIcon(paths, size = 22) {
  return el("span", { className: "dl-ico", innerHTML: `<svg viewBox="0 0 24 24" width="${size}" height="${size}" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${paths}</svg>` });
}

const COPY_ICON = '<rect x="9" y="9" width="11" height="11" rx="2"/><path d="M5 15V6a2 2 0 012-2h9"/>';
const CHECK_ICON = '<path d="M5 12.5l4.5 4.5L19 7.5"/>';

// Icon-only copy button: shows a check mark for two seconds after copying.
function copyButton(getText, className = "dl-copy") {
  const btn = el("button", { type: "button", className, title: t("download.copy"), "aria-label": t("download.copy") });
  const draw = (paths) => { btn.innerHTML = `<svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${paths}</svg>`; };
  draw(COPY_ICON);
  btn.addEventListener("click", async () => {
    try {
      await navigator.clipboard.writeText(getText());
      draw(CHECK_ICON);
      btn.classList.add("done");
      btn.title = t("download.copied");
      setTimeout(() => { draw(COPY_ICON); btn.classList.remove("done"); btn.title = t("download.copy"); }, 2000);
    } catch {
      // no clipboard access: the text can still be selected by hand
    }
  });
  return btn;
}

// The "?" next to the SHA-256: what it is and how to check it on Windows. Opens on hover, focus or click.
function shaHelp(installerName) {
  const code = (text) => el("div", { className: "dl-cmd" }, [el("code", { textContent: text }), copyButton(() => text)]);
  const pop = el("span", { className: "dl-pop", role: "tooltip" }, [
    el("strong", { textContent: t("download.shaHelpTitle") }),
    el("span", { textContent: t("download.shaHelpText") }),
    el("strong", { textContent: t("download.shaHowTitle") }),
    el("span", { textContent: t("download.shaHow1") }),
    el("span", { textContent: t("download.shaHow2") }),
    code(`Get-FileHash .\\${installerName} -Algorithm SHA256`),
    el("span", { textContent: t("download.shaHow3") }),
    el("span", { className: "dl-pop-alt", textContent: t("download.shaAlt") }),
    code(`certutil -hashfile ${installerName} SHA256`),
  ]);
  const q = el("button", { type: "button", className: "dl-q", textContent: "?", "aria-label": t("download.shaHelpTitle") });
  const wrap = el("span", { className: "dl-qwrap" }, [q, pop]);
  q.addEventListener("click", () => wrap.classList.toggle("open"));
  q.addEventListener("blur", () => wrap.classList.remove("open"));
  return wrap;
}

// Latest client changes, one line each: from changelog.json (German texts from changelog.de.json when the site is German, otherwise English).
async function downloadReleaseRows() {
  const german = getLocale() === "de";
  const [releases, texts] = await Promise.all([
    fetchJson("/changelog.json"),
    german ? fetchJson("/changelog.de.json").catch(() => ({})) : Promise.resolve({}),
  ]);
  const fmt = (d) => new Date(`${d}T00:00:00`).toLocaleDateString(intlTag());
  return releases
    .filter((r) => r.version !== "Unreleased")
    .map((r) => ({ r, items: r.items.filter((i) => i.areas.includes("Client")).slice(0, 2) }))
    .filter(({ items }) => items.length > 0)
    .slice(0, 4)
    .map(({ r, items }) => el("div", { className: "dl-rel" }, [
      el("div", { className: "dl-rel-head" }, [el("b", { textContent: r.version }), el("span", { textContent: fmt(r.date) })]),
      ...items.map((i) => {
        const text = texts[i.text] ?? i.text;
        return el("p", { textContent: text.charAt(0).toUpperCase() + text.slice(1), title: text });
      }),
    ]));
}

async function renderDownload() {
  setBreadcrumb([link(t("breadcrumb.home"), "/"), t("breadcrumb.download")]);
  showLoading(t("loading.version"));

  let latest = null;
  let setupAsset = null;
  let loadError = null;
  try {
    const releases = await fetchJson(`https://api.github.com/repos/${GITHUB_REPO}/releases`);
    // a tag with a suffix ("v1.0.0-alpha.1") is a test release: never offered here
    latest = releases.find((r) => !r.draft && !r.tag_name.includes("-"));
    setupAsset = latest?.assets?.find((a) => a.name.endsWith("-Setup.exe"));
  } catch (err) {
    loadError = err;
  }
  const releaseRows = await downloadReleaseRows().catch(() => []);

  // --- left: the download card ---
  const left = el("div", { className: "dl-card dl-main" });
  if (latest && setupAsset) {
    const body = latest.body ?? "";
    const sha = /SHA-256 of the installer \([^)]*\): ([0-9a-f]{64})\b/.exec(body)?.[1];
    const vt = /VirusTotal scan of the installer: (\d+\/\d+) detections - (https:\/\/www\.virustotal\.com\/gui\/file\/[0-9a-f]{64})/.exec(body);
    const published = new Date(latest.published_at).toLocaleDateString(intlTag());
    const size = (setupAsset.size / (1024 * 1024)).toLocaleString(intlTag(), { maximumFractionDigits: 1 });
    left.append(
      el("span", { className: "dl-pill", textContent: `● ${latest.tag_name} · ${published}` }),
      el("h2", {}, ["Aion DPS", el("br"), el("span", { textContent: t("download.forWindows") })]),
      el("p", { className: "dl-lead", textContent: t("download.heroDescription") }),
      el("a", { className: "dl-cta", href: "/download/latest" }, [
        el("span", { innerHTML: DOWNLOAD_CIRCLE_SVG, "aria-hidden": "true" }),
        el("span", { textContent: `${t("download.buttonLabel")} · ${size} MB` }),
      ]),
      el("div", { className: "dl-tiles" }, [
        ...(vt ? [el("a", { href: vt[2], target: "_blank", rel: "noopener", title: t("download.vtReportLink") }, [el("b", { textContent: vt[1] }), el("span", { textContent: t("download.tileVt") })])] : []),
        el("div", {}, [el("b", { textContent: "MIT" }), el("span", { textContent: t("download.tileOpen") })]),
        el("div", {}, [el("b", { textContent: String(LOCALES.length) }), el("span", { textContent: t("download.tileLanguages") })]),
      ]),
      el("ul", { className: "dl-checks" }, [1, 2, 3].map((n) => el("li", { textContent: t(`download.check${n}`) }))),
    );
    if (sha) {
      const copy = copyButton(() => sha);
      left.append(el("div", { className: "dl-sha" }, [
        el("span", { textContent: "SHA-256" }),
        shaHelp(setupAsset.name),
        el("code", { textContent: sha, title: sha }),
        copy,
      ]));
    }
  } else if (loadError) {
    left.append(el("p", { className: "error", textContent: t("download.versionLoadError", { msg: loadError.message }) }));
  } else {
    left.append(el("p", { className: "empty", textContent: t("download.noInstallerFound") }));
  }

  // --- middle: screenshot viewer ---
  const shotSrc = (name) => `/images/features/${name}@2x.webp`;
  const stageImg = el("img", { src: shotSrc(DOWNLOAD_VIEWS[0][1]), alt: t(`download.${DOWNLOAD_VIEWS[0][0]}`) });
  const tabs = [];
  const thumbs = [];
  const show = (n) => {
    const [key, name] = DOWNLOAD_VIEWS[n];
    stageImg.src = shotSrc(name);
    stageImg.alt = t(`download.${key}`);
    tabs.forEach((x, i) => x.classList.toggle("on", i === n));
    thumbs.forEach((x, i) => x.classList.toggle("on", i === n));
  };
  DOWNLOAD_VIEWS.forEach(([key, name], n) => {
    const tab = el("button", { type: "button", className: n === 0 ? "on" : "", textContent: t(`download.${key}`) });
    tab.addEventListener("click", () => show(n));
    tabs.push(tab);
    const thumb = el("button", { type: "button", className: n === 0 ? "on" : "", "aria-label": t(`download.${key}`) }, [el("img", { src: shotSrc(name), alt: "", loading: "lazy" })]);
    thumb.addEventListener("click", () => show(n));
    thumbs.push(thumb);
  });
  const middle = el("div", { className: "dl-card dl-stage" }, [
    el("div", { className: "dl-tabs" }, tabs),
    el("div", { className: "dl-view" }, [stageImg]),
    el("div", { className: "dl-thumbs" }, thumbs),
  ]);

  // --- right: latest client changes and requirements ---
  const right = el("div", { className: "dl-card dl-side" }, [
    el("h2", { textContent: t("download.releasesHeading") }),
    el("p", { className: "dl-sub", textContent: t("download.releasesSub") }),
    ...releaseRows,
    el("p", { className: "dl-all" }, [link(t("download.allChanges"), "/changelog")]),
    el("h3", { textContent: t("download.requirementsHeading") }),
    el("p", { className: "dl-sub", textContent: t("download.requirementsText") }),
  ]);

  // --- below: install steps and FAQ ---
  const steps = el("div", { className: "dl-steps" }, [1, 2, 3, 4, 5].map((n) => el("div", { className: "dl-card dl-step" }, [
    el("span", { className: "dl-num", textContent: String(n) }),
    el("div", {}, [
      el("b", { textContent: t(`download.s${n}T`) }),
      el("span", { textContent: t(`download.s${n}D`) }),
      // Npcap has no "latest" download link, so step 2 points to its download section
      ...(n === 2 ? [el("a", { className: "dl-step-link", href: "https://npcap.com/#download", target: "_blank", rel: "noopener", textContent: "npcap.com/#download ↗" })] : []),
    ]),
  ])));
  const faq = el("div", { className: "dl-faq" }, [1, 2, 3, 4, 5].map((n) => el("div", { className: "dl-card dl-faq-card", id: n === 1 ? "faq-safe" : `faq-${n}` }, [
    el("span", { className: "dl-faq-ico" }, [svgIcon(DOWNLOAD_FAQ_ICONS[n - 1])]),
    el("b", { textContent: t(`download.faq${n}Q`) }),
    el("span", { textContent: t(`download.faq${n}A`) }),
  ])));
  const note = el("p", { className: "download-meta" }, [
    t("download.vtNote") + " ",
    el("a", { href: `https://github.com/${GITHUB_REPO}/releases/latest`, target: "_blank", rel: "noopener", textContent: t("download.vtLink") }),
    " · ",
    t("download.repoLinkPrefix"),
    el("a", { href: `https://github.com/${GITHUB_REPO}`, textContent: `github.com/${GITHUB_REPO}`, target: "_blank", rel: "noopener" }),
  ]);

  app.replaceChildren(el("div", { className: "dl-page" }, [
    el("div", { className: "dl-top" }, [left, middle, right]),
    steps,
    el("h2", { className: "dl-faq-head", textContent: t("download.faqHeading") }),
    faq,
    note,
  ]));
  scrollToHash();
}

// Legions: every legion the uploads have shown. A sidebar of regions (collapsible) and servers, the larger legions as cards with
// their class mix, the small ones as a quiet list; everything in alphabetical order.
const guildUi = { q: "", server: "", closed: new Set() };
const GUILD_CLASSES = ["Gladiator", "Templar", "Assassin", "Ranger", "Sorcerer", "Spiritmaster", "Cleric", "Chanter"];

function guildRegionOf(g) {
  const cut = g.serverName.indexOf(" - ");
  return cut >= 0 ? g.serverName.slice(0, cut) : "";
}
function guildServerOf(g) {
  const cut = g.serverName.indexOf(" - ");
  return serverLabel(cut >= 0 ? g.serverName.slice(cut + 3) : g.serverName);
}
const factionDot = (faction) => el("span", { className: `faction-dot${faction === "Elyos" ? " elyos" : faction === "Asmodian" ? " asmodian" : ""}` });

function guildMixBar(g) {
  const total = GUILD_CLASSES.reduce((sum, c) => sum + (g.classes?.[c] ?? 0), 0);
  if (!total) {
    return el("span", { className: "guild-nomix", textContent: t("guild.noClasses") });
  }
  const title = GUILD_CLASSES.filter((c) => g.classes[c]).map((c) => `${classLabel(c)} ${g.classes[c]}`).join(" · ");
  return el("div", { className: "guild-mix", title, role: "img", "aria-label": title },
    GUILD_CLASSES.filter((c) => g.classes[c]).map((c) => el("i", { style: `width:${(g.classes[c] / total) * 100}%;background:${classMeta(c).color}` })));
}

async function renderGuilds() {
  setBreadcrumb([link(t("breadcrumb.home"), "/"), t("guild.listTitle")]);
  showLoading(t("loading.search"));
  const guilds = await fetchJson("/api/guilds");
  const byName = (x, y) => x.name.localeCompare(y.name, getLocale(), { sensitivity: "base", numeric: true });
  const filter = el("input", { type: "search", className: "guild-filter", placeholder: t("guild.filter"), autocomplete: "off", value: guildUi.q, "aria-label": t("guild.filter") });
  const side = el("nav", { className: "guild-servers", "aria-label": t("guild.servers") });
  const main = el("div", { className: "guild-main" });

  const serverKey = (g) => g.serverName;
  const drawSide = () => {
    const regions = new Map();
    for (const g of guilds) {
      const region = guildRegionOf(g);
      const servers = regions.get(region) ?? new Map();
      const entry = servers.get(serverKey(g)) ?? { key: serverKey(g), label: guildServerOf(g), faction: g.faction, count: 0 };
      entry.count += 1;
      servers.set(entry.key, entry);
      regions.set(region, servers);
    }
    const pick = (key, label, count, faction) => el("button", { type: "button", className: "guild-server", "aria-pressed": String(guildUi.server === key),
      onclick: () => { guildUi.server = key; drawSide(); drawMain(); } }, [factionDot(faction), el("span", { textContent: label }), el("small", { textContent: String(count) })]);
    const nodes = [pick("", t("guild.allServers"), guilds.length, "")];
    for (const [region, servers] of [...regions].sort((x, y) => (x[0] === "" ? 1 : y[0] === "" ? -1 : x[0].localeCompare(y[0])))) {
      const list = [...servers.values()].sort((x, y) => y.count - x.count || x.label.localeCompare(y.label, getLocale()));
      const open = !guildUi.closed.has(region);
      const group = el("div", { className: "guild-group-body", id: `guild-region-${region || "other"}`.replace(/\s+/g, "-"), hidden: !open }, list.map((sv) => pick(sv.key, sv.label, sv.count, sv.faction)));
      nodes.push(
        el("button", { type: "button", className: "guild-region", "aria-expanded": String(open), "aria-controls": group.id,
          onclick: () => { if (guildUi.closed.has(region)) guildUi.closed.delete(region); else guildUi.closed.add(region); drawSide(); } }, [
          el("span", { className: "chev" }),
          el("span", { textContent: region ? regionLabel(region) : t("guild.regionOther") }),
          el("small", { textContent: t("guild.regionStats", { servers: list.length, legions: list.reduce((sum, sv) => sum + sv.count, 0) }) }),
        ]),
        group,
      );
    }
    side.replaceChildren(...nodes);
  };

  const card = (g) => el("a", { className: "guild-card", href: `/legions/${g.slug}` }, [
    el("div", { className: "guild-card-top" }, [el("strong", { textContent: g.name }), el("span", { textContent: t("guild.members", { count: g.memberCount }) })]),
    el("span", { className: "guild-chip" }, [factionDot(g.faction), guildServerOf(g)]),
    guildMixBar(g),
    el("div", { className: "guild-card-foot" }, [el("span", { textContent: t("guild.avgItemLevel") }), el("b", { textContent: g.avgItemLevel ? formatNumber(g.avgItemLevel) : "–" })]),
  ]);

  const drawMain = () => {
    const q = guildUi.q.trim().toLowerCase();
    const shown = guilds.filter((g) => (!q || g.name.toLowerCase().includes(q) || serverLabel(g.serverName).toLowerCase().includes(q)) && (!guildUi.server || g.serverName === guildUi.server)).sort(byName);
    const big = shown.filter((g) => g.memberCount >= 3);
    const small = shown.filter((g) => g.memberCount < 3);
    const scope = guildUi.server ? serverLabel(guildUi.server) : t("guild.allServers");
    const legend = el("div", { className: "guild-legend" }, GUILD_CLASSES.map((c) => el("span", {}, [el("i", { style: `background:${classMeta(c).color}` }), classLabel(c)])));
    if (shown.length === 0) {
      main.replaceChildren(filter, el("p", { className: "empty", textContent: t("guild.empty") }));
      return;
    }
    main.replaceChildren(
      filter,
      el("div", { className: "guild-sec" }, [el("h3", { textContent: t("guild.bigHeading", { server: scope }) }), el("span", { textContent: t("guild.bigCount", { count: big.length }) })]),
      legend,
      big.length ? el("div", { className: "guild-cards" }, big.map(card)) : el("p", { className: "empty", textContent: t("guild.noBig") }),
      el("div", { className: "guild-sec" }, [el("h3", { textContent: t("guild.smallHeading") }), el("span", { textContent: t("guild.smallCount", { count: small.length }) })]),
      el("div", { className: "guild-small" }, small.map((g) => el("a", { href: `/legions/${g.slug}` }, [el("span", { textContent: g.name }), el("small", { textContent: `${g.memberCount} · ${guildServerOf(g)}` })]))),
    );
  };
  filter.addEventListener("input", () => {
    guildUi.q = filter.value;
    const at = filter.selectionStart;
    drawMain();
    filter.focus();
    filter.setSelectionRange(at, at);
  });
  drawSide();
  drawMain();
  app.replaceChildren(el("h2", { textContent: t("guild.listTitle") }), el("div", { className: "guild-layout" }, [side, main]));
}

async function renderGuild(slug) {
  setBreadcrumb([link(t("breadcrumb.home"), "/"), link(t("guild.listTitle"), "/legions"), slug]);
  showLoading(t("loading.search"));
  let data;
  try {
    data = await fetchJson(`/api/guilds/${encodeURIComponent(slug)}`);
  } catch {
    app.replaceChildren(el("p", { className: "empty", textContent: t("guild.notFound") }));
    return;
  }
  const { guild, members } = data;
  setBreadcrumb([link(t("breadcrumb.home"), "/"), link(t("guild.listTitle"), "/legions"), guild.name]);
  // Sortable by a click on the heading: text columns start ascending, number columns descending; a second click reverses.
  const columns = [
    { label: t("guild.player"), text: true, value: (m) => m.name },
    { label: t("guild.class"), text: true, value: (m) => (m.className ? classLabel(m.className) : "") },
    { label: t("guild.level"), value: (m) => m.level },
    { label: t("guild.gearScore"), title: t("guild.gearScoreHint"), value: (m) => m.gearScore },
    { label: t("profile.combatPowerShort"), value: (m) => m.combatPower },
    { label: t("guild.avgItemLevel"), title: t("guild.avgItemLevelHint"), value: (m) => m.avgItemLevel },
  ];
  const cells = (m) => [
    el("td", {}, [link(m.name, `/players/${m.slug ?? m.id}`), profileIcon({ ...m, hasProfile: m.profileKind != null, playerId: m.id, playerSlug: m.slug })]),
    el("td", {}, [m.className ? iconLabel(classIcon(m.className), classLabel(m.className)) : ""]),
    el("td", { textContent: m.level ?? "" }),
    el("td", { textContent: m.gearScore ? formatNumber(m.gearScore) : "–" }),
    el("td", { textContent: m.combatPower ? formatNumber(m.combatPower) : "–" }),
    el("td", { textContent: m.avgItemLevel ? formatNumber(m.avgItemLevel) : "–" }),
  ];
  let sortIndex = 2;
  let sortDesc = true;
  const tbody = el("tbody", {});
  const headers = columns.map((c, i) => el("th", { className: "sortable", textContent: c.label, title: c.title ?? "", onclick: () => {
    sortDesc = sortIndex === i ? !sortDesc : !c.text;
    sortIndex = i;
    draw();
  } }));
  function draw() {
    const { value, text } = columns[sortIndex];
    const dir = sortDesc ? -1 : 1;
    const sorted = [...members].sort((x, y) => {
      const a = value(x);
      const b = value(y);
      // Players without a value always sit at the bottom, whichever way it is sorted.
      if ((a == null || a === "") !== (b == null || b === "")) {
        return a == null || a === "" ? 1 : -1;
      }
      const diff = text ? String(a).localeCompare(String(b)) : (a ?? 0) - (b ?? 0);
      return diff * dir || x.name.localeCompare(y.name);
    });
    tbody.replaceChildren(...sorted.map((m) => el("tr", {}, cells(m))));
    headers.forEach((h, i) => {
      h.setAttribute("aria-sort", i === sortIndex ? (sortDesc ? "descending" : "ascending") : "none");
      h.dataset.arrow = i === sortIndex ? (sortDesc ? "▼" : "▲") : "";
    });
  }
  draw();
  app.replaceChildren(
    el("h2", { textContent: guild.name }),
    el("p", { className: "download-meta", textContent: `${serverLabel(guild.serverName)} · ${t("guild.members", { count: members.length })}` }),
    el("table", { className: "guild-table" }, [el("thead", {}, [el("tr", {}, headers)]), tbody]),
  );
}

// Statistics - English only like the changelog. Without a secret it is the public page (totals
// only); with the secret from the private address it also shows the operator numbers.
async function renderStats(secret) {
  setBreadcrumb([link(t("breadcrumb.home"), "/"), "Statistics"]);
  showLoading("Loading statistics…");
  const d = await fetchJson(secret ? `/api/private-stats/${encodeURIComponent(secret)}` : "/api/stats/overview");
  const nf = (n) => Number(n).toLocaleString("en-US");
  const tile = (label, value, sub) => el("div", { className: "stat-tile" }, [
    el("div", { className: "stat-value", textContent: nf(value) }),
    el("div", { className: "stat-label", textContent: label }),
    sub ? el("div", { className: "stat-sub", textContent: sub }) : null,
  ].filter(Boolean));
  const bars = (rows, labelOf, valueOf, title) => {
    const max = Math.max(1, ...rows.map(valueOf));
    return el("section", { className: "stat-block" }, [
      el("h3", { textContent: title }),
      rows.length === 0 ? el("p", { className: "empty", textContent: "No data yet." }) : el("div", { className: "stat-bars" }, rows.map((r) => el("div", { className: "stat-bar-row" }, [
        el("span", { className: "stat-bar-label" }, [labelOf(r)]),
        el("span", { className: "stat-bar-track" }, [el("span", { className: "stat-bar-fill", style: `width:${(valueOf(r) / max) * 100}%` })]),
        el("span", { className: "stat-bar-value", textContent: nf(valueOf(r)) }),
      ]))),
    ]);
  };
  // Client versions: every bar is split by whether the upload came from the newest release at that moment ("current"),
  // from an older one ("outdated") or from a build that was never released ("unknown"). Release times, not version
  // numbers, decide - the numbers are not in release order.
  const versionBars = (data) => {
    const rows = data.versions;
    const max = Math.max(1, ...rows.map((r) => r.uploads));
    const t = data.versionTotals;
    const all = Math.max(1, t.current + t.outdated + t.unknown);
    const pct = (n) => `${Math.round((n / all) * 100)} %`;
    const day = (iso) => (iso ? iso.slice(0, 10) : "not released");
    return el("section", { className: "stat-block" }, [
      el("h3", { textContent: "Client versions in use (uploads, last 30 days)" }),
      el("p", { className: "stat-sub", textContent: `Newest release at the time of the upload: ${nf(t.current)} uploads (${pct(t.current)}). Older release still in use: ${nf(t.outdated)} (${pct(t.outdated)}).${t.unknown ? ` Not a released version: ${nf(t.unknown)}.` : ""}` }),
      rows.length === 0 ? el("p", { className: "empty", textContent: "No data yet." }) : el("div", { className: "stat-bars" }, rows.map((r) => el("div", { className: "stat-bar-row" }, [
        el("span", { className: "stat-bar-label", title: `${r.version}, released ${day(r.released)}` }, [`${r.version} · ${day(r.released)} · ${r.uploaders} uploader${r.uploaders === 1 ? "" : "s"}`]),
        el("span", { className: "stat-bar-track version-track", style: `width:${(r.uploads / max) * 100}%` }, [
          r.current ? el("span", { className: "version-seg current", style: `flex:${r.current}`, title: `${r.current} with the newest release` }) : null,
          r.outdated ? el("span", { className: "version-seg outdated", style: `flex:${r.outdated}`, title: `${r.outdated} with an older release` }) : null,
          r.unknown ? el("span", { className: "version-seg unknown", style: `flex:${r.unknown}`, title: `${r.unknown} not a released version` }) : null,
        ].filter(Boolean)),
        el("span", { className: "stat-bar-value", textContent: nf(r.uploads) }),
      ]))),
      el("p", { className: "stat-sub" }, [el("span", { className: "version-key current" }), " newest release at the time   ", el("span", { className: "version-key outdated" }), " older release   ", el("span", { className: "version-key unknown" }), " not released"]),
    ]);
  };
  const out = [
    el("h2", { textContent: secret ? "Statistics (private)" : "Statistics" }),
    el("div", { className: "stat-tiles" }, [
      tile("Players known", d.players, "name + server, from uploads and group members"),
      tile("With a full profile", d.ownProfiles, "own character, uploaded by the client itself"),
      tile("Boss fights", d.encounters),
      tile("Uploads", d.uploads),
      tile("Downloads (website)", d.downloads.total, `${nf(d.downloads.last30)} in the last 30 days`),
    ]),
    bars([...d.encountersPerDay].reverse(), (r) => r.day, (r) => r.count, "Boss fights per day (last 30 days)"),
    bars(d.classes, (r) => classLabel(r.name), (r) => r.count, "Players per class"),
    // The difficulty label tells "Ruthilis of Pain (Transcendence 1)" and "Ruthilis of Pain (Transcendence 3)" apart.
    bars(d.topBosses, (r) => (r.label ? `${r.name} (${r.label})` : r.name), (r) => r.count, "Most fought bosses"),
  ];
  // Last block of the public part (on the private page the private blocks follow): it lists every server, so it is the longest one.
  out.push(
    // The server's faction icon sits before its name, as tall as the text.
    bars(d.servers, (r) => iconLabel(factionIcon(r.faction), r.unknown ? `${regionLabel(r.name)} - ${t("stats.unknownServer")}` : serverLabel(r.name)), (r) => r.count, "Players per server"),
  );
  if (secret) {
    out.push(
      el("div", { className: "stat-tiles" }, [
        tile("Active uploaders, 24 h", d.activeUploaders.d1),
        tile("Active uploaders, 7 days", d.activeUploaders.d7),
        tile("Active uploaders, 30 days", d.activeUploaders.d30),
        tile("Last upload", 0, d.lastUpload ? `${d.lastUpload} UTC` : "never"),
        tile("Database", Math.round(d.dbBytes / 1048576), "MB"),
      ]),
      bars([...d.uploadsPerDay].reverse(), (r) => `${r.day} · ${r.uploaders} uploader${r.uploaders === 1 ? "" : "s"}`, (r) => r.uploads, "Uploads per day"),
      versionBars(d),
      // How many different uploaders (= people running the client) each server has.
      bars(d.uploaderServers ?? [], (r) => iconLabel(factionIcon(r.faction), serverLabel(r.name)), (r) => r.count, "Unique uploaders per server"),
      bars([...d.downloadsPerDay].reverse(), (r) => `${r.day} · ${r.unique} unique`, (r) => r.clicks, "Downloads per day"),
      bars(d.downloadsByTag, (r) => r.tag, (r) => r.count, `Downloads per version (${nf(d.botDownloads)} bot clicks not counted)`),
      bars(d.gameServers, (r) => `${r.address} · ${r.servers || "?"}`, (r) => r.uploads, "Game servers the clients captured from (uploads)"),
      bars(d.newPlayersPerWeek, (r) => r.week, (r) => r.count, "New players per week"),
      bars(Object.entries(d.uploadStatus), (r) => r[0], (r) => r[1], "Uploads by status"),
      bars(Object.entries(d.ncMatch ?? {}), (r) => r[0], (r) => r[1], "Players checked against NC's character search"),
      bars(
        [["gear", d.ownProfileCompleteness.gear], ["skills", d.ownProfileCompleteness.skills], ["daevanion", d.ownProfileCompleteness.daevanion]],
        (r) => `${r[0]} (of ${d.ownProfileCompleteness.total} own profiles)`, (r) => r[1], "How complete the own profiles are",
      ),
    );
  }
  app.replaceChildren(...out);
}

// Server status, in the site language. Login servers and game servers are checked from the website's server by a plain TCP connect every minute
// (GET /api/server-status); the page refreshes itself. A running maintenance (read from NC's notice) turns every "online" into "maintenance",
// because the servers still accept connections while nobody can log in. The game addresses are split by region (read from the server ids of the
// players who connected to them) and shown as one tile per address, grouped by network block.
const STATUS_REGIONS = ["eu", "latam", "nae", "naw", "asia"];
let statusTimer = null;
let statusRegion = null; // the tab the visitor picked survives the refresh
async function renderStatus() {
  setBreadcrumb([link(t("breadcrumb.home"), "/"), t("status.title")]);
  showLoading(t("status.loading"));
  if (statusTimer) {
    clearInterval(statusTimer);
  }
  const draw = async () => {
    const d = await fetchJson("/api/server-status");
    const intl = intlTag();
    const label = { up: t("status.up"), down: t("status.down"), unknown: t("status.unknown"), maintenance: t("status.maintenance") };
    const ago = (iso) => {
      if (!iso) {
        return t("status.notChecked");
      }
      const sec = Math.max(0, Math.round((Date.now() - Date.parse(iso)) / 1000));
      return sec < 90 ? t("status.secAgo", { n: sec }) : t("status.minAgo", { n: Math.round(sec / 60) });
    };
    const span = (ms) => {
      const min = Math.max(1, Math.round(ms / 60000));
      return min < 90 ? t("status.minutes", { n: min }) : t("status.hours", { n: (Math.round(min / 6) / 10).toLocaleString(getLocale()) });
    };
    const clock = new Intl.DateTimeFormat(intl, { weekday: "short", day: "numeric", month: "numeric", hour: "2-digit", minute: "2-digit", hourCycle: "h23" });
    const hhmm = new Intl.DateTimeFormat(intl, { hour: "2-digit", minute: "2-digit", hourCycle: "h23" });
    const pct = (v) => (v === null ? "–" : `${v.toLocaleString(getLocale())} %`);
    const reached = (x) => x.state === "up" || x.state === "maintenance";
    const worst = (list) => (["down", "maintenance", "unknown", "up"].find((s) => list.some((x) => x.state === s)) ?? "unknown");
    const mean = (list, f) => {
      const v = list.map(f).filter((x) => x !== null && x !== undefined);
      return v.length ? v.reduce((a, b) => a + b, 0) / v.length : null;
    };
    const login = d.servers.filter((x) => x.kind === "login");
    const game = d.servers.filter((x) => x.kind === "game");

    // Banner for a running or announced maintenance, on top of everything.
    const m = d.maintenance;
    const banner = m
      ? el("a", { className: `status-banner ${m.state}`, href: m.url, target: "_blank", rel: "noopener" }, [
          el("i", { className: "status-dot maintenance" }),
          el("strong", { textContent: m.state === "active" ? t("status.maintActiveUntil", { time: hhmm.format(m.end) }) : t("status.maintScheduled") }),
          el("span", { textContent: m.state === "active" ? t("status.maintActiveText") : t("status.maintFrom", { time: clock.format(m.start), left: span(m.start - Date.now()), len: span(m.end - m.start) }) }),
          el("em", { textContent: m.state === "active" ? t("status.maintLeft", { start: hhmm.format(m.start), left: span(m.end - Date.now()) }) : t("status.maintNotice") }),
        ])
      : null;

    // The four figures.
    const tile = (title, state, big, sub, extra) => el("div", { className: `status-kpi ${state ?? ""}` }, [
      el("small", {}, [...(state ? [el("i", { className: `status-dot ${state}` })] : []), title]),
      el("b", { textContent: big }),
      el("span", { textContent: sub }),
      ...(extra ? [extra] : []),
    ]);
    const upAll = game.filter(reached).length;
    const meter = el("div", { className: `status-meter ${worst(game)}` }, [el("span", { style: `width:${game.length ? Math.round((upAll / game.length) * 100) : 0}%` })]);
    const loginSv = login[0];
    const msAll = mean(game, (x) => x.latencyMs);
    const kpis = el("div", { className: "status-kpis" }, [
      loginSv
        ? tile(t("status.loginAll"), worst(login), label[worst(login)], loginSv.latencyMs === null ? ago(loginSv.checkedAt) : `${loginSv.latencyMs} ms · ${pct(loginSv.uptime24h)} ${t("status.in24")}`)
        : tile(t("status.loginAll"), "unknown", label.unknown, t("status.loginUnknown")),
      game.length ? tile(t("status.gameTotal"), worst(game), `${upAll} / ${game.length}`, t("status.reachableIn", { n: new Set(game.map((x) => x.region)).size }), meter) : tile(t("status.game"), "unknown", label.unknown, t("status.gameNone")),
      tile(t("status.colResponse"), null, msAll === null ? "–" : `${Math.round(msAll)} ms`, t("status.avgAll")),
      tile(t("status.colUptime"), null, pct(mean(game, (x) => x.uptime24h) === null ? null : Math.round(mean(game, (x) => x.uptime24h) * 10) / 10), m?.state === "active" ? t("status.avgMinusMaint") : t("status.avgAll")),
    ]);

    // Region tabs: the five regions in a fixed order, then the addresses whose region is not known.
    const keys = [...STATUS_REGIONS.filter((r) => game.some((x) => x.region === r)), ...(game.some((x) => !x.region) ? ["unknown"] : [])];
    const inRegion = (k) => game.filter((x) => (x.region ?? "unknown") === k);
    if (!keys.includes(statusRegion)) {
      statusRegion = keys[0] ?? null;
    }
    const regionName = (k) => t(`status.region.${k}`);
    const tabs = el("div", { className: "status-tabs" }, keys.map((k) => {
      const list = inRegion(k);
      return el("button", { type: "button", className: `status-tab ${worst(list)}${k === statusRegion ? " on" : ""}`, onclick: () => { statusRegion = k; draw().catch(() => undefined); } }, [
        el("b", {}, [el("i", { className: `status-dot ${worst(list)}` }), regionName(k)]),
        el("small", { textContent: t("status.tabCount", { up: list.filter(reached).length, total: list.length, ms: Math.round(mean(list, (x) => x.latencyMs) ?? 0) }) }),
      ]);
    }));

    // The chosen region: one tile per address, grouped by network block.
    const blocks = new Map();
    for (const sv of inRegion(statusRegion)) {
      const key = sv.address.split(".").slice(0, 3).join(".");
      blocks.set(key, [...(blocks.get(key) ?? []), sv]);
    }
    const panel = statusRegion
      ? el("section", { className: "status-panel" }, [
          el("div", { className: "status-panel-head" }, [
            el("h3", { textContent: regionName(statusRegion) }),
            el("span", { textContent: t("status.panelCount", { n: inRegion(statusRegion).length, blocks: blocks.size }) }),
          ]),
          ...[...blocks.entries()].sort((a, b) => a[0].localeCompare(b[0], "en", { numeric: true })).flatMap(([key, list]) => {
            const ok = list.filter(reached).length;
            return [
              el("div", { className: "status-sec" }, [
                el("strong", { textContent: `${key}.x` }),
                el("small", { textContent: t("status.blockCount", { up: ok, total: list.length }) }),
                el("div", { className: `status-meter ${worst(list)}` }, [el("span", { style: `width:${Math.round((ok / list.length) * 100)}%` })]),
              ]),
              el("div", { className: "status-tiles" }, list.map((sv) => el("div", {
                className: `status-tile ${sv.state}`,
                title: `${sv.address} · ${label[sv.state]} · ${t("status.colUptime")} ${pct(sv.uptime24h)} · ${ago(sv.checkedAt)}${sv.behavior ? ` · ${t(`status.behavior.${sv.behavior}`)}` : ""}`,
              }, [
                el("b", { textContent: `.${sv.address.split(":")[0].split(".")[3]}` }),
                el("small", { textContent: sv.latencyMs === null ? label[sv.state] : `${sv.latencyMs} ms` }),
              ]))),
            ];
          }),
        ])
      : el("p", { className: "empty", textContent: t("status.gameNone") });

    app.replaceChildren(
      el("h2", { textContent: t("status.title") }),
      ...(banner ? [banner] : []),
      kpis,
      tabs,
      panel,
      el("div", { className: "status-legend" }, [
        ...["up", "maintenance", "down"].map((s) => el("span", {}, [el("i", { className: `status-dot ${s}` }), label[s]])),
        el("span", { textContent: t("status.legendTile") }),
      ]),
      el("p", { className: "stat-sub", textContent: t("status.intro", { ago: ago(d.checkedAt) }) }),
      el("p", { className: "stat-sub", textContent: t("status.gameNote") }),
    );
  };
  await draw();
  statusTimer = setInterval(() => {
    if (location.pathname !== "/status") {
      clearInterval(statusTimer);
      statusTimer = null;
      return;
    }
    draw().catch(() => undefined);
  }, 30_000);
}

// AION 2 announcements: NCSoft's official notices in the site language (English where NC has no board in it), each with a colored label for its kind
// and a green one once the title says completed / resolved. The list comes from GET /api/announcements; patchnotes are not part of it.
const ANN_CATEGORIES = ["maintenance", "outage", "newServers", "serverTransfer", "compensation", "account", "twitchDrops", "knownIssues", "other"];
async function renderAnnouncements() {
  setBreadcrumb([link(t("breadcrumb.home"), "/"), t("ann.title")]);
  showLoading(t("ann.loading"));
  const d = await fetchJson(`/api/announcements?lang=${encodeURIComponent(getLocale())}`);
  const labelOf = (category) => t(`ann.cat.${ANN_CATEGORIES.includes(category) ? category : "other"}`);
  const intl = intlTag();
  const dayOf = (iso) => new Date(iso).toLocaleDateString("sv-SE"); // local calendar day, sortable
  const dayTitle = new Intl.DateTimeFormat(intl, { weekday: "long", day: "numeric", month: "long", year: "numeric" });
  const card = (x) => el("article", { className: `ann-item cat-${ANN_CATEGORIES.includes(x.category) ? x.category : "other"}` }, [
    el("div", { className: "ann-head" }, [
      el("span", { className: "ann-label", textContent: labelOf(x.category) }),
      ...(x.status === "done" ? [el("span", { className: "ann-label done", textContent: t("ann.done") })] : []),
      el("time", { className: "ann-date", dateTime: x.postedAt, textContent: new Date(x.postedAt).toLocaleTimeString(intl, { hour: "2-digit", minute: "2-digit", hourCycle: "h23" }) }),
    ]),
    el("h3", { className: "ann-title" }, [el("a", { href: x.url, target: "_blank", rel: "noopener", textContent: x.title })]),
    // account notices carry the number of restricted accounts instead of NC's generic opening lines
    ...(typeof x.restricted === "number"
      ? [el("p", { className: "ann-restricted" }, [
        el("strong", { textContent: x.restricted.toLocaleString(intl) }),
        el("span", { textContent: t("ann.restricted") }),
      ])]
      : x.summary ? [el("p", { className: "ann-summary", textContent: x.summary })] : []),
    el("a", { className: "ann-more", href: x.url, target: "_blank", rel: "noopener", textContent: t("ann.read") }),
  ]);
  // One heading per calendar day (the list is newest first), the day's notices in a grid below it.
  const days = [];
  for (const x of d.items) {
    const key = dayOf(x.postedAt);
    if (!days.length || days[days.length - 1].key !== key) {
      days.push({ key, date: new Date(x.postedAt), list: [] });
    }
    days[days.length - 1].list.push(x);
  }
  // The visitor picks the kinds to show (one checkbox per kind, remembered in this browser); the days without a visible notice disappear.
  const STORE_KEY = "dpsmeter.annHidden";
  let hidden = new Set();
  try {
    const stored = JSON.parse(localStorage.getItem(STORE_KEY) ?? "[]");
    hidden = new Set(Array.isArray(stored) ? stored.filter((c) => ANN_CATEGORIES.includes(c)) : []);
  } catch {
    // storage blocked or damaged - everything shown
  }
  const catOf = (x) => (ANN_CATEGORIES.includes(x.category) ? x.category : "other");
  const counts = new Map();
  for (const x of d.items) {
    counts.set(catOf(x), (counts.get(catOf(x)) ?? 0) + 1);
  }
  const present = ANN_CATEGORIES.filter((c) => counts.has(c));
  const daysBox = el("div", { className: "ann-days" });
  const paint = () => {
    const sections = days
      .map((day) => ({ day, list: day.list.filter((x) => !hidden.has(catOf(x))) }))
      .filter((g) => g.list.length > 0)
      .map((g) => el("section", { className: "ann-day" }, [
        el("h3", { className: "ann-day-title", textContent: dayTitle.format(g.day.date) }),
        el("div", { className: "ann-list" }, g.list.map(card)),
      ]));
    daysBox.replaceChildren(...(sections.length ? sections : [el("p", { className: "empty", textContent: t(d.items.length ? "ann.filterEmpty" : "ann.empty") })]));
  };
  const save = () => {
    try {
      localStorage.setItem(STORE_KEY, JSON.stringify([...hidden]));
    } catch {
      // the choice just does not survive the visit
    }
  };
  const boxes = present.map((c) => {
    const input = el("input", { type: "checkbox", checked: !hidden.has(c), onchange: () => {
      if (input.checked) {
        hidden.delete(c);
      } else {
        hidden.add(c);
      }
      save();
      paint();
    } });
    return { c, input, node: el("label", { className: `ann-filter cat-${c}` }, [input, el("span", { textContent: `${labelOf(c)} (${counts.get(c)})` })]) };
  });
  const setAll = (on) => {
    hidden = on ? new Set() : new Set(present);
    boxes.forEach((b) => { b.input.checked = on; });
    save();
    paint();
  };
  const filterBar = el("div", { className: "ann-filters", role: "group", "aria-label": t("ann.filter") }, [
    ...boxes.map((b) => b.node),
    el("button", { type: "button", className: "ann-filter-all", textContent: t("ann.filterAll"), onclick: () => setAll(true) }),
    el("button", { type: "button", className: "ann-filter-all", textContent: t("ann.filterNone"), onclick: () => setAll(false) }),
  ]);
  paint();
  app.replaceChildren(
    el("h2", { textContent: t("ann.title") }),
    el("p", { className: "stat-sub", textContent: t("ann.intro") }),
    ...(d.lang !== getLocale() ? [el("p", { className: "stat-sub", textContent: t("ann.fallback") })] : []),
    ...(d.items.length ? [filterBar] : []),
    daysBox,
  );
}

// AION 2 patch notes: the only place with NC's full text (GET /api/patchnotes answers a small tree of paragraphs, bold/italic text, lists, tables and https links,
// built here with createElement - NC's markup never reaches the page). The newest note is open, older ones fold; every note links to NC's own page.
const PN_TAGS = { p: "p", strong: "strong", em: "em", u: "u", ul: "ul", ol: "ol", li: "li", br: "br", h: "h4", a: "a", table: "table", tr: "tr", td: "td", th: "th" };
function patchnoteNodes(nodes) {
  return nodes.map((n) => {
    if (typeof n === "string") {
      return document.createTextNode(n);
    }
    const tag = PN_TAGS[n.tag] ?? "span";
    const props = n.tag === "a" && typeof n.href === "string" && n.href.startsWith("https://") ? { href: n.href, target: "_blank", rel: "noopener" } : {};
    if (n.align === "center" || n.align === "right") {
      props.className = `pn-${n.align}`;
    }
    if (n.tag === "td" || n.tag === "th") {
      if (Number.isInteger(n.rowspan) && n.rowspan > 1) {
        props.rowSpan = n.rowspan;
      }
      if (Number.isInteger(n.colspan) && n.colspan > 1) {
        props.colSpan = n.colspan;
      }
    }
    const node = el(tag, props, tag === "br" ? [] : patchnoteNodes(n.children ?? []));
    return n.tag === "table" ? el("div", { className: "pn-table" }, [node]) : node;
  });
}
async function renderPatchnotes() {
  setBreadcrumb([link(t("breadcrumb.home"), "/"), t("pn.title")]);
  showLoading(t("pn.loading"));
  const d = await fetchJson(`/api/patchnotes?lang=${encodeURIComponent(getLocale())}`);
  const notes = d.items.map((x, i) => el("details", { className: "pn-item", open: i === 0 }, [
    el("summary", {}, [
      el("span", { className: "pn-title", textContent: x.title }),
      el("time", { className: "ann-date", dateTime: x.postedAt, textContent: formatDate(new Date(x.postedAt)) }),
    ]),
    el("div", { className: "pn-body" }, patchnoteNodes(x.body)),
    el("a", { className: "ann-more", href: x.url, target: "_blank", rel: "noopener", textContent: t("pn.read") }),
  ]));
  app.replaceChildren(
    el("h2", { textContent: t("pn.title") }),
    el("p", { className: "stat-sub", textContent: t("pn.intro") }),
    ...(d.lang !== getLocale() ? [el("p", { className: "stat-sub", textContent: t("ann.fallback") })] : []),
    notes.length ? el("div", { className: "ann-list" }, notes) : el("p", { className: "empty", textContent: t("pn.empty") }),
  );
}

// Twitch Drops - English, plus German when the site language is German, like the changelog. Twitch has no public list of drop
// campaigns, so the backend reads them from NC's announcements (tables of watch time, reward and quantity) and adds the channels
// that are live with Drops right now from Twitch's public streams list. Reward names come from the game client's texts
// (twitch-drops-items.json, in the eight client languages); an item the table does not know yet is shown with NC's English name.
const TWITCH_DROPS_TEXT = {
  en: {
    loading: "Loading Twitch Drops…", title: "Twitch Drops",
    intro: "Watch AION 2 streams on Twitch that have Drops enabled and earn in-game rewards. The campaigns are run by NC; the rewards arrive at the NC account your Twitch account is linked to.",
    live: "Running now", upcoming: "Upcoming", ended: "Ended",
    endsIn: (d, h) => `Ends in ${d} d ${h} h`, startsIn: (d, h) => `Starts in ${d} d ${h} h`,
    period: "Runs", days: "Days (Pacific time)", dateTbd: "Start and end date follow", part: (n) => `Part ${n}`,
    min: "min", hour: "h", total: (h) => `${h} h in total`,
    liveNow: (s, v) => `Live right now with Drops: ${s} streams · ${v} viewers`, liveNone: "No stream with Drops is live right now.",
    claimBy: "Claim your rewards in the Drops Inventory by",
    howTitle: "How it works",
    how: ["Link your Twitch account to your NC account (My Page > Manage Login on the NC website).", "Watch a live stream in the AION 2 category with the “Drops Enabled” tag. Progress counts on every participating channel, but only one stream at a time.", "Open your Drops Inventory on Twitch and claim each reward when it is ready. A missed claim stays possible until the claim deadline."],
    globalOnly: "Rewards are for AION 2 Global server players; the Korea and Taiwan servers are not eligible. If a claimed reward does not arrive, contact NC Support.",
    watchLink: "AION 2 on Twitch", inventoryLink: "Drops Inventory", linkAccount: "Link accounts",
    pastTitle: "Earlier campaigns", empty: "No campaigns found yet.",
    footer: "The campaigns are read from NC's announcements, the live channels from Twitch. Last checked:",
  },
  de: {
    loading: "Twitch Drops werden geladen…", title: "Twitch Drops",
    intro: "Schau AION-2-Streams auf Twitch mit aktivierten Drops und verdiene Belohnungen im Spiel. Die Kampagnen veranstaltet NC; die Belohnungen kommen auf dem NC-Account an, mit dem dein Twitch-Account verknüpft ist.",
    live: "Läuft gerade", upcoming: "Kommt bald", ended: "Beendet",
    endsIn: (d, h) => `Endet in ${d} T ${h} Std.`, startsIn: (d, h) => `Startet in ${d} T ${h} Std.`,
    period: "Zeitraum", days: "Tage (Pazifikzeit)", dateTbd: "Start und Ende folgen", part: (n) => `Teil ${n}`,
    min: "Min.", hour: "Std.", total: (h) => `${h} Std. insgesamt`,
    liveNow: (s, v) => `Gerade live mit Drops: ${s} Streams · ${v} Zuschauer`, liveNone: "Gerade ist kein Stream mit Drops live.",
    claimBy: "Belohnungen im Drops-Inventar einlösen bis",
    howTitle: "So funktioniert es",
    how: ["Verknüpfe deinen Twitch-Account mit deinem NC-Account (Meine Seite > Login verwalten auf der NC-Webseite).", "Schau einen Live-Stream in der Kategorie AION 2 mit dem Tag „Drops aktiviert“. Der Fortschritt zählt auf jedem teilnehmenden Kanal, aber immer nur in einem Stream gleichzeitig.", "Öffne dein Drops-Inventar auf Twitch und löse jede Belohnung ein, sobald sie bereit ist. Ein verpasstes Einlösen geht bis zur Einlösefrist noch."],
    globalOnly: "Die Belohnungen gibt es nur für Spieler des AION-2-Global-Servers; die Server in Korea und Taiwan sind ausgeschlossen. Kommt ein eingelöster Drop nicht an, wende dich an den NC-Support.",
    watchLink: "AION 2 auf Twitch", inventoryLink: "Drops-Inventar", linkAccount: "Accounts verknüpfen",
    pastTitle: "Frühere Kampagnen", empty: "Noch keine Kampagnen gefunden.",
    footer: "Die Kampagnen werden aus den Ankündigungen von NC gelesen, die Live-Kanäle von Twitch. Zuletzt geprüft:",
  },
  fr: {
    loading: "Chargement des Twitch Drops…", title: "Twitch Drops",
    intro: "Regardez des streams AION 2 sur Twitch avec les Drops activés et gagnez des récompenses en jeu. Les campagnes sont organisées par NC ; les récompenses arrivent sur le compte NC lié à votre compte Twitch.",
    live: "En cours", upcoming: "À venir", ended: "Terminée",
    endsIn: (d, h) => `Se termine dans ${d} j ${h} h`, startsIn: (d, h) => `Commence dans ${d} j ${h} h`,
    period: "Période", days: "Jours (heure du Pacifique)", dateTbd: "Dates de début et de fin à venir", part: (n) => `Partie ${n}`,
    min: "min", hour: "h", total: (h) => `${h} h au total`,
    liveNow: (s, v) => `En direct avec Drops : ${s} streams · ${v} spectateurs`, liveNone: "Aucun stream avec Drops n'est en direct pour le moment.",
    claimBy: "Récupérez vos récompenses dans l'inventaire des Drops avant le",
    howTitle: "Comment ça marche",
    how: ["Liez votre compte Twitch à votre compte NC (My Page > Manage Login sur le site de NC).", "Regardez un stream en direct dans la catégorie AION 2 avec le tag « Drops activés ». La progression compte sur chaque chaîne participante, mais dans un seul stream à la fois.", "Ouvrez votre inventaire des Drops sur Twitch et récupérez chaque récompense dès qu'elle est prête. Une récompense manquée reste récupérable jusqu'à la date limite."],
    globalOnly: "Les récompenses sont réservées aux joueurs du serveur Global d'AION 2 ; les serveurs de Corée et de Taïwan ne sont pas concernés. Si une récompense récupérée n'arrive pas, contactez le support NC.",
    watchLink: "AION 2 sur Twitch", inventoryLink: "Inventaire des Drops", linkAccount: "Lier les comptes",
    pastTitle: "Campagnes précédentes", empty: "Aucune campagne trouvée pour le moment.",
    footer: "Les campagnes sont lues dans les annonces de NC, les chaînes en direct sur Twitch. Dernière vérification :",
  },
  es: {
    loading: "Cargando Twitch Drops…", title: "Twitch Drops",
    intro: "Mira streams de AION 2 en Twitch con los Drops activados y consigue recompensas en el juego. Las campañas las organiza NC; las recompensas llegan a la cuenta de NC vinculada a tu cuenta de Twitch.",
    live: "En curso", upcoming: "Próximamente", ended: "Finalizada",
    endsIn: (d, h) => `Termina en ${d} d ${h} h`, startsIn: (d, h) => `Empieza en ${d} d ${h} h`,
    period: "Periodo", days: "Días (hora del Pacífico)", dateTbd: "Las fechas de inicio y fin se anunciarán", part: (n) => `Parte ${n}`,
    min: "min", hour: "h", total: (h) => `${h} h en total`,
    liveNow: (s, v) => `En directo con Drops: ${s} streams · ${v} espectadores`, liveNone: "Ahora mismo no hay ningún stream con Drops en directo.",
    claimBy: "Reclama tus recompensas en el inventario de Drops antes del",
    howTitle: "Cómo funciona",
    how: ["Vincula tu cuenta de Twitch con tu cuenta de NC (My Page > Manage Login en la web de NC).", "Mira un stream en directo de la categoría AION 2 con la etiqueta «Drops activados». El progreso cuenta en cada canal participante, pero solo en un stream a la vez.", "Abre tu inventario de Drops en Twitch y reclama cada recompensa cuando esté lista. Una recompensa no reclamada se puede reclamar hasta la fecha límite."],
    globalOnly: "Las recompensas son para jugadores del servidor Global de AION 2; los servidores de Corea y Taiwán quedan excluidos. Si una recompensa reclamada no llega, contacta con el soporte de NC.",
    watchLink: "AION 2 en Twitch", inventoryLink: "Inventario de Drops", linkAccount: "Vincular cuentas",
    pastTitle: "Campañas anteriores", empty: "Todavía no se han encontrado campañas.",
    footer: "Las campañas se leen de los anuncios de NC y los canales en directo, de Twitch. Última comprobación:",
  },
  pt: {
    loading: "Carregando Twitch Drops…", title: "Twitch Drops",
    intro: "Assista a streams de AION 2 na Twitch com Drops ativados e ganhe recompensas no jogo. As campanhas são da NC; as recompensas chegam à conta NC vinculada à sua conta Twitch.",
    live: "Em andamento", upcoming: "Em breve", ended: "Encerrada",
    endsIn: (d, h) => `Termina em ${d} d ${h} h`, startsIn: (d, h) => `Começa em ${d} d ${h} h`,
    period: "Período", days: "Dias (horário do Pacífico)", dateTbd: "Datas de início e término serão divulgadas", part: (n) => `Parte ${n}`,
    min: "min", hour: "h", total: (h) => `${h} h no total`,
    liveNow: (s, v) => `Ao vivo com Drops: ${s} streams · ${v} espectadores`, liveNone: "Nenhum stream com Drops está ao vivo agora.",
    claimBy: "Resgate suas recompensas no inventário de Drops até",
    howTitle: "Como funciona",
    how: ["Vincule sua conta Twitch à sua conta NC (My Page > Manage Login no site da NC).", "Assista a um stream ao vivo na categoria AION 2 com a tag “Drops ativados”. O progresso conta em todos os canais participantes, mas em um stream por vez.", "Abra seu inventário de Drops na Twitch e resgate cada recompensa quando estiver pronta. Uma recompensa não resgatada pode ser resgatada até o prazo final."],
    globalOnly: "As recompensas são para jogadores do servidor Global de AION 2; os servidores da Coreia e de Taiwan não são elegíveis. Se uma recompensa resgatada não chegar, entre em contato com o suporte da NC.",
    watchLink: "AION 2 na Twitch", inventoryLink: "Inventário de Drops", linkAccount: "Vincular contas",
    pastTitle: "Campanhas anteriores", empty: "Nenhuma campanha encontrada ainda.",
    footer: "As campanhas são lidas dos anúncios da NC e os canais ao vivo, da Twitch. Última verificação:",
  },
  ru: {
    loading: "Загрузка Twitch Drops…", title: "Twitch Drops",
    intro: "Смотрите стримы AION 2 на Twitch с включёнными дропсами и получайте награды в игре. Кампании проводит NC; награды приходят на аккаунт NC, привязанный к вашему аккаунту Twitch.",
    live: "Идёт сейчас", upcoming: "Скоро", ended: "Завершена",
    endsIn: (d, h) => `Закончится через ${d} д ${h} ч`, startsIn: (d, h) => `Начнётся через ${d} д ${h} ч`,
    period: "Период", days: "Дни (тихоокеанское время)", dateTbd: "Даты начала и окончания будут объявлены", part: (n) => `Часть ${n}`,
    min: "мин", hour: "ч", total: (h) => `${h} ч всего`,
    liveNow: (s, v) => `Сейчас в эфире с дропсами: ${s} стримов · ${v} зрителей`, liveNone: "Сейчас нет стримов с дропсами.",
    claimBy: "Получите награды в инвентаре дропсов до",
    howTitle: "Как это работает",
    how: ["Привяжите аккаунт Twitch к аккаунту NC (My Page > Manage Login на сайте NC).", "Смотрите стрим в категории AION 2 с тегом «Дропсы включены». Прогресс засчитывается на любом участвующем канале, но только в одном стриме одновременно.", "Откройте инвентарь дропсов на Twitch и получите каждую награду, когда она будет готова. Пропущенную награду можно получить до конца срока."],
    globalOnly: "Награды доступны игрокам глобального сервера AION 2; серверы Кореи и Тайваня не участвуют. Если полученная награда не пришла, обратитесь в поддержку NC.",
    watchLink: "AION 2 на Twitch", inventoryLink: "Инвентарь дропсов", linkAccount: "Привязать аккаунты",
    pastTitle: "Прошлые кампании", empty: "Кампании пока не найдены.",
    footer: "Кампании берутся из объявлений NC, прямые эфиры — с Twitch. Последняя проверка:",
  },
  ja: {
    loading: "Twitchドロップを読み込み中…", title: "Twitchドロップ",
    intro: "ドロップ有効のAION 2の配信をTwitchで視聴して、ゲーム内報酬を獲得しましょう。キャンペーンはNCが実施し、報酬はTwitchアカウントと連携したNCアカウントに届きます。",
    live: "開催中", upcoming: "近日開催", ended: "終了",
    endsIn: (d, h) => `終了まで${d}日${h}時間`, startsIn: (d, h) => `開始まで${d}日${h}時間`,
    period: "期間", days: "日付（太平洋時間）", dateTbd: "開始日・終了日は後日発表", part: (n) => `第${n}部`,
    min: "分", hour: "時間", total: (h) => `合計${h}時間`,
    liveNow: (s, v) => `ドロップ付きで配信中: ${s}配信 · 視聴者${v}人`, liveNone: "現在ドロップ付きの配信はありません。",
    claimBy: "ドロップのインベントリで報酬を受け取る期限:",
    howTitle: "受け取り方",
    how: ["TwitchアカウントをNCアカウントと連携します（NC公式サイトの My Page > Manage Login）。", "「ドロップ有効」タグ付きのAION 2カテゴリの配信を視聴します。進行状況は対象のどのチャンネルでも加算されますが、同時に視聴できるのは1配信のみです。", "Twitchのドロップのインベントリを開き、準備ができた報酬を受け取ります。受け取り忘れも受け取り期限までは受け取れます。"],
    globalOnly: "報酬はAION 2グローバルサーバーのプレイヤー向けです。韓国・台湾サーバーは対象外です。受け取った報酬が届かない場合はNCサポートにお問い合わせください。",
    watchLink: "TwitchでAION 2", inventoryLink: "ドロップのインベントリ", linkAccount: "アカウント連携",
    pastTitle: "過去のキャンペーン", empty: "キャンペーンはまだ見つかっていません。",
    footer: "キャンペーンはNCのお知らせから、配信中のチャンネルはTwitchから取得しています。最終確認:",
  },
  ko: {
    loading: "트위치 드롭 불러오는 중…", title: "트위치 드롭",
    intro: "드롭이 활성화된 AION 2 방송을 트위치에서 시청하고 게임 내 보상을 받으세요. 캠페인은 NC가 진행하며, 보상은 트위치 계정과 연동된 NC 계정으로 지급됩니다.",
    live: "진행 중", upcoming: "예정", ended: "종료",
    endsIn: (d, h) => `종료까지 ${d}일 ${h}시간`, startsIn: (d, h) => `시작까지 ${d}일 ${h}시간`,
    period: "기간", days: "날짜 (태평양 시간)", dateTbd: "시작일과 종료일은 추후 공지", part: (n) => `${n}부`,
    min: "분", hour: "시간", total: (h) => `총 ${h}시간`,
    liveNow: (s, v) => `드롭 방송 중: ${s}개 방송 · 시청자 ${v}명`, liveNone: "현재 드롭이 있는 방송이 없습니다.",
    claimBy: "드롭 인벤토리에서 보상을 받을 수 있는 기한:",
    howTitle: "이용 방법",
    how: ["트위치 계정을 NC 계정과 연동하세요 (NC 웹사이트의 My Page > Manage Login).", "“드롭 활성화” 태그가 있는 AION 2 카테고리 방송을 시청하세요. 진행도는 참여 채널 어디서나 쌓이지만 동시에 한 방송만 인정됩니다.", "트위치 드롭 인벤토리를 열어 준비된 보상을 받으세요. 놓친 보상도 수령 기한까지는 받을 수 있습니다."],
    globalOnly: "보상은 AION 2 글로벌 서버 플레이어 대상이며, 한국·대만 서버는 해당되지 않습니다. 받은 보상이 도착하지 않으면 NC 고객지원에 문의하세요.",
    watchLink: "트위치에서 AION 2", inventoryLink: "드롭 인벤토리", linkAccount: "계정 연동",
    pastTitle: "지난 캠페인", empty: "아직 캠페인을 찾지 못했습니다.",
    footer: "캠페인은 NC 공지에서, 방송 중인 채널은 트위치에서 가져옵니다. 마지막 확인:",
  },
  zh: {
    loading: "正在加载 Twitch 掉落…", title: "Twitch 掉落",
    intro: "在 Twitch 观看已开启掉落的 AION 2 直播，即可获得游戏内奖励。活动由 NC 举办，奖励会发放到与你的 Twitch 账号绑定的 NC 账号。",
    live: "进行中", upcoming: "即将开始", ended: "已结束",
    endsIn: (d, h) => `还剩 ${d} 天 ${h} 小时结束`, startsIn: (d, h) => `${d} 天 ${h} 小时后开始`,
    period: "时间", days: "日期（太平洋时间）", dateTbd: "开始和结束日期待公布", part: (n) => `第 ${n} 部分`,
    min: "分钟", hour: "小时", total: (h) => `共 ${h} 小时`,
    liveNow: (s, v) => `当前有掉落的直播：${s} 个 · ${v} 位观众`, liveNone: "目前没有开启掉落的直播。",
    claimBy: "请在掉落库存中领取奖励，截止时间：",
    howTitle: "领取方法",
    how: ["将 Twitch 账号与 NC 账号绑定（NC 官网 My Page > Manage Login）。", "观看 AION 2 分类中带有“已开启掉落”标签的直播。进度在任何参与的频道都会累计，但同一时间只能计入一个直播。", "在 Twitch 打开掉落库存，奖励就绪后领取。错过的奖励在领取截止前仍可领取。"],
    globalOnly: "奖励仅限 AION 2 国际服玩家；韩国和台湾服务器不在范围内。如领取的奖励未到账，请联系 NC 客服。",
    watchLink: "Twitch 上的 AION 2", inventoryLink: "掉落库存", linkAccount: "绑定账号",
    pastTitle: "往期活动", empty: "暂未找到活动。",
    footer: "活动内容来自 NC 公告，直播频道来自 Twitch。上次检查：",
  },
  "zh-TW": {
    loading: "正在載入 Twitch 掉落…", title: "Twitch 掉落",
    intro: "在 Twitch 觀看已開啟掉落的 AION 2 直播，即可獲得遊戲內獎勵。活動由 NC 舉辦，獎勵會發放到與你的 Twitch 帳號綁定的 NC 帳號。",
    live: "進行中", upcoming: "即將開始", ended: "已結束",
    endsIn: (d, h) => `還剩 ${d} 天 ${h} 小時結束`, startsIn: (d, h) => `${d} 天 ${h} 小時後開始`,
    period: "時間", days: "日期（太平洋時間）", dateTbd: "開始與結束日期待公布", part: (n) => `第 ${n} 部分`,
    min: "分鐘", hour: "小時", total: (h) => `共 ${h} 小時`,
    liveNow: (s, v) => `目前有掉落的直播：${s} 個 · ${v} 位觀眾`, liveNone: "目前沒有開啟掉落的直播。",
    claimBy: "請在掉落庫存中領取獎勵，截止時間：",
    howTitle: "領取方式",
    how: ["將 Twitch 帳號與 NC 帳號綁定（NC 官網 My Page > Manage Login）。", "觀看 AION 2 分類中帶有「已開啟掉落」標籤的直播。進度在任何參與的頻道都會累計，但同一時間只能計入一個直播。", "在 Twitch 開啟掉落庫存，獎勵就緒後領取。錯過的獎勵在領取截止前仍可領取。"],
    globalOnly: "獎勵僅限 AION 2 國際服玩家；韓國與台灣伺服器不在範圍內。若領取的獎勵未到帳，請聯絡 NC 客服。",
    watchLink: "Twitch 上的 AION 2", inventoryLink: "掉落庫存", linkAccount: "綁定帳號",
    pastTitle: "過往活動", empty: "尚未找到活動。",
    footer: "活動內容來自 NC 公告，直播頻道來自 Twitch。上次檢查：",
  },
};
const DROP_ITEM_LOCALES = ["en", "de", "es", "fr", "ja", "ko", "pt", "ru"];
// "Skin Chest x10 (Bound)" -> "skin chest x10", "Cyber Glasses (Skin: Eyewear) (Bound)" -> "cyber glasses": the key of twitch-drops-items.json
const dropKey = (name) => name.replace(/\([^)]*\)/g, "").replace(/\s+/g, " ").trim().toLowerCase();
function dropReward(items, reward) {
  const entry = items[dropKey(reward.name)];
  const code = getLocale();
  const name = entry?.names?.[DROP_ITEM_LOCALES.includes(code) ? code : "en"] ?? entry?.names?.en ?? reward.name.replace(/\s*\(Bound\)\s*$/i, "");
  return { name: reward.qty > 1 ? `${name} ×${reward.qty.toLocaleString(code === "de" ? "de-DE" : "en-US")}` : name, icon: entry?.icon ?? null };
}
async function renderTwitchDrops() {
  const code = getLocale();
  const L = TWITCH_DROPS_TEXT[code] ?? TWITCH_DROPS_TEXT.en;
  const loc = LOCALES.find((x) => x.code === code)?.intl ?? "en-US";
  const num = (n) => n.toLocaleString(loc);
  const hours = (m) => (m < 60 ? `${m} ${L.min}` : `${m / 60} ${L.hour}`);
  setBreadcrumb([link(t("breadcrumb.home"), "/"), L.title]);
  showLoading(L.loading);
  const [data, items] = await Promise.all([fetchJson("/api/twitch-drops"), fetchJson("/twitch-drops-items.json")]);
  const now = Date.now();
  const stateOf = (c) => (!c.startAt ? "upcoming" : Date.parse(c.startAt) > now ? "upcoming" : c.endAt && Date.parse(c.endAt) > now ? "live" : c.endAt ? "ended" : "live");
  const fmt = (iso) => new Intl.DateTimeFormat(loc, { dateStyle: "medium", timeStyle: "short" }).format(new Date(iso));
  const fmtDay = (iso) => new Intl.DateTimeFormat(loc, { dateStyle: "medium", timeZone: "America/Los_Angeles" }).format(new Date(iso));
  const left = (iso) => { const ms = Math.max(0, Date.parse(iso) - now); return [Math.floor(ms / 86400000), Math.floor((ms % 86400000) / 3600000)]; };

  const campaignCard = (c, state) => {
    const last = c.rewards[c.rewards.length - 1];
    let period = L.dateTbd;
    if (c.startAt && c.endAt) {
      period = c.dateOnly ? `${L.days}: ${fmtDay(c.startAt)} – ${fmtDay(new Date(Date.parse(c.endAt) - 60000))}` : `${L.period}: ${fmt(c.startAt)} – ${fmt(c.endAt)}`;
    }
    return el("article", { className: `td-campaign td-campaign--${state}` }, [
      el("header", { className: "td-head" }, [
        el("div", {}, [
          el("h3", {}, [c.name, c.part ? ` · ${L.part(c.part)}` : "", " ", el("span", { className: `td-badge td-badge--${state}`, textContent: L[state] })]),
          el("p", { className: "td-period", textContent: period }),
          ...(state === "live" && c.endAt ? [el("p", { className: "td-countdown", textContent: L.endsIn(...left(c.endAt)) })] : []),
          ...(state === "upcoming" && c.startAt ? [el("p", { className: "td-countdown", textContent: L.startsIn(...left(c.startAt)) })] : []),
          el("p", { className: "stat-sub", textContent: L.total(last.minutes / 60) }),
        ]),
      ]),
      el("ul", { className: "td-drops" }, c.rewards.map((r) => {
        const { name, icon } = dropReward(items, r);
        return el("li", { className: "td-drop" }, [
          icon ? el("img", { className: "td-drop-icon", src: `/${icon}`, alt: "", width: 64, height: 64, loading: "lazy" }) : el("span", { className: "td-drop-icon td-drop-icon--blank", "aria-hidden": "true" }),
          el("span", { className: "td-drop-name", textContent: name }),
          el("span", { className: "td-drop-time", textContent: hours(r.minutes) }),
        ]);
      })),
    ]);
  };

  const sorted = data.campaigns.map((c) => [c, stateOf(c)]);
  const current = sorted.filter(([, s]) => s !== "ended").reverse(); // newest start first, like the past ones
  const past = sorted.filter(([, s]) => s === "ended").reverse();
  const liveBlock = data.live
    ? el("section", { className: "td-live" }, [
        el("p", { className: "td-live-head", textContent: data.live.streams ? L.liveNow(num(data.live.streams), num(data.live.viewers)) : L.liveNone }),
        ...(data.live.channels.length ? [el("ul", { className: "td-live-list" }, data.live.channels.map((ch) =>
          el("li", {}, [el("a", { href: `https://www.twitch.tv/${encodeURIComponent(ch.login)}`, target: "_blank", rel: "noopener", textContent: ch.name }), el("span", { className: "stat-sub", textContent: ` ${num(ch.viewers)}` })])))] : []),
      ])
    : null;

  app.replaceChildren(
    el("h2", { textContent: L.title }),
    el("p", { className: "stat-sub", textContent: L.intro }),
    el("p", { className: "td-links" }, [
      el("a", { className: "td-link td-link--primary", href: "https://www.twitch.tv/directory/category/aion-2", target: "_blank", rel: "noopener", textContent: `${L.watchLink} ↗` }),
      el("a", { className: "td-link", href: "https://www.twitch.tv/drops/inventory", target: "_blank", rel: "noopener", textContent: `${L.inventoryLink} ↗` }),
      el("a", { className: "td-link", href: "https://id.plaync.com/account/connect/twitch?appGroupCode=aion2global", target: "_blank", rel: "noopener", textContent: `${L.linkAccount} ↗` }),
    ]),
    ...(liveBlock ? [liveBlock] : []),
    ...(data.claimBy ? [el("p", { className: "stat-sub", textContent: `${L.claimBy} ${fmt(data.claimBy)}.` })] : []),
    ...(sorted.length === 0 ? [el("p", { className: "empty", textContent: L.empty })] : []),
    ...current.map(([c, s]) => campaignCard(c, s)),
    el("section", { className: "td-how" }, [
      el("h3", { textContent: L.howTitle }),
      el("ol", {}, L.how.map((x) => el("li", { textContent: x }))),
      el("p", { className: "stat-sub", textContent: L.globalOnly }),
    ]),
    ...(past.length ? [el("h3", { textContent: L.pastTitle }), ...past.map(([c, s]) => campaignCard(c, s))] : []),
    el("p", { className: "stat-sub", textContent: `${L.footer} ${data.checkedAt ? fmt(data.checkedAt) : "–"}` }),
  );
}

// Changelog - English, plus German when the site language is German (texts in changelog.de.json,
// keyed by the English text; anything missing there stays English). The data is changelog.json,
// built from the release commits by Tools/changelog/gen.mjs; releases are grouped by minor version
// (0.10.x) so the long list stays scannable.
const CHANGELOG_TEXT = {
  en: {
    loading: "Loading changelog…",
    title: "Changelog",
    intro: "What changed, newest first: the Windows client, the website, the backend and the database. ",
    download: "Download",
    unreleased: "Unreleased",
    before: "Before 0.4.0",
    changes: (n) => `${n} change${n === 1 ? "" : "s"} since the last release`,
    releases: (n) => `${n} release${n === 1 ? "" : "s"}`,
    days: (n) => `${n} day${n === 1 ? "" : "s"}`,
    preNote: "start of the version numbers",
    areas: { Client: "Client", Website: "Website", Backend: "Backend", Database: "Database" },
  },
  de: {
    loading: "Änderungsprotokoll wird geladen…",
    title: "Änderungsprotokoll",
    intro: "Was sich geändert hat, neueste zuerst: der Windows-Client, die Website, das Backend und die Datenbank. ",
    download: "Download",
    unreleased: "Unveröffentlicht",
    before: "Vor 0.4.0",
    changes: (n) => `${n} Änderung${n === 1 ? "" : "en"} seit dem letzten Release`,
    releases: (n) => `${n} Release${n === 1 ? "" : "s"}`,
    days: (n) => `${n} Tag${n === 1 ? "" : "e"}`,
    preNote: "Beginn der Versionsnummern",
    areas: { Client: "Client", Website: "Website", Backend: "Backend", Database: "Datenbank" },
  },
};
const PRE_VERSION = "Before 0.4.0";
async function renderChangelog() {
  const lang = getLocale() === "de" ? "de" : "en";
  const L = CHANGELOG_TEXT[lang];
  setBreadcrumb([link(t("breadcrumb.home"), "/"), L.title]);
  showLoading(L.loading);
  const [releases, german] = await Promise.all([
    fetchJson("/changelog.json"),
    lang === "de" ? fetchJson("/changelog.de.json").catch(() => ({})) : Promise.resolve({}),
  ]);
  const groups = new Map();
  for (const r of releases) {
    const minor = r.version === "Unreleased" || r.version === PRE_VERSION ? r.version : `${r.version.split(".").slice(0, 2).join(".")}.x`;
    if (!groups.has(minor)) {
      groups.set(minor, []);
    }
    groups.get(minor).push(r);
  }
  const fmt = (d) => new Date(`${d}T00:00:00`).toLocaleDateString(lang === "de" ? "de-DE" : "en-GB", { year: "numeric", month: "short", day: "numeric" });
  const sections = [...groups].map(([minor, list], i) => {
    const first = list[list.length - 1];
    const last = list[0];
    const pre = minor === PRE_VERSION;
    const span = `${fmt(first.date)}${first.date === last.date ? "" : ` – ${fmt(last.date)}`}`;
    return el("details", { className: "changelog-group", open: i === 0 }, [
      el("summary", {}, [
        el("strong", { textContent: pre ? L.before : minor === "Unreleased" ? L.unreleased : minor }),
        el("span", { className: "changelog-range", textContent: pre
          ? `${L.preNote} · ${L.days(list.length)} · ${span}`
          : minor === "Unreleased"
            ? L.changes(list[0].items.length)
            : `${L.releases(list.length)} · ${span}` }),
      ]),
      el("ul", { className: "changelog-list" }, list.flatMap((r) => r.items.map((item, n) => el("li", {}, [
        el("span", { className: "changelog-version", textContent: n === 0 && r.version !== "Unreleased" && !pre ? r.version : "" }),
        el("span", { className: "changelog-date", textContent: n === 0 && r.version !== "Unreleased" ? fmt(r.date) : "" }),
        el("span", { className: "changelog-text" }, [
          ...item.areas.map((a) => el("span", { className: `changelog-tag changelog-tag--${a.toLowerCase()}`, textContent: L.areas[a] ?? a })),
          (german[item.text] ?? item.text).replace(/^./, (c) => c.toUpperCase()),
        ]),
      ])))),
    ]);
  });
  app.replaceChildren(el("h2", { textContent: L.title }), el("p", { className: "download-meta" }, [L.intro, link(L.download, "/download")]), ...sections);
}

// Boss order of the expedition and transcendence dungeons by instance slug, English boss names, the final boss last. sequence: true = the
// bosses have to be killed one after the other in this order, otherwise the ones before the final boss are interchangeable.
const EXPEDITION_BOSS_ORDER = {
  "urugugu-canyon": { sequence: false, bosses: ["Guardian Captain Raur", "Judge Urahum", "Divine Auldor"] },
  "draupnir": { sequence: false, bosses: ["Phantasmal Lakshmi", "Predator Saraswati", "Transcendent Bakarma"] },
  "krao-cave": { sequence: false, bosses: ["Enhanced Harcon", "Neglected Gadioton", "Ultimate Berk"] },
  "fire-temple": { sequence: false, bosses: ["Black Smoke Murute", "Red Spark Ignus", "Silver Blade Rotan", "Kromede's Desire"] },
  "vakron-sky-island": { sequence: true, bosses: ["Tiere", "Thamon", "Vakron"] },
  "ferocious-horn-den": { sequence: true, bosses: ["Watchdog Kwapo", "Shining Mau Totem", "Necromancer Duanka", "Ferocious Horn Nuakum"] },
  "cradle-of-nihility": { sequence: false, bosses: ["Multiverse Veltiras", "Solemn Valkan", "Afflicted Bakarma"] },
  "hall-of-illusion": { sequence: false, bosses: ["Glassvein", "Nazarak", "Nathara"] },
  "azure-breath-island": { sequence: false, bosses: ["Expanding Xyrepe", "Wise Baumnute", "Awakened Alturadon"] },
  "citadel-of-the-fallen-daeva": { sequence: false, bosses: ["Forbidden Hexbeast Griosa", "Willful Turgen", "Basilus the False"] },
  "consumed-deus-research-base": { sequence: false, bosses: ["Lab Director Zylus", "Overseer Gromkas", "Prideful Atiel"] },
  "shattered-arkanis": { sequence: true, bosses: ["Ruthilis of Pain", "Siliator of Deceit", "Talisra of the Void"] },
  "submerged-life-temple": { sequence: true, bosses: ["Singing Ellyde", "Deep Sea Pydeon", "Submerged Emon"] },
  "noirans-hidden-legacy": { sequence: true, bosses: ["Incomplete Braunt", "Mad Klominster", "Askran"] },
  "abyssal-horn-den": { sequence: true, bosses: ["Kapu", "Dhakan", "Gargaum"] },
  "mirror-of-scarlet-desire": { sequence: true, bosses: ["Rotar", "Robstino", "Ordeal of Love", "Kromede's Desolation"] },
  "deus-research-base": { sequence: true, bosses: ["Gelcos", "Nazmun", "Atiel"] },
};

// Shared by the instance grid and the boss grid below - a "poster" tile is just a photo (optional),
// a dark scrim for legibility, and a light title overlaid on top, linking somewhere. objectPosition
// defaults to centered (right for a landscape instance photo) but a boss portrait is a tall
// character render - "top" keeps the face in frame instead of centering on the torso.
function posterCard(href, photo, title, objectPosition, badge) {
  const children = [el("div", { className: "poster-scrim" })];
  if (badge) {
    children.push(el("div", { className: badge.final ? "poster-badge poster-badge--final" : "poster-badge", textContent: badge.text }));
  }
  if (photo) {
    const img = icon(smallPhoto(photo), "poster-photo");
    if (objectPosition) {
      img.style.objectPosition = objectPosition;
    }
    children.unshift(img);
  }
  children.push(el("div", { className: "poster-title", textContent: title }));
  // The title text is always white (matches a real photo's own dark scrim) - without one, the
  // card needs its own fixed dark backdrop instead of the theme's --color-surface, which is
  // near-white on the light theme and would make that white text unreadable (see .poster-card
  // vs .poster-card--no-photo in style.css).
  return el("a", { className: photo ? "poster-card" : "poster-card poster-card--no-photo", href }, children);
}

/** Display name as the UI translation table knows it, else the English name the DB carries, else the raw name. */
/** A server label as stored ("Europe - Tsenka", "NA East - Siel", "Hogalum [1317]") with the server's name in the
 * reader's language, as the game spells it (only a few names differ - see SERVER_NAMES). The region is translated too. */
function regionLabel(region) {
  return ["Europe", "Asia", "North America", "NA East", "NA West", "LATAM", "Taiwan"].includes(region) ? t(`region.${region}`) : region;
}

function serverLabel(label) {
  if (!label) {
    return label;
  }
  const cut = label.lastIndexOf(" - ");
  const head = cut >= 0 ? regionLabel(label.slice(0, cut)) + " - " : "";
  const m = /^(.+?)( \[\d+\])?$/.exec(label.slice(cut >= 0 ? cut + 3 : 0));
  const name = m ? m[1] : label;
  return head + (SERVER_NAMES[name]?.[getLocale()] ?? name) + (m?.[2] ?? "");
}

function displayName(row) {
  // Conquest instances read "Draupnir", not "Draupnir (Conquest)": the stage is a label next to the name (instanceDiffPill).
  const conquest = splitConquest(row.name);
  if (conquest) {
    return displayName({ name: conquest.base });
  }
  const translated = translateGameName(row.name);
  return translated !== row.name ? translated : row.nameEn ?? row.name;
}

/** displayName() plus " ( Lv. N )" when INSTANCE_MIN_LEVEL has an entry for this instance - per
 * the user, for the instance grid's own card titles. No suffix (not "Lv. ?" or similar) when the
 * level isn't known, same "don't show, don't guess" rule INSTANCE_IMAGES already follows. */
function instanceCardLabel(instance) {
  const level = INSTANCE_MIN_LEVEL[instance.name];
  return level === undefined ? displayName(instance) : `${displayName(instance)} ( Lv. ${level} )`;
}

/** Highest minimum level first, unknown-level instances last (not first - an unranked instance is
 * not the same as a confirmed low-level one) - per the user. Stable, so instances that tie (most
 * classic-Aion ones, which have no entry at all yet) keep the API's own name order. */
function byMinLevelDescending(a, b) {
  const av = INSTANCE_MIN_LEVEL[a.name] ?? -1;
  const bv = INSTANCE_MIN_LEVEL[b.name] ?? -1;
  return bv - av;
}

// ---- Instances page: categories as tabs. Expeditions split into Exploration (normal) and Conquest
// (hard) - separate instances with their own rankings; Nightmare is laid out like the in-game boss
// tree; Ascension and Transcendence show their difficulty steps. Everything shown comes from the API;
// nothing about a player's own progress is shown (the page is public).
const INSTANCE_TAB_ORDER = ["expedition", "nightmare", "ascension", "transcendence"];

// World bosses (Verteron, Altgard, Abyss) are their own area (/worldbosses), not an instance category.
const isWorldBossArea = (instance) => instance?.category === "worldboss";
const areaPath = (instance) => (isWorldBossArea(instance) ? `/worldbosses/${instance.slug}` : `/instances/${instance.slug}`);
/** Root crumbs of an instance or world boss area: Home > Instances | Home > World Bosses. */
/** Crumbs down to a boss: root crumbs › its instance (same as the boss page); `e` carries bossId + instance fields. */
function bossCrumbs(e) {
  const worldBoss = e.instanceCategory === "worldboss";
  const tail = e.instanceSlug
    ? [
        e.instanceCategory === "nightmare"
          ? link(t("category.nightmare"), gp("/instances/nightmare"))
          : crumbWithPill(link(crumbInstanceName(e.instanceName, e.instanceNameEn), gp(`${worldBoss ? "/worldbosses" : "/instances"}/${e.instanceSlug}`)), instanceDiffPill(e.instanceCategory, e.instanceName)),
      ]
    : [];
  return [...(worldBoss ? [link(t("breadcrumb.home"), "/"), link(t("category.worldboss"), gp("/worldbosses"))] : gameCrumbs()), ...tail, el("span", {}, [link(translateGameName(e.bossName), gp(`/bosses/${e.bossSlug ?? e.bossId}${e.mode ? `?mode=${encodeURIComponent(e.mode)}` : ""}`)), ...[diffPill(e.instanceCategory, e.mode, e.instanceName)].filter((x) => x != null).flatMap((p) => [" ", p])])];
}

function areaCrumbs(instance) {
  return isWorldBossArea(instance) ? [link(t("breadcrumb.home"), "/"), link(t("category.worldboss"), gp("/worldbosses"))] : gameCrumbs();
}

/** The instance name for a breadcrumb: without the Conquest / Hard suffix, the pill behind it says the difficulty. */
function crumbInstanceName(name, nameEn) {
  const base = (nameEn ?? name).replace(/ \(Conquest(, Hard)?\)$/, "");
  return displayName({ name: base, nameEn: base });
}

function baseInstanceName(i) {
  return i.name.replace(/ \(Conquest(, Hard)?\)$/, "");
}

/** Card with photo, name and facts (level, players are only shown where the data has them). */
function instanceFactCard(i, { chips = [], label } = {}) {
  const level = INSTANCE_MIN_LEVEL[i.name];
  const facts = INSTANCE_FACTS[i.name] ?? {};
  const photo = INSTANCE_IMAGES[i.name];
  const pills = [];
  if (level !== undefined) {
    pills.push(t("instances.levelFrom", { n: level }));
  }
  if (facts.players) {
    pills.push(t("instances.playersRange", { n: facts.players.replace("-", "–") }));
  }
  if (facts.itemLevel) {
    pills.push(t("instances.itemLevelPill", { n: formatNumber(facts.itemLevel) }));
  }
  const title = [document.createTextNode(label ?? displayName(i))];
  const stage = instanceDiffPill(i.category, i.name);
  if (stage) {
    title.push(" ", stage);
  }
  const text = [el("b", {}, title)];
  if (pills.length > 0) {
    text.push(el("div", { className: "ip-meta" }, pills.map((p) => el("span", { className: "ip-pill", textContent: p }))));
  }
  if (chips.length > 0) {
    text.push(el("div", { className: "ip-chips" }, chips.map((c) => el("span", { className: "ip-chip", textContent: c }))));
  }
  return el("a", { className: "ip-card", href: gp(areaPath(i)) }, [photo ? icon(smallPhoto(photo), "ip-photo") : null, el("div", { className: "ip-text" }, text)].filter((x) => x != null));
}

/** A breadcrumb part followed by its label pill (just the part when there is none). */
function crumbWithPill(part, pill) {
  return pill ? el("span", {}, [part, " ", pill]) : part;
}

/** The Exploration / Conquest label of an expedition instance (they share their names), null for every other category. */
function instanceDiffPill(category, instanceName) {
  return category === "expedition" ? diffPill(category, null, instanceName) : null;
}

/** An instance's name without the Conquest stars (the fight's own label says the stage). */
function plainInstanceName(row) {
  const conquest = splitConquest(row.instanceName);
  return conquest ? displayName({ name: conquest.base }) : displayName({ name: row.instanceName, nameEn: row.instanceNameEn });
}

/** A fight's difficulty label ("Nightmare 10", "Conquest 3", "Ascension Hard", "World Boss") with its colour class; null for none (plain exploration). */
function difficultyOf(category, mode, instanceName) {
  const stage = /^\d+$/.test(mode ?? "") ? Number(mode) : 0;
  if (category === "nightmare" && stage > 0) {
    return { text: `${t("category.nightmare")} ${stage}`, cls: "nm" };
  }
  if (category === "transcendence" && stage > 0) {
    return { text: `${t("category.transcendence")} ${stage}`, cls: "tr" };
  }
  if (category === "ascension" && ["easy", "medium", "hard", "extreme"].includes(mode)) {
    return { text: `${t("category.ascension")} ${t(`mode.${mode}`)}`, cls: "as" };
  }
  if (category === "expedition") {
    const conquest = splitConquest(instanceName);
    if (conquest) {
      return { text: `${t("mode.conquest")} ${conquest.stars.length}${conquest.hard ? ` · ${t("mode.hardSub")}` : ""}`, cls: conquest.hard ? "ch" : "cq" };
    }
    return { text: t("mode.explore"), cls: "ex" };
  }
  if (category === "hideout") {
    return { text: t("category.hideout"), cls: "hi" };
  }
  if (category === "sanctuary") {
    return { text: t("category.sanctuary"), cls: "sa" };
  }
  return category === "worldboss" ? { text: t("category.worldbossOne"), cls: "wb" } : null;
}

/** The difficulty label as a coloured pill (see difficultyOf); null when the fight has none. */
/** The small orange "Partial" pill on a run row: a boss fight uploaded by someone who joined late (the API's isPartial; absent = not partial). */
function partialBadge(row) {
  return row?.isPartial === true ? el("span", { className: "partial-pill", title: t("encounter.partialBadgeTip"), textContent: t("encounter.partialBadge") }) : null;
}

function diffPill(category, mode, instanceName) {
  const d = difficultyOf(category, mode, instanceName);
  return d ? el("span", { className: `diff-pill diff-${d.cls}`, textContent: d.text }) : null;
}

function modeLabel(mode) {
  if (mode === "") {
    return t("mode.unknown"); // runs whose difficulty step the client could not tell
  }
  return /^\d+$/.test(mode) ? t("mode.stage", { n: mode }) : t(`mode.${mode}`);
}

const CATEGORY_MODE_LIST = {
  ascension: ["easy", "medium", "hard", "extreme"],
  transcendence: ["1", "2", "3", "4"],
};

/** The two difficulty tabs of the expeditions as the game's dungeon window has them; Conquest has the two steps Normal and Hard below. */
const EXPEDITION_TABS = [
  ["explore", "/instances/expedition", "mode.explore"],
  ["conquest", "/instances/expedition/conquest", "mode.conquest"],
];

function expeditionPane(list, variant, step) {
  const tab = variant === "conquest" ? "conquest" : "explore";
  const hard = tab === "conquest" && step === "hard";
  const wanted = tab === "explore" ? "explore" : hard ? "advanced" : "conquest";
  const items = list.filter((i) => (i.variant ?? "explore") === wanted);
  const seg = el("div", { className: "ip-seg", role: "group" }, EXPEDITION_TABS.map(([v, href, label]) => {
    const a = el("a", { href: gp(href), textContent: t(label) });
    a.setAttribute("aria-pressed", String(v === tab));
    return a;
  }));
  const bar = [seg];
  if (tab === "conquest") {
    bar.push(
      el("div", { className: "ip-seg ip-seg-step", role: "group" }, [
        ["/instances/expedition/conquest", "mode.normalSub", false],
        ["/instances/expedition/conquest/hard", "mode.hardSub", true],
      ].map(([href, label, isHard]) => {
        const a = el("a", { href: gp(href), textContent: t(label) });
        a.setAttribute("aria-pressed", String(isHard === hard));
        return a;
      })),
    );
  }
  const body = el("div", { className: "ip-grid" }, items.map((i) => instanceFactCard(i, { label: displayName({ name: baseInstanceName(i), nameEn: baseInstanceName(i) }) })));
  return el("div", {}, [...bar, body]);
}

function modeCardsPane(list, category) {
  const chips = CATEGORY_MODE_LIST[category].map(modeLabel);
  return el("div", { className: "ip-grid" }, list.map((i) => instanceFactCard(i, { chips })));
}

async function nightmarePane(list) {
  const entries = await Promise.all(
    list.map(async (instance) => {
      const bosses = await fetchJson(`/api/instances/${encodeURIComponent(instance.slug)}/bosses?game=${currentGame}`).catch(() => []);
      return { instance, boss: bosses[0] ?? null };
    }),
  );
  const slots = ["a1", "a2", "b1", "b2", "n1", "n2", "z"];
  const lines = (cls, paths) => {
    const wrap = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    wrap.setAttribute("class", `ip-lines ${cls}`);
    wrap.setAttribute("viewBox", "0 0 110 426");
    wrap.setAttribute("preserveAspectRatio", "none");
    wrap.innerHTML = `<g fill="none" stroke="currentColor" stroke-width="2" vector-effect="non-scaling-stroke">${paths.map((d) => `<path d="${d}" vector-effect="non-scaling-stroke"/>`).join("")}</g>`;
    return wrap;
  };

  const detail = el("aside", { className: "ip-detail" });
  const nodes = [];
  let activeEntry = null;

  async function selectLevel(entry, mode, chip, levelBox, statBox) {
    levelBox.querySelectorAll(".ip-lvl").forEach((x) => x.classList.toggle("sel", x === chip));
    statBox.textContent = "…";
    try {
      const d = await fetchJson(`/api/bosses/${encodeURIComponent(entry.boss.slug)}/leaderboard?game=${currentGame}&mode=${encodeURIComponent(mode)}`);
      const s = d.stats;
      const row = (label, value) => el("div", { className: "ip-row" }, [el("span", { textContent: label }), el("b", { textContent: value })]);
      statBox.replaceChildren(
        el("h4", { textContent: `${displayName(entry.boss)} · ${modeLabel(mode)}` }),
        row(t("nm.runs"), formatNumber(s.runCount)),
        row(t("nm.bestIdps"), s.bestIdps === null ? "–" : formatNumber(s.bestIdps)),
        row(t("nm.avgTime"), s.avgDurationSeconds === null ? "–" : formatDuration(s.avgDurationSeconds)),
        el("a", { className: "ip-open", href: gp(`/bosses/${entry.boss.slug}?mode=${encodeURIComponent(mode)}`), textContent: t("nm.openRanking") }),
      );
    } catch {
      statBox.textContent = "";
    }
  }

  function select(entry, node) {
    activeEntry = entry;
    nodes.forEach((n) => n.classList.toggle("active", n === node));
    const levelBox = el("div", { className: "ip-lvls" });
    const statBox = el("div", { className: "ip-stats" }, [el("p", { className: "ip-hint", textContent: t("nm.pickLevel") })]);
    for (let n = 1; n <= 10; n++) {
      const chip = el("button", { type: "button", className: "ip-lvl", textContent: String(n) });
      chip.addEventListener("click", () => selectLevel(entry, String(n), chip, levelBox, statBox));
      levelBox.append(chip);
    }
    detail.replaceChildren(
      el("div", { className: "ip-loc", textContent: displayName(entry.instance) }),
      el("h3", { textContent: entry.boss ? displayName(entry.boss) : "" }),
      el("div", { className: "ip-challenge", textContent: t("nm.challenge") }),
      levelBox,
      statBox,
    );
  }

  const treeChildren = [];
  entries.forEach((entry, idx) => {
    if (!entry.boss) {
      return;
    }
    const photo = BOSS_IMAGES[entry.boss.name] ?? INSTANCE_IMAGES[entry.instance.name];
    const node = el("button", { type: "button", className: `ip-node ${slots[idx] ?? ""}` }, [
      el("span", { className: "ip-node-text" }, [el("small", { textContent: displayName(entry.instance) }), el("b", { textContent: displayName(entry.boss) })]),
      el("span", { className: "ip-node-pt", style: photo ? `background-image:url(${smallPhoto(photo)})` : "" }),
    ]);
    node.addEventListener("click", () => select(entry, node));
    nodes.push(node);
    treeChildren.push(node);
  });
  treeChildren.splice(4, 0, lines("l1", ["M0 48H55V103H110M0 158H55V103", "M0 268H55V323H110M0 378H55V323"]));
  treeChildren.push(lines("l2", ["M0 103H55V213H110M0 323H55V213"]));
  const tree = el("div", { className: "ip-tree" }, treeChildren);
  if (nodes.length > 0) {
    select(entries.find((e) => e.boss), nodes[0]);
  }
  return el("div", { className: "ip-nightmare" }, [tree, detail]);
}

async function renderInstances(categoryParam, variantParam, stepParam) {
  const instances = await (async () => {
    setBreadcrumb([link(t("breadcrumb.home"), "/"), t("breadcrumb.instances")]);
    showLoading(t("loading.instances"));
    return fetchJson(`/api/instances?game=${currentGame}`);
  })();
  if (instances.length === 0) {
    app.replaceChildren(el("p", { className: "empty", textContent: t("instances.emptyNoInstances") }));
    return;
  }

  const groups = new Map();
  for (const i of instances.filter((x) => !isWorldBossArea(x))) {
    const key = i.category ?? "other";
    if (!groups.has(key)) {
      groups.set(key, []);
    }
    groups.get(key).push(i);
  }
  const categories = [...INSTANCE_TAB_ORDER.filter((c) => groups.has(c)), ...[...groups.keys()].filter((c) => !INSTANCE_TAB_ORDER.includes(c))];
  const countOf = (category) => {
    const list = groups.get(category);
    return category === "expedition" ? list.filter((i) => (i.variant ?? "explore") === "explore").length : list.length;
  };
  const current = categories.includes(categoryParam) ? categoryParam : categories[0];

  // Every category has its own address (/instances/nightmare ...), so it can be linked, shared and found.
  const tabs = el(
    "nav",
    { className: "ip-tabs", "aria-label": t("instances.heading") },
    categories.map((c) => {
      const b = el("a", { className: "ip-tab", href: gp(`/instances/${c}`) }, [t(`category.${c}`), el("small", { textContent: String(countOf(c)) })]);
      b.setAttribute("aria-pressed", String(c === current));
      return b;
    }),
  );
  setBreadcrumb([link(t("breadcrumb.home"), "/"), categoryParam ? link(t("breadcrumb.instances"), gp("/instances")) : t("breadcrumb.instances"), ...(categoryParam ? [t(`category.${current}`)] : [])]);

  const list = [...groups.get(current)].sort((a, b) => a.sortOrder - b.sortOrder);
  let pane;
  if (current === "expedition") {
    pane = expeditionPane(list, variantParam, stepParam);
  } else if (current === "nightmare") {
    pane = await nightmarePane(list);
  } else if (CATEGORY_MODE_LIST[current]) {
    pane = modeCardsPane(list, current);
  } else {
    pane = el("div", { className: "ip-grid" }, list.map((i) => instanceFactCard(i)));
  }
  app.replaceChildren(el("h2", { textContent: t("instances.heading") }), tabs, el("div", { className: "ip-panel" }, [pane]));
  if (currentGame === "aion2") {
    app.append(el("p", { className: "derived-note", textContent: t("aion2.derivedNote") }));
  }
}

/** One compact KPI card (aiondps_design_pack_v1 section 8) - a big real number over a short label. */
function statCard(value, label, note) {
  const valueChildren = [document.createTextNode(value)];
  if (note) {
    valueChildren.push(el("span", { className: "stat-card-note", textContent: note }));
  }
  return el("div", { className: "stat-card" }, [
    el("div", { className: "stat-card-value" }, valueChildren),
    el("div", { className: "stat-card-label", textContent: label }),
  ]);
}

// Small generic glyphs (not game art, just UI icons) for the homepage's floating stats card -
// copied verbatim from the aiondps_claude_design_pack prototype comparison (index-3.html).
const ICON_STAT_ENCOUNTERS =
  '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14.5 3.5 20.5 9.5M6 18l-2.5 2.5M9 15l-5.5 5.5M14.5 3.5 5 13l1.5 1.5L16 5l-1.5-1.5z"/><path d="M18 4l2 2"/></svg>';
const ICON_STAT_PARSES = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M13 2 3 14h7l-1 8 10-12h-7z"/></svg>';
const ICON_STAT_PLAYERS =
  '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="8" r="4"/><path d="M4 20c0-4.4 3.6-8 8-8s8 3.6 8 8"/></svg>';

// Footer social icons - real, currently-true destinations (same ones already wired up in
// production nginx as /twitch, /discord, /steam_aion2 redirects). Official brand marks
// (simple-icons project), not hand-drawn approximations.
const ICON_SOCIAL_TWITCH =
  '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M11.571 4.714h1.715v5.143H11.57zm4.715 0H18v5.143h-1.714zM6 0L1.714 4.286v15.428h5.143V24l4.286-4.286h3.428L22.286 12V0zm14.571 11.143l-3.428 3.428h-3.429l-3 3v-3H6.857V1.714h13.714Z"/></svg>';
const ICON_SOCIAL_DISCORD =
  '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M20.317 4.3698a19.7913 19.7913 0 00-4.8851-1.5152.0741.0741 0 00-.0785.0371c-.211.3753-.4447.8648-.6083 1.2495-1.8447-.2762-3.68-.2762-5.4868 0-.1636-.3933-.4058-.8742-.6177-1.2495a.077.077 0 00-.0785-.037 19.7363 19.7363 0 00-4.8852 1.515.0699.0699 0 00-.0321.0277C.5334 9.0458-.319 13.5799.0992 18.0578a.0824.0824 0 00.0312.0561c2.0528 1.5076 4.0413 2.4228 5.9929 3.0294a.0777.0777 0 00.0842-.0276c.4616-.6304.8731-1.2952 1.226-1.9942a.076.076 0 00-.0416-.1057c-.6528-.2476-1.2743-.5495-1.8722-.8923a.077.077 0 01-.0076-.1277c.1258-.0943.2517-.1923.3718-.2914a.0743.0743 0 01.0776-.0105c3.9278 1.7933 8.18 1.7933 12.0614 0a.0739.0739 0 01.0785.0095c.1202.099.246.1981.3728.2924a.077.077 0 01-.0066.1276 12.2986 12.2986 0 01-1.873.8914.0766.0766 0 00-.0407.1067c.3604.698.7719 1.3628 1.225 1.9932a.076.076 0 00.0842.0286c1.961-.6067 3.9495-1.5219 6.0023-3.0294a.077.077 0 00.0313-.0552c.5004-5.177-.8382-9.6739-3.5485-13.6604a.061.061 0 00-.0312-.0286zM8.02 15.3312c-1.1825 0-2.1569-1.0857-2.1569-2.419 0-1.3332.9555-2.4189 2.157-2.4189 1.2108 0 2.1757 1.0952 2.1568 2.419 0 1.3332-.9555 2.4189-2.1569 2.4189zm7.9748 0c-1.1825 0-2.1569-1.0857-2.1569-2.419 0-1.3332.9554-2.4189 2.1569-2.4189 1.2108 0 2.1757 1.0952 2.1568 2.419 0 1.3332-.946 2.4189-2.1568 2.4189Z"/></svg>';
const ICON_SOCIAL_STEAM =
  '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M11.979 0C5.678 0 .511 4.86.022 11.037l6.432 2.658c.545-.371 1.203-.59 1.912-.59.063 0 .125.004.188.006l2.861-4.142V8.91c0-2.495 2.028-4.524 4.524-4.524 2.494 0 4.524 2.031 4.524 4.527s-2.03 4.525-4.524 4.525h-.105l-4.076 2.911c0 .052.004.105.004.159 0 1.875-1.515 3.396-3.39 3.396-1.635 0-3.016-1.173-3.331-2.727L.436 15.27C1.862 20.307 6.486 24 11.979 24c6.627 0 11.999-5.373 11.999-12S18.605 0 11.979 0zM7.54 18.21l-1.473-.61c.262.543.714.999 1.314 1.25 1.297.539 2.793-.076 3.332-1.375.263-.63.264-1.319.005-1.949s-.75-1.121-1.377-1.383c-.624-.26-1.29-.249-1.878-.03l1.523.63c.956.4 1.409 1.5 1.009 2.455-.397.957-1.497 1.41-2.454 1.012H7.54zm11.415-9.303c0-1.662-1.353-3.015-3.015-3.015-1.665 0-3.015 1.353-3.015 3.015 0 1.665 1.35 3.015 3.015 3.015 1.663 0 3.015-1.35 3.015-3.015zm-5.273-.005c0-1.252 1.013-2.266 2.265-2.266 1.249 0 2.266 1.014 2.266 2.266 0 1.251-1.017 2.265-2.266 2.265-1.253 0-2.265-1.014-2.265-2.265z"/></svg>';

/** One item in the homepage's floating stats card (icon + big number + label) - distinct from the
 * generic statCard() above, which other pages (boss/instance stat rows) still use unmodified. */
function homeStatItem(icon, value, label) {
  return el("div", { className: "home-stat-item" }, [
    el("div", { className: "home-stat-icon", innerHTML: icon }),
    el("div", { className: "home-stat-text" }, [
      el("strong", { textContent: value }),
      el("span", { textContent: label }),
    ]),
  ]);
}

/** Best iDPS / avg kill time / run count row (Backend/src/routes/bosses.ts statsForBossIds) - null
 * (never rendered as a zero) with zero runs, since "empty beats wrong" applies to stats exactly
 * like it does to assets: a boss/instance nobody has fought yet shows no stats row at all. */
function fightStatsRow(stats) {
  if (!stats || stats.runCount === 0) {
    return null;
  }
  const cards = [];
  if (stats.bestIdps !== null) {
    cards.push(statCard(formatNumber(stats.bestIdps), t("stats.bestDps")));
  }
  if (stats.avgDurationSeconds !== null) {
    cards.push(statCard(formatDuration(stats.avgDurationSeconds), t("stats.avgTime")));
  }
  cards.push(statCard(formatNumber(stats.runCount), t("stats.runs")));
  return el("div", { className: "stats-row" }, cards);
}

/** Cinematic hero banner for an instance's boss list (aiondps_design_pack_v1 section 6) - only
 * ever real data: the instance's own photo (INSTANCE_IMAGES) and level (INSTANCE_MIN_LEVEL) when
 * known, the real boss count already fetched, and (once any exist) real best-DPS/run-count pills
 * from /api/instances/:id/stats. No difficulty/player-count pill - the API has no such fields yet,
 * and this project doesn't show a number it can't back with real data. */
function instanceHero(instance, bossCount, stats) {
  const photo = INSTANCE_IMAGES[instance.name];
  const level = INSTANCE_MIN_LEVEL[instance.name];
  const pills = [];
  const stage = difficultyOf(instance.category, null, instance.name);
  if (stage && instance.category === "expedition") {
    pills.push(el("span", { className: "instance-hero-pill", textContent: stage.text }));
  }
  if (level !== undefined) {
    pills.push(el("span", { className: "instance-hero-pill", textContent: `Lv. ${level}` }));
  }
  pills.push(
    el("span", {
      className: "instance-hero-pill",
      textContent: bossCount === 1 ? t("bosses.countLabelOne") : t("bosses.countLabel", { count: bossCount }),
    }),
  );
  if (stats && stats.runCount > 0) {
    if (stats.bestIdps !== null) {
      pills.push(el("span", { className: "instance-hero-pill", textContent: `${t("stats.bestDps")} ${formatNumber(stats.bestIdps)}` }));
    }
    pills.push(el("span", { className: "instance-hero-pill", textContent: `${formatNumber(stats.runCount)} ${t("stats.runs")}` }));
  }

  const children = [el("div", { className: "instance-hero-scrim" })];
  if (photo) {
    // Eager + high priority, unlike icon()'s card photos below the fold (aiondps_design_pack_v1
    // section 16: "Hero-Bilder priorisieren").
    const heroImg = el("img", { src: photo, alt: "", className: "instance-hero-photo", fetchPriority: "high" });
    heroImg.style.objectPosition = INSTANCE_FOCUS[instance.name] ?? "50% 50%";
    children.unshift(heroImg);
  }
  children.push(
    el("div", { className: "instance-hero-content" }, [
      el("h1", { className: "instance-hero-title", textContent: displayName(instance) }),
      el("div", { className: "instance-hero-meta" }, pills),
    ]),
  );
  return el("div", { className: "instance-hero" }, children);
}

/** World bosses: the areas (Verteron, Altgard, Abyss) as cards; each opens its boss list like an instance does. */
async function renderWorldBosses() {
  setBreadcrumb([link(t("breadcrumb.home"), "/"), t("category.worldboss")]);
  showLoading(t("loading.instances"));
  const areas = (await fetchJson(`/api/instances?game=${currentGame}`)).filter(isWorldBossArea).sort((a, b) => a.sortOrder - b.sortOrder);
  if (areas.length === 0) {
    app.replaceChildren(el("p", { className: "empty", textContent: t("instances.emptyNoInstances") }));
    return;
  }
  app.replaceChildren(el("h2", { textContent: t("category.worldboss") }), el("div", { className: "ip-panel" }, [el("div", { className: "ip-grid" }, areas.map((i) => instanceFactCard(i)))]));
  if (currentGame === "aion2") {
    app.append(el("p", { className: "derived-note", textContent: t("aion2.derivedNote") }));
  }
}


/** The statistics page of a dungeon (/instances/<dungeon>/statistics): every boss with its fight numbers and class medians, and the share of the classes. */
async function renderInstanceStatistics(instanceSlug, params = new URLSearchParams()) {
  setBreadcrumb([...gameCrumbs(), t("breadcrumb.bosses")]);
  showLoading(t("loading.bosses"));
  const statsQuery = new URLSearchParams({ game: currentGame });
  if (params.get("mode")) {
    statsQuery.set("mode", params.get("mode"));
  }
  if (params.get("gear")) {
    statsQuery.set("gear", params.get("gear"));
  }
  const [bosses, instances, stats, data] = await Promise.all([
    fetchJson(`/api/instances/${encodeURIComponent(instanceSlug)}/bosses?game=${currentGame}`),
    fetchJson(`/api/instances?game=${currentGame}`),
    fetchJson(`/api/instances/${encodeURIComponent(instanceSlug)}/stats?${statsQuery}`),
    fetchJson(`/api/instances/${encodeURIComponent(instanceSlug)}/statistics?${statsQuery}`),
  ]);
  const instance = instances.find((i) => i.slug === instanceSlug || String(i.id) === String(instanceSlug));
  if (instance) {
    setBreadcrumb([...areaCrumbs(instance), crumbWithPill(crumbInstanceName(instance.name, instance.nameEn), instanceDiffPill(instance.category, instance.name)), t("stats.tabStats")]);
  }
  const hero = instance ? [instanceHero(instance, bosses.length, stats)] : [];
  const instancePath = gp(`/${instance && isWorldBossArea(instance) ? "worldbosses" : "instances"}/${instanceSlug}`);
  hero.push(instanceViewButtons(instancePath, true));
  const gearHref = (range) => {
    const next = new URLSearchParams(params);
    range ? next.set("gear", range) : next.delete("gear");
    return next.toString() ? `${instancePath}/statistics?${next}` : `${instancePath}/statistics`;
  };
  app.replaceChildren(...hero, ...buildInstanceStats(data, { el, t, formatNumber, classLabel, classIcon, displayName, formatDuration, gp, gearHref }));
}

/** The two views of a dungeon, as on a boss page: its bosses and its statistics. */
function instanceViewButtons(instancePath, statsView) {
  const ICONS = {
    bosses: '<svg viewBox="0 0 24 24" width="26" height="26" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/></svg>',
    stats: '<svg viewBox="0 0 24 24" width="26" height="26" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 3v3M12 18v3M7 6h10v12H7zM7 12h10"/></svg>',
  };
  const button = (label, href, active, key) => {
    const a = el("a", { href, className: `view-button${active ? " active" : ""}`, title: label, innerHTML: ICONS[key] });
    a.setAttribute("aria-label", label);
    if (active) {
      a.setAttribute("aria-current", "page");
    }
    return a;
  };
  return el("nav", { className: "view-buttons dungeon-view-buttons" }, [button(t("stats.tabBosses"), instancePath, !statsView, "bosses"), button(t("stats.tabStats"), `${instancePath}/statistics`, statsView, "stats")]);
}

async function renderBosses(instanceSlug, params = new URLSearchParams()) {
  setBreadcrumb([...gameCrumbs(), t("breadcrumb.bosses")]);
  showLoading(t("loading.bosses"));

  // Combined across the servers, like a boss leaderboard (see renderLeaderboard).
  const statsQuery = new URLSearchParams({ game: currentGame });
  if (params.get("mode")) {
    statsQuery.set("mode", params.get("mode"));
  }

  // The bosses endpoint doesn't carry the instance's own name (see Backend/src/routes/instances.ts)
  // - fetched separately (the instances list is tiny) rather than adding a field there just for
  // this. Falls back to the instance's own photo only for a boss BOSS_IMAGES has no dedicated
  // portrait for (see that const's own remarks) - better than no image at all, but a real per-boss
  // photo always wins when one exists.
  const [bosses, instances, stats] = await Promise.all([
    fetchJson(`/api/instances/${encodeURIComponent(instanceSlug)}/bosses?game=${currentGame}`),
    fetchJson(`/api/instances?game=${currentGame}`),
    fetchJson(`/api/instances/${encodeURIComponent(instanceSlug)}/stats?${statsQuery}`),
  ]);
  const instance = instances.find((i) => i.slug === instanceSlug || String(i.id) === String(instanceSlug));
  if (instance) {
    setBreadcrumb([...areaCrumbs(instance), crumbWithPill(crumbInstanceName(instance.name, instance.nameEn), instanceDiffPill(instance.category, instance.name))]);
  }

  const hero = instance ? [instanceHero(instance, bosses.length, stats)] : [];
  const dungeonPath = gp(`/${instance && isWorldBossArea(instance) ? "worldbosses" : "instances"}/${instanceSlug}`);
  if (instance && stats.runCount > 0) {
    hero.push(instanceViewButtons(dungeonPath, false));
  }
  // Difficulty steps of Transcendence / Ascension / Nightmare instances: the chosen one narrows the figures above and
  // the boss cards open that step's ranking.
  const instancePath = gp(`/${instance && isWorldBossArea(instance) ? "worldbosses" : "instances"}/${instanceSlug}`);
  const modeQuery = stats.selectedMode ? `?mode=${encodeURIComponent(stats.selectedMode)}` : "";
  if (stats.modes?.length) {
    hero.push(
      el("nav", { className: "mode-chips" }, [
        el("span", { textContent: t("mode.heading") }),
        el("a", { className: `mode-chip${stats.selectedMode ? "" : " active"}`, href: instancePath, textContent: t("mode.all") }),
        ...stats.modes.map((m) => el("a", { className: `mode-chip${m.mode === stats.selectedMode ? " active" : ""}`, href: `${instancePath}?mode=${encodeURIComponent(m.mode)}` }, [modeLabel(m.mode), el("small", { textContent: String(m.runs) })])),
      ]),
    );
  }
  if (bosses.length === 0) {
    app.replaceChildren(...hero, el("p", { className: "empty", textContent: t("bosses.emptyNoBosses") }));
    return;
  }

  const instancePhoto = instance ? INSTANCE_IMAGES[instance.name] : undefined;

  // Per the user: Sauro's two keyed bosses must show 1-key before 2-key - the API's own ordering
  // is alphabetical on the raw (untranslated) name, which happens to put Sheba's ahead of
  // Ahuradim's. A tiny curated override rather than a general boss-ordering feature.
  const BOSS_SORT_OVERRIDE = { "Gardenführer Achradim": 0, "Brigade General Sheba": 1 };
  // Expedition runs (explore and conquest share the bosses): the final boss always comes last. `sequence` says the bosses
  // must be killed in exactly this order; without it the ones before the final boss may be taken in any order. The
  // badges on the cards show which case applies.
  const dungeonOrder = EXPEDITION_BOSS_ORDER[(instance?.slug ?? instanceSlug).replace(/-conquest(-hard)?$/, "")];
  const orderIndex = (b) => dungeonOrder?.bosses.indexOf(b.nameEn ?? b.name) ?? -1;
  const sortedBosses = [...bosses].sort((a, b) => {
    if (dungeonOrder && orderIndex(a) !== orderIndex(b)) {
      return orderIndex(a) - orderIndex(b);
    }
    const priority = (BOSS_SORT_OVERRIDE[a.name] ?? Infinity) - (BOSS_SORT_OVERRIDE[b.name] ?? Infinity);
    return priority !== 0 ? priority : a.name.localeCompare(b.name);
  });
  const bossBadge = (b) => {
    const index = orderIndex(b);
    if (!dungeonOrder || index < 0 || sortedBosses.length !== dungeonOrder.bosses.length) {
      return undefined;
    }
    if (index === dungeonOrder.bosses.length - 1) {
      return { text: t("boss.finalBoss"), final: true };
    }
    return { text: dungeonOrder.sequence ? t("boss.orderStep", { n: index + 1 }) : t("boss.anyOrder") };
  };

  const grid = el(
    "div",
    { className: "poster-grid" },
    sortedBosses.map((b) => posterCard(gp(`/bosses/${b.slug}${modeQuery}`), bossCards[b.name] ?? BOSS_IMAGES[b.name] ?? instancePhoto, displayName(b), bossCards[b.name] ? "center" : BOSS_IMAGES[b.name] ? "64% center" : "top", bossBadge(b))),
  );
  app.replaceChildren(...hero, el("h2", { textContent: t("bosses.heading") }), grid);
}

// Faction + class icon + name in one cell - matches myaion.eu's own combined "Player" column
// rather than the three separate ones the old accordion-based rosterTable used. Used for a single
// person (an encounter's own roster, or one solo attempt) - see groupPlayersCell below for the
// boss leaderboard's group rows, which need to show every member, not just one.
// A small round mark behind a player's name that opens the character profile - only for players
// who have one (the API says so with hasProfile). profileKind says whose upload the profile is
// ("self" = the player's own, "seen" = taken from another player's client) and profileNc whether
// NC's official character page added data; see .profile-link in style.css.
const ICON_PROFILE =
  '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><circle cx="12" cy="8" r="4"/><path d="M4 21c0-4.4 3.6-7 8-7s8 2.6 8 7"/></svg>';

function profileTitle(player) {
  if (!player.profileKind) {
    return t("player.openProfile");
  }
  const origin = t(player.profileKind === "self" ? "player.profileSelf" : "player.profileSeen");
  return player.profileNc ? `${t("player.openProfile")} · ${origin} · ${t("player.profileNc")}` : `${t("player.openProfile")} · ${origin}`;
}

function profileMarkAttrs(player) {
  const title = profileTitle(player);
  const kind = player.profileKind ? ` pf-${player.profileKind}` : "";
  return { className: `profile-link${kind}`, title, "aria-label": title, innerHTML: ICON_PROFILE + (player.profileNc ? '<i class="pf-nc"></i>' : "") };
}

function profileIcon(player) {
  if (!player || !player.hasProfile || !player.playerId) {
    return null;
  }

  return el("a", { ...profileMarkAttrs(player), href: gp(`/players/${player.playerSlug ?? player.playerId}`) });
}

function playerCell(faction, className, name, href, serverName, player) {
  // Aion 2 rankings mix servers (per the user: cross-server runs exist there), so the server rides
  // along as a tag; classic Aion's pages are always scoped to one server and need none.
  const tag = currentGame === "aion2" && serverName ? el("span", { className: "server-tag", textContent: serverLabel(serverName), title: serverLabel(serverName) }) : null;
  return el(
    "span",
    { className: "icon-label" },
    [factionIcon(faction), classIcon(className), href ? link(name, href) : name, profileIcon(player), tag].filter((x) => x != null),
  );
}

// Per the user: a top-10-groups row must show every group member, not just one "face of this
// run" - each name still links to the same encounter (there's no single-player page a group row
// could point at instead).
function groupPlayersCell(roster, encounterId) {
  return el(
    "span",
    { className: "group-players" },
    (roster ?? []).map((p) => playerCell(p.faction, p.className, p.playerName, gp(`/encounters/${encounterId}`), p.serverName, p)),
  );
}

// Real reinforcements (see the client's ChatLog/BuffCastEvent and Backend/src/skills/topBuffs.ts)
// - icon + cast count per buff, ranked by how often it was cast. Per the user: this must be actual
// buffs, not a damage/heal skill breakdown (the roster table already has that).
function buffsCell(buffs) {
  return el(
    "span",
    { className: "buffs-row" },
    (buffs ?? []).map((b) => el("span", { className: "buff-count", title: b.skillName, textContent: `${b.skillName} ${b.casts}` })),
  );
}

// One ranked entry - either one of a boss's top 10 groups, one member of a single encounter's own
// roster, or one solo attempt; all three need the same rank/player-cell/DPS/DMG/Heal shape, so all
// three render through this one row and its table wrapper. playerCellNode is a prebuilt DOM node
// (playerCell for one person, groupPlayersCell for a whole group) rather than raw fields, since a
// group row's "player" column is structurally different (many people, not one).
//
// showBuffs defaults to true but every call site currently passes false, hiding the column
// everywhere (leaderboards, and the encounter roster table it used to show on) - per the user, buff
// tracking doesn't work reliably enough yet (real Chat.log limitations: e.g. another player's own
// item-based transformation is never narrated at all, only their skill-based ones are) to keep
// showing it. Flip a call site back to true (or drop this default) once that's solid again -
// buffsCell/topBuffs/groupBuffs themselves are untouched, this only stops rendering the column.
function rankedRow(rank, playerCellNode, dps, dmg, heal, buffs, showBuffs = true) {
  return el("tr", {}, [
    el("td", { textContent: `${rank}` }),
    el("td", {}, [playerCellNode]),
    el("td", { textContent: formatNumber(dps) }),
    el("td", { textContent: formatNumber(dmg) }),
    el("td", { textContent: formatNumber(heal) }),
    ...(showBuffs ? [el("td", {}, [buffsCell(buffs)])] : []),
  ]);
}

function rankedTable(rows, showBuffs = true) {
  return el("table", { className: "ranked-table" }, [
    el("thead", {}, [
      el("tr", {}, [
        el("th", { textContent: "#" }),
        el("th", { textContent: t("table.player") }),
        el("th", { textContent: t("participant.dps") }),
        el("th", { textContent: t("table.damage") }),
        el("th", { textContent: t("participant.totalHealing") }),
        ...(showBuffs ? [el("th", { textContent: t("table.buffs") })] : []),
      ]),
    ]),
    el("tbody", {}, rows),
  ]);
}

// One tab per server that has fights for this boss (per the user: never merged across servers -
// gear standards differ completely). Tabs are real links (?server=slug), so every server's
// ranking has its own shareable address.
function serverTabs(data, bossPath) {
  if (data.servers.length === 0 || currentGame === "aion2") {
    return null;
  }
  return el("nav", { className: "server-tabs" }, [
    el("span", { className: "server-tabs-label", textContent: t("leaderboard.servers") }),
    ...data.servers.map((s) => {
      const href = s.slug ? `${bossPath}?server=${encodeURIComponent(s.slug)}` : `${bossPath}?serverId=${s.id}`;
      const a = link(s.name ?? t("serverIndicator.number", { id: s.id }), href);
      if (s.id === data.selectedServerId) {
        a.className = "active";
        a.setAttribute("aria-current", "page");
      }
      return a;
    }),
  ]);
}

/** Cinematic hero for a boss detail page (aiondps_design_pack_v1 section 7) - same real-photo-or-
 * nothing rule as instanceHero: the boss's own portrait when one exists, else its instance's photo
 * (same fallback renderBosses already uses for a boss's poster-card), else no image at all. Level
 * comes from the instance (INSTANCE_MIN_LEVEL has no per-boss entries); isSolo is real schema data,
 * not a guess. No difficulty pill - that field doesn't exist in the API yet. */
function bossHero(data) {
  const boss = data.boss;
  const photo = BOSS_IMAGES[boss.name] ?? INSTANCE_IMAGES[boss.instanceName];
  const level = INSTANCE_MIN_LEVEL[boss.instanceName];
  const pills = [];
  const stage = difficultyOf(boss.instanceCategory, data.selectedMode, boss.instanceName);
  if (stage) {
    pills.push(el("span", { className: "instance-hero-pill", textContent: stage.text }));
  }
  if (level !== undefined) {
    pills.push(el("span", { className: "instance-hero-pill", textContent: `Lv. ${level}` }));
  }
  if (boss.isSolo) {
    pills.push(el("span", { className: "instance-hero-pill", textContent: t("bosses.soloPill") }));
  }

  const children = [el("div", { className: "instance-hero-scrim" })];
  if (photo) {
    // "top", not centered: a boss portrait is a tall character render (see posterCard's own
    // objectPosition remarks) - centering would crop the face out of frame.
    const img = el("img", { src: photo, alt: "", className: "instance-hero-photo", fetchPriority: "high" });
    // The Nightmare boss banners are wide strips with the boss right of centre; other portraits are tall renders.
    img.style.objectPosition = BOSS_IMAGES[boss.name] ? "64% center" : (INSTANCE_FOCUS[boss.instanceName] ?? "50% 50%");
    children.unshift(img);
  }
  // Nightmare bosses lead back to the category's boss tree, like the breadcrumb (see renderLeaderboard).
  const isNightmare = boss.instanceCategory === "nightmare";
  const instanceLink = el("a", {
    href: isNightmare ? gp("/instances/nightmare") : gp(`${boss.instanceCategory === "worldboss" ? "/worldbosses" : "/instances"}/${boss.instanceSlug}`),
    textContent: isNightmare ? t("category.nightmare") : displayName({ name: boss.instanceName, nameEn: boss.instanceNameEn }),
    className: "instance-hero-subtitle",
  });
  children.push(
    el("div", { className: "instance-hero-content" }, [
      instanceLink,
      el("h1", { className: "instance-hero-title", textContent: displayName(boss) }),
      el("div", { className: "instance-hero-meta" }, pills),
    ]),
  );
  // The wide boss banners (1920x272) show whole at their own proportions instead of being zoomed into the taller photo hero.
  return el("div", { className: BOSS_IMAGES[boss.name] ? "instance-hero instance-hero--banner" : "instance-hero" }, children);
}

// ---- Group leaderboard: top three as cards, the rest as compact rows; sortable in the browser ----
const CLASS_EMBLEM = { Gladiator: "gladiator", Templar: "templar", Ranger: "ranger", Assassin: "assassin", Spiritmaster: "elementalist", Sorcerer: "sorcerer", Cleric: "cleric", Chanter: "chanter", Brawler: "fighter" };
const GROUP_SORTS = {
  idps: (a, b) => b.groupIDps - a.groupIDps,
  damage: (a, b) => b.totalDamage - a.totalDamage,
  time: (a, b) => a.durationSeconds - b.durationSeconds,
};
const MEDAL_COLORS = ["#f0b030", "#b9c2cc", "#d08a4a"];

// Duotone filters (one per emblem): the silver/gold emblem keeps its shading, dark -> class colour -> light.
// Added to the page once, on first use; an emblem opts in with `tint` and picks its filter via --tint.
function ensureEmblemTints() {
  if (document.getElementById("emblem-tints")) {
    return;
  }
  const mixTo = (c, target, a) => c.map((v) => v + (target - v) * a);
  const filters = Object.entries(CLASS_EMBLEM)
    .map(([className, file]) => {
      const hex = classMeta(className).color;
      const c = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255);
      const dark = mixTo(c, 0, 0.62);
      const light = mixTo(c, 1, 0.55);
      const table = (ch) => `${dark[ch].toFixed(3)} ${c[ch].toFixed(3)} ${light[ch].toFixed(3)}`;
      return `<filter id="emblem-tint-${file}" color-interpolation-filters="sRGB"><feColorMatrix type="matrix" values="0.33 0.33 0.33 0 0 0.33 0.33 0.33 0 0 0.33 0.33 0.33 0 0 0 0 0 1 0"/><feComponentTransfer><feFuncR type="table" tableValues="${table(0)}"/><feFuncG type="table" tableValues="${table(1)}"/><feFuncB type="table" tableValues="${table(2)}"/></feComponentTransfer></filter>`;
    })
    .join("");
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.id = "emblem-tints";
  svg.setAttribute("width", "0");
  svg.setAttribute("height", "0");
  svg.setAttribute("aria-hidden", "true");
  svg.style.position = "absolute";
  svg.innerHTML = filters;
  document.body.append(svg);
}

function classEmblem(className, small, tint) {
  const file = CLASS_EMBLEM[className];
  if (!file) {
    return classIcon(className);
  }
  if (tint) {
    ensureEmblemTints();
  }
  return el("img", { className: `lb-emblem${small ? " sm" : ""}${tint ? " tinted" : ""}`, src: `/images/aion2/classes/${file}.webp`, alt: classLabel(className), title: classLabel(className), loading: "lazy", style: tint ? `--tint:url(#emblem-tint-${file})` : "" });
}

function memberHref(p, encounterId) {
  return p.hasProfile && p.playerId ? gp(`/players/${p.playerId}`) : gp(`/encounters/${encounterId}`);
}

function renderGroupBoard(groups) {
  const holder = el("div", { className: "lb" });
  let sortKey = "idps";

  const draw = () => {
    const list = [...groups].sort(GROUP_SORTS[sortKey]);
    const seg = el(
      "div",
      { className: "lb-seg", role: "group" },
      [["idps", "leaderboard.sortIdps"], ["damage", "leaderboard.sortDamage"], ["time", "leaderboard.sortFastest"]].map(([key, label]) => {
        const b = el("button", { type: "button", textContent: t(label) });
        b.setAttribute("aria-selected", String(key === sortKey));
        b.addEventListener("click", () => {
          sortKey = key;
          draw();
        });
        return b;
      }),
    );

    const card = (g, i) =>
      el("article", { className: "lb-card", style: `--medal:${MEDAL_COLORS[i]}` }, [
        el("div", { className: "lb-card-head" }, [
          el("b", { className: "lb-rank", textContent: `#${i + 1}` }),
          link(formatDate(new Date(g.startedAt)), gp(`/encounters/${g.encounterId}`), "lb-open"),
          ...[partialBadge(g)].filter((x) => x != null),
          el("div", { className: "lb-big" }, [el("strong", { textContent: formatNumber(g.groupIDps) }), el("span", { textContent: t("leaderboard.groupIdps") })]),
        ]),
        el("div", { className: "lb-meta" }, [
          el("span", {}, [`${t("leaderboard.time")} `, el("b", { textContent: formatDuration(g.durationSeconds) })]),
          el("span", {}, [`${t("table.damage")} `, el("b", { textContent: formatNumber(g.totalDamage) })]),
          el("span", {}, [`${t("leaderboard.healing")} `, el("b", { textContent: formatNumber(g.totalHealing) })]),
        ]),
        el(
          "div",
          { className: "lb-chips" },
          (g.roster ?? []).map((p) =>
            el("a", { className: "lb-chip", href: memberHref(p, g.encounterId), style: `--cc:${classMeta(p.className).color}`, title: `${p.playerName} · ${p.className} · ${formatNumber(p.totalDamage)}` }, [
              classEmblem(p.className, false, true),
              el("span", { className: "lb-name", textContent: p.playerName }),
            ]),
          ),
        ),
      ]);

    const row = (g, i) =>
      el("div", { className: "lb-row" }, [
        el("span", { className: "lb-rank-small", textContent: `#${i + 4}` }),
        el("span", { className: "lb-strip" }, (g.roster ?? []).map((p) => classEmblem(p.className, true, true))),
        el("span", { className: "lb-names" }, (g.roster ?? []).flatMap((p, k) => [k > 0 ? ", " : "", link(p.playerName, memberHref(p, g.encounterId))])),
        el("span", { className: "lb-score" }, [
          el("b", { textContent: formatNumber(g.groupIDps) }),
          ` ${t("leaderboard.idpsShort")}`,
          el("br"),
          el("small", {}, [link(`${formatDuration(g.durationSeconds)} · ${new Date(g.startedAt).toLocaleString(undefined, { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit", hourCycle: "h23" })}`, gp(`/encounters/${g.encounterId}`), "lb-open"), ...[partialBadge(g)].filter((x) => x != null).map((p) => [" ", p]).flat()]),
        ]),
        el("span", { className: "lb-totals" }, [`${formatNumber(g.totalDamage)} ${t("leaderboard.dmgShort")}`, el("br"), `${formatNumber(g.totalHealing)} ${t("leaderboard.healShort")}`]),
      ]);

    holder.replaceChildren(
      el("div", { className: "lb-bar" }, [el("span", { className: "lb-bar-label", textContent: t("leaderboard.sortBy") }), seg]),
      el("section", { className: "lb-podium" }, list.slice(0, 3).map(card)),
      ...(list.length > 3 ? [el("section", { className: "lb-rest" }, list.slice(3).map(row))] : []),
    );
  };

  draw();
  return holder;
}

async function renderLeaderboard(bossSlug, params, statisticsPage = false) {
  setBreadcrumb([...gameCrumbs(), t("breadcrumb.leaderboard")]);
  showLoading(t("loading.leaderboard"));

  // An explicit ?server=/?serverId= in the address (a shared link or a tab click) narrows the
  // leaderboard to that server; otherwise it is combined across the servers.
  const query = new URLSearchParams({ game: currentGame });
  if (params.get("server")) {
    query.set("server", params.get("server"));
  } else if (params.get("serverId")) {
    query.set("serverId", params.get("serverId"));
  }
  if (params.get("mode")) {
    query.set("mode", params.get("mode"));
  }
  if (params.get("gear")) {
    query.set("gear", params.get("gear"));
  }
  const data = await fetchJson(`/api/bosses/${encodeURIComponent(bossSlug)}/leaderboard?${query}`);
  const bossPath = gp(`/bosses/${data.boss.slug}`);
  const bossName = displayName(data.boss);
  // Nightmare bosses are reached through the category's boss tree, so the way back leads there
  // (/instances/nightmare), not to the single "instance" (Root Cellar ...); every other category keeps the instance.
  const worldBoss = data.boss.instanceCategory === "worldboss";
  const instanceCrumb =
    data.boss.instanceCategory === "nightmare"
      ? link(t("category.nightmare"), gp("/instances/nightmare"))
      : crumbWithPill(link(crumbInstanceName(data.boss.instanceName, data.boss.instanceNameEn), gp(`${worldBoss ? "/worldbosses" : "/instances"}/${data.boss.instanceSlug}`)), instanceDiffPill(data.boss.instanceCategory, data.boss.instanceName));
  setBreadcrumb([...(worldBoss ? [link(t("breadcrumb.home"), "/"), link(t("category.worldboss"), gp("/worldbosses"))] : gameCrumbs()), instanceCrumb, el("span", {}, [bossName, ...[diffPill(data.boss.instanceCategory, data.selectedMode, data.boss.instanceName)].filter((x) => x != null).flatMap((p) => [" ", p])])]);

  const hero = bossHero(data);
  // Difficulty steps (Nightmare level, Ascension difficulty, ...): each one is ranked on its own.
  const modeChips = data.modes?.length
    ? el("nav", { className: "mode-chips" }, [
        el("span", { textContent: t("mode.heading") }),
        ...data.modes.map((m) => {
          const a = el("a", { className: `mode-chip${m.mode === data.selectedMode ? " active" : ""}`, href: `${bossPath}?mode=${encodeURIComponent(m.mode || "unknown")}` }, [modeLabel(m.mode), el("small", { textContent: String(m.runs) })]);
          return a;
        }),
      ])
    : null;
  const tabs = serverTabs(data, bossPath);
  const stats = fightStatsRow(data.stats);

  // Two views of one boss: the ranking (default) and the statistics (iDPS box plot per class, top 10 of each class). The two
  // switches sit at the right of the fight numbers, boxes of the same size.
  const statsView = statisticsPage || params.get("view") === "stats";
  if (params.get("view") === "stats" && !statisticsPage) {
    // the old ?view=stats address: the statistics have a path of their own now
    const keep = new URLSearchParams(params);
    keep.delete("view");
    history.replaceState(null, "", `${bossPath}/statistics${keep.toString() ? `?${keep}` : ""}`);
  }
  const viewHref = (view) => {
    const next = new URLSearchParams(params);
    next.delete("view");
    const base = view === "stats" ? `${bossPath}/statistics` : bossPath;
    return next.toString() ? `${base}?${next}` : base;
  };
  // The gear score filter keeps everything else of the address (difficulty step, server) and stays on the view that is open.
  const gearHref = (range) => {
    const next = new URLSearchParams(params);
    next.delete("view");
    range ? next.set("gear", range) : next.delete("gear");
    const base = statsView ? `${bossPath}/statistics` : bossPath;
    return next.toString() ? `${base}?${next}` : base;
  };
  // Icons with a tooltip: a podium for the ranking, a box plot for the statistics.
  const VIEW_ICONS = {
    ranking: '<svg viewBox="0 0 24 24" width="26" height="26" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 21h18M4 21v-7h5v7M9.5 21V6h5v15M15 21v-10h5v10"/></svg>',
    stats: '<svg viewBox="0 0 24 24" width="26" height="26" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 3v3M12 18v3M7 6h10v12H7zM7 12h10"/></svg>',
  };
  const viewButton = (label, href, active, iconKey) => {
    const a = el("a", { href, className: `view-button${active ? " active" : ""}`, title: label, innerHTML: VIEW_ICONS[iconKey] });
    a.setAttribute("aria-label", label);
    if (active) {
      a.setAttribute("aria-current", "page");
    }
    return a;
  };
  const viewButtons = el("nav", { className: "view-buttons" }, [viewButton(t("stats.tabRanking"), viewHref(null), !statsView, "ranking"), viewButton(t("stats.tabStats"), viewHref("stats"), statsView, "stats")]);
  const statsBar = el("div", { className: "stats-bar" }, [stats ?? el("div"), viewButtons]);
  const sections = [hero, ...(modeChips ? [modeChips] : []), statsBar];
  if (tabs) {
    sections.push(tabs);
  }

  if (statsView) {
    const statsQuery = new URLSearchParams(query);
    if (data.selectedMode !== null && data.selectedMode !== undefined) {
      statsQuery.set("mode", data.selectedMode || "unknown");
    }
    const bossStats = await fetchJson(`/api/bosses/${encodeURIComponent(bossSlug)}/stats?${statsQuery}`);
    app.replaceChildren(...sections, ...buildBossStats(bossStats, { el, t, formatNumber, classLabel, classIcon, iconLabel, playerCell, rankedRow, rankedTable, gp, getLocale, gearHref }));
    return;
  }

  // Per the user: a real group fight and a solo practice target (e.g. Training Dummy) are never
  // both at once, so the page shows exactly one of these two rankings, never both - "top 10 per
  // class" only makes sense (and only appears) for a solo boss.
  if (data.boss.isSolo) {
    const classNames = Object.keys(data.topByClass).sort();
    if (classNames.length === 0) {
      app.replaceChildren(...sections, el("p", { className: "empty", textContent: t("leaderboard.emptyNoFights") }));
      return;
    }

    const classSection = el("section", {}, [
      el("h2", { textContent: t("leaderboard.topByClassHeading") }),
      el(
        "div",
        { className: "class-grid" },
        classNames.map((className) => {
          const rows = data.topByClass[className].map((p, i) =>
            rankedRow(
              i + 1,
              playerCell(p.faction, className, p.playerName, gp(`/encounters/${p.encounterId}`), p.serverName, p),
              p.idps,
              p.totalDamage,
              p.totalHealing,
              p.topBuffs,
              false,
            ),
          );
          return el("div", { className: "class-block" }, [
            el("h3", {}, [iconLabel(classIcon(className), classLabel(className))]),
            rankedTable(rows, false),
          ]);
        }),
      ),
    ]);

    app.replaceChildren(...sections, classSection);
    return;
  }

  const gearBars = buildGearFilters(data, gearHref, { el, t, formatNumber });
  if (data.topGroups.length === 0) {
    app.replaceChildren(...sections, ...gearBars, el("p", { className: "empty", textContent: t(data.gear?.selected || data.power?.selected ? "gear.empty" : "leaderboard.emptyNoFights") }));
    return;
  }

  app.replaceChildren(...sections, el("h3", {}, [t("leaderboard.topGroupsHeading", { n: data.topGroups.length })]), ...gearBars, renderGroupBoard(data.topGroups));
}

// m:ss - short enough to sit next to "Zeitpunkt"/"App Version" in a two-column meta table, unlike
// the full duration-implying-precision formatting a stopwatch library would produce.
function formatDuration(totalSeconds) {
  const rounded = Math.round(totalSeconds);
  const minutes = Math.floor(rounded / 60);
  const seconds = rounded % 60;
  return `${minutes}:${String(seconds).padStart(2, "0")}`;
}

// The database stamps its own columns (createdAt, updatedAt) as UTC "2026-10-04 15:35:14", without a zone: a
// browser reads that as LOCAL time, which made a fresh run look two hours old in Germany. Everything the API
// sends as a timestamp goes through this.
function parseServerTime(value) {
  const text = String(value);
  return new Date(/^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d$/.test(text) ? `${text.replace(" ", "T")}Z` : text);
}

// "vor 2h" style relative time for the homepage's recent-activity feed - real elapsed time from
// the encounter's own createdAt, never a made-up freshness label.
function formatRelativeTime(iso) {
  const minutes = Math.round((Date.now() - parseServerTime(iso).getTime()) / 60000);
  if (minutes < 1) {
    return t("time.justNow");
  }
  if (minutes < 60) {
    return t("time.minutesAgo", { count: minutes });
  }
  const hours = Math.round(minutes / 60);
  if (hours < 24) {
    return t("time.hoursAgo", { count: hours });
  }
  return t("time.daysAgo", { count: Math.round(hours / 24) });
}

function hexToRgba(hex, alpha) {
  const v = hex.replace("#", "");
  const r = parseInt(v.substring(0, 2), 16);
  const g = parseInt(v.substring(2, 4), 16);
  const b = parseInt(v.substring(4, 6), 16);
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}

const ROLE_LABEL_KEYS = {
  healer: "participant.roleHealer",
  tank: "participant.roleTank",
  dd: "participant.roleDd",
  companion: "participant.roleCompanion",
};

function roleLabel(role) {
  return t(ROLE_LABEL_KEYS[role] ?? ROLE_LABEL_KEYS.dd);
}

// One ranked row of the encounter's own Schaden/Heilung meter (see meterPanel) - the whole row is
// the link (to that participant's own breakdown), not just the name, for a bigger click target
// than the old roster table had. Bar fill width is relative to the CURRENT metric's top value
// (recomputed by meterPanel on every toggle), never a fixed scale.
// The meter row is one big link to the participant's fight page, so its profile icon cannot be a
// nested anchor - it navigates on its own click instead.
function meterProfileLink(p) {
  if (!p.hasProfile || !p.playerId) {
    return null;
  }

  const target = gp(`/players/${p.playerId}`);
  const icon = el("span", { ...profileMarkAttrs(p), role: "link", tabindex: "0" });
  const open = (event) => {
    event.preventDefault();
    event.stopPropagation();
    navigate(target);
  };
  icon.addEventListener("click", open);
  icon.addEventListener("keydown", (event) => {
    if (event.key === "Enter") {
      open(event);
    }
  });
  return icon;
}

function meterRow(rank, p, metric, max, opts = {}) {
  const meta = classMeta(p.className);
  const value = p[metric];
  const pct = max > 0 ? (value / max) * 100 : 0;
  // Damage taken: a red bar (the group shields' share is hatched behind the part that got through, see aggroPanel), not the class colour.
  const absorbed = opts.danger ? (p.damageAbsorbed ?? 0) : 0;
  const fills = opts.danger
    ? [
        el("div", { className: "meter-row-fill meter-row-fill-danger", style: `width: ${pct.toFixed(1)}%;` }),
        absorbed > 0 ? el("div", { className: "meter-row-fill meter-row-fill-shield", style: `left: ${pct.toFixed(1)}%; width: ${max > 0 ? ((absorbed / max) * 100).toFixed(1) : 0}%;` }) : null,
      ].filter((x) => x != null)
    : [el("div", { className: "meter-row-fill", style: `width: ${pct.toFixed(1)}%; background: ${hexToRgba(meta.color, 0.28)};` })];
  const isPet = p.className === "?";
  const serverTag = currentGame === "aion2" && p.serverName ? el("span", { className: "server-tag", textContent: serverLabel(p.serverName), title: serverLabel(p.serverName) }) : null;

  return el(
    "a",
    { className: "meter-row" + (isPet ? " meter-row-pet" : ""), href: participantHref(p.playerSlug, p.participantId) },
    [
      ...fills,
      el("div", { className: "meter-row-content" }, [
        el("span", { className: "meter-rank" + (rank === 1 ? " meter-rank-top" : ""), textContent: `${rank}` }),
        ...[factionIcon(p.faction), classIcon(p.className)].filter((x) => x != null),
        el("span", { className: "meter-name", textContent: p.playerName }),
        meterProfileLink(p),
        serverTag,
        (p.role ?? meta.role)
          ? el("span", { className: "meter-role-badge", style: `color: ${meta.color}; background: ${hexToRgba(meta.color, 0.16)};`, textContent: roleLabel(p.role ?? meta.role) })
          : null,
        el("span", { className: "meter-value", textContent: formatNumber(value) }),
      ].filter((x) => x != null)),
    ],
  );
}

// Toggleable Schaden/Heilung ranking - replaces the old flat roster table with meter bars (per
// the user: easier to scan at a glance than a column of raw numbers), and folds the metric switch
// into one panel instead of two separate always-visible tables.
function meterPanel(roster) {
  assignRoles(roster);
  let metric = "totalDamage";
  const petCount = roster.filter((p) => p.className === "?").length;
  const petsSuffix = petCount > 0 ? t("encounter.petsSuffix", { count: petCount }) : "";

  const titleEl = el("span", { className: "meter-panel-title" });
  const subtitleEl = el("span", { className: "meter-panel-subtitle" });
  const listEl = el("div", { className: "meter-list" });

  const dmgBtn = el("button", { type: "button", className: "active", textContent: t("table.damage") });
  const healBtn = el("button", { type: "button", textContent: t("participant.totalHealing") });
  dmgBtn.setAttribute("aria-pressed", "true");
  healBtn.setAttribute("aria-pressed", "false");

  function renderRows() {
    const sorted = [...roster].sort((a, b) => b[metric] - a[metric]);
    const max = Math.max(...sorted.map((p) => p[metric]), 1);
    titleEl.textContent = metric === "totalDamage" ? t("table.damage") : t("participant.totalHealing");
    subtitleEl.textContent = t("encounter.rosterSubtitle", {
      count: roster.length,
      pets: petsSuffix,
      sorted: t(metric === "totalDamage" ? "encounter.sortedByDamage" : "encounter.sortedByHealing"),
    });
    listEl.replaceChildren(...sorted.map((p, i) => meterRow(i + 1, p, metric, max)));
  }

  function selectMetric(newMetric, activeBtn, inactiveBtn) {
    if (metric === newMetric) {
      return;
    }
    metric = newMetric;
    activeBtn.classList.add("active");
    activeBtn.setAttribute("aria-pressed", "true");
    inactiveBtn.classList.remove("active");
    inactiveBtn.setAttribute("aria-pressed", "false");
    renderRows();
  }
  dmgBtn.addEventListener("click", () => selectMetric("totalDamage", dmgBtn, healBtn));
  healBtn.addEventListener("click", () => selectMetric("totalHealing", healBtn, dmgBtn));
  renderRows();

  return el("div", { className: "meter-panel" }, [
    el("div", { className: "meter-panel-header" }, [
      el("div", { className: "meter-panel-heading" }, [titleEl, subtitleEl]),
      el("div", { className: "category-filter" }, [dmgBtn, healBtn]),
    ]),
    listEl,
  ]);
}

// ---- Players panel with the buff columns (encounters whose uploads carry buff data) ----------------------------------------------------------------------

const BUFF_OWN_MAX = 8;
const BUFF_GROUP_SLOTS = 10;
const BUFF_SCROLL_SLOTS = 8;
const BUFF_DIM_BELOW = 300; // thousandths: a buff that ran less than 30 % of the fight is shown dimmed

function buffLabel(b) {
  return b.names?.[getLocale()] ?? b.names?.en ?? b.name;
}

function buffTile(b, roster, owner) {
  const label = buffLabel(b);
  const tip =
    t("encounter.buffTip", { name: label, uptime: (b.uptimePermille / 10).toFixed(0), casts: b.casts }) +
    (b.givers.length > 0 ? t("encounter.buffTipGivers", { givers: b.givers.join(", ") }) : "");
  const tile = el("span", { className: "bf-tile" + (b.uptimePermille < BUFF_DIM_BELOW ? " dim" : ""), title: tip });
  const src = b.icon && b.iconKind ? `/images/aion2/icons/${b.iconKind}/${b.icon}.webp` : null;
  if (src) {
    const img = el("img", { className: "bf-icon", src, alt: "", loading: "lazy", width: 64, height: 64 });
    img.addEventListener("error", () => {
      img.remove();
      tile.prepend(el("span", { className: "bf-initials", textContent: label.slice(0, 2) }));
    });
    tile.append(img);
  } else {
    tile.append(el("span", { className: "bf-initials", textContent: label.slice(0, 2) }));
  }
  if (b.casts > 1) {
    tile.append(el("s", { className: "bf-count", textContent: String(Math.min(b.casts, 99)) }));
  }
  if (b.kind === "group") {
    const giver = b.givers.length > 0 ? roster.find((p) => p.playerName === b.givers[0]) : owner;
    if (giver && giver.className !== "?") {
      tile.append(el("u", { className: "bf-badge" }, [classIcon(giver.className)]));
    }
  }
  return tile;
}

// Group and consumable buffs sit in fixed slots, the same buff in the same place for every player (a gap where a player did not have it);
// all buff food shares one slot (only one can be active at a time). Own buffs are class specific and are listed freely.
function buffSlotKeys(roster, kind) {
  const stats = new Map();
  for (const p of roster) {
    for (const b of p.buffs ?? []) {
      if (b.kind !== kind || (kind === "consumable" && b.food)) {
        continue;
      }
      const e = stats.get(b.name) ?? { n: 0, up: 0 };
      e.n += 1;
      e.up = Math.max(e.up, b.uptimePermille);
      stats.set(b.name, e);
    }
  }
  const keys = [...stats.entries()].sort((a, b) => b[1].n - a[1].n || b[1].up - a[1].up).map(([k]) => k);
  if (kind === "consumable") {
    const hasFood = roster.some((p) => (p.buffs ?? []).some((b) => b.kind === "consumable" && b.food));
    return [...keys.slice(0, BUFF_SCROLL_SLOTS), ...(hasFood ? ["\u0000food"] : [])];
  }
  return keys.slice(0, BUFF_GROUP_SLOTS);
}

function buffCell(p, roster, kind, slotKeys) {
  const mine = (p.buffs ?? []).filter((b) => b.kind === kind);
  let tiles;
  if (kind === "own") {
    tiles = mine.slice(0, BUFF_OWN_MAX).map((b) => buffTile(b, roster, p));
  } else {
    tiles = slotKeys.map((key) => {
      const pool = key === "\u0000food" ? mine.filter((b) => b.food) : mine.filter((b) => b.name === key && !b.food);
      const b = pool.sort((a, c) => c.uptimePermille - a.uptimePermille)[0];
      return b ? buffTile(b, roster, p) : el("span", { className: "bf-tile bf-gap" });
    });
  }
  return el("td", { className: "pp-buffs" }, [el("div", { className: "bf-cell" }, tiles)]);
}

const PLAYER_METRICS = [
  { key: "totalDamage", label: () => t("table.damage"), sorted: () => t("encounter.sortedByDamage"), danger: false },
  { key: "totalHealing", label: () => t("participant.totalHealing"), sorted: () => t("encounter.sortedByHealing"), danger: false },
  { key: "damageTaken", label: () => t("encounter.aggroHeading"), sorted: () => t("encounter.sortedByTaken"), danger: true },
];

// Replaces the damage list and the damage-taken list: one row per player with the damage / healing / damage-taken bar (switchable) and the buff columns.
function playersPanel(roster) {
  assignRoles(roster);
  let metric = PLAYER_METRICS[0];
  const slots = { group: buffSlotKeys(roster, "group"), consumable: buffSlotKeys(roster, "consumable") };
  const petCount = roster.filter((p) => p.className === "?").length;
  const petsSuffix = petCount > 0 ? t("encounter.petsSuffix", { count: petCount }) : "";

  const titleEl = el("span", { className: "meter-panel-title" });
  const subtitleEl = el("span", { className: "meter-panel-subtitle" });
  const body = el("tbody");
  const buttons = PLAYER_METRICS.map((m) => {
    const b = el("button", { type: "button", textContent: m.label() });
    b.addEventListener("click", () => select(m));
    return b;
  });

  function totalOf(p) {
    return metric.danger ? (p.damageTaken ?? 0) + (p.damageAbsorbed ?? 0) : p[metric.key];
  }

  function renderRows() {
    const sorted = [...roster].sort((a, b) => totalOf(b) - totalOf(a));
    const max = Math.max(...sorted.map(totalOf), 1);
    titleEl.textContent = metric.label();
    subtitleEl.textContent = t("encounter.rosterSubtitle", { count: roster.length, pets: petsSuffix, sorted: metric.sorted() });
    body.replaceChildren(
      ...sorted.map((p, i) =>
        el("tr", {}, [
          el("td", { className: "pp-main" }, [meterRow(i + 1, p, metric.key, max, { danger: metric.danger })]),
          buffCell(p, roster, "own", []),
          buffCell(p, roster, "group", slots.group),
          buffCell(p, roster, "consumable", slots.consumable),
        ]),
      ),
    );
  }

  function select(m) {
    metric = m;
    buttons.forEach((b, i) => {
      const on = PLAYER_METRICS[i] === m;
      b.classList.toggle("active", on);
      b.setAttribute("aria-pressed", String(on));
    });
    renderRows();
  }

  const table = el("table", { className: "players-table" }, [
    el("thead", {}, [
      el("tr", {}, [
        el("th", { textContent: t("table.player") }),
        el("th", { className: "r", textContent: t("encounter.buffsOwn") }),
        el("th", { className: "r", textContent: t("encounter.buffsGroup") }),
        el("th", { className: "r", textContent: t("encounter.buffsConsumable") }),
      ]),
    ]),
    body,
  ]);
  select(metric);
  return el("div", { className: "meter-panel players-panel" }, [
    el("div", { className: "meter-panel-header" }, [
      el("div", { className: "meter-panel-heading" }, [titleEl, subtitleEl]),
      el("div", { className: "category-filter" }, buttons),
    ]),
    table,
    el("p", { className: "buff-legend", textContent: t("encounter.buffLegend") }),
  ]);
}

// Damage TAKEN from the boss, i.e. aggro/tanking - a different question from the Schaden panel
// above (who hit the boss) and kept visually distinct (compact rows, danger-tinted bars, its own
// subheading) so the two are never mistaken for the same ranking, unlike the old page's two
// same-looking bar charts. damageTaken is 0 for every row from a client older than the field
// itself (see uploadSchema.ts) - such an encounter just renders an all-zero list rather than
// erroring.
function aggroPanel(roster, protectionOnly = false) {
  // Group shields (Chanter, Templar): damageAbsorbed is what the shields on a player soaked up, drawn
  // as a hatched blue section behind the part that got through. Empty until the client reports it;
  // without any absorbed damage the panel looks exactly as before.
  const withShield = roster.some((p) => (p.damageAbsorbed ?? 0) > 0);
  const totalOf = (p) => p.damageTaken + (p.damageAbsorbed ?? 0);
  const sorted = [...roster].sort((a, b) => totalOf(b) - totalOf(a));
  const max = Math.max(...sorted.map(totalOf), 1);
  const rows = sorted.map((p) => {
    const absorbed = p.damageAbsorbed ?? 0;
    const label = absorbed > 0 ? t("encounter.absorbedLabel", { amount: formatNumber(absorbed), pct: Math.round((absorbed / totalOf(p)) * 100) }) : "";
    return el("a", { className: "meter-row meter-row-compact", href: participantHref(p.playerSlug, p.participantId), title: label ? `${formatNumber(p.damageTaken)} · ${label}` : "" }, [
      el("div", { className: "meter-row-fill meter-row-fill-danger", style: `width: ${((p.damageTaken / max) * 100).toFixed(1)}%;` }),
      absorbed > 0 ? el("div", { className: "meter-row-fill meter-row-fill-shield", style: `left: ${((p.damageTaken / max) * 100).toFixed(1)}%; width: ${((absorbed / max) * 100).toFixed(1)}%;` }) : null,
      el("div", { className: "meter-row-content" }, [
        el("span", { className: "meter-name", textContent: p.playerName }),
        el("span", { className: "meter-value", textContent: formatNumber(p.damageTaken) }),
      ]),
    ].filter((x) => x != null));
  });
  const legend = withShield
    ? el("div", { className: "shield-legend" }, [
        el("span", {}, [el("i", { className: "sl-taken" }), t("encounter.legendTaken")]),
        el("span", {}, [el("i", { className: "sl-shield" }), t("encounter.legendShield")]),
      ])
    : null;
  const panels = protectionOnly ? [] : [
    el("div", { className: "meter-panel" }, [
      el("div", { className: "meter-panel-heading" }, [
        el("span", { className: "meter-panel-title", textContent: t("encounter.aggroHeading") }),
        el("span", { className: "meter-panel-subtitle", textContent: t("encounter.aggroSubheading") }),
      ]),
      legend,
      el("div", { className: "meter-list meter-list-compact", style: "margin-top: 14px" }, rows),
    ].filter((x) => x != null)),
  ];

  // Who cast shields and on whom - only players who actually did; nothing at all when no one did.
  const casters = roster.filter((p) => (p.shieldsGiven ?? []).some((g) => g.amount > 0));
  if (casters.length > 0) {
    panels.push(
      el("div", { className: "meter-panel" }, [
        el("div", { className: "meter-panel-heading" }, [
          el("span", { className: "meter-panel-title", textContent: t("encounter.protectionHeading") }),
          el("span", { className: "meter-panel-subtitle", textContent: t("encounter.protectionSub") }),
        ]),
        el("div", { className: "shield-cards" }, casters.map((p) => {
          const given = [...p.shieldsGiven].sort((a, b) => b.amount - a.amount);
          const sum = given.reduce((s, g) => s + g.amount, 0);
          return el("div", { className: "shield-card" }, [
            el("div", { className: "shield-card-head" }, [classIcon(p.className), el("b", { textContent: p.playerName })]),
            el("div", { className: "shield-card-sum", textContent: formatNumber(sum) }),
            el("ul", {}, given.map((g) => el("li", {}, [el("span", { textContent: g.playerName }), el("span", { textContent: formatNumber(g.amount) })]))),
          ]);
        })),
      ]),
    );
  }
  return el("div", {}, panels);
}

async function renderEncounter(encounterId) {
  setBreadcrumb([...gameCrumbs(), t("loading.encounter")]);
  showLoading(t("loading.encounter"));

  const data = await fetchJson(`/api/encounters/${encodeURIComponent(encounterId)}`);
  setBreadcrumb([...bossCrumbs(data.encounter), t("breadcrumb.run", { id: data.encounter.id })]);

  const bossName = translateGameName(data.encounter.bossName);
  const pills = [
    el("span", { className: "instance-hero-pill", textContent: formatDate(new Date(data.encounter.startedAt)) }),
    el("span", {
      className: "instance-hero-pill",
      textContent: `${t("encounter.appVersion")} ${data.encounter.appVersion ?? t("encounter.appVersionUnknown")}`,
    }),
  ];
  // The difficulty label (Nightmare 10, Conquest 3, World Boss, ...) leads the pills.
  const difficulty = difficultyOf(data.encounter.instanceCategory, data.encounter.mode, data.encounter.instanceName);
  if (difficulty) {
    pills.unshift(el("span", { className: "instance-hero-pill", textContent: difficulty.text }));
  } else if (data.encounter.mode && !["explore", "conquest"].includes(data.encounter.mode)) {
    pills.unshift(el("span", { className: "instance-hero-pill", textContent: modeLabel(data.encounter.mode) }));
  }
  // Same picture as the boss page: the boss's own banner, else the instance photo.
  const bossBanner = BOSS_IMAGES[data.encounter.bossName];
  const photo = bossBanner ?? INSTANCE_IMAGES[data.encounter.instanceName] ?? null;
  const heroChildren = [el("div", { className: "instance-hero-scrim" })];
  if (photo) {
    const img = el("img", { src: photo, alt: "", className: "instance-hero-photo", fetchPriority: "high" });
    img.style.objectPosition = bossBanner ? "64% center" : (INSTANCE_FOCUS[data.encounter.instanceName] ?? "50% 50%");
    heroChildren.unshift(img);
  }
  heroChildren.push(
    el("div", { className: "instance-hero-content" }, [
      el("h1", { className: "instance-hero-title", textContent: bossName }),
      el("div", { className: "instance-hero-meta" }, pills),
    ]),
  );
  const hero = el("div", { className: bossBanner ? "instance-hero instance-hero--banner" : "instance-hero" }, heroChildren);

  const realPlayerCount = data.roster.filter((p) => p.className !== "?").length;
  const petCount = data.roster.length - realPlayerCount;
  const totalDamage = data.roster.reduce((sum, p) => sum + p.totalDamage, 0);
  const statsRow = el("div", { className: "stats-row" }, [
    statCard(formatNumber(data.encounter.groupIDps), t("stats.groupIdps")),
    statCard(`${realPlayerCount}`, t("stats.participants"), petCount > 0 ? t("stats.participantsNote", { count: petCount }) : undefined),
    statCard(formatDuration(data.encounter.durationSeconds), t("encounter.duration")),
    statCard(formatNumber(totalDamage), t("stats.totalDamage")),
  ]);
  // A fight uploaded by someone who joined late: one orange card with the share of the boss's health the recorder saw (old data without the fields is simply not partial).
  if (data.encounter.isPartial === true) {
    const share = typeof data.encounter.hpSeenShare === "number" ? data.encounter.hpSeenShare : null;
    const realMax = data.encounter.bossRealMaxHp > 0 ? data.encounter.bossRealMaxHp : null;
    let tip = t("encounter.partialTipPlain");
    if (share !== null) {
      const pct = (share * 100).toFixed(1);
      tip = realMax !== null
        ? t("encounter.partialTip", { pct, seen: formatNumber(Math.round(share * realMax)), max: formatNumber(realMax) })
        : t("encounter.partialTipNoHp", { pct });
    }
    const card = statCard(share !== null ? `${(share * 100).toFixed(1)} %` : t("encounter.partialBadge"), t("encounter.partialLabel"));
    card.classList.add("partial");
    card.title = tip;
    statsRow.append(card);
  }
  // A crowded world boss: the damage is partly estimated, so the share of the boss's hit points the list adds up to is shown (the server asks for 75 %).
  if (data.encounter.bossMaxHp > 0 && data.roster.some((p) => p.estimated)) {
    statsRow.append(statCard(`${((totalDamage / data.encounter.bossMaxHp) * 100).toFixed(0)}%`, t("stats.bossHpCovered"), t("stats.bossHpCoveredNote")));
  }

  // An encounter whose uploads carry buff data gets the players panel with the buff columns (it also holds the damage-taken list);
  // an older one keeps the damage list with the damage-taken panel beside it.
  const hasBuffs = data.roster.some((p) => (p.buffs ?? []).length > 0);
  const grid = hasBuffs
    ? el("div", { className: "encounter-stack" }, [playersPanel(data.roster), aggroPanel(data.roster, true)])
    : el("div", { className: "encounter-grid" }, [meterPanel(data.roster), el("div", { className: "encounter-side-col" }, [aggroPanel(data.roster)])]);

  // The compare link is an icon tile at the far end of the stats row.
  statsRow.append(compareTile(t("compare.ctaRuns"), compareLink("/compare/runs", { a: data.encounter.id })));
  // The other bosses of the same run as tabs above the lists; a tab is the page of that boss's fight.
  const run = data.run ?? [];
  const bossTabs = run.length > 1
    ? el("nav", { className: "boss-tabs", "aria-label": t("encounter.runBosses") }, run.map((r) => {
        const photo = bossCards[r.bossNameEn] ?? bossCards[r.bossName] ?? BOSS_IMAGES[r.bossNameEn] ?? BOSS_IMAGES[r.bossName];
        return el("a", { className: "boss-tab", href: gp(`/encounters/${r.encounterId}`), "aria-current": r.encounterId === data.encounter.id ? "page" : null }, [
          el("span", { className: "boss-tab-thumb", style: photo ? `background-image:url('${smallPhoto(photo)}')` : "" }),
          el("span", {}, [el("b", { textContent: translateGameName(r.bossName) }), el("small", { textContent: `${formatDuration(r.durationSeconds)} · ${formatNumber(r.groupIDps)} iDPS` })]),
        ]);
      }))
    : null;
  app.replaceChildren(...[hero, statsRow, bossTabs, grid].filter((x) => x != null));

  // Fights whose uploads carry a time axis get the group timeline and the buffs per skill below the lists (a failed request just leaves them out).
  if (data.encounter.hasTimeline) {
    try {
      const timeline = await fetchJson(`/api/encounters/${encodeURIComponent(encounterId)}/timeline`);
      const deps = { el, t, formatNumber, classLabel, classIcon, iconLabel, getLocale };
      // The health curve is thousandths of the highest reading the recorder saw, so it starts at 100 % even when they joined late;
      // a partial fight draws it against the boss's real health instead (the seen share is where it starts).
      if (data.encounter.isPartial === true && typeof data.encounter.hpSeenShare === "number" && Array.isArray(timeline.bossHp)) {
        timeline.bossHp = timeline.bossHp.map(([s, permille]) => [s, Math.round(permille * data.encounter.hpSeenShare)]);
      }
      const panels = [buildGroupTimeline(timeline, deps), buildFocus(timeline, deps), buildBossDebuffs(timeline, deps), buildBuffsPerSkill(timeline, deps)].filter((p) => p !== null);
      grid.after(...panels);
    } catch {
      // the lists above stay as they are
    }
  }
}

function skillTable(skills) {
  const rows = skills.map((s) =>
    el("tr", {}, [
      el("td", { textContent: s.skillName }),
      el("td", { textContent: formatNumber(s.hits) }),
      el("td", { textContent: formatNumber(s.critHits) }),
      el("td", { textContent: s.hits > 0 ? `${((s.critHits / s.hits) * 100).toFixed(1)}%` : "-" }),
      el("td", { textContent: formatNumber(s.totalDamage) }),
      el("td", { textContent: s.hits > 0 ? formatNumber(s.totalDamage / s.hits) : "-" }),
      el("td", { textContent: formatNumber(s.maxHit) }),
    ]),
  );
  return el("table", {}, [
    el("thead", {}, [
      el("tr", {}, [
        el("th", { textContent: t("skillTable.skill") }),
        el("th", { textContent: t("skillTable.hits") }),
        el("th", { textContent: t("skillTable.crits") }),
        el("th", { textContent: t("skillTable.critPercent") }),
        el("th", { textContent: t("skillTable.total") }),
        el("th", { textContent: t("skillTable.avg") }),
        el("th", { textContent: t("skillTable.max") }),
      ]),
    ]),
    el("tbody", {}, rows),
  ]);
}

// One skill as a table row: game icon (initials when the client has none), hit statistics, share
// bar relative to the strongest skill and the total.
function skillRow(s, total, top, heal) {
  const hits = s.hits > 0 ? s.hits : 1;
  const name = s.names?.[getLocale()] ?? s.skillName;
  const tile = el("span", { className: "pt-ico" });
  if (s.icon) {
    const img = el("img", { src: `/images/aion2/icons/skill/${s.icon}.webp`, alt: "", loading: "lazy", width: 128, height: 128 });
    img.addEventListener("error", () => {
      img.remove();
      tile.textContent = name.slice(0, 2);
    });
    tile.append(img);
  } else {
    tile.textContent = name.slice(0, 2);
  }
  const num = (text) => el("span", { className: "num", textContent: text });
  return el("div", { className: "pt-tr" }, [
    tile,
    el("b", { textContent: name }),
    num(formatNumber(s.hits)),
    num(`${s.hits > 0 ? ((s.critHits / s.hits) * 100).toFixed(0) : 0}%`),
    num(formatNumber(s.totalDamage / hits)),
    num(formatNumber(s.maxHit)),
    num(`${total > 0 ? ((s.totalDamage / total) * 100).toFixed(1) : "0.0"}%`),
    el("div", { className: `pt-bar${heal ? " heal" : ""}` }, [el("i", { style: `width:${top > 0 ? (s.totalDamage / top) * 100 : 0}%` })]),
    el("b", { className: "num", textContent: formatNumber(s.totalDamage) }),
  ]);
}

/** A fight-detail address: "<player slug>-<participant id>" (name and server make it readable, the id picks the fight). */
function participantHref(slug, participantId) {
  return gp(`/participants/${slug ? `${slug}-` : ""}${participantId}`);
}

async function renderParticipant(param) {
  setBreadcrumb([...gameCrumbs(), t("loading.participant")]);
  showLoading(t("loading.participant"));

  // The address is "<player slug>-<participant id>" (name and server make it readable, the id picks the fight); a bare id still works.
  const participantId = /(\d+)$/.exec(param)?.[1] ?? param;
  const data = await fetchJson(`/api/participants/${encodeURIComponent(participantId)}`);
  setBreadcrumb([...bossCrumbs(data.encounter), data.participant.playerName]);
  // Reached through a bare-id link: show the name address instead (no reload, no history entry).
  const wanted = data.participant.playerSlug ? `${data.participant.playerSlug}-${data.participant.id}` : String(data.participant.id);
  if (decodeURIComponent(location.pathname.split("/").pop()) !== wanted) {
    history.replaceState(history.state, "", `${participantHref(data.participant.playerSlug, data.participant.id)}${location.search}${location.hash}`);
  }

  const p = data.participant;
  const e = data.encounter;
  const minutes = Math.floor(e.durationSeconds / 60);
  const seconds = String(Math.round(e.durationSeconds % 60)).padStart(2, "0");
  const tags = [el("span", { className: "pt-tag", textContent: p.className })];
  if (p.faction) {
    tags.push(el("span", { className: "pt-tag" }, [factionIcon(p.faction), p.faction]));
  }
  if (p.serverName) {
    tags.push(el("span", { className: "pt-tag", textContent: serverLabel(p.serverName) }));
  }
  const stat = (label, value, cls) => el("div", { className: "pt-sk" }, [el("span", { textContent: label }), el("b", { className: cls ?? "", textContent: value })]);
  const side = el("aside", { className: "pt-side" }, [
    el("span", { className: "pt-emb" }, [classEmblem(p.className, false)]),
    el("h2", {}, [p.playerName, profileIcon({ hasProfile: true, playerId: p.playerId, playerSlug: p.playerSlug, profileKind: p.profileKind, profileNc: p.profileNc })]),
    el("div", { className: "pt-tags" }, tags),
    el("div", { className: "pt-sub" }, [translateGameName(e.bossName), " ", diffPill(e.instanceCategory, e.mode, e.instanceName), ` · ${formatDate(new Date(e.startedAt))} · ${minutes}:${seconds}`].filter((x) => x != null)),
    stat(t("participant.dps"), formatNumber(p.dps), "accent"),
    stat(t("table.damage"), formatNumber(p.totalDamage)),
    stat(t("participant.critRate"), `${p.critRatePercent.toFixed(1)}%`),
    stat(t("participant.hps"), formatNumber(p.hps), "heal"),
    stat(t("participant.totalHealing"), formatNumber(p.totalHealing)),
    el("a", { className: "cmp-btn pt-cmp", href: compareLink("/compare/players", { a: p.playerId, boss: e.bossId }), textContent: `⇄ ${t("compare.ctaPlayers")}` }),
  ]);
  const main = el("div", { className: "pt-maincol" });
  const group = (heading, list, heal) => {
    const total = list.reduce((sum, s) => sum + s.totalDamage, 0);
    const top = list.reduce((m, s) => Math.max(m, s.totalDamage), 0);
    const th = (label, num) => el("span", { className: num ? "num" : "", textContent: label });
    main.append(
      el("h3", { className: `pt-mh${heal ? " heal" : ""}`, textContent: heading }),
      el("div", { className: "pt-tbl" }, [
        el("div", { className: "pt-tr pt-th" }, [el("span"), th(t("skillTable.skill")), th(t("skillTable.hits"), true), th(t("skillTable.critPercent"), true), th(t("skillTable.avg"), true), th(t("skillTable.max"), true), th(t("skillTable.share"), true), el("span"), th(t("skillTable.total"), true)]),
        ...list.map((s) => skillRow(s, total, top, heal)),
      ]),
    );
  };
  if (data.damageSkills.length > 0) {
    group(t("participant.damageSkillsHeading"), data.damageSkills, false);
  }
  if (data.healSkills.length > 0) {
    group(t("participant.healSkillsHeading"), data.healSkills, true);
  }

  app.replaceChildren(el("div", { className: "pt-lay" }, [el("div", { className: "pt-sidecol" }, [el("div", { className: "pt-lh", textContent: t("participant.playerHeading") }), side]), main]));
}

// Daevanion stat tokens come from the game data as CamelCase ("CriticalResist"); shown spaced out.
function statLabel(token) {
  return token
    .replace(/^Pv([PE])/, "Pv$1 ")
    .replace(/([a-z])([A-Z])/g, (m, a, b) => (a === "v" && /^[PE]$/.test(b) ? m : `${a} ${b}`))
    .replace(/ +/g, " ")
    .trim();
}

// Skill names come in the game client's languages (de, en, es, fr, ja, ko, pt, ru); the site's other
// languages (pl, tr, zh, zh-TW) fall back to the English name.
function localizedSkillName(skill) {
  return skill.names?.[getLocale()] ?? skill.name;
}

// ---- Player page (Aion 2): the character strip stays on top, the content below is split into tabs
// (runs, equipment, skills, Daevanion boards). Everything is resolved server-side from ids (see
// Backend/src/profile.ts); skills and Daevanion only exist when the player's own client uploaded
// them. Icons are the game's own textures (images/aion2/icons); an id without one gets a tile with
// the name's initials instead.
const ICON_BASE = "/images/aion2/icons";
// Rarity colours as the game paints them (the grade letters in its UI atlas): silver, green, blue, gold,
// orange; the names are the client's own enum (Legend is the blue one, Unique the gold one).
const GRADE_COLOR = { 1: "#aab2bd", 2: "#4cc46a", 3: "#3a9be8", 4: "#f0b030", 5: "#f07a20", 6: "#d94f4f", 7: "#2fd0c0" };
const GRADE_NAME = { 1: "Common", 2: "Rare", 3: "Legend", 4: "Unique", 5: "Epic", 6: "Mythic", 7: "Special" };
const GEAR_GROUPS = [
  ["armor", ["Helmet", "Shoulder", "Torso", "Gloves", "Pants", "Boots", "Cape", "Belt"]],
  ["accessories", ["Necklace", "Earring", "Ring", "Bracelet", "Amulet", "Brooch", "Pendant"]],
];

// A square item/skill tile: the real icon when there is one, initials otherwise. `badge` is the
// small number in the corner (enchant level, skill level).
function iconTile(kind, icon, name, { color, badge, badgeClass, zenit } = {}) {
  const tile = el("span", { className: "pf-ico", style: color ? `--ico-color:${color}` : "" });
  if (icon) {
    const img = el("img", { src: `${ICON_BASE}/${kind}/${icon}.webp`, alt: "", loading: "lazy", width: 128, height: 128 });
    img.addEventListener("error", () => {
      img.remove();
      tile.classList.add("noimg");
      tile.dataset.initials = name.slice(0, 2);
    });
    tile.append(img);
  } else {
    tile.classList.add("noimg");
    tile.dataset.initials = name.slice(0, 2);
  }
  if (badge != null && badge !== "") {
    tile.append(el("b", { className: `pf-badge-num ${badgeClass ?? ""}`, textContent: String(badge) }));
  }
  if (zenit > 0) {
    tile.append(el("b", { className: "pf-zenit", textContent: String(zenit) }));
  }
  return tile;
}

// One tooltip for the whole page, shown next to the pointer over anything with a `tip` builder. `enhance` (optional) is asked for
// something fuller while the pointer stays: a promise of the nodes to show instead (or null to keep the first ones).
function attachTooltip(target, build, enhance) {
  let tip = document.querySelector(".pf-tip");
  if (!tip) {
    tip = el("div", { className: "pf-tip", hidden: true });
    document.body.append(tip);
  }
  let last = null;
  let over = false;
  const place = (e) => {
    last = e;
    const w = tip.offsetWidth;
    const h = tip.offsetHeight;
    tip.style.left = `${Math.min(e.clientX + 16, window.innerWidth - w - 8)}px`;
    tip.style.top = `${Math.max(8, Math.min(e.clientY + 12, window.innerHeight - h - 8))}px`;
  };
  target.addEventListener("mouseenter", (e) => {
    over = true;
    tip.classList.remove("pf-tip-wide");
    tip.replaceChildren(...build());
    tip.hidden = false;
    place(e);
    enhance?.().then((nodes) => {
      if (over && nodes) {
        tip.classList.add("pf-tip-wide");
        tip.replaceChildren(...nodes);
        if (last) {
          place(last);
        }
      }
    });
  });
  target.addEventListener("mousemove", place);
  target.addEventListener("mouseleave", () => {
    over = false;
    tip.hidden = true;
  });
}

// Zenit: an enchant byte of 16..20 is +15 with 1..5 Zenit stages (the backend sends `zenit`); the plain level is capped at 15 then.
function shownEnchant(g) {
  return g.zenit > 0 ? 15 : g.enchant;
}

function gearTip(g) {
  const name = localizedSkillName(g);
  const color = GRADE_COLOR[g.grade] ?? GRADE_COLOR[1];
  return [
    el("div", { className: "pf-tip-head", style: `--ico-color:${color}` }, [
      el("div", { className: "pf-tip-title", textContent: name + (g.enchant > 0 ? ` +${shownEnchant(g)}` : "") }),
      el("div", { className: "pf-tip-sub" }, [el("span", { style: `color:${color}`, textContent: `${GRADE_NAME[g.grade] ?? ""}${g.tier > 0 ? ` · ${t("profile.tier")} ${g.tier}` : ""}` }), ` ${g.slotName ? t(`slot.${g.slotName}`) : ""}`]),
      g.itemLevel > 0 ? el("div", { className: "pf-tip-il", textContent: `${t("profile.itemLevel")} ${g.itemLevel}` }) : null,
    ].filter((x) => x != null)),
    ...zenitTip(g),
    ...gearTipDetails(g),
  ];
}

// The rolled stats and mana stone slots of a piece (only when the uploader's client read them). A stone's own
// amount is not part of the game's data, so a stone shows its stat, coloured by its tier (1 white, 2 green, 3 blue).
function zenitTip(g) {
  if (!(g.zenit > 0)) {
    return [];
  }
  const rows = [el("div", { className: "pf-tip-row" }, [el("span", { textContent: `${t("profile.zenit")} ${t("profile.tier")} ${g.zenit}` })])];
  // the orange values of a weapon are derived from the stage (10 / 1 % per stage), see the backend
  if (g.zenitBonus) {
    rows.push(
      el("div", { className: "pf-tip-row pf-tip-orange" }, [el("span", { textContent: t("profile.zenitAttack") }), el("strong", { textContent: String(g.zenitBonus.attack) })]),
      el("div", { className: "pf-tip-row pf-tip-orange" }, [el("span", { textContent: t("profile.zenitBoost") }), el("strong", { textContent: `${g.zenitBonus.damageBoostPercent} %` })]),
    );
  }
  return [el("div", { className: "pf-tip-sec" }, rows)];
}

function gearTipDetails(g) {
  const locale = getLocale();
  const statName = (x) => x.names?.[locale] ?? x.names?.en ?? x.name;
  const sections = [];
  // The rolled stats and the skill bonuses of a piece are one block, as in the game and in the tooltip built from NC's page: a skill belongs there as much as Might or Critical Hit does.
  const rolled = [
    ...(g.stats ?? []).map((x) => el("div", { className: "pf-tip-row" }, [el("span", { textContent: statName(x) }), el("strong", { textContent: speciesValue(x) })])),
    ...(g.skillBonuses ?? []).map((x) => el("div", { className: "pf-tip-row pf-tip-skill" }, [el("span", { textContent: x.names?.[locale] ?? x.name }), el("strong", { textContent: `${t("profile.levelAbbr")} +${x.level}` })])),
  ];
  if (rolled.length > 0) {
    sections.push(
      el("div", { className: "pf-tip-sec" }, [
        g.stats?.length ? el("div", { className: "pf-tip-sec-title", textContent: t("profile.rolledStats") }) : null,
        ...rolled,
      ].filter((x) => x != null)),
    );
  }
  if (g.stones?.length) {
    sections.push(
      el("div", { className: "pf-tip-sec" }, [
        el("div", { className: "pf-tip-sec-title", textContent: t("profile.stones") }),
        ...g.stones.map((x) => (x.stat > 0
          ? el("div", { className: `pf-tip-row pf-tip-stone q${x.tier}` }, [el("span", { textContent: statName(x) }), x.value != null ? el("strong", { textContent: `+${x.value}` }) : null].filter((n) => n != null))
          : el("div", { className: "pf-tip-row pf-tip-stone empty" }, [el("span", { textContent: t("profile.stoneEmpty") })]))),
      ]),
    );
  }
  if (g.godstone) {
    sections.push(el("div", { className: "pf-tip-sec" }, [el("div", { textContent: g.godstone.names?.[locale] ?? g.godstone.names?.en ?? g.godstone.name })]));
  }
  return sections;
}

// The piece as NC's own item page shows it (base stats with the enchant bonus, soul bind, rolled stats with their range, stone values,
// the god stone's text, where it drops), asked for from the server when the pointer rests on a piece. Only players NC knows have it;
// for everybody else (and while the server's NC budget is used up) the tooltip stays as the player's own client reported it.
let gearPlayerId = null;
const ncTipCache = new Map();
const STONE_Q = { Common: 1, Rare: 2, Legend: 3, Unique: 4 };

function ncGearTip(g) {
  if (gearPlayerId == null) {
    return Promise.resolve(null);
  }
  const key = `${gearPlayerId}:${g.slot}:${getLocale()}`;
  let hit = ncTipCache.get(key);
  if (!hit) {
    hit = fetch(`/api/players/${gearPlayerId}/items/${g.slot}?lang=${encodeURIComponent(getLocale())}`)
      .then((res) => (res.ok ? res.json() : null))
      .catch(() => null);
    ncTipCache.set(key, hit);
    // a miss (busy, no data) is asked again after a minute, a hit stays
    hit.then((tip) => {
      if (!tip) {
        setTimeout(() => ncTipCache.delete(key), 60_000);
      }
    });
  }
  return hit.then((tip) => (tip ? ncTipNodes(g, tip) : null));
}

function ncTipNodes(g, n) {
  const color = GRADE_COLOR[g.grade] ?? GRADE_COLOR[1];
  const row = (label, value, cls, extra) => el("div", { className: `pf-tip-row${cls ? ` ${cls}` : ""}` }, [el("span", { textContent: label }), value != null ? el("strong", {}, [value, extra ?? ""]) : null].filter((x) => x != null));
  const range = (s) => (s.min && s.max && s.min !== s.max ? el("span", { className: "pf-tip-range", textContent: ` (${s.min}–${s.max}${parseFloat(s.value) >= parseFloat(s.max) ? `, ${t("profile.maxRoll")}` : ""})` }) : "");
  const sections = [];
  const level = n.level ?? g.itemLevel;
  sections.push(
    el("div", { className: "pf-tip-head", style: `--ico-color:${color}` }, [
      el("div", { className: "pf-tip-title", textContent: n.name + (g.enchant > 0 ? ` +${shownEnchant(g)}` : "") }),
      el("div", { className: "pf-tip-sub" }, [el("span", { style: `color:${color}`, textContent: n.gradeName || (GRADE_NAME[g.grade] ?? "") }), ` · ${[n.categoryName, g.slotName ? t(`slot.${g.slotName}`) : null].filter((x) => x).join(" · ")}`]),
      level > 0 ? el("div", { className: "pf-tip-il", textContent: `${t("profile.itemLevel")} ${level}${n.levelBonus != null ? ` (+${n.levelBonus})` : ""}` }) : null,
    ].filter((x) => x != null)),
  );
  if (n.mainStats.length > 0) {
    sections.push(
      el("div", { className: "pf-tip-sec" }, n.mainStats.map((s) => row(s.name, s.min ? `${s.min}–${s.value}` : s.value, null, s.bonus ? el("span", { className: "pf-tip-orange", textContent: ` (+${s.bonus})` }) : ""))),
    );
  }
  sections.push(...zenitTip(g));
  if (n.soulBind != null) {
    sections.push(
      el("div", { className: "pf-tip-sec" }, [
        el("div", { className: "pf-tip-row" }, [el("span", { className: "pf-tip-soul", textContent: t("profile.soulBind") }), el("strong", { className: "pf-tip-gold", textContent: `${n.soulBind.toFixed(1)} %` })]),
        el("div", { className: "pf-tip-bar" }, [el("i", { style: `width:${n.soulBind}%` })]),
      ]),
    );
  }
  if (n.rolled.length > 0 || n.skills.length > 0) {
    sections.push(
      el("div", { className: "pf-tip-sec" }, [
        n.rolled.length > 0 ? el("div", { className: "pf-tip-sec-title", textContent: t("profile.rolledStats") }) : null,
        ...n.rolled.map((s) => row(s.name, s.value, null, range(s))),
        ...n.skills.map((s) => row(s.name, `${t("profile.levelAbbr")} +${s.level}`, "pf-tip-skill")),
      ].filter((x) => x != null)),
    );
  }
  if (n.stones.length > 0) {
    sections.push(
      el("div", { className: "pf-tip-sec" }, [
        el("div", { className: "pf-tip-sec-title", textContent: t("profile.stones") }),
        ...n.stones.map((s) => row(s.name, s.value, `pf-tip-stone q${STONE_Q[s.grade] ?? 1}`)),
      ]),
    );
  }
  for (const god of n.godStones) {
    sections.push(
      el("div", { className: "pf-tip-sec" }, [
        el("div", { className: `pf-tip-stone q${STONE_Q[god.grade] ?? 2}`, style: "font-weight:700", textContent: god.name }),
        el("div", { className: "pf-tip-desc", textContent: god.desc }),
      ]),
    );
  }
  const foot = [];
  if (n.sources.length > 0) {
    foot.push(el("div", { className: "pf-tip-src" }, [el("span", { textContent: t("profile.source") }), el("span", { textContent: n.sources.join(" · ") })]));
  }
  if (n.equipLevel) {
    foot.push(el("div", { className: "pf-tip-src" }, [el("span", { textContent: t("profile.equipLevel") }), el("span", { textContent: String(n.equipLevel) })]));
  }
  const tags = [...n.classNames, n.tradable === false ? t("profile.notTradable") : null].filter((x) => x);
  if (tags.length > 0) {
    foot.push(el("div", { className: "pf-tip-tags" }, tags.map((x) => el("span", { className: "pf-tip-tag", textContent: x }))));
  }
  if (foot.length > 0) {
    sections.push(el("div", { className: "pf-tip-sec" }, foot));
  }
  return sections;
}

function gearSlot(g) {
  const name = localizedSkillName(g);
  const color = GRADE_COLOR[g.grade] ?? GRADE_COLOR[1];
  const slot = el("div", { className: "pf-slot", style: `--ico-color:${color}` }, [
    iconTile("item", g.icon, name, { color, badge: g.enchant > 0 ? `+${shownEnchant(g)}` : "", zenit: g.zenit }),
    el("div", {}, [
      el("div", { className: "pf-slot-name", textContent: name }),
      el("div", { className: "pf-slot-sub", textContent: `${g.slotName ? t(`slot.${g.slotName}`) : `#${g.slot}`}${g.itemLevel > 0 ? ` · ${t("profile.itemLevelShort")} ${g.itemLevel}` : ""}` }),
      g.grade >= 4 && g.tier > 0 ? el("span", { className: "pf-tier", textContent: `${GRADE_NAME[g.grade] ?? ""} ${t("profile.tier")} ${g.tier}` }) : null,
    ].filter((x) => x != null)),
  ]);
  attachTooltip(slot, () => gearTip(g), () => ncGearTip(g));
  return slot;
}

function renderEquipmentTab(profile) {
  const rank = (names, g) => {
    const i = names.indexOf(g.slotName);
    return i < 0 ? 99 : i;
  };
  const groups = { armor: [], accessories: [], other: [] };
  for (const g of profile.gear.filter((x) => x.slotName !== "Arcana")) {
    const home = GEAR_GROUPS.find(([, names]) => names.includes(g.slotName));
    groups[home ? home[0] : "other"].push(g);
  }
  for (const [key, names] of GEAR_GROUPS) {
    groups[key].sort((a, b) => rank(names, a) - rank(names, b) || a.slot - b.slot);
  }
  const col = (items) => el("div", { className: "pf-col" }, items.map(gearSlot));
  return el("div", { className: "pf-doll" }, [
    col(groups.armor),
    el("div", { className: "pf-center" }, [
      el("div", { className: "pf-center-badge" }, [profile.className ? classIcon(profile.className) : null].filter((x) => x != null)),
      el("div", { className: "pf-center-num", textContent: String(profile.averageItemLevel ?? "–") }),
      el("div", { className: "pf-sub", textContent: t("profile.avgShort") }),
      el("div", { className: "pf-sub small", textContent: t("profile.hoverHint") }),
    ]),
    col(groups.accessories),
    groups.other.length > 0 ? el("div", { className: "pf-weapons" }, groups.other.map(gearSlot)) : null,
  ].filter((x) => x != null));
}

// The five arcana slots are ordinary items in the inspect gear list (slots 25-29); the number on the
// tile is the arcana's level (+N).
// The own ranks the game's ranking window showed (Abyss, Nightmare ...): rank, how it changed, the points and the stats beside them. As of the moment the uploader opened that window.
const RANK_TYPES = { 1: "abyss", 3: "nightmare", 4: "transcendence", 5: "arena1", 6: "arena5", 21: "ascension" };
const RANK_STATS = ["kill_count", "death_count", "assist_count", "play_count", "win_count", "lose_count"];

function renderRanksTab(profile) {
  const rows = profile.ranks.map((r) => {
    const name = t(`ranks.type.${RANK_TYPES[r.type] ?? "other"}`, { n: r.type });
    const scope = t(r.scope === 2 ? "ranks.scopeClass" : "ranks.scopeAll");
    const change = r.change === 0 ? "–" : r.change > 0 ? `▲ ${formatNumber(r.change)}` : `▼ ${formatNumber(-r.change)}`;
    const stats = r.stats ? RANK_STATS.filter((k) => k in r.stats).map((k) => `${t(`ranks.stat.${k}`)} ${formatNumber(r.stats[k])}`).join(" · ") : "";
    return el("tr", {}, [
      el("td", { textContent: name }),
      el("td", { textContent: scope }),
      el("td", { textContent: `#${formatNumber(r.rank)}` }),
      el("td", { className: r.change > 0 ? "rank-up" : r.change < 0 ? "rank-down" : "", textContent: change }),
      el("td", { textContent: formatNumber(r.points) }),
      el("td", { textContent: stats }),
      el("td", { textContent: formatDate(new Date(r.at)) }),
    ]);
  });
  return el("div", {}, [
    el("table", { className: "ranked-table compact" }, [
      el("thead", {}, [el("tr", {}, ["ranks.content", "ranks.scope", "ranks.rank", "ranks.change", "ranks.points", "ranks.stats", "ranks.asOf"].map((k) => el("th", { textContent: t(k) })))]),
      el("tbody", {}, rows),
    ]),
    el("p", { className: "profile-source", textContent: t("ranks.note") }),
  ]);
}

// The own Pantheon, drawn the way the game's window shows it: twelve paintings on the outer ring (slot 1-2 top right, then clockwise in pairs), four statues in
// hexagons, the colossus in the middle; beside it the sums of the decorations set up (one setup at a time), the decorations by slot and, folded away, the whole
// collection. Names, effects and icons come from the game's tables on the server; a decoration it does not know shows its number.
const PANTHEON_FRAME_ANGLES = [36, 53, 82, 98, 127, 144, 216, 233, 262, 278, 307, 324];
const PANTHEON_STATUE_SPOTS = { 13: [33, 33], 14: [33, 67], 15: [67, 67], 16: [67, 33] };

function renderPantheon(pan) {
  const locale = getLocale();
  const pick = (names) => names?.[locale] ?? names?.en ?? "";
  const statName = (stat) => pick(pan.statNames[stat]) || stat;
  const effectsText = (effects) => effects.map((e) => `${statName(e.stat)} ${e.value}`).join(" · ");
  const decoName = (d) => pick(d.names) || `#${d.id}`;
  const kinds = ["frame", "statue", "colossus"];

  const ring = el("div", { className: "pf-pan-ring" });
  const drawRing = (preset) => {
    const bySlot = new Map(preset.slots.map((d) => [d.slot, d]));
    const nodes = [];
    for (let slot = 1; slot <= 17; slot++) {
      const d = bySlot.get(slot);
      const kind = slot <= 12 ? "frame" : slot <= 16 ? "statue" : "colossus";
      const [x, y] = slot <= 12
        ? [50 + 44.5 * Math.sin((PANTHEON_FRAME_ANGLES[slot - 1] * Math.PI) / 180), 50 - 44.5 * Math.cos((PANTHEON_FRAME_ANGLES[slot - 1] * Math.PI) / 180)]
        : slot <= 16 ? PANTHEON_STATUE_SPOTS[slot] : [50, 50];
      const node = el("div", { className: `pf-pan-slot ${kind}${d ? "" : " empty"}` });
      node.style.left = `${x}%`;
      node.style.top = `${y}%`;
      if (d) {
        node.style.setProperty("--g", GRADE_COLOR[d.grade] ?? "#8a93a0");
        if (d.icon) {
          node.append(el("img", { src: `/images/aion2/pantheon/${d.icon}.webp`, alt: decoName(d), loading: "lazy" }));
        } else {
          node.append(el("span", { textContent: decoName(d).replace(/^[^:：]*[:：]\s*/, "").slice(0, 2) }));
        }

        attachTooltip(node, () => [
          el("div", { className: "pf-tip-title", textContent: decoName(d) }),
          ...d.effects.map((e) => el("div", { className: "pf-tip-row" }, [el("span", { textContent: `${statName(e.stat)} ${e.value}` })])),
        ]);
      }

      nodes.push(node);
    }

    ring.replaceChildren(...nodes);
  };

  const sums = el("div", { className: "pf-pan-sums" });
  const slotsBox = el("div", { className: "pf-pan-slots" });
  const draw = (preset) => {
    drawRing(preset);
    sums.replaceChildren(
      el("h4", { textContent: t("pantheon.effects") }),
      preset.totals.length > 0
        ? el("table", { className: "pf-pan-totals" }, [el("tbody", {}, preset.totals.map((x) => el("tr", {}, [el("td", { textContent: pick(x.names) || x.stat }), el("td", { textContent: String(x.value) })])))])
        : el("p", { className: "profile-source", textContent: t("pantheon.empty") }),
    );
    slotsBox.replaceChildren(
      ...kinds
        .map((kind) => [kind, preset.slots.filter((d) => d.kind === kind).sort((a, b) => a.slot - b.slot)])
        .filter(([, list]) => list.length > 0)
        .map(([kind, list]) => el("div", { className: "pf-pan-group" }, [
          el("h4", { textContent: `${t(`pantheon.kind.${kind}`)} (${list.length})` }),
          ...list.map((d) => el("div", { className: "pf-pan-row" }, [el("span", { textContent: decoName(d) }), el("small", { textContent: effectsText(d.effects) })])),
        ])),
    );
  };

  const buttons = pan.presets.map((preset) =>
    el("button", { type: "button", textContent: t("pantheon.preset", { n: preset.preset }) }, [el("small", { textContent: String(preset.slots.length) })]),
  );
  const choose = (i) => {
    buttons.forEach((b, k) => b.setAttribute("aria-selected", String(k === i)));
    draw(pan.presets[i]);
  };
  buttons.forEach((b, i) => b.addEventListener("click", () => choose(i)));
  const placedCount = pan.owned.filter((d) => d.placed).length;
  const collection = pan.owned.length > 0
    ? el("details", { className: "pf-pan-owned" }, [
      el("summary", { textContent: t("pantheon.owned", { n: pan.owned.length, placed: placedCount }) }),
      ...kinds.map((kind) => [kind, pan.owned.filter((d) => d.kind === kind).sort((a, b) => decoName(a).localeCompare(decoName(b), locale))]).filter(([, list]) => list.length > 0).map(([kind, list]) =>
        el("div", { className: "pf-pan-group" }, [
          el("h4", { textContent: `${t(`pantheon.kind.${kind}`)} (${list.length})` }),
          ...list.map((d) => el("div", { className: `pf-pan-row${d.placed ? " placed" : ""}` }, [el("span", { textContent: decoName(d) }), el("small", { textContent: effectsText(d.effects) })])),
        ])),
    ])
    : null;
  if (pan.presets.length > 0) {
    choose(0);
  }

  return el("section", { className: "pf-pan" }, [
    pan.presets.length > 1 ? el("div", { className: "pf-pan-tabs", role: "tablist" }, buttons) : null,
    el("div", { className: "pf-pan-body" }, [ring, sums, slotsBox]),
    collection,
    el("p", { className: "profile-source", textContent: t("pantheon.note") }),
  ].filter((x) => x != null));
}

// What the game's tables say about arcana (arcana.json, Tools/aion2-dat/build_arcana.py): the experience a piece needs per level (only the five
// base card types, Grail to Mirror) and the effects of the sets, in the game client's languages. Loaded when the tab is drawn; until it arrived the
// pieces simply show their experience without a limit and no set effect.
let arcanaTables = { data: null, pending: null };

function loadArcanaTables() {
  arcanaTables.pending ??= fetch("/arcana.json")
    .then((r) => (r.ok ? r.json() : null))
    .then((data) => {
      arcanaTables.data = data;
    })
    .catch(() => {
      arcanaTables.data = null;
    });
  return arcanaTables.pending;
}

const LORD_KEYS = { 7: "justice", 8: "freedom", 9: "illusion", 10: "life", 11: "time" };

function renderArcanaTab(profile) {
  const list = profile.gear.filter((g) => g.slotName === "Arcana").sort((a, b) => a.slot - b.slot);
  // Details (experience, god stat, skill bonuses) only exist for a character whose own client uploaded them; otherwise the plain tiles.
  if (!list.some((g) => g.arcana)) {
    return el("div", { className: "pf-arcana" }, list.map(gearSlot));
  }
  const body = el("div", { className: "pf-arc" });
  const draw = () => body.replaceChildren(...arcanaBlocks(profile, list));
  draw();
  if (!arcanaTables.data) {
    loadArcanaTables().then(() => arcanaTables.data && draw());
  }
  return body;
}

function arcanaBlocks(profile, list) {
  const locale = getLocale();
  const tables = arcanaTables.data;
  const lang = tables?.sets && ["de", "en", "es", "fr", "ja", "ko", "pt", "ru"].includes(locale) ? locale : "en";
  const sumBonus = (pieces) => {
    const total = new Map();
    for (const g of pieces) {
      for (const b of g.arcana?.skillBonuses ?? g.skillBonuses ?? []) {
        total.set(b.skill, (total.get(b.skill) ?? 0) + b.level);
      }
    }
    return total;
  };
  const arcanaBonus = sumBonus(list);
  const gearBonus = sumBonus(profile.gear.filter((g) => g.slotName !== "Arcana"));
  const trained = new Map((profile.skills ?? []).map((k) => [k.id, k]));
  const skillName = (id, names, name) => names?.[locale] ?? name;
  const names = new Map(list.flatMap((g) => (g.arcana?.skillBonuses ?? []).map((b) => [b.skill, b])));

  // the final level of a skill is its trained level plus the equipment, the arcana and further sources; without the skill list only the parts known from the pieces
  const skillTip = (id) => {
    const known = trained.get(id);
    const fromGear = gearBonus.get(id) ?? 0;
    const fromArcana = arcanaBonus.get(id) ?? 0;
    const entry = names.get(id);
    const rows = [el("div", { className: "pf-tip-title", textContent: skillName(id, entry?.names, entry?.name ?? String(id)) })];
    const line = (text) => el("div", { className: "pf-tip-row" }, [el("span", { textContent: text })]);
    if (known) {
      rows.push(line(t("arcana.tipTotal", { n: known.level })), line(t("arcana.tipBase", { n: known.baseLevel > 0 ? known.baseLevel : known.level })));
    }
    rows.push(line(t("arcana.tipGear", { n: fromGear })), line(t("arcana.tipArcana", { n: fromArcana })));
    const other = known && known.baseLevel > 0 ? known.level - known.baseLevel - fromGear - fromArcana : 0;
    if (other > 0) {
      rows.push(line(t("arcana.tipOther", { n: other })));
    }
    return rows;
  };

  const skillRow = (b, level) => {
    const row = el("div", { className: "pf-arc-skill" }, [
      el("span", { className: "pf-arc-skname", textContent: skillName(b.skill, b.names, b.name) }),
      el("strong", { className: level >= 3 ? "hi" : "", textContent: `${t("profile.levelAbbr")} +${level}` }),
    ]);
    attachTooltip(row, () => skillTip(b.skill));
    return row;
  };

  const expLimit = (g) => {
    const card = Math.floor(g.itemId / 100000) % 100;
    if (!tables || Math.floor(g.itemId / 10_000_000) !== 81 || card < 1 || card > 5) {
      return { limit: null, maxed: false };
    }
    const row = tables.expPerLevel?.[String(g.grade)];
    const value = row?.[g.enchant];
    return { limit: value > 0 ? value : null, maxed: Array.isArray(row) && value === 0 };
  };

  const piece = (g) => {
    const a = g.arcana;
    const color = GRADE_COLOR[g.grade] ?? GRADE_COLOR[1];
    const name = localizedSkillName(g);
    const head = el("div", { className: "pf-arc-head" }, [
      iconTile("item", g.icon, name, { color, badge: g.enchant > 0 ? `+${shownEnchant(g)}` : "" }),
      el("div", {}, [
        el("div", { className: "pf-slot-name", textContent: name }),
        el("div", { className: "pf-slot-sub", textContent: g.itemLevel > 0 ? `${t("slot.Arcana")} · ${t("profile.itemLevelShort")} ${g.itemLevel}` : t("slot.Arcana") }),
      ]),
    ]);
    attachTooltip(head, () => gearTip(g));
    const card = el("div", { className: "pf-slot pf-arc-card", style: `--ico-color:${color}` }, [head]);
    if (a) {
      const { limit, maxed } = expLimit(g);
      card.append(
        el("div", { className: "pf-arc-exp" }, [
          el("span", { textContent: t("arcana.reinforcement") }),
          el("strong", { textContent: maxed ? t("arcana.max") : limit ? `${formatNumber(a.exp)} / ${formatNumber(limit)}` : formatNumber(a.exp) }),
        ]),
      );
      if (limit) {
        card.append(el("div", { className: "pf-arc-bar" }, [el("i", { style: `width:${Math.min(100, (a.exp / limit) * 100)}%` })]));
      }
      const lord = LORD_KEYS[a.god];
      if (lord) {
        card.append(
          el("div", { className: "pf-arc-god" }, [
            el("span", {}, [el("strong", { textContent: t(`attr.${lord}`) }), el("small", { textContent: ` [${t(`lord.${lord}`)}]` })]),
            el("strong", { textContent: String(a.godValue) }),
          ]),
        );
      }
      card.append(...a.skillBonuses.map((b) => skillRow(b, b.level)));
    }
    return card;
  };

  // the effects of the sets the pieces belong to; a set counts the pieces of it that are worn
  const sets = new Map();
  for (const g of list) {
    const key = g.itemId % 10000;
    sets.set(key, (sets.get(key) ?? 0) + 1);
  }
  const setCards = [...sets]
    .map(([key, count]) => [tables?.sets?.[String(key).padStart(3, "0")], count])
    .filter(([set]) => set)
    .map(([set, count]) =>
      el("div", { className: "pf-arc-set" }, [
        el("div", { className: "pf-arc-set-title" }, [el("h3", { textContent: set.name[lang] ?? set.name.en }), el("span", { textContent: `${count} / ${list.length}` })]),
        ...set.effects.map((e) =>
          el("div", { className: `pf-arc-eff${count >= e.pieces ? "" : " off"}` }, [el("em", { textContent: String(e.pieces) }), el("span", { textContent: e.text[lang] ?? e.text.en })]),
        ),
      ]),
    );
  const lords = list.filter((g) => g.arcana && LORD_KEYS[g.arcana.god]);
  const top = el("div", { className: "pf-arc-top" }, [
    ...setCards,
    lords.length > 0
      ? el("div", { className: "pf-card pf-arc-lords" }, [
          el("h4", { textContent: t("profile.attrLords") }),
          el("div", { className: "pf-arc-lordrow" }, lords.map((g) => {
            const key = LORD_KEYS[g.arcana.god];
            return el("div", { title: `${t(`attr.${key}`)} [${t(`lord.${key}`)}]` }, [attrIcon(key, true), el("strong", { textContent: String(g.arcana.godValue) }), el("small", { textContent: t(`attr.${key}`) })]);
          })),
        ])
      : null,
  ].filter((x) => x != null));

  const sum = [...arcanaBonus].sort((x, y) => y[1] - x[1] || skillName(x[0], names.get(x[0])?.names, names.get(x[0])?.name).localeCompare(skillName(y[0], names.get(y[0])?.names, names.get(y[0])?.name), locale));
  const sumCard = el("div", { className: "pf-card pf-arc-sum" }, [
    el("h4", { textContent: t("arcana.sum") }),
    ...sum.map(([id, level]) => skillRow(names.get(id) ?? { skill: id, name: String(id) }, level)),
  ]);
  return [top, el("div", { className: "pf-arc-grid" }, [el("div", { className: "pf-arc-pieces" }, list.map(piece)), sumCard])];
}

// The three lists the skills tab shows; the tab counts and the header count use the same lists.
function visibleSkills(profile, skills = profile.skills) {
  const hasBar = skills.some((s) => s.equipped);
  return {
    // With a skill bar in the profile only the equipped actives are listed, as in the game's skill window.
    active: skills.filter((s) => !s.passive && !s.stigma && (!hasBar || s.equipped)),
    // ids like 11000000 are the class's weapon-equip entry, not a skill the player trains
    passive: skills.filter((s) => s.passive && s.id % 1000000 !== 0),
    // a stigma at level 0 is one the player has not learned: the game's list carries it, the page does not show it
    stigma: skills.filter((s) => s.stigma && s.level > 0),
  };
}

function visibleSkillCount(profile) {
  const v = visibleSkills(profile);
  return v.active.length + v.passive.length + v.stigma.length;
}

// ---- Specialisation texts: the five stones of an active skill and the effects of a stigma (levels 5, 10, 15, 20), from the game
// client's own texts (Web-Frontend/skill-spec/<language>.json, { skill id: [text x 5] }). Loaded when the skills tab is drawn;
// a tooltip opened before it arrived simply lacks that part.
const SPEC_LANGUAGES = ["de", "en", "es", "fr", "ja", "ko", "pt", "ru"];
const STONE_BASE = "/images/aion2/stones";
const STIGMA_EFFECT_LEVELS = [5, 10, 15, 20];
// A slot of an active skill opens with the skill's level 8, 12 and 20 (only the third one is known to stay shut below its level).
let skillSpec = { lang: "", data: null, pending: null };

function loadSkillSpec() {
  const lang = SPEC_LANGUAGES.includes(getLocale()) ? getLocale() : "en";
  if (skillSpec.lang !== lang) {
    skillSpec = { lang, data: null, pending: null };
  }
  skillSpec.pending ??= fetch(`/skill-spec/${lang}.json`)
    .then((r) => (r.ok ? r.json() : {}))
    .then((data) => {
      skillSpec.data = data;
    })
    .catch(() => {
      skillSpec.data = {};
    });
  return skillSpec.pending;
}

function skillSpecTexts(skill) {
  return skillSpec.data?.[String(skill.id)] ?? null;
}

// The stones carry the emblem of the class (the game's stigma icons per class); a class without a set shows the Gladiator one.
const STONE_CLASSES = ["gladiator", "templar", "ranger", "assassin", "spiritmaster", "sorcerer", "cleric", "chanter"];
let stoneClass = "";

function stoneImg(number, size) {
  const dir = STONE_CLASSES.includes(stoneClass) ? `${STONE_BASE}/${stoneClass}` : STONE_BASE;
  return el("img", { className: "pf-stone-img", src: `${dir}/stone${number}.webp`, alt: "", width: size, height: size, loading: "lazy" });
}

// The three slots of an active skill as small shields under its icon: a stone, an empty slot or (the third) a shut one.
function stoneSlotState(skill, index) {
  const stone = skill.stones?.[index] ?? 0;
  if (stone > 0) {
    return "stone";
  }
  return index === 2 && skill.level < 20 ? "locked" : "empty";
}

function stoneRow(skill, name) {
  const row = el("div", { className: "pf-stones" });
  for (let i = 0; i < 3; i++) {
    const state = stoneSlotState(skill, i);
    const slot = el("span", { className: `pf-stone ${state}` }, state === "stone" ? [stoneImg(skill.stones[i], 17)] : []);
    if (state === "stone") {
      attachTooltip(slot, () => stoneTip(skill, name, i));
    }
    row.append(slot);
  }
  return row;
}

function stoneText(skill, stone) {
  return skillSpecTexts(skill)?.[stone - 1] || "";
}

function stoneTip(skill, name, index) {
  const stone = skill.stones[index];
  return [
    el("div", { className: "pf-tip-head", style: "--ico-color:#f0a030" }, [
      el("div", { className: "pf-tip-stonehead" }, [
        stoneImg(stone, 24),
        el("div", {}, [
          el("div", { className: "pf-tip-title", textContent: stoneText(skill, stone) || `${t("profile.skillSpecial")} ${stone}` }),
          el("div", { className: "pf-tip-sub", textContent: `${name} · ${t("profile.specialSlot", { n: index + 1 })}` }),
        ]),
      ]),
    ]),
  ];
}

// The tooltip of a skill: name, base level with the bonus beside it, and what its specialisations do. An active skill lists its
// three slots and all five stones (the chosen ones lit); a stigma lists the four effects it unlocks by level.
function skillTip(skill, name) {
  const base = skill.baseLevel > 0 ? skill.baseLevel : skill.level;
  const bonus = skill.level - base;
  const sections = [
    el("div", { className: "pf-tip-head", style: `--ico-color:${skill.passive ? "#3fcf55" : "#f0a030"}` }, [
      el("div", { className: "pf-tip-title", textContent: name }),
      el("div", { className: "pf-tip-sub" }, [
        `${t("profile.skillLevelLong")} ${base}`,
        bonus > 0 ? el("b", { className: "pf-tip-bonus", textContent: ` (+${bonus})` }) : null,
      ].filter((x) => x != null)),
    ]),
  ];
  const texts = skillSpecTexts(skill);
  if (skill.stigma && texts) {
    sections.push(
      el("div", { className: "pf-tip-sec" }, STIGMA_EFFECT_LEVELS.map((need, i) => {
        const on = skill.level >= need;
        return el("div", { className: `pf-spec-row gold ${on ? "on" : "off"}` }, [
          el("span", { className: "pf-eff" }, [
            el("img", { src: `${STONE_BASE}/effect.webp`, alt: "", width: 20, height: 20 }),
            on ? null : el("b", { textContent: String(need) }),
          ].filter((x) => x != null)),
          el("span", { className: "pf-spec-text", textContent: texts[i] || "" }),
          on ? null : el("img", { className: "pf-spec-lock", src: `${STONE_BASE}/lock.webp`, alt: "", width: 16, height: 16 }),
        ].filter((x) => x != null));
      })),
    );
  } else if (!skill.passive && !skill.stigma && skill.stones && texts) {
    const slots = [0, 1, 2].map((i) => {
      const state = stoneSlotState(skill, i);
      return el("span", { className: `pf-slot-box ${state}` }, [
        state === "stone" ? stoneImg(skill.stones[i], 34) : state === "locked" ? el("img", { src: `${STONE_BASE}/lock.webp`, alt: "", width: 34, height: 34 }) : null,
      ].filter((x) => x != null));
    });
    sections.push(
      el("div", { className: "pf-tip-sec" }, [
        el("div", { className: "pf-tip-sec-title", textContent: t("profile.skillSpecial") }),
        el("div", { className: "pf-slot-boxes" }, slots),
        ...[1, 2, 3, 4, 5].map((n) =>
          el("div", { className: `pf-spec-row ${skill.stones.includes(n) ? "on" : "off"}` }, [
            stoneImg(n, 18),
            el("span", { className: "pf-spec-text", textContent: texts[n - 1] || "" }),
          ]),
        ),
      ]),
    );
  }
  return sections;
}

function skillTile(s) {
  const name = localizedSkillName(s);
  const tile = el("div", { className: "pf-skill" }, [
    iconTile("skill", s.icon, name, { color: s.passive ? "#3fcf55" : "#f0a030", badge: `${t("profile.levelShort")} ${s.level}`, badgeClass: s.level > s.baseLevel ? "bonus" : "" }),
    el("span", { className: "pf-skill-name", textContent: name }),
  ]);
  for (const target of [...tile.children]) {
    // the wide tooltip (the same builder asked again) for the ones that list specialisations
    attachTooltip(target, () => skillTip(s, name), () => Promise.resolve(skillSpecTexts(s) && (s.stigma || s.stones) ? skillTip(s, name) : null));
  }
  if (!s.passive && !s.stigma && s.stones) {
    tile.append(stoneRow(s, name));
  }
  return tile;
}

function skillCard(title, list, cls) {
  return el("div", { className: "pf-card" }, [
    el("h4", {}, [el("span", { className: `pf-dot ${cls}` }), `${title} · ${list.length}`]),
    el("div", { className: "pf-skill-grid" }, list.map(skillTile)),
  ]);
}

// The three blocks of one skill list: active | passive | stigma (the stigma block only exists once a profile carries the flag;
// the game lets a player equip only four of them).
function skillBlocks(profile, skills) {
  const v = visibleSkills(profile, skills);
  const blocks = [
    skillCard(t("profile.skillsActive"), v.active, "active"),
    skillCard(t("profile.skillsPassive"), v.passive, "passive"),
    v.stigma.length > 0 ? skillCard(t("profile.skillsStigma"), v.stigma, "stigma") : null,
  ].filter((x) => x != null);
  return el("div", { className: "pf-skills", style: `--blocks:${blocks.length}` }, blocks);
}

function renderSkillsTab(profile) {
  loadSkillSpec();
  stoneClass = (profile.className ?? "").toLowerCase();
  // A character with several skill presets gets the three blocks once per preset, headed with the preset's number (the game
  // sends only the active one, so the others are as they were when last active).
  const body =
    profile.presets?.length > 1
      ? el("div", { className: "pf-presets" }, profile.presets.map((preset) =>
          el("section", { className: "pf-preset" }, [
            el("div", { className: "pf-preset-head" }, [
              el("h3", { textContent: `${t("profile.skillPreset")} ${preset.index}` }),
              el("span", { className: `pf-preset-chip${preset.active ? " on" : ""}`, textContent: preset.active ? t("profile.presetActive") : t("profile.presetSaved") }),
              preset.seenAt && !preset.active ? el("span", { className: "pf-preset-seen", textContent: t("profile.presetSeen", { when: formatDate(new Date(preset.seenAt)) }) }) : null,
            ].filter((x) => x != null)),
            skillBlocks(profile, preset.skills),
          ]),
        ))
      : skillBlocks(profile, profile.skills);
  return el("div", {}, [body, el("p", { className: "profile-source" }, [el("i", { className: "pf-key bonus" }), ` ${t("profile.legendBonus")}`])]);
}

// Species knowledge (pet window): one card per species with its level and the analysed effects. Percent
// stats arrive in hundredths (145 = 1.45 %) and are shown with one decimal, as the game does.
function speciesValue(effect) {
  if (!effect.percent) {
    return formatNumber(effect.value);
  }
  // like the game: "2 %" and "1,2 %", no trailing zero
  const text = (effect.value / 100).toFixed(1).replace(/\.0$/, "");
  const decimal = (1.1).toLocaleString(getLocale()).charAt(1);
  return `${text.replace(".", decimal)} %`;
}

// The pet circles of the game's growth overview: one per species, a ring of dots coloured by the quality of each
// effect slot (1 white, 2 green, 3 blue, 4 gold, 5 orange, 0 empty). The game runs the ring counter-clockwise.
const PET_GLYPHS = {
  cognia: '<path d="M30 40l6-10 8 6 8-6 6 10-6 14H36z"/>',
  fera: '<path d="M44 28l14 26H30z"/>',
  natura: '<circle cx="44" cy="44" r="12"/>',
  varia: '<path d="M44 28a16 16 0 1 0 0 32 12 12 0 1 1 0-24z"/>',
  specia: '<path d="M44 26l6 14 14 4-14 4-6 14-6-14-14-4 14-4z"/>',
};

function petCircle(pet) {
  // Nine places (a species level 10 has nine effect slots); the ones a lower level has not unlocked stay dark. The right side
  // of the game's ring is left open for its runes.
  const dots = Array.from({ length: Math.max(9, pet.kinds.length) }, (_, i) => {
    const kind = pet.kinds[i] ?? 0;
    const angle = (i / 12) * Math.PI * 2 - Math.PI / 2 - 0.4;
    return `<circle class="pf-pet-dot q${kind}" cx="${(44 - 34 * Math.cos(angle)).toFixed(1)}" cy="${(44 + 34 * Math.sin(angle)).toFixed(1)}" r="4.2"/>`;
  });
  const holder = el("div", { className: "pf-pet" });
  // Built from numbers and fixed strings only (kinds are integers from the API, the glyph comes from the table above).
  holder.innerHTML = `<svg viewBox="0 0 88 88" aria-hidden="true"><circle class="pf-pet-disc" cx="44" cy="44" r="42"/><g class="pf-pet-glyph">${PET_GLYPHS[pet.key] ?? ""}</g>${dots.join("")}</svg>`;
  const name = pet.names[getLocale()] ?? pet.names.en ?? pet.key;
  holder.append(el("span", { textContent: t("profile.speciesLevel", { level: pet.level }) }));
  holder.title = `${name} · ${t("profile.speciesLevel", { level: pet.level })}`;
  return holder;
}

// Another player's Daevanion boards: only the number of activated nodes is known, no map.
function renderBoardSummary(profile) {
  const rows = profile.boards.map((b) => {
    const percent = b.total > 0 ? Math.round((b.count / b.total) * 100) : 0;
    return el("div", { className: "pf-card pf-board-sum" }, [
      el("h4", {}, [el("span", { textContent: b.name }), el("em", { textContent: `${b.count} / ${b.total || "?"} · ${percent} %` })]),
      el("div", { className: "pf-bar" }, [el("i", { style: `width:${percent}%` })]),
    ]);
  });
  return el("div", { className: "pf-species" }, rows);
}

function renderTitleChips(profile) {
  if (!(profile?.titles?.length > 0)) {
    return null;
  }
  const locale = getLocale();
  return el(
    "div",
    { className: "pf-titles" },
    profile.titles.map((x) => el("span", { className: `pf-title g-${x.grade ?? "Common"}`, textContent: x.names[locale] ?? x.name })),
  );
}

// Wing, wing skin and active pet of the own character (character window), behind the titles, set off by a thin divider.
function renderLookChips(profile) {
  const locale = getLocale();
  const chip = (item, base, { badge, level } = {}) => {
    const holder = el("span", { className: `pf-chip${item.grade ? ` g-${item.grade}` : ""}` });
    if (item.icon) {
      const img = el("img", { src: `${ICON_BASE}/${base}/${item.icon}.webp`, alt: "", width: 30, height: 30, loading: "lazy" });
      img.addEventListener("error", () => img.remove());
      holder.append(img);
    }
    holder.append(el("span", { textContent: item.names?.[locale] ?? item.name }));
    if (badge) {
      holder.append(el("span", { className: "pf-pill", textContent: badge }));
    }
    if (level != null) {
      holder.append(el("span", { className: "pf-chip-lvl", textContent: `Lv ${level}` }));
    }
    return holder;
  };
  const chips = [
    profile?.wing ? chip(profile.wing, "item") : null,
    profile?.wingSkin ? chip(profile.wingSkin, "item", { badge: t("profile.wingSkin") }) : null,
    profile?.activePet ? chip(profile.activePet, "pet", { level: profile.activePet.level }) : null,
  ].filter((x) => x != null);
  return chips.length > 0 ? el("div", { className: "pf-looks" }, [el("span", { className: "pf-vsep" }), ...chips]) : null;
}

// Character tab (from the player's own client or NC's official character page): main attributes, the ring of lord values around the six main attributes, the lord list.
const ATTR_ICON = "/images/aion2/stats";
function attrIcon(key, lord) {
  return el("img", { className: "pf-attr-ico", src: `${ATTR_ICON}/${lord ? "lords_" : ""}${key === "con" ? "constitution" : key}.webp`, alt: "", width: 48, height: 48, loading: "lazy" });
}

function renderCharacterTab(profile) {
  const { main, lords } = profile.attributes;
  const top = Math.max(1, ...main.map((a) => a.value));
  const attrName = (a) => t(`attr.${a.key}`);
  const lordName = (a) => `${attrName(a)} [${t(`lord.${a.key}`)}]`;

  const bars = el("div", { className: "pf-card pf-char-main" }, [
    profile.portraitUrl ? el("div", { className: "pf-portrait" }, [portraitImg(profile.portraitUrl)]) : null,
    el("h4", {}, [el("span", { textContent: t("profile.attrMain") })]),
    ...main.map((a) =>
      el("div", { className: `pf-stat${a.value === 0 ? " zero" : ""}` }, [
        attrIcon(a.key, false),
        el("span", { textContent: attrName(a) }),
        el("i", {}, [el("b", { style: `width:${Math.round((a.value / top) * 100)}%` })]),
        el("em", { textContent: String(a.value) }),
      ]),
    ),
  ].filter((x) => x != null));
  if (profile.portraitUrl) {
    bars.classList.add("has-portrait");
  }

  // Ring positions are percentages of the ring box (600 x 600 design size): 10 lords on r=238 clockwise from the top, the six
  // main attributes as a hexagon on r=108, Might on top.
  const pos = (index, count, radius) => {
    const angle = (index / count) * Math.PI * 2;
    return `left:${(50 + (Math.sin(angle) * radius) / 6).toFixed(2)}%;top:${(50 - (Math.cos(angle) * radius) / 6).toFixed(2)}%`;
  };
  const ring = el("div", { className: "pf-ring" }, [
    // Two guide circles: fixed SVG shapes, no data in them.
    el("div", { className: "pf-ring-guides", innerHTML: '<svg viewBox="0 0 600 600" aria-hidden="true"><circle cx="300" cy="300" r="238" class="pf-ring-c1"/><circle cx="300" cy="300" r="165" class="pf-ring-c2"/></svg>' }),
    ...lords.map((a, i) =>
      el("div", { className: "pf-lord-node", style: pos(i, lords.length, 238), title: lordName(a) }, [
        el("div", { className: "pf-lord-ib" }, [attrIcon(a.key, true), el("strong", { textContent: String(a.value) })]),
        el("span", { textContent: attrName(a) }),
      ]),
    ),
    ...main.map((a, i) =>
      el("div", { className: "pf-hex-node", style: pos(i, main.length, 108), title: attrName(a) }, [attrIcon(a.key, false), el("span", { textContent: attrName(a) }), el("strong", { textContent: String(a.value) })]),
    ),
  ]);

  const list = el("div", { className: "pf-card pf-lord-list" }, [
    el("h4", {}, [el("span", { textContent: t("profile.attrLords") })]),
    ...lords.map((a) =>
      el("div", { className: "pf-lord-row" }, [
        el("span", { className: "pf-lord-name" }, [attrIcon(a.key, true), el("span", {}, [el("span", { textContent: attrName(a) }), el("small", { textContent: t(`lord.${a.key}`) })])]),
        el("strong", { textContent: String(a.value) }),
      ]),
    ),
  ]);
  return el("div", { className: "pf-char" }, [
    bars,
    el("div", { className: "pf-card pf-char-ring" }, [el("h4", {}, [el("span", { textContent: t("profile.attrLords") })]), ring]),
    list,
    profile.characterSource
      ? el("p", {
          className: "profile-source pf-char-source",
          textContent: t(profile.characterSource === "nc" ? "profile.charSourceNc" : "profile.charSourceClient", { date: formatDate(parseServerTime(profile.characterFetchedAt)) }),
        })
      : null,
  ].filter((x) => x != null));
}

function renderSpeciesTab(profile) {
  const locale = getLocale();
  const cards = profile.species.map((k) => {
    const maxed = k.progress === 0 && k.level >= 10;
    const rows = k.effects.map((e) =>
      el("li", {}, [
        el("span", { className: `pf-sp-stat q${e.kind ?? 0}`, textContent: (e.names?.[locale] ?? e.names?.en ?? e.name) }),
        el("strong", { textContent: speciesValue(e) }),
      ]),
    );
    const card = el("div", { className: "pf-card pf-species-card" }, [
      el("h4", {}, [el("span", { textContent: k.names[locale] ?? k.names.en ?? k.key }), el("em", { textContent: t("profile.speciesLevel", { level: k.level }) })]),
      el("div", { className: "pf-sp-sub", textContent: maxed ? t("profile.speciesMax") : `${t("profile.speciesProgress")}: ${formatNumber(k.progress)}` }),
      el("ul", { className: "pf-sp-list" }, rows),
    ]);
    // The species' circle sits centred above its table.
    const pet = profile.pets?.find((p) => p.species === k.id);
    return el("div", { className: "pf-species-col" }, [pet ? petCircle(pet) : null, card].filter((x) => x != null));
  });
  const shown = new Set(profile.species.map((k) => k.id));
  const loose = (profile.pets ?? []).filter((p) => !shown.has(p.species));
  return el("div", {}, [
    loose.length > 0 ? el("div", { className: "pf-pets" }, loose.map(petCircle)) : null,
    cards.length > 0 ? el("div", { className: "pf-species" }, cards) : null,
    cards.length > 0 ? el("p", { className: "profile-source", textContent: t("profile.speciesNote") }) : null,
  ].filter((x) => x != null));
}

// The board tiles are the game's own node art, picked by the node's rarity (common = grey rune, rare =
// blue, legend = green, unique = orange); a node that is not unlocked uses the dark "disabled" variant.
const NODE_ART = { 1: "common", 2: "rare", 3: "legend", 4: "unique" };

// The start node shows the class emblem; Spiritmaster is the client's "Elementalist". A class without its own
// art (not extracted yet) falls back to the plain start tile.
const START_ART = { gladiator: "start-gladiator", templar: "start-templar", ranger: "start-ranger", assassin: "start-assassin", spiritmaster: "start-elementalist", sorcerer: "start-sorcerer", cleric: "start-cleric", chanter: "start-chanter" };

function boardView(board, skillsById, className) {
  const rows = board.cells.map((c) => c[0]);
  const cols = board.cells.map((c) => c[1]);
  const [r0, r1, c0, c1] = [Math.min(...rows), Math.max(...rows), Math.min(...cols), Math.max(...cols)];
  const width = c1 - c0 + 1;
  const grid = Array.from({ length: (r1 - r0 + 1) * width }, () => el("i", { className: "pf-cell empty" }));
  for (const [row, col, kind, ref, active, grade, value] of board.cells) {
    const art = kind === 0 ? (START_ART[String(className).toLowerCase()] ?? "start") : (NODE_ART[grade] ?? "common") + (active ? "" : "-off");
    const cell = el("i", { className: `pf-cell${active ? " on" : ""}` }, [el("img", { src: `/images/aion2/daevanion/${art}.webp`, alt: "", loading: "lazy" })]);
    attachTooltip(cell, () => {
      let title = t("profile.legendStart");
      let sub = "";
      if (kind === 1) {
        title = `${statLabel(ref)} +${value}`;
        sub = t("profile.legendStat");
      } else if (kind === 2) {
        const skill = skillsById.get(ref);
        title = skill ? localizedSkillName(skill) : String(ref);
        sub = `${t("profile.legendSkill")} +${value}`;
      }
      return [el("div", { className: "pf-tip-head" }, [el("div", { className: "pf-tip-title", textContent: title }), sub ? el("div", { className: "pf-tip-sub", textContent: sub }) : null].filter((x) => x != null))];
    });
    grid[(row - r0) * width + (col - c0)] = cell;
  }
  return el("div", { className: "pf-board", style: `--cols:${width}` }, grid);
}

function renderBoardTab(profile) {
  const skillsById = new Map(profile.skills.map((s) => [s.id, s]));
  const holder = el("div", { className: "pf-board-wrap" });
  const buttons = profile.daevanion.map((b) => {
    const total = b.totalPoints;
    const percent = total > 0 ? ` · ${Math.round((b.activePoints / total) * 100)} %` : "";
    return el("button", { type: "button", className: "pf-pill" }, [b.name, el("b", { textContent: ` ${b.activePoints} / ${total || "?"}${percent}` })]);
  });
  const show = (i) => {
    const b = profile.daevanion[i];
    buttons.forEach((x, j) => x.setAttribute("aria-selected", String(i === j)));
    const bonuses = b.skillBonuses.map((x) => `${localizedSkillName(x)} +${x.value}`).join(", ");
    const stats = Object.entries(b.stats)
      .sort((x, y) => y[1] - x[1])
      .map(([token, value]) => `${statLabel(token)} +${value}`)
      .join(", ");
    holder.replaceChildren(
      boardView(b, skillsById, profile.className),
      bonuses ? el("p", { className: "pf-sub", textContent: t("profile.skillBonuses", { list: bonuses }) }) : null,
      stats ? el("p", { className: "pf-sub small", textContent: stats }) : null,
    );
  };
  buttons.forEach((x, i) => x.addEventListener("click", () => show(i)));
  show(0);
  return el("div", {}, [el("div", { className: "pf-pills" }, buttons), el("div", { className: "pf-card" }, [holder])]);
}

// The runs tab: the fights grouped into runs (same instance and step, at most 40 minutes between two fights, no boss twice), newest
// first, only the newest one open. Each fight is a tile with its iDPS measured against the player's best for that boss.
const RUN_SESSION_GAP_MS = 40 * 60 * 1000;
const RUN_SESSIONS_PER_PAGE = 14;
const RUN_KINDS = ["conquest", "expedition", "transcendence", "ascension", "nightmare", "other"];

function runKind(h) {
  if (/\(Conquest/.test(h.instanceName ?? "")) {
    return "conquest";
  }
  return RUN_KINDS.includes(h.instanceCategory) ? h.instanceCategory : "other";
}

function runKindLabel(kind) {
  return kind === "conquest" ? t("mode.conquest") : kind === "other" ? t("runs.other") : t(`category.${kind}`);
}

// Runs in the order given (newest first) cut into sessions, each one real run (3-4 bosses); the data has no run id, so a
// fight joins the open session when it is the same instance and step, starts less than the gap after the session's last
// fight and is a boss the session has not had yet. A boss that comes round again starts the next run, also in the same instance.
function runSessions(runs) {
  const sessions = [];
  for (const r of [...runs].reverse()) {
    const cur = sessions.at(-1);
    if (cur && cur.instance === r.h.instanceName && cur.mode === r.h.mode && r.t - cur.end <= RUN_SESSION_GAP_MS && !cur.bosses.has(r.h.bossName)) {
      cur.runs.push(r);
      cur.bosses.add(r.h.bossName);
      cur.end = r.t;
    } else {
      sessions.push({ instance: r.h.instanceName, mode: r.h.mode, runs: [r], bosses: new Set([r.h.bossName]), start: r.t, end: r.t });
    }
  }
  for (const s of sessions) {
    s.runs.reverse();
  }
  return sessions.reverse();
}

function renderRunsTab(history, party = {}, selfId = null) {
  if (history.length === 0) {
    return el("p", { className: "empty", textContent: t("player.emptyNoFights") });
  }
  const runs = history
    .map((h) => ({ h, t: Date.parse(h.startedAt), ok: h.totalDamage > 0 && h.idps > 0, kind: runKind(h), key: `${h.instanceName}|${h.mode}|${h.bossName}` }))
    .sort((a, b) => b.t - a.t);
  const best = new Map();
  for (const r of runs) {
    if (r.ok && r.h.idps > (best.get(r.key) ?? 0)) {
      best.set(r.key, r.h.idps);
    }
  }
  for (const r of runs) {
    r.record = r.ok && r.h.idps === best.get(r.key);
  }

  const intl = intlTag();
  const dayFormat = new Intl.DateTimeFormat(intl, { weekday: "short", day: "numeric", month: "numeric", year: "numeric" });
  const clockFormat = new Intl.DateTimeFormat(intl, { hour: "2-digit", minute: "2-digit", hourCycle: "h23" });
  const compact = new Intl.NumberFormat(intl, { notation: "compact", maximumFractionDigits: 2 });
  const stat = (value, label, accent = false) => el("div", {}, [el("strong", { className: accent ? "accent" : "", textContent: value }), el("span", { textContent: label })]);

  const tile = (r) => {
    const h = r.h;
    const pct = r.ok ? Math.min(100, (h.idps / best.get(r.key)) * 100) : 0;
    return el("a", { className: `run-tile${r.record ? " record" : ""}${r.ok ? "" : " empty-run"}`, href: participantHref(h.playerSlug, h.participantId), title: `${dayFormat.format(r.t)} ${clockFormat.format(r.t)}` }, [
      r.record ? el("span", { className: "run-star", textContent: "★", title: t("runs.record") }) : null,
      el("div", { className: "run-boss" }, [translateGameName(h.bossName), ...[partialBadge(h)].filter((x) => x != null).flatMap((p) => [" ", p])]),
      el("div", { className: "run-idps" }, [r.ok ? formatNumber(h.idps) : "–", el("small", { textContent: " iDPS" })]),
      el("div", { className: "run-meter" }, [el("i", { style: `width:${pct}%` })]),
      el("div", { className: "run-meta" }, [
        el("span", { textContent: r.ok ? compact.format(h.totalDamage) : t("runs.noData") }),
        el("span", { textContent: r.ok ? t("runs.crit", { n: Math.round(h.critRatePercent) }) : clockFormat.format(r.t) }),
      ]),
    ].filter((x) => x != null));
  };

  // The group of a run: everyone who fought in it, the player first and then the ones present in most of its fights. The same name
  // and class counts once (one fight can have the player under another server's entry).
  const groupRow = (s) => {
    const seen = new Map();
    for (const r of s.runs) {
      for (const m of party[r.h.encounterId] ?? []) {
        const key = `${m.name.toLowerCase()}|${m.className}`;
        const entry = seen.get(key) ?? { m, n: 0 };
        entry.n++;
        seen.set(key, entry);
      }
    }
    if (seen.size === 0) {
      return null;
    }
    const entries = [...seen.values()].sort((a, b) => (b.m.id === selfId) - (a.m.id === selfId) || b.n - a.n);
    const members = entries.map(({ m }) =>
      el("a", { className: `run-member${m.id === selfId ? " self" : ""}`, href: gp(`/players/${m.slug ?? m.id}`) }, [classIcon(m.className), m.name]),
    );
    return el("div", { className: "run-group" }, [el("span", { className: "run-group-label", textContent: t("runs.group") }), ...members]);
  };

  const sessionCard = (s, open) => {
    const ok = s.runs.filter((r) => r.ok);
    const avg = ok.length > 0 ? ok.reduce((sum, r) => sum + r.h.idps, 0) / ok.length : 0;
    const top = ok.reduce((m, r) => Math.max(m, r.h.idps), 0);
    const records = s.runs.filter((r) => r.record).length;
    const first = s.runs[0].h;
    const openLink = el("a", { className: "run-open", href: gp(`/encounters/${s.runs.at(-1).h.encounterId}`), textContent: t("runs.view") });
    const group = groupRow(s);
    return el("details", { className: "run-session", open }, [
      el("summary", { className: "run-head" }, [
        el("span", { className: "run-time", textContent: `${clockFormat.format(s.start)} – ${clockFormat.format(s.end)}` }),
        el("h3", { textContent: displayName({ name: s.instance }) }),
        diffPill(first.instanceCategory, first.mode, first.instanceName),
        records > 0 ? el("span", { className: "diff-pill diff-cq", textContent: t("runs.records", { n: records }) }) : null,
      ].filter((x) => x != null)),
      el("div", { className: "run-bar" }, [openLink, el("div", { className: "run-stats" }, [stat(formatNumber(s.runs.length), t("runs.fights")), stat(formatNumber(avg), t("runs.avgIdps")), stat(formatNumber(top), t("runs.bestIdps"), true)])]),
      group,
      el("div", { className: "run-tiles" }, [...s.runs].reverse().map(tile)),
    ]);
  };

  const kinds = RUN_KINDS.filter((k) => runs.some((r) => r.kind === k));
  const filters = el("div", { className: "pf-pills run-filters" });
  const list = el("div", { className: "run-list" });
  const more = el("button", { type: "button", className: "pf-pill run-more" });
  let kind = "all";
  let sessions = [];
  let shown = 0;
  let lastDay = "";

  // Appends the next sessions to the list, with a day heading whenever the calendar day changes.
  const addSessions = () => {
    const from = shown;
    shown = Math.min(sessions.length, shown + RUN_SESSIONS_PER_PAGE);
    for (let i = from; i < shown; i++) {
      const day = dayFormat.format(sessions[i].end);
      if (day !== lastDay) {
        list.append(el("div", { className: "run-day", textContent: day }));
        lastDay = day;
      }
      list.append(sessionCard(sessions[i], i === 0));
    }
    more.hidden = shown >= sessions.length;
    more.textContent = t("runs.more", { n: sessions.length - shown });
  };
  const pick = (k) => {
    kind = k;
    filterButtons.forEach(([id, b]) => b.setAttribute("aria-selected", String(id === k)));
    sessions = runSessions(runs.filter((r) => k === "all" || r.kind === k));
    list.replaceChildren();
    shown = 0;
    lastDay = "";
    addSessions();
  };
  const filterButtons = ["all", ...kinds].map((k) => {
    const count = k === "all" ? runs.length : runs.filter((r) => r.kind === k).length;
    const b = el("button", { type: "button", className: "pf-pill" }, [k === "all" ? t("runs.all") : runKindLabel(k), el("b", { textContent: ` ${count}` })]);
    b.addEventListener("click", () => pick(k));
    return [k, b];
  });
  filters.append(...filterButtons.map(([, b]) => b));
  more.addEventListener("click", addSessions);
  pick(kind);
  return el("div", { className: "runs" }, [filters, list, more]);
}

// Official character portrait (cut out on the server, any player on a European server once it has been fetched); hides itself
// when the picture does not load, so a missing file never leaves a broken image.
function portraitImg(url) {
  const img = el("img", { src: url, alt: "", loading: "lazy", decoding: "async" });
  img.addEventListener("error", () => {
    const holder = img.closest(".pf-avatar, .pf-portrait");
    holder?.closest(".pf-char-main")?.classList.remove("has-portrait");
    holder?.classList.add("failed");
    img.remove();
  });
  return img;
}

// Left end of the player strip: the round portrait with the class symbol as a small badge on its corner; without a portrait
// (not fetched yet, nothing found, no European server) the class symbol alone as before.
function stripEmblem(profile, player) {
  const badge = profile?.className ? el("div", { className: "pf-badge", title: classLabel(profile.className) }, [classIcon(profile.className)]) : null;
  if (!player.portraitUrl) {
    return badge;
  }
  const avatar = el("div", { className: "pf-avatar" }, [el("div", { className: "pf-avatar-pic" }, [portraitImg(player.portraitUrl)]), badge]);
  return avatar;
}

// The player strip: shown above every tab.
function renderPlayerStrip(profile, player, character) {
  // The class is only shown as the badge on the portrait (tooltip carries its name), so the line under the name is just the level.
  const sub = profile?.level ? [`${t("guild.level")} ${profile.level}`] : [];
  // Item level (own client), gear power and combat power come last; the gear power and combat power are from NC's official character page
  // (the player's own client does not send them). The big number on the right is the combat power.
  const stat = (value, label, accent = false) => el("div", {}, [el("strong", { className: accent ? "accent" : "", textContent: formatNumber(value) }), el("span", { textContent: label })]);
  const numbers = [
    profile?.averageItemLevel ? stat(profile.averageItemLevel, t("profile.avgShort")) : null,
    character?.itemLevel ? stat(character.itemLevel, t("profile.gearPowerShort")) : null,
    character?.combatPower ? stat(character.combatPower, t("profile.combatPowerShort"), true) : null,
  ].filter((x) => x != null);
  return el("div", { className: "pf-strip" }, [
    stripEmblem(profile, player),
    // Name, class and the faction/legion/server line stay together in one column, so the badges and the numbers fit in one row.
    el("div", { className: "pf-id" }, [
      el("div", { className: "pf-name" }, [profile?.faction ? factionIcon(profile.faction) : null, player.name].filter((x) => x != null)),
      sub.length > 0 ? el("div", { className: "pf-sub", textContent: sub.join(" · ") }) : null,
      el("div", { className: "pf-meta" }, [
        player.guild ? el("span", {}, [`${t("profile.guild")}: `, player.guildSlug ? link(player.guild, `/legions/${player.guildSlug}`) : player.guild]) : null,
        player.serverName ? el("span", { textContent: serverLabel(player.serverName) }) : null,
      ].filter((x) => x != null)),
    ]),
    renderTitleChips(profile),
    renderLookChips(character),
    el("div", { className: "pf-numbers" }, numbers),
  ].filter((x) => x != null));
}

async function renderPlayerProfile(playerId, attempt = 0) {
  if (attempt === 0) {
    setBreadcrumb([link(t("breadcrumb.home"), "/"), t("breadcrumb.playerProfile")]);
    showLoading(t("loading.playerProfile"));
  }

  const data = await fetchJson(`/api/players/${encodeURIComponent(playerId)}`);
  setBreadcrumb([link(t("breadcrumb.home"), "/"), data.player.name]);
  // Reached through an old numeric link: show the name address instead (no reload, no history entry).
  if (data.player.slug && decodeURIComponent(location.pathname.split("/").pop()) !== data.player.slug) {
    history.replaceState(history.state, "", `${gp(`/players/${data.player.slug}`)}${location.search}${location.hash}`);
  }
  document.querySelector(".pf-tip")?.remove();
  gearPlayerId = data.player.id;

  const profile = data.profile;
  // Attributes, wing and pet from the player's client and/or NC; also there when the player has no profile at all.
  const character = data.character ?? profile;
  const strip = renderPlayerStrip(profile, data.player, character);
  const tabs = [["runs", t("profile.tabRuns"), data.history.length, () => renderRunsTab(data.history, data.party, data.player.id)]];
  if (profile?.gear.length > 0) {
    tabs.push(["equipment", t("profile.tabEquipment"), profile.gear.filter((g) => g.slotName !== "Arcana").length, () => renderEquipmentTab(profile)]);
  }
  if (profile && visibleSkillCount(profile) > 0) {
    tabs.push(["skills", t("profile.tabSkills"), visibleSkillCount(profile), () => renderSkillsTab(profile)]);
  }
  if (profile?.daevanion.length > 0) {
    tabs.push(["daevanion", t("profile.tabBoard"), profile.daevanion.reduce((s, b) => s + b.activePoints, 0), () => renderBoardTab(profile)]);
  }
  if (!(profile?.daevanion.length > 0) && profile?.boards?.length > 0) {
    tabs.push(["daevanion", t("profile.tabBoard"), profile.boards.reduce((s, b) => s + b.count, 0), () => renderBoardSummary(profile)]);
  }
  if (profile?.species?.length > 0 || profile?.pets?.length > 0) {
    tabs.push(["species", t("profile.tabSpecies"), profile.species.length || profile.pets.length, () => renderSpeciesTab(profile)]);
  }
  if (character?.attributes) {
    tabs.unshift(["character", t("profile.tabCharacter"), character.attributes.main.length + character.attributes.lords.length, () => renderCharacterTab({ ...character, portraitUrl: data.player.portraitUrl })]);
  }
  if (profile?.gear.some((g) => g.slotName === "Arcana")) {
    tabs.push(["arcana", t("slot.Arcana"), profile.gear.filter((g) => g.slotName === "Arcana").length, () => renderArcanaTab(profile)]);
  }
  // The Pantheon only exists for a character whose own client uploaded it.
  if (profile?.pantheon) {
    tabs.push(["pantheon", t("profile.tabPantheon"), profile.pantheon.presets[0]?.slots.length ?? 0, () => renderPantheon(profile.pantheon)]);
  }
  if (profile?.ranks?.length > 0) {
    tabs.push(["ranks", t("profile.tabRanks"), profile.ranks.length, () => renderRanksTab(profile)]);
  }

  const scrollY = window.scrollY;
  const panel = el("div", { className: "pf-panel" });
  const buttons = tabs.map(([id, label, count]) =>
    el("button", { type: "button", role: "tab", textContent: label }, [el("small", { textContent: String(count) })]),
  );
  // Only a tab the reader picked goes into the address: the tab shown by default must stay free to change when
  // the character data arrives later and the Character tab becomes the first one.
  const show = (index, picked = true) => {
    buttons.forEach((b, i) => b.setAttribute("aria-selected", String(i === index)));
    panel.replaceChildren(tabs[index][3]());
    if (picked) {
      history.replaceState(history.state, "", `${location.pathname}${location.search}#${tabs[index][0]}`);
    }
  };
  buttons.forEach((b, i) => b.addEventListener("click", () => show(i)));
  const wanted = tabs.findIndex(([id]) => id === location.hash.slice(1));

  const source = profile
    ? el("p", { className: "profile-source", textContent: t(profile.source === "self" ? "profile.sourceSelf" : profile.source === "nc" ? "profile.charSourceNc" : "profile.sourceSeen", { date: formatDate(parseServerTime(profile.updatedAt)) }) })
    : null;
  app.replaceChildren(
    el("div", { className: "pf-hero" }, [strip, tabs.length > 1 ? el("div", { className: "pf-tabs", role: "tablist" }, buttons) : null].filter((x) => x != null)),
    ...(data.history.length > 0 ? [compareButton(t("compare.ctaPlayers"), compareLink("/compare/players", { a: data.player.id }))] : []),
    panel,
    ...(source ? [source, el("p", { className: "profile-source", textContent: t("profile.note") })] : []),
  );
  show(wanted >= 0 ? wanted : 0, wanted >= 0);
  if (attempt > 0) {
    // A look-again pass rebuilds the page outside route(), which is what normally appends the footer;
    // and the reader must stay where they were scrolled to.
    app.appendChild(buildSiteFooter());
    window.scrollTo(0, scrollY);
  }

  // The server fetches the portrait and the character data from NC in the background the first time a profile is
  // opened: look again a few times (still on this page) instead of making the reader reload.
  if ((!data.player.portraitUrl || !character?.attributes) && attempt < 3) {
    const path = location.pathname;
    setTimeout(() => {
      if (location.pathname === path) {
        renderPlayerProfile(playerId, attempt + 1).catch(() => {});
      }
    }, [6000, 10000, 15000][attempt]);
  }
}

async function renderSearchResults(query) {
  setBreadcrumb([...gameCrumbs(), t("breadcrumb.search", { query })]);
  showLoading(t("loading.search"));

  // Every server at once; each hit says which server it is from (two servers can each have a
  // player of the same name).
  const results = await fetchJson(`/api/players/search?${new URLSearchParams({ q: query })}`);
  if (results.length === 1) {
    // replaceState, not pushState: Back from the profile must not land on a search that would just
    // redirect forward again.
    navigate(gp(`/players/${results[0].slug ?? results[0].id}`), { replace: true });
    return;
  }
  if (results.length === 0) {
    app.replaceChildren(el("p", { className: "empty", textContent: t("search.noResults", { query }) }));
    return;
  }

  const list = el(
    "ul",
    { className: "plain" },
    results.map((p) => el("li", {}, [link(p.serverName ? `${p.name} (${serverLabel(p.serverName)})` : p.name, gp(`/players/${p.slug ?? p.id}`))])),
  );
  app.replaceChildren(el("h2", { textContent: t("search.multipleResultsHeading") }), list);
}

// ---- Comparisons: two runs of one boss side by side, and two players on one boss. Both are
// step-by-step pickers driven by the query string (so every step is a shareable link): the page
// shows the next missing choice until everything needed is in the URL, then the comparison.

function formatDelta(a, b) {
  if (!(b > 0)) {
    return "–";
  }
  const pct = ((a - b) / b) * 100;
  if (Math.abs(pct) > 999) {
    return pct > 0 ? "> +999 %" : "< -999 %";
  }
  return `${pct > 0 ? "+" : ""}${pct.toFixed(1)} %`;
}

/** One metric row: both values, the better one highlighted, and A's difference relative to B. */
function compareRow(label, a, b, format, higherIsBetter = true) {
  const better = a === b ? 0 : (a > b) === higherIsBetter ? 1 : -1;
  return el("tr", {}, [
    el("td", { textContent: label }),
    el("td", { className: better > 0 ? "cmp-win" : "", textContent: format(a) }),
    el("td", { className: better < 0 ? "cmp-win" : "", textContent: format(b) }),
    el("td", { className: "cmp-delta", textContent: formatDelta(a, b) }),
  ]);
}

function compareTable(headA, headB, rows) {
  return el("table", { className: "cmp-table" }, [
    el("thead", {}, [el("tr", {}, [el("th", {}), el("th", {}, [headA]), el("th", {}, [headB]), el("th", { textContent: t("compare.difference") })])]),
    el("tbody", {}, rows),
  ]);
}

function compareSteps(labels, active) {
  return el(
    "ol",
    { className: "cmp-steps" },
    labels.map((label, i) => el("li", { className: i === active ? "active" : i < active ? "done" : "", textContent: label })),
  );
}

function compareLink(path, params) {
  return gp(`${path}?${new URLSearchParams(params)}`);
}

/** The compare-with-someone button shown on encounter and player pages. */
function compareButton(text, href) {
  return el("div", { className: "cmp-cta" }, [el("a", { className: "cmp-btn", href, textContent: text })]);
}

/** The icon-only compare tile (two opposite arrows) with a CSS tooltip; sits at the right end of a stats row. */
function compareTile(text, href) {
  return el("a", {
    className: "stat-card cmp-tile",
    href,
    title: text,
    "aria-label": text,
    innerHTML: '<svg viewBox="0 0 24 24" width="30" height="30" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 7h13"/><path d="M13 3l4 4-4 4"/><path d="M20 17H7"/><path d="M11 13l-4 4 4 4"/></svg>',
  });
}

function runCaption(encounter) {
  return `${formatDate(new Date(encounter.startedAt))}${encounter.serverName ? ` · ${serverLabel(encounter.serverName)}` : ""}`;
}

/** A solid dot in the class colour: pale emblem icons were too faint in a row of names. */
const CMP_CLASS_COLORS = {
  Gladiator: "#3fd7ff",
  Templar: "#3d6bff",
  Assassin: "#b266ff",
  Ranger: "#3ddc84",
  Sorcerer: "#ff7ad9",
  Spiritmaster: "#ff4d6d",
  Cleric: "#ffe27a",
  Chanter: "#ffa31a",
  Brawler: "#ff8c5a",
};
function classDot(className) {
  return el("span", { className: "cmp-dot", style: `background: ${CMP_CLASS_COLORS[className] ?? classMeta(className).color};`, title: classLabel(className) });
}

/**
 * The run picker: the boss's runs as expandable rows, each with everyone who played in it. Filters: only runs with
 * exactly my group (the players of run A), one player of my group, and the sort order. Both filters run in the API
 * before its limit, so a rarely played group is found even when 100 newer runs of other groups exist.
 */
function renderRunPicker(a, first, bossName) {
  const mine = first.roster.filter((p) => p.className !== "?");
  const mineIds = new Set(mine.map((p) => p.playerId));
  const baseIdps = first.encounter.groupIDps;
  const state = { group: false, player: null, sort: "newest", search: "" };
  let ticket = 0;

  const countLine = el("p", { className: "cmp-count" });
  const list = el("div", { className: "cmp-run-list" });
  const chips = el("div", { className: "cmp-mine" });
  const groupBtn = el("button", { type: "button", className: "cmp-chip", textContent: t("compare.myGroup") });
  const sortBtns = [
    ["newest", "compare.sortNewest"],
    ["fastest", "compare.sortFastest"],
    ["idps", "compare.sortIdps"],
  ].map(([key, label]) => el("button", { type: "button", className: "cmp-chip", textContent: t(label), onclick: () => { state.sort = key; reload(); } }));
  const sortKeys = ["newest", "fastest", "idps"];
  const search = el("input", { type: "search", className: "cmp-search", placeholder: t("compare.searchPlayer"), autocomplete: "off" });

  function drawChips() {
    const needle = state.search.trim().toLowerCase();
    chips.replaceChildren(
      ...mine
        .filter((p) => needle === "" || p.playerName.toLowerCase().includes(needle))
        .map((p) => el("button", { type: "button", className: "cmp-mem" + (state.player === p.playerId ? " on" : ""), onclick: () => { state.player = state.player === p.playerId ? null : p.playerId; drawChips(); reload(); } }, [classDot(p.className), p.playerName])),
    );
  }

  function memberChip(m) {
    return el("span", { className: "cmp-mem" + (mineIds.has(m.playerId) ? "" : " other") }, [classDot(m.className), m.playerName]);
  }

  function runRow(r) {
    const shared = r.members.filter((m) => mineIds.has(m.playerId)).length;
    const sameGroup = shared === mineIds.size && r.members.length === mineIds.size;
    const diff = r.groupIDps - baseIdps;
    const detail = el("div", { className: "cmp-run-detail", hidden: true }, [
      el("div", { className: "cmp-detail-title", textContent: t("compare.groupOfRun") }),
      el(
        "div",
        { className: "cmp-detail-grid" },
        r.members.map((m) =>
          el("div", { className: "cmp-detail-card" + (mineIds.has(m.playerId) ? "" : " other") }, [
            el("div", { className: "cmp-detail-name" }, [classDot(m.className), m.playerName]),
            el("small", { textContent: classLabel(m.className) }),
            el("strong", { textContent: `${formatNumber(m.idps)} ${t("leaderboard.idpsShort")}` }),
          ]),
        ),
      ),
    ]);
    const toggle = el("button", { type: "button", className: "cmp-toggle", title: t("compare.showGroup"), "aria-label": t("compare.showGroup"), textContent: "▾", onclick: () => { detail.hidden = !detail.hidden; toggle.textContent = detail.hidden ? "▾" : "▴"; } });
    return el("div", { className: "cmp-run" }, [
      el("div", { className: "cmp-run-head" }, [
        el("span", { className: "cmp-run-date" }, [formatDate(new Date(r.startedAt)), ...[r.mode ? el("span", { className: "diff-pill cmp-mode", textContent: modeLabel(r.mode) }) : null, partialBadge(r)].filter((x) => x != null).flatMap((p) => [" ", p])]),
        el("span", { className: "cmp-run-idps", textContent: formatNumber(r.groupIDps) }),
        el("span", { className: diff === 0 ? "cmp-delta" : diff > 0 ? "cmp-up" : "cmp-down", textContent: baseIdps > 0 ? `${diff > 0 ? "▲" : diff < 0 ? "▼" : ""} ${formatDelta(r.groupIDps, baseIdps)}`.trim() : "–" }),
        el("span", { className: "cmp-run-dur", textContent: formatDuration(r.durationSeconds) }),
        el("span", { className: "cmp-run-members" }, [
          ...r.members.map(memberChip),
          el("span", { className: "cmp-overlap" + (sameGroup ? " same" : ""), textContent: sameGroup ? t("compare.yourGroup") : t("compare.groupOverlap", { n: shared, total: mineIds.size }) }),
        ]),
        el("span", { className: "cmp-run-actions" }, [toggle, el("a", { className: "cmp-btn", href: compareLink("/compare/runs", { a, b: r.encounterId }), textContent: t("compare.compareBtn") })]),
      ]),
      detail,
    ]);
  }

  async function reload() {
    const mineTicket = ++ticket;
    groupBtn.classList.toggle("on", state.group);
    sortBtns.forEach((btn, i) => btn.classList.toggle("on", sortKeys[i] === state.sort));
    const query = new URLSearchParams({ limit: "100", sort: state.sort, exclude: a });
    if (state.group) {
      query.set("group", a);
    }
    if (state.player !== null) {
      query.set("player", String(state.player));
    }
    const data = await fetchJson(`/api/bosses/${first.encounter.bossId}/runs?${query}`);
    if (mineTicket !== ticket) {
      return; // a newer filter click already asked for its own list
    }
    const others = data.runs;
    countLine.textContent = t("compare.runsShown", { shown: others.length, total: data.total });
    list.replaceChildren(...(others.length > 0 ? others.map(runRow) : [el("p", { className: "empty", textContent: t(state.group || state.player !== null ? "compare.runsNoneFiltered" : "compare.runsNone") })]));
  }

  groupBtn.onclick = () => { state.group = !state.group; reload(); };
  search.oninput = () => { state.search = search.value; drawChips(); };
  drawChips();
  reload().catch(() => list.replaceChildren(el("p", { className: "empty", textContent: t("compare.runsNone") })));

  return [
    el("h2", { textContent: t("compare.runsPickHeading", { boss: bossName }) }),
    el("p", { className: "pf-sub", textContent: `${t("compare.yourRun")}: ${runCaption(first.encounter)} · ${formatDuration(first.encounter.durationSeconds)} · ${formatNumber(baseIdps)} ${t("leaderboard.idpsShort")}` }),
    chips,
    el("div", { className: "cmp-filters" }, [groupBtn, search, el("span", { className: "cmp-sort-label", textContent: t("compare.sortLabel") }), ...sortBtns]),
    el("div", { className: "cmp-run-cols" }, ["compare.colDate", "compare.colIdps", "compare.colDelta", "compare.colDuration", "compare.colGroup"].map((k) => el("span", { textContent: t(k) })).concat([el("span")])),
    list,
    countLine,
  ];
}

async function renderCompareRuns(params) {
  const a = params.get("a");
  const b = params.get("b");
  setBreadcrumb([...gameCrumbs(), t("compare.runsTitle")]);
  if (!a) {
    renderNotFound();
    return;
  }
  showLoading(t("compare.runsTitle"));

  const first = await fetchJson(`/api/encounters/${encodeURIComponent(a)}`);
  const bossName = translateGameName(first.encounter.bossName);

  if (!b) {
    app.replaceChildren(...renderRunPicker(a, first, bossName));
    return;
  }

  const second = await fetchJson(`/api/encounters/${encodeURIComponent(b)}`);
  const ea = first.encounter;
  const eb = second.encounter;
  const real = (roster) => roster.filter((p) => p.className !== "?");
  const ra = real(first.roster);
  const rb = real(second.roster);
  const sum = (roster, key) => roster.reduce((acc, p) => acc + p[key], 0);

  const heading = (label, encounter) =>
    el("div", { className: "cmp-head" }, [
      el("strong", { textContent: label }),
      el("a", { href: gp(`/encounters/${encounter.id}`), textContent: runCaption(encounter) }),
    ]);

  const metrics = compareTable(heading(t("compare.runA"), ea), heading(t("compare.runB"), eb), [
    compareRow(t("stats.groupIdps"), ea.groupIDps, eb.groupIDps, formatNumber),
    compareRow(t("encounter.duration"), ea.durationSeconds, eb.durationSeconds, formatDuration, false),
    compareRow(t("stats.totalDamage"), sum(ra, "totalDamage"), sum(rb, "totalDamage"), formatNumber),
    compareRow(t("participant.totalHealing"), sum(ra, "totalHealing"), sum(rb, "totalHealing"), formatNumber),
    compareRow(t("stats.participants"), ra.length, rb.length, (n) => String(n), false),
  ]);

  // Both rosters on ONE bar scale, so a bar means the same amount in either column.
  const sortedA = [...ra].sort((x, y) => y.totalDamage - x.totalDamage);
  const sortedB = [...rb].sort((x, y) => y.totalDamage - x.totalDamage);
  const max = Math.max(...sortedA.map((p) => p.totalDamage), ...sortedB.map((p) => p.totalDamage), 1);
  assignRoles(ra);
  assignRoles(rb);
  const column = (label, sorted) =>
    el("div", { className: "meter-panel" }, [
      el("div", { className: "meter-panel-heading" }, [el("span", { className: "meter-panel-title", textContent: label }), el("span", { className: "meter-panel-subtitle", textContent: t("table.damage") })]),
      el("div", { className: "meter-list" }, sorted.map((p, i) => meterRow(i + 1, p, "totalDamage", max))),
    ]);

  const sameBoss = ea.bossId === eb.bossId;
  app.replaceChildren(
    el("h2", { textContent: `${t("compare.runsTitle")}: ${bossName}${sameBoss ? "" : ` / ${translateGameName(eb.bossName)}`}` }),
    ...(sameBoss ? [] : [el("p", { className: "error", textContent: t("compare.differentBoss") })]),
    el("div", { className: "cmp-actions" }, [
      link(t("compare.changeRun"), compareLink("/compare/runs", { a })),
      link(t("compare.swap"), compareLink("/compare/runs", { a: b, b: a })),
    ]),
    metrics,
    el("div", { className: "cmp-columns" }, [column(t("compare.runA"), sortedA), column(t("compare.runB"), sortedB)]),
  );
}

async function renderComparePlayers(params) {
  const a = params.get("a");
  const bossParam = params.get("boss");
  const b = params.get("b");
  setBreadcrumb([...gameCrumbs(), t("compare.playersTitle")]);
  if (!a) {
    renderNotFound();
    return;
  }
  showLoading(t("compare.playersTitle"));

  const steps = [t("compare.stepPlayer"), t("compare.stepBoss"), t("compare.stepOpponent")];

  // Step 2: which boss (only those the first player has data for).
  if (!bossParam) {
    const [data, bossList] = await Promise.all([fetchJson(`/api/players/${encodeURIComponent(a)}`), fetchJson(`/api/players/${encodeURIComponent(a)}/bosses`)]);
    const name = data.player.name;
    const rows = bossList.map((r) =>
      el("a", { className: "cmp-pick", href: compareLink("/compare/players", { a, boss: r.bossId }) }, [
        el("span", { className: "cmp-pick-main", textContent: translateGameName(r.bossName) }),
        el("span", { className: "cmp-pick-sub", textContent: `${translateGameName(r.instanceName)} · ${t("compare.runsCount", { count: r.runs })}` }),
        el("span", { className: "cmp-pick-value", textContent: `${formatNumber(r.bestIdps)} ${t("leaderboard.idpsShort")}` }),
      ]),
    );
    app.replaceChildren(
      compareSteps(steps, 1),
      el("h2", { textContent: t("compare.pickBoss", { name }) }),
      rows.length > 0 ? el("div", { className: "cmp-pick-list" }, rows) : el("p", { className: "empty", textContent: t("compare.noBosses", { name }) }),
    );
    return;
  }

  // Step 3: which opponent (only players with data for that boss).
  if (!b) {
    const [data, candidates] = await Promise.all([
      fetchJson(`/api/players/${encodeURIComponent(a)}`),
      fetchJson(`/api/bosses/${encodeURIComponent(bossParam)}/players?exclude=${encodeURIComponent(a)}`),
    ]);
    const bossRow = (await fetchJson(`/api/players/${encodeURIComponent(a)}/bosses`)).find((r) => String(r.bossId) === String(bossParam));
    const bossName = bossRow ? translateGameName(bossRow.bossName) : "";
    const filterInput = el("input", { type: "search", className: "instance-search", placeholder: t("compare.filterPlayers") });
    const list = el("div", { className: "cmp-pick-list" });
    const draw = () => {
      const needle = filterInput.value.trim().toLowerCase();
      const shown = candidates.filter((c) => !needle || c.name.toLowerCase() === needle);
      list.replaceChildren(
        ...(shown.length > 0
          ? shown.map((c) =>
              el("a", { className: "cmp-pick", href: compareLink("/compare/players", { a, boss: bossParam, b: c.playerId }) }, [
                el("span", { className: "cmp-pick-main" }, [iconLabel(c.className ? classIcon(c.className) : null, c.name)]),
                el("span", { className: "cmp-pick-sub", textContent: [serverLabel(c.serverName), c.guild, t("compare.runsCount", { count: c.runs })].filter((x) => x).join(" · ") }),
                el("span", { className: "cmp-pick-value", textContent: `${formatNumber(c.bestIdps)} ${t("leaderboard.idpsShort")}` }),
              ]),
            )
          : [el("p", { className: "empty", textContent: candidates.length === 0 ? t("compare.noOpponents", { name: data.player.name }) : t("search.noResults", { query: filterInput.value }) })]),
      );
    };
    filterInput.addEventListener("input", draw);
    draw();
    app.replaceChildren(
      compareSteps(steps, 2),
      el("h2", { textContent: t("compare.pickOpponent", { name: data.player.name, boss: bossName }) }),
      ...(candidates.length > 0 ? [el("div", { className: "instances-toolbar" }, [filterInput])] : []),
      list,
    );
    return;
  }

  // The comparison.
  const data = await fetchJson(`/api/compare/players?${new URLSearchParams({ a, b, boss: bossParam })}`);
  const bossName = translateGameName(data.boss.name);
  setBreadcrumb([...gameCrumbs(), link(bossName, gp(`/bosses/${data.boss.slug ?? data.boss.id}`)), t("compare.playersTitle")]);

  const head = (x) =>
    el("div", { className: "cmp-head" }, [
      el("strong", {}, [iconLabel(classIcon(x.best.className), x.player.name)]),
      el("a", { href: gp(`/players/${x.player.id}`), textContent: [serverLabel(x.player.serverName), x.player.guild].filter((v) => v).join(" · ") || t("compare.openProfile") }),
    ]);
  const A = data.a;
  const B = data.b;
  const pct = (n) => `${n.toFixed(1)} %`;

  const metrics = compareTable(head(A), head(B), [
    compareRow(t("compare.bestIdps"), A.best.idps, B.best.idps, formatNumber),
    compareRow(t("compare.avgIdps"), A.averages.idps, B.averages.idps, formatNumber),
    compareRow(t("compare.bestDamage"), A.best.totalDamage, B.best.totalDamage, formatNumber),
    compareRow(t("compare.avgDamage"), A.averages.totalDamage, B.averages.totalDamage, formatNumber),
    compareRow(t("compare.avgCrit"), A.averages.critRatePercent, B.averages.critRatePercent, pct),
    compareRow(t("compare.avgHps"), A.averages.hps, B.averages.hps, formatNumber),
    compareRow(t("compare.runs"), A.runs, B.runs, (n) => String(n)),
    compareRow(t("compare.bestRunDuration"), A.best.durationSeconds, B.best.durationSeconds, formatDuration, false),
  ]);

  // Skill split of each player's best run: share of that player's own damage, so a player with a
  // higher total isn't automatically "ahead" in every row.
  const skillMap = (x) => {
    const total = x.skills.reduce((acc, s) => acc + s.totalDamage, 0);
    return new Map(x.skills.map((s) => [s.skillName, { share: total > 0 ? (s.totalDamage / total) * 100 : 0, damage: s.totalDamage, icon: s.icon, names: s.names }]));
  };
  const skillsA = skillMap(A);
  const skillsB = skillMap(B);
  const names = [...new Set([...skillsA.keys(), ...skillsB.keys()])].sort(
    (x, y) => (skillsB.get(y)?.damage ?? 0) + (skillsA.get(y)?.damage ?? 0) - ((skillsB.get(x)?.damage ?? 0) + (skillsA.get(x)?.damage ?? 0)),
  );
  const skillCell = (m, name) => {
    const v = m.get(name);
    return el("td", { textContent: v ? `${v.share.toFixed(1)} % · ${formatNumber(v.damage)}` : "–" });
  };
  const skills =
    names.length > 0
      ? el("div", {}, [
          el("h3", { textContent: t("compare.skillsHeading") }),
          el("table", { className: "cmp-table" }, [
            el("thead", {}, [el("tr", {}, [el("th", { textContent: t("skillTable.skill") }), el("th", { textContent: A.player.name }), el("th", { textContent: B.player.name })])]),
            el("tbody", {}, names.map((n) => {
              const info = skillsA.get(n) ?? skillsB.get(n);
              return el("tr", {}, [el("td", {}, [el("span", { className: "cmp-skill" }, [iconTile("skill", info.icon, n), info.names?.[getLocale()] ?? n])]), skillCell(skillsA, n), skillCell(skillsB, n)]);
            })),
          ]),
        ])
      : null;

  app.replaceChildren(
    compareSteps(steps, 3),
    el("h2", { textContent: `${A.player.name} ${t("compare.vs")} ${B.player.name} – ${bossName}` }),
    el("div", { className: "cmp-actions" }, [
      link(t("compare.changeOpponent"), compareLink("/compare/players", { a, boss: bossParam })),
      link(t("compare.changeBoss"), compareLink("/compare/players", { a })),
      link(t("compare.swap"), compareLink("/compare/players", { a: b, boss: bossParam, b: a })),
    ]),
    metrics,
    el("p", { className: "pf-sub", textContent: t("compare.bestRunNote") }),
    ...(skills ? [skills] : []),
    el("p", { className: "cmp-actions" }, [
      link(`${A.player.name}: ${t("compare.bestRun")}`, participantHref(A.player.slug, A.best.participantId)),
      link(`${B.player.name}: ${t("compare.bestRun")}`, participantHref(B.player.slug, B.best.participantId)),
    ]),
  );
}

function renderNotFound() {
  setBreadcrumb([link(t("breadcrumb.home"), "/"), t("notFound.title")]);
  app.replaceChildren(el("h2", { textContent: t("notFound.title") }), el("p", {}, [link(t("notFound.backHome"), "/")]));
}

function navigate(path, { replace = false } = {}) {
  if (replace) {
    history.replaceState(null, "", path);
  } else {
    history.pushState(null, "", path);
  }
  return route();
}

// /download/latest is not a page of the app but the server's redirect to the installer: a click on it must reach the server (the app would
// swallow it, show the download page again and only a reload - which asks the server - started the download).
const SERVER_PATHS = ["/download/latest"];

function isAppPath(pathname) {
  if (SERVER_PATHS.includes(pathname.replace(/\/+$/, ""))) {
    return false;
  }

  return pathname === "/" || APP_SECTIONS.includes(pathname.split("/")[1]);
}

async function route() {
  if (featuresCleanup) {
    featuresCleanup();
    featuresCleanup = null;
  }
  markActiveNav();
  // Old links such as /instances#nightmare move to the real address of that category.
  if (location.pathname.replace(/\/+$/, "") === "/instances" && INSTANCE_TAB_ORDER.includes(location.hash.replace("#", ""))) {
    history.replaceState(null, "", `/instances/${location.hash.replace("#", "")}`);
  }
  const path = location.pathname.replace(/\/+$/, "") || "/";
  const params = new URLSearchParams(location.search);
  const segments = path.split("/").filter(Boolean);

  let section;
  let param;
  if (segments.length === 0) {
    currentGame = DEFAULT_GAME;
    section = "home";
  } else if (segments[0] === "download") {
    section = "download";
  } else if (segments[0] === "features") {
    section = "features";
  } else if (segments[0] === "changelog") {
    section = "changelog";
  } else if (segments[0] === "twitch-drops") {
    section = "twitch-drops";
  } else if (segments[0] === "legions" || segments[0] === "guilds") {
    section = "guilds";
    param = segments[1];
  } else if (segments[0] === "p" && segments[1]) {
    section = "private-stats";
    param = segments[1];
  } else if (segments[0] === "feedback") {
    section = "feedback";
  } else if (segments[0] === "privacy") {
    section = "privacy";
  } else if (segments[0] === "terms") {
    section = "terms";
  } else if (segments[0] === "aion2") {
    // Old address with the game segment: same page without it.
    location.replace(`/${segments.slice(1).join("/") || "instances"}${location.search}${location.hash}`);
    return;
  } else if (APP_SECTIONS.includes(segments[0])) {
    section = segments[0];
    param = segments[1];
    if (section === "compare") {
      param = segments[1];
    }
  } else {
    section = "notfound";
  }

  const isHome = section === "home";

  try {
    if (isHome) {
      await renderHome();
    } else if (section === "download") {
      await renderDownload();
    } else if (section === "features") {
      renderFeatures();
    } else if (section === "aion-2-dps-meter") {
      renderGuide();
    } else if (section === "how-it-works") {
      await renderArticle();
    } else if (section === "changelog") {
      await renderChangelog();
    } else if (section === "twitch-drops") {
      await renderTwitchDrops();
    } else if (section === "stats") {
      await renderStats();
    } else if (section === "status") {
      await renderStatus();
    } else if (section === "announcements") {
      await renderAnnouncements();
    } else if (section === "patchnotes") {
      await renderPatchnotes();
    } else if (section === "guilds") {
      await (param ? renderGuild(param) : renderGuilds());
    } else if (section === "private-stats") {
      await renderStats(param);
    } else if (section === "feedback") {
      renderFeedback();
    } else if (section === "privacy") {
      await renderPrivacy();
    } else if (section === "terms") {
      await renderTerms();
    } else if (section === "worldbosses" && !param) {
      await renderWorldBosses();
    } else if ((section === "worldbosses" || section === "instances") && param && segments[2] === "statistics") {
      await renderInstanceStatistics(param, params);
    } else if (section === "worldbosses") {
      await renderBosses(param, params);
    } else if (section === "instances" && (!param || INSTANCE_TAB_ORDER.includes(param))) {
      await renderInstances(param, segments[2], segments[3]);
    } else if (section === "instances") {
      await renderBosses(param, params);
    } else if (section === "bosses" && param) {
      await renderLeaderboard(param, params, segments[2] === "statistics");
    } else if (section === "encounters" && param) {
      await renderEncounter(param);
    } else if (section === "participants" && param) {
      await renderParticipant(param);
    } else if (section === "players" && param) {
      await renderPlayerProfile(param);
    } else if (section === "compare" && param === "runs") {
      await renderCompareRuns(params);
    } else if (section === "compare" && param === "players") {
      await renderComparePlayers(params);
    } else if (section === "search" && params.get("q")) {
      await renderSearchResults(params.get("q"));
    } else {
      renderNotFound();
    }
  } catch (err) {
    app.replaceChildren(el("p", { className: "error", textContent: t("general.error", { msg: err.message }) }));
  } finally {
    hydrating = false;
  }

  // Appended once here, after whichever branch above replaced #app's content - every route gets
  // it this way (including the error branch), not just whichever render function remembered to
  // build it itself. See buildSiteFooter's own remarks.
  app.appendChild(buildSiteFooter());
}

// Internal links navigate in place (History API) instead of reloading; anything else - external
// links, modifier-clicks for a new tab, downloads, static files - keeps the browser's default.
document.addEventListener("click", (e) => {
  const anchor = e.target.closest("a[href]");
  if (!anchor || anchor.target === "_blank" || anchor.hasAttribute("download") || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) {
    return;
  }
  const url = new URL(anchor.href, location.href);
  if (url.origin !== location.origin || !isAppPath(url.pathname)) {
    return;
  }
  e.preventDefault();
  if (url.hash && url.pathname + url.search === location.pathname + location.search) {
    document.getElementById(url.hash.slice(1))?.scrollIntoView({ behavior: "smooth", block: "start" });
    return;
  }
  if (url.pathname + url.search !== location.pathname + location.search) {
    navigate(url.pathname + url.search);
  }
});

document.getElementById("search-form").addEventListener("submit", (e) => {
  e.preventDefault();
  const query = document.getElementById("search-input").value.trim();
  if (query) {
    navigate(gp(`/search?q=${encodeURIComponent(query)}`));
  }
});

window.addEventListener("popstate", route);
setupLanguageSwitcher();
initThemeSwitcher(t);
setupNavToggle();
applyStaticTranslations();
route();
