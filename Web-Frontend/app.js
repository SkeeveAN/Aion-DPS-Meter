import { LOCALES, getLocale, setLocale, t, formatNumber, formatDate, translateGameName } from "./i18n.js";
import { smallPhoto, INSTANCE_IMAGES, INSTANCE_FOCUS, BOSS_IMAGES, BOSS_CARDS, INSTANCE_MIN_LEVEL, INSTANCE_FACTS, splitConquest } from "./game-data.js";
import { initThemeSwitcher } from "./theme.js";

const app = document.getElementById("app");
const breadcrumb = document.getElementById("breadcrumb");

// Real path URLs (/aion2/bosses/enhanced-harcon) - one address per page, so search engines and
// Discord previews see distinct pages. The first path segment names the game (only Aion 2 exists
// now; the segment stays so every shared link keeps working).
const APP_SECTIONS = ["download", "feedback", "privacy", "terms", "instances", "worldbosses", "bosses", "players", "encounters", "participants", "compare", "search"];
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
  Object.assign(node, props);
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

// Aion 2's nine classes have no icon files - a short text badge stands in.
function classIcon(className) {
  const file = CLASS_EMBLEM[className];
  if (file) {
    return el("img", { className: "class-emblem", src: `/images/aion2/classes/badge/${file}.webp`, alt: className, title: className, width: 128, height: 128, loading: "lazy" });
  }
  return el("span", { className: "class-badge", title: className, textContent: AION2_CLASS_ABBREVIATIONS[className] ?? className.slice(0, 3).toUpperCase() });
}

function factionIcon(faction) {
  return faction ? icon(`/icons/races/${encodeURIComponent(faction)}.png`, "faction-icon") : null;
}

function iconLabel(iconEl, text) {
  return el("span", { className: "icon-label" }, [iconEl, text].filter((x) => x != null));
}

const SITE_TITLE = "Aion DPS Meter";
const HOME_TITLE = "Aion DPS Meter – Free Damage Meter & Boss Leaderboards";

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
  select.addEventListener("change", () => {
    setLocale(select.value);
    applyStaticTranslations();
    initThemeSwitcher(t);
    route();
  });
}

function applyStaticTranslations() {
  document.getElementById("search-input").placeholder = t("nav.searchPlaceholder");
  document.getElementById("search-button").textContent = t("nav.searchButton");
  document.getElementById("feedback-link").textContent = t("nav.feedback");
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

const GITHUB_REPO = "SkeeveAN/Aion-DPS-Meter";

/**
 * Homepage (aiondps_design_pack_v1 startseite brief) - a fast way in to the client download and
 * the real Aion/Aion 2 data, not a marketing funnel. Everything below the hero is real data or
 * omitted entirely: the live stats bar only appears once at least one real number is non-zero
 * (Backend/src/routes/stats.ts already returns honest zeros for an empty game), and the top
 * players/recent activity columns only draw from whichever games actually have real rows.
 */
async function renderHome() {
  setBreadcrumb([]);
  document.title = HOME_TITLE;
  showLoading(t("loading.instances"));

  // Centered hero (aiondps_claude_design_pack prototype comparison, 2026-09-24, "index-3.html"):
  // no side quick-start card competing with the hero text - those same three real destinations
  // fold into inline pills below the CTAs instead.
  const hero = el("div", { className: "home-hero" }, [
    el("p", { className: "home-hero-eyebrow", textContent: t("home.eyebrow") }),
    el("h1", { className: "home-hero-title", textContent: SITE_TITLE }),
    el("p", { className: "home-hero-slogan", textContent: t("home.slogan") }),
    el("p", { className: "home-hero-tagline", textContent: t("home.tagline") }),
    el("div", { className: "home-hero-ctas button-row" }, [
      el("a", { className: "btn btn-blue", href: "/instances" }, [el("span", { textContent: t("breadcrumb.instances") })]),
      el("a", { className: "btn btn-blue", href: "/worldbosses" }, [el("span", { textContent: t("category.worldboss") })]),
    ]),
  ]);
  const heroRow = el("div", { className: "home-hero-row" }, [hero]);

  const aion2Stats = await fetchJson("/api/stats/summary?game=aion2").catch(() => null);
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
  const topPlayers = await fetchJson("/api/players/top?game=aion2&limit=5").catch(() => []);
  const topPlayersGame = "aion2";
  const topPlayersSection = topPlayers.length > 0 ? buildTopPlayersSection(topPlayers, topPlayersGame) : null;

  const recentActivity = (await fetchJson("/api/activity/recent?game=aion2&limit=6").catch(() => []))
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
  app.replaceChildren(heroRow, el("div", { className: "home-below-hero" }, [...(statsBar ? [statsBar] : []), contentGrid]));
}

// 3-column footer (aiondps_claude_design_pack prototype comparison, index-3.html): logo left, CTA
// centered, legal + real social links right - the prototype only had the center CTA wired up
// before; these are the same real destinations that comparison page used. Used to be built only
// inside renderHome, so every other page (encounters, instances, bosses, leaderboard, participant/
// player profiles, search, legal pages, download, 404...) silently had no footer at all - reported
// by the user for the encounter page specifically, but it was really every non-home route. Now a
// standalone builder appended once, centrally, by route() itself (see its own remarks) rather than
// something every individual render function has to remember to add.
function buildSiteFooter() {
  const cta = el("div", { className: "home-footer-cta" }, [
    el("div", { className: "home-footer-logo" }, [
      el("img", { src: "/logo.webp", alt: "", className: "home-footer-emblem", loading: "lazy" }),
      el("img", { src: "/images/ui/aion-dps-wordmark.webp", alt: "Aion DPS", className: "wordmark", width: 186, height: 96, loading: "lazy" }),
    ]),
    el("div", { className: "home-footer-center" }, [
      el("h2", { textContent: t("home.footerCtaHeading") }),
      el("a", { className: "btn btn-orange", href: "/download" }, [
        el("span", { className: "btn-icon", textContent: "↓" }),
        el("span", { textContent: t("home.downloadCta").replace(/^⬇\s*/, "") }),
      ]),
    ]),
    el("div", { className: "home-footer-links" }, [
      el("div", { className: "home-footer-legal" }, [
        link(t("legal.privacyTitle"), "/privacy"),
        link(t("legal.termsTitle"), "/terms"),
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
        el("span", { textContent: [displayName({ name: r.bossName, nameEn: r.bossNameEn }), r.serverName].filter(Boolean).join(" · ") }),
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
    const instanceName = displayName({ name: r.instanceName, nameEn: r.instanceNameEn });
    const photo = BOSS_CARDS[r.bossNameEn] ?? BOSS_CARDS[r.bossName] ?? BOSS_IMAGES[r.bossNameEn] ?? BOSS_IMAGES[r.bossName] ?? INSTANCE_IMAGES[r.instanceNameEn] ?? INSTANCE_IMAGES[r.instanceName];
    return el("a", { className: "hp-run", href: gp(`/encounters/${r.encounterId}`) }, [
      el("span", { className: "hp-thumb", style: photo ? `background-image:url('${smallPhoto(photo)}')` : "" }),
      el("span", { className: "hp-run-mid" }, [
        el("b", {}, [
          displayName({ name: r.bossName, nameEn: r.bossNameEn }),
          // A Nightmare run carries its difficulty step (1-10) in the mode.
          ...(r.instanceCategory === "nightmare" && /^\d+$/.test(r.mode ?? "")
            ? [el("span", { className: "hp-nm-pill", textContent: `${t("category.nightmare")} ${r.mode}` })]
            : []),
        ]),
        el("span", { className: "hp-run-sub" }, [
          `${instanceName} · ${formatDuration(r.durationSeconds)} · ${formatRelativeTime(r.createdAt)}`,
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
    sections.push(el("section", { className: "legal-section" }, [el("h2", { textContent: heading }), el("p", { textContent: t(bodyKey) })]));
  }
  app.replaceChildren(
    el("div", { className: "legal-page" }, [el("h1", { textContent: t(titleKey) }), el("p", { className: "legal-intro", textContent: t(introKey) }), ...sections]),
  );
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

async function renderDownload() {
  setBreadcrumb([link(t("breadcrumb.home"), "/"), t("breadcrumb.download")]);
  showLoading(t("loading.version"));

  const hero = el("div", { className: "download-hero" }, [
    el("img", { src: "/logo.webp", alt: "" }),
    el("div", {}, [
      el("h2", { textContent: "Aion DPS" }),
      el("p", { textContent: t("download.heroDescription") }),
    ]),
  ]);

  const features = el("div", { className: "feature-grid" }, [
    featureCard(t("download.feature1Title"), t("download.feature1Text")),
    featureCard(t("download.feature2Title"), t("download.feature2Text")),
    featureCard(t("download.feature3Title"), t("download.feature3Text")),
    featureCard(t("download.feature4Title"), t("download.feature4Text")),
  ]);

  const stepsSection = el("div", {}, [
    el("h2", { textContent: t("download.installationHeading") }),
    el("ol", { className: "steps" }, [
      el("li", { textContent: t("download.step1") }),
      el("li", { textContent: t("download.step2") }),
      el("li", { textContent: t("download.step3") }),
      el("li", { textContent: t("download.step4") }),
      el("li", { textContent: t("download.step5") }),
    ]),
  ]);

  const repoLink = el("p", { className: "download-meta" }, [
    t("download.repoLinkPrefix"),
    el("a", { href: `https://github.com/${GITHUB_REPO}`, textContent: `github.com/${GITHUB_REPO}`, target: "_blank", rel: "noopener" }),
  ]);

  let downloadSection;
  try {
    const releases = await fetchJson(`https://api.github.com/repos/${GITHUB_REPO}/releases`);
    const latest = releases[0];
    const setupAsset = latest?.assets?.find((a) => a.name.endsWith("-Setup.exe"));

    if (latest && setupAsset) {
      downloadSection = el("div", {}, [
        el("a", { className: "download-cta", href: setupAsset.browser_download_url, textContent: t("download.downloadButton", { tag: latest.tag_name }) }),
        el("p", { className: "download-meta", textContent: t("download.meta", { name: setupAsset.name, size: formatBytes(setupAsset.size) }) }),
      ]);
    } else {
      downloadSection = el("p", { className: "empty", textContent: t("download.noInstallerFound") });
    }
  } catch (err) {
    downloadSection = el("p", { className: "error", textContent: t("download.versionLoadError", { msg: err.message }) });
  }

  app.replaceChildren(hero, downloadSection, features, stepsSection, repoLink);
}

function featureCard(title, text) {
  return el("div", { className: "feature-card" }, [
    el("h3", { textContent: title }),
    el("p", { textContent: text }),
  ]);
}

function formatBytes(bytes) {
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

// Shared by the instance grid and the boss grid below - a "poster" tile is just a photo (optional),
// a dark scrim for legibility, and a light title overlaid on top, linking somewhere. objectPosition
// defaults to centered (right for a landscape instance photo) but a boss portrait is a tall
// character render - "top" keeps the face in frame instead of centering on the torso.
function posterCard(href, photo, title, objectPosition) {
  const children = [el("div", { className: "poster-scrim" })];
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
function displayName(row) {
  // Conquest instances read "Draupnir ★" (their star rating), not "Draupnir (Conquest)".
  const conquest = splitConquest(row.name);
  if (conquest) {
    return `${displayName({ name: conquest.base })} ${conquest.stars}`;
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
          : link(displayName({ name: e.instanceName, nameEn: e.instanceNameEn }), gp(`${worldBoss ? "/worldbosses" : "/instances"}/${e.instanceSlug}`)),
      ]
    : [];
  return [...(worldBoss ? [link(t("breadcrumb.home"), "/"), link(t("category.worldboss"), gp("/worldbosses"))] : gameCrumbs()), ...tail, link(translateGameName(e.bossName), gp(`/bosses/${e.bossId}`))];
}

function areaCrumbs(instance) {
  return isWorldBossArea(instance) ? [link(t("breadcrumb.home"), "/"), link(t("category.worldboss"), gp("/worldbosses"))] : gameCrumbs();
}

function baseInstanceName(i) {
  return i.name.replace(/ \(Conquest\)$/, "");
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
  // The card adds its own gold stars below, so a Conquest name is shown without the stars displayName() appends.
  const plainName = splitConquest(i.name) ? displayName({ name: splitConquest(i.name).base }) : displayName(i);
  const title = [document.createTextNode(label ?? plainName)];
  if (facts.stars) {
    title.push(el("span", { className: "ip-stars", textContent: ` ${"★".repeat(facts.stars)}`, title: `${facts.stars} / 3` }));
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

function modeLabel(mode) {
  return /^\d+$/.test(mode) ? t("mode.stage", { n: mode }) : t(`mode.${mode}`);
}

const CATEGORY_MODE_LIST = {
  ascension: ["easy", "medium", "hard", "extreme"],
  transcendence: ["1", "2", "3", "4"],
};

function expeditionPane(list, variant) {
  const hard = variant === "hard";
  const items = list.filter((i) => (hard ? i.variant === "conquest" : i.variant !== "conquest"));
  const seg = el("div", { className: "ip-seg", role: "group" }, [
    ["normal", "/instances/expedition", t("mode.explore"), t("mode.normalSub")],
    ["hard", "/instances/expedition/hard", t("mode.conquest"), t("mode.hardSub")],
  ].map(([v, href, name, sub]) => {
    const a = el("a", { href: gp(href), textContent: `${name} · ${sub}` });
    a.setAttribute("aria-pressed", String((v === "hard") === hard));
    return a;
  }));
  const body = el("div", { className: "ip-grid" }, items.map((i) => instanceFactCard(i, { label: displayName({ name: baseInstanceName(i), nameEn: baseInstanceName(i) }) })));
  return el("div", {}, [seg, body]);
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

async function renderInstances(categoryParam, variantParam) {
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
    return category === "expedition" ? list.filter((i) => i.variant !== "conquest").length : list.length;
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
    pane = expeditionPane(list, variantParam);
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

async function renderBosses(instanceSlug) {
  setBreadcrumb([...gameCrumbs(), t("breadcrumb.bosses")]);
  showLoading(t("loading.bosses"));

  // Combined across the servers, like a boss leaderboard (see renderLeaderboard).
  const statsQuery = new URLSearchParams({ game: currentGame });

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
    setBreadcrumb([...areaCrumbs(instance), displayName(instance)]);
  }

  const hero = instance ? [instanceHero(instance, bosses.length, stats)] : [];
  if (bosses.length === 0) {
    app.replaceChildren(...hero, el("p", { className: "empty", textContent: t("bosses.emptyNoBosses") }));
    return;
  }

  const instancePhoto = instance ? INSTANCE_IMAGES[instance.name] : undefined;

  // Per the user: Sauro's two keyed bosses must show 1-key before 2-key - the API's own ordering
  // is alphabetical on the raw (untranslated) name, which happens to put Sheba's ahead of
  // Ahuradim's. A tiny curated override rather than a general boss-ordering feature.
  const BOSS_SORT_OVERRIDE = { "Gardenführer Achradim": 0, "Brigade General Sheba": 1 };
  const sortedBosses = [...bosses].sort((a, b) => {
    const priority = (BOSS_SORT_OVERRIDE[a.name] ?? Infinity) - (BOSS_SORT_OVERRIDE[b.name] ?? Infinity);
    return priority !== 0 ? priority : a.name.localeCompare(b.name);
  });

  const grid = el(
    "div",
    { className: "poster-grid" },
    sortedBosses.map((b) => posterCard(gp(`/bosses/${b.slug}`), BOSS_CARDS[b.name] ?? BOSS_IMAGES[b.name] ?? instancePhoto, displayName(b), BOSS_CARDS[b.name] ? "center" : BOSS_IMAGES[b.name] ? "64% center" : "top")),
  );
  app.replaceChildren(...hero, el("h2", { textContent: t("bosses.heading") }), grid);
}

// Faction + class icon + name in one cell - matches myaion.eu's own combined "Player" column
// rather than the three separate ones the old accordion-based rosterTable used. Used for a single
// person (an encounter's own roster, or one solo attempt) - see groupPlayersCell below for the
// boss leaderboard's group rows, which need to show every member, not just one.
// A small person icon behind a player's name that opens the character profile - only for players
// who have one (the API says so with hasProfile).
const ICON_PROFILE =
  '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><circle cx="12" cy="8" r="4"/><path d="M4 21c0-4.4 3.6-7 8-7s8 2.6 8 7"/></svg>';

function profileIcon(player) {
  if (!player || !player.hasProfile || !player.playerId) {
    return null;
  }

  return el("a", { className: "profile-link", href: gp(`/players/${player.playerSlug ?? player.playerId}`), title: t("player.openProfile"), "aria-label": t("player.openProfile"), innerHTML: ICON_PROFILE });
}

function playerCell(faction, className, name, href, serverName, player) {
  // Aion 2 rankings mix servers (per the user: cross-server runs exist there), so the server rides
  // along as a tag; classic Aion's pages are always scoped to one server and need none.
  const tag = currentGame === "aion2" && serverName ? el("span", { className: "server-tag", textContent: serverName, title: serverName }) : null;
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
  // The wide boss banners (2400x340) show whole at their own proportions instead of being zoomed into the taller photo hero.
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
  return el("img", { className: `lb-emblem${small ? " sm" : ""}${tint ? " tinted" : ""}`, src: `/images/aion2/classes/${file}.webp`, alt: className, title: className, loading: "lazy", style: tint ? `--tint:url(#emblem-tint-${file})` : "" });
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
          el("small", {}, [link(`${formatDuration(g.durationSeconds)} · ${new Date(g.startedAt).toLocaleString(undefined, { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" })}`, gp(`/encounters/${g.encounterId}`), "lb-open")]),
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

async function renderLeaderboard(bossSlug, params) {
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
  const data = await fetchJson(`/api/bosses/${encodeURIComponent(bossSlug)}/leaderboard?${query}`);
  const bossPath = gp(`/bosses/${data.boss.slug}`);
  const bossName = displayName(data.boss);
  // Nightmare bosses are reached through the category's boss tree, so the way back leads there
  // (/instances/nightmare), not to the single "instance" (Root Cellar ...); every other category keeps the instance.
  const worldBoss = data.boss.instanceCategory === "worldboss";
  const instanceCrumb =
    data.boss.instanceCategory === "nightmare"
      ? link(t("category.nightmare"), gp("/instances/nightmare"))
      : link(displayName({ name: data.boss.instanceName, nameEn: data.boss.instanceNameEn }), gp(`${worldBoss ? "/worldbosses" : "/instances"}/${data.boss.instanceSlug}`));
  setBreadcrumb([...(worldBoss ? [link(t("breadcrumb.home"), "/"), link(t("category.worldboss"), gp("/worldbosses"))] : gameCrumbs()), instanceCrumb, bossName]);

  const hero = bossHero(data);
  // Difficulty steps (Nightmare level, Ascension difficulty, ...): each one is ranked on its own.
  const modeChips = data.modes?.length
    ? el("nav", { className: "mode-chips" }, [
        el("span", { textContent: t("mode.heading") }),
        ...data.modes.map((m) => {
          const a = el("a", { className: `mode-chip${m.mode === data.selectedMode ? " active" : ""}`, href: `${bossPath}?mode=${encodeURIComponent(m.mode)}` }, [modeLabel(m.mode), el("small", { textContent: String(m.runs) })]);
          return a;
        }),
      ])
    : null;
  const tabs = serverTabs(data, bossPath);
  const stats = fightStatsRow(data.stats);
  const sections = [hero, ...(modeChips ? [modeChips] : []), ...(stats ? [stats] : [])];
  if (tabs) {
    sections.push(tabs);
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
            el("h3", {}, [iconLabel(classIcon(className), className)]),
            rankedTable(rows, false),
          ]);
        }),
      ),
    ]);

    app.replaceChildren(...sections, classSection);
    return;
  }

  if (data.topGroups.length === 0) {
    app.replaceChildren(...sections, el("p", { className: "empty", textContent: t("leaderboard.emptyNoFights") }));
    return;
  }

  app.replaceChildren(...sections, el("h3", {}, [t("leaderboard.topGroupsHeading", { n: data.topGroups.length })]), renderGroupBoard(data.topGroups));
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
  const icon = el("span", { className: "profile-link", role: "link", tabindex: "0", title: t("player.openProfile"), "aria-label": t("player.openProfile"), innerHTML: ICON_PROFILE });
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

function meterRow(rank, p, metric, max) {
  const meta = classMeta(p.className);
  const value = p[metric];
  const pct = max > 0 ? (value / max) * 100 : 0;
  const isPet = p.className === "?";
  const serverTag = currentGame === "aion2" && p.serverName ? el("span", { className: "server-tag", textContent: p.serverName, title: p.serverName }) : null;

  return el(
    "a",
    { className: "meter-row" + (isPet ? " meter-row-pet" : ""), href: gp(`/participants/${p.participantId}`) },
    [
      el("div", { className: "meter-row-fill", style: `width: ${pct.toFixed(1)}%; background: ${hexToRgba(meta.color, 0.28)};` }),
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

// Damage TAKEN from the boss, i.e. aggro/tanking - a different question from the Schaden panel
// above (who hit the boss) and kept visually distinct (compact rows, danger-tinted bars, its own
// subheading) so the two are never mistaken for the same ranking, unlike the old page's two
// same-looking bar charts. damageTaken is 0 for every row from a client older than the field
// itself (see uploadSchema.ts) - such an encounter just renders an all-zero list rather than
// erroring.
function aggroPanel(roster) {
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
    return el("a", { className: "meter-row meter-row-compact", href: gp(`/participants/${p.participantId}`), title: label ? `${formatNumber(p.damageTaken)} · ${label}` : "" }, [
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
  const panels = [
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

  const grid = el("div", { className: "encounter-grid" }, [
    meterPanel(data.roster),
    el("div", { className: "encounter-side-col" }, [aggroPanel(data.roster)]),
  ]);

  app.replaceChildren(hero, statsRow, compareButton(t("compare.ctaRuns"), compareLink("/compare/runs", { a: data.encounter.id })), grid);
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

// One skill as a ranked row: game icon (initials when the client has none), share bar relative to
// the strongest skill, and the hit statistics underneath.
function skillRow(s, total, top, heal) {
  const hits = s.hits > 0 ? s.hits : 1;
  const tile = el("span", { className: "pt-ico" });
  if (s.icon) {
    const img = el("img", { src: `/images/aion2/icons/skill/${s.icon}.webp`, alt: "", loading: "lazy", width: 128, height: 128 });
    img.addEventListener("error", () => {
      img.remove();
      tile.textContent = (s.names?.[getLocale()] ?? s.skillName).slice(0, 2);
    });
    tile.append(img);
  } else {
    tile.textContent = s.skillName.slice(0, 2);
  }
  return el("div", { className: "pt-row" }, [
    tile,
    el("div", { className: "pt-main" }, [
      el("div", { className: "pt-name" }, [el("span", { textContent: s.names?.[getLocale()] ?? s.skillName }), el("span", { className: "pt-share", textContent: `${total > 0 ? ((s.totalDamage / total) * 100).toFixed(1) : "0.0"}%` })]),
      el("div", { className: `pt-bar${heal ? " heal" : ""}` }, [el("i", { style: `width:${top > 0 ? (s.totalDamage / top) * 100 : 0}%` })]),
      el("div", { className: "pt-meta" }, [
        el("span", { textContent: `${t("skillTable.hits")} ${formatNumber(s.hits)}` }),
        el("span", { textContent: `${t("skillTable.critPercent")} ${s.hits > 0 ? ((s.critHits / s.hits) * 100).toFixed(0) : 0}%` }),
        el("span", { textContent: `${t("skillTable.avg")} ${formatNumber(s.totalDamage / hits)}` }),
        el("span", { textContent: `${t("skillTable.max")} ${formatNumber(s.maxHit)}` }),
      ]),
    ]),
    el("div", { className: "pt-total", textContent: formatNumber(s.totalDamage) }),
  ]);
}

async function renderParticipant(participantId) {
  setBreadcrumb([...gameCrumbs(), t("loading.participant")]);
  showLoading(t("loading.participant"));

  const data = await fetchJson(`/api/participants/${encodeURIComponent(participantId)}`);
  setBreadcrumb([...bossCrumbs(data.encounter), data.participant.playerName]);

  const p = data.participant;
  const e = data.encounter;
  const minutes = Math.floor(e.durationSeconds / 60);
  const seconds = String(Math.round(e.durationSeconds % 60)).padStart(2, "0");
  const kpi = (label, value, cls) => el("div", { className: "pt-kpi" }, [el("small", { textContent: label }), el("b", { className: cls ?? "", textContent: value })]);
  const tags = [el("span", { className: "pt-tag", textContent: p.className })];
  if (p.faction) {
    tags.push(el("span", { className: "pt-tag" }, [factionIcon(p.faction), p.faction]));
  }
  if (p.serverName) {
    tags.push(el("span", { className: "pt-tag", textContent: p.serverName }));
  }

  const sections = [
    el("div", { className: "pt-hero" }, [
      el("span", { className: "pt-emb" }, [classEmblem(p.className, false)]),
      el("div", {}, [
        el("h2", {}, [p.playerName, profileIcon({ hasProfile: true, playerId: p.playerId, playerSlug: p.playerSlug })]),
        el("div", { className: "pt-sub", textContent: `${translateGameName(e.bossName)} · ${formatDate(new Date(e.startedAt))} · ${minutes}:${seconds}` }),
        el("div", { className: "pt-tags" }, tags),
      ]),
    ]),
    el("div", { className: "pt-kpis" }, [
      kpi(t("participant.dps"), formatNumber(p.dps), "accent"),
      kpi(t("table.damage"), formatNumber(p.totalDamage)),
      kpi(t("participant.critRate"), `${p.critRatePercent.toFixed(1)}%`),
      kpi(t("participant.hps"), formatNumber(p.hps), "heal"),
      kpi(t("participant.totalHealing"), formatNumber(p.totalHealing)),
    ]),
  ];
  sections.push(
    el("div", { className: "cmp-cta pt-actions" }, [
      el("a", { className: "cmp-btn", href: compareLink("/compare/players", { a: p.playerId, boss: e.bossId }), textContent: t("compare.ctaPlayers") }),
      el("a", { className: "cmp-btn", href: compareLink("/compare/runs", { a: e.id }), textContent: t("compare.ctaRuns") }),
    ]),
  );
  const group = (heading, list, heal) => {
    const total = list.reduce((sum, s) => sum + s.totalDamage, 0);
    const top = list.reduce((m, s) => Math.max(m, s.totalDamage), 0);
    sections.push(el("h3", { className: `pt-h${heal ? " heal" : ""}`, textContent: heading }), ...list.map((s) => skillRow(s, total, top, heal)));
  };
  if (data.damageSkills.length > 0) {
    group(t("participant.damageSkillsHeading"), data.damageSkills, false);
  }
  if (data.healSkills.length > 0) {
    group(t("participant.healSkillsHeading"), data.healSkills, true);
  }

  app.replaceChildren(...sections);
}

// Daevanion stat tokens come from the game data as CamelCase ("CriticalResist"); shown spaced out.
function statLabel(token) {
  return token.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/^HP /, "HP ").replace(/^MP /, "MP ");
}

// Skill names come in the game client's languages (de, en, es, fr, ja, ko, pt, ru); the site's other
// languages (pl, tr, zh) fall back to the English name.
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
function iconTile(kind, icon, name, { color, badge, badgeClass } = {}) {
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
  return tile;
}

// One tooltip for the whole page, shown next to the pointer over anything with a `tip` builder.
function attachTooltip(target, build) {
  let tip = document.querySelector(".pf-tip");
  if (!tip) {
    tip = el("div", { className: "pf-tip", hidden: true });
    document.body.append(tip);
  }
  const place = (e) => {
    const w = tip.offsetWidth;
    const h = tip.offsetHeight;
    tip.style.left = `${Math.min(e.clientX + 16, window.innerWidth - w - 8)}px`;
    tip.style.top = `${Math.max(8, Math.min(e.clientY + 12, window.innerHeight - h - 8))}px`;
  };
  target.addEventListener("mouseenter", (e) => {
    tip.replaceChildren(...build());
    tip.hidden = false;
    place(e);
  });
  target.addEventListener("mousemove", place);
  target.addEventListener("mouseleave", () => {
    tip.hidden = true;
  });
}

function gearTip(g) {
  const color = GRADE_COLOR[g.grade] ?? GRADE_COLOR[1];
  return [
    el("div", { className: "pf-tip-head", style: `--ico-color:${color}` }, [
      el("div", { className: "pf-tip-title", textContent: g.name + (g.enchant > 0 ? ` +${g.enchant}` : "") }),
      el("div", { className: "pf-tip-sub" }, [el("span", { style: `color:${color}`, textContent: `${GRADE_NAME[g.grade] ?? ""}${g.tier > 0 ? ` · ${t("profile.tier")} ${g.tier}` : ""}` }), ` ${g.slotName ? t(`slot.${g.slotName}`) : ""}`]),
      g.itemLevel > 0 ? el("div", { className: "pf-tip-il", textContent: `${t("profile.itemLevel")} ${g.itemLevel}` }) : null,
    ].filter((x) => x != null)),
  ];
}

function gearSlot(g) {
  const color = GRADE_COLOR[g.grade] ?? GRADE_COLOR[1];
  const slot = el("div", { className: "pf-slot", style: `--ico-color:${color}` }, [
    iconTile("item", g.icon, g.name, { color, badge: g.enchant > 0 ? `+${g.enchant}` : "" }),
    el("div", {}, [
      el("div", { className: "pf-slot-name", textContent: g.name }),
      el("div", { className: "pf-slot-sub", textContent: `${g.slotName ? t(`slot.${g.slotName}`) : `#${g.slot}`}${g.itemLevel > 0 ? ` · ${t("profile.itemLevelShort")} ${g.itemLevel}` : ""}` }),
      g.grade >= 4 && g.tier > 0 ? el("span", { className: "pf-tier", textContent: `${GRADE_NAME[g.grade] ?? ""} ${t("profile.tier")} ${g.tier}` }) : null,
    ].filter((x) => x != null)),
  ]);
  attachTooltip(slot, () => gearTip(g));
  return slot;
}

function renderEquipmentTab(profile) {
  const rank = (names, g) => {
    const i = names.indexOf(g.slotName);
    return i < 0 ? 99 : i;
  };
  const groups = { armor: [], accessories: [], other: [] };
  for (const g of profile.gear) {
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

function renderSkillsTab(profile) {
  const section = (title, list, cls) =>
    el("div", { className: "pf-card" }, [
      el("h4", {}, [el("span", { className: `pf-dot ${cls}` }), `${title} · ${list.length}`]),
      el(
        "div",
        { className: "pf-skill-grid" },
        list.map((s) => {
          const name = localizedSkillName(s);
          const tile = el("div", { className: "pf-skill" }, [
            iconTile("skill", s.icon, name, { color: s.passive ? "#3fcf55" : "#f0a030", badge: `${t("profile.levelShort")} ${s.level}`, badgeClass: s.level > s.baseLevel ? "bonus" : "" }),
            el("span", { className: "pf-skill-name", textContent: name }),
          ]);
          attachTooltip(tile, () => [
            el("div", { className: "pf-tip-head" }, [
              el("div", { className: "pf-tip-title", textContent: name }),
              el("div", { className: "pf-tip-sub", textContent: `${t("profile.skillLevel")} ${s.level}${s.level > s.baseLevel ? ` (${s.baseLevel} + ${s.level - s.baseLevel})` : ""}` }),
            ]),
          ]);
          return tile;
        }),
      ),
    ]);
  // Side by side: active | passive | stigma (the stigma block only exists once a profile carries
  // the flag; the game lets a player equip only four of them).
  const hasBar = profile.skills.some((s) => s.equipped);
  const blocks = [
    // With a skill bar in the profile only the equipped actives are listed, as in the game's skill window.
    section(t("profile.skillsActive"), profile.skills.filter((s) => !s.passive && !s.stigma && (!hasBar || s.equipped)), "active"),
    // ids like 11000000 are the class's weapon-equip entry, not a skill the player trains
    section(t("profile.skillsPassive"), profile.skills.filter((s) => s.passive && s.id % 1000000 !== 0), "passive"),
    profile.skills.some((s) => s.stigma) ? section(t("profile.skillsStigma"), profile.skills.filter((s) => s.stigma), "stigma") : null,
  ].filter((x) => x != null);
  return el("div", {}, [
    el("div", { className: "pf-skills", style: `--blocks:${blocks.length}` }, blocks),
    el("p", { className: "profile-source" }, [el("i", { className: "pf-key bonus" }), ` ${t("profile.legendBonus")}`]),
  ]);
}

// Species knowledge (pet window): one card per species with its level and the analysed effects. Percent
// stats arrive in hundredths (145 = 1.45 %) and are shown with one decimal, as the game does.
function speciesValue(effect) {
  if (!effect.percent) {
    return formatNumber(effect.value);
  }
  const text = (effect.value / 100).toFixed(1);
  const decimal = (1.1).toLocaleString(getLocale()).charAt(1);
  return `${text.replace(".", decimal)} %`;
}

function renderSpeciesTab(profile) {
  const locale = getLocale();
  const cards = profile.species.map((k) => {
    const maxed = k.progress === 0 && k.level >= 10;
    const rows = k.effects.map((e) =>
      el("li", {}, [
        el("span", { className: "pf-sp-stat", textContent: (e.names?.[locale] ?? e.names?.en ?? e.name) }),
        el("strong", { textContent: speciesValue(e) }),
      ]),
    );
    return el("div", { className: "pf-card pf-species-card" }, [
      el("h4", {}, [el("span", { textContent: k.names[locale] ?? k.names.en ?? k.key }), el("em", { textContent: t("profile.speciesLevel", { level: k.level }) })]),
      el("div", { className: "pf-sp-sub", textContent: maxed ? t("profile.speciesMax") : `${t("profile.speciesProgress")}: ${formatNumber(k.progress)}` }),
      el("ul", { className: "pf-sp-list" }, rows),
    ]);
  });
  return el("div", {}, [el("div", { className: "pf-species" }, cards), el("p", { className: "profile-source", textContent: t("profile.speciesNote") })]);
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
    const total = b.cells.filter((c) => c[2] !== 0).length;
    return el("button", { type: "button", className: "pf-pill" }, [b.name, el("b", { textContent: ` ${b.activeNodes} / ${total || "?"}` })]);
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

function renderRunsTab(history) {
  if (history.length === 0) {
    return el("p", { className: "empty", textContent: t("player.emptyNoFights") });
  }
  const rows = history.map((h) =>
    el("tr", {}, [
      el("td", { textContent: formatDate(new Date(h.startedAt)) }),
      el("td", {}, [link(translateGameName(h.bossName), gp(`/encounters/${h.encounterId}`))]),
      el("td", {}, [iconLabel(classIcon(h.className), h.className)]),
      el("td", { textContent: formatNumber(h.totalDamage) }),
      el("td", { textContent: formatNumber(h.idps) }),
      el("td", { textContent: `${h.critRatePercent.toFixed(1)}%` }),
      el("td", {}, [link(t("table.details"), gp(`/participants/${h.participantId}`))]),
    ]),
  );
  return el("table", {}, [
    el("thead", {}, [
      el("tr", {}, [
        el("th", { textContent: t("table.date") }),
        el("th", { textContent: t("table.boss") }),
        el("th", { textContent: t("table.class") }),
        el("th", { textContent: t("table.damage") }),
        el("th", { textContent: t("table.idps") }),
        el("th", { textContent: t("table.critPercent") }),
        el("th", {}),
      ]),
    ]),
    el("tbody", {}, rows),
  ]);
}

// The player strip: shown above every tab.
function renderPlayerStrip(profile, player) {
  const sub = [];
  if (profile?.className) {
    sub.push(profile.level ? t("profile.classLevel", { className: profile.className, level: profile.level }) : profile.className);
  } else if (profile?.level) {
    sub.push(`Level ${profile.level}`);
  }
  const nodes = profile ? profile.daevanion.reduce((sum, b) => sum + b.activeNodes, 0) : 0;
  const numbers = profile
    ? [
        el("div", {}, [el("strong", { className: "accent", textContent: String(profile.averageItemLevel ?? "–") }), el("span", { textContent: t("profile.avgShort") })]),
        profile.skills.length > 0 ? el("div", {}, [el("strong", { textContent: String(profile.skills.length) }), el("span", { textContent: t("profile.skillsShort") })]) : null,
        nodes > 0 ? el("div", {}, [el("strong", { textContent: String(nodes) }), el("span", { textContent: t("profile.nodesShort") })]) : null,
      ].filter((x) => x != null)
    : [];
  return el("div", { className: "pf-strip" }, [
    profile?.className ? el("div", { className: "pf-badge" }, [classIcon(profile.className)]) : null,
    el("div", {}, [el("div", { className: "pf-name", textContent: player.name }), el("div", { className: "pf-sub", textContent: sub.join(" · ") })]),
    el("div", { className: "pf-meta" }, [
      profile?.faction ? iconLabel(factionIcon(profile.faction), profile.faction) : null,
      player.guild ? el("span", { textContent: `${t("profile.guild")}: ${player.guild}` }) : null,
      player.serverName ? el("span", { textContent: player.serverName }) : null,
    ].filter((x) => x != null)),
    el("div", { className: "pf-numbers" }, numbers),
  ].filter((x) => x != null));
}

async function renderPlayerProfile(playerId) {
  setBreadcrumb([...gameCrumbs(), t("breadcrumb.playerProfile")]);
  showLoading(t("loading.playerProfile"));

  const data = await fetchJson(`/api/players/${encodeURIComponent(playerId)}`);
  setBreadcrumb([...gameCrumbs(), data.player.name]);
  // Reached through an old numeric link: show the name address instead (no reload, no history entry).
  if (data.player.slug && decodeURIComponent(location.pathname.split("/").pop()) !== data.player.slug) {
    history.replaceState(history.state, "", `${gp(`/players/${data.player.slug}`)}${location.search}${location.hash}`);
  }
  document.querySelector(".pf-tip")?.remove();

  const profile = data.profile;
  const strip = renderPlayerStrip(profile, data.player);
  const tabs = [["runs", t("profile.tabRuns"), data.history.length, () => renderRunsTab(data.history)]];
  if (profile?.gear.length > 0) {
    tabs.push(["equipment", t("profile.tabEquipment"), profile.gear.length, () => renderEquipmentTab(profile)]);
  }
  if (profile?.skills.length > 0) {
    tabs.push(["skills", t("profile.tabSkills"), profile.skills.length, () => renderSkillsTab(profile)]);
  }
  if (profile?.daevanion.length > 0) {
    tabs.push(["daevanion", t("profile.tabBoard"), profile.daevanion.reduce((s, b) => s + b.activeNodes, 0), () => renderBoardTab(profile)]);
  }
  if (profile?.species?.length > 0) {
    tabs.push(["species", t("profile.tabSpecies"), profile.species.length, () => renderSpeciesTab(profile)]);
  }

  const panel = el("div", { className: "pf-panel" });
  const buttons = tabs.map(([id, label, count]) =>
    el("button", { type: "button", role: "tab", textContent: label }, [el("small", { textContent: String(count) })]),
  );
  const show = (index) => {
    buttons.forEach((b, i) => b.setAttribute("aria-selected", String(i === index)));
    panel.replaceChildren(tabs[index][3]());
    history.replaceState(history.state, "", `${location.pathname}${location.search}#${tabs[index][0]}`);
  };
  buttons.forEach((b, i) => b.addEventListener("click", () => show(i)));
  const wanted = tabs.findIndex(([id]) => id === location.hash.slice(1));

  const source = profile
    ? el("p", { className: "profile-source", textContent: t(profile.source === "self" ? "profile.sourceSelf" : "profile.sourceSeen", { date: formatDate(parseServerTime(profile.updatedAt)) }) })
    : null;
  app.replaceChildren(
    el("div", { className: "pf-hero" }, [strip, tabs.length > 1 ? el("div", { className: "pf-tabs", role: "tablist" }, buttons) : null].filter((x) => x != null)),
    ...(data.history.length > 0 ? [compareButton(t("compare.ctaPlayers"), compareLink("/compare/players", { a: data.player.id }))] : []),
    panel,
    ...(source ? [source, el("p", { className: "profile-source", textContent: t("profile.note") })] : []),
  );
  show(wanted >= 0 ? wanted : 0);
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
    results.map((p) => el("li", {}, [link(p.serverName ? `${p.name} (${p.serverName})` : p.name, gp(`/players/${p.slug ?? p.id}`))])),
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

function runCaption(encounter) {
  return `${formatDate(new Date(encounter.startedAt))}${encounter.serverName ? ` · ${encounter.serverName}` : ""}`;
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
    const list = await fetchJson(`/api/bosses/${first.encounter.bossId}/runs?limit=60`);
    const others = list.runs.filter((r) => String(r.encounterId) !== String(a));
    const rows = others.map((r) =>
      el("a", { className: "cmp-pick", href: compareLink("/compare/runs", { a, b: r.encounterId }) }, [
        el("span", { className: "cmp-pick-main", textContent: formatDate(new Date(r.startedAt)) }),
        el("span", { className: "cmp-pick-sub", textContent: [r.serverName, t("compare.playersCount", { count: r.playerCount }), r.topPlayerName].filter((x) => x).join(" · ") }),
        el("span", { className: "cmp-pick-value", textContent: `${formatNumber(r.groupIDps)} ${t("leaderboard.idpsShort")} · ${formatDuration(r.durationSeconds)}` }),
      ]),
    );
    app.replaceChildren(
      el("h2", { textContent: t("compare.runsPickHeading", { boss: bossName }) }),
      el("p", { className: "pf-sub", textContent: `${t("compare.runA")}: ${runCaption(first.encounter)} · ${formatNumber(first.encounter.groupIDps)} ${t("leaderboard.idpsShort")}` }),
      rows.length > 0 ? el("div", { className: "cmp-pick-list" }, rows) : el("p", { className: "empty", textContent: t("compare.runsNone") }),
    );
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
      const shown = candidates.filter((c) => c.name.toLowerCase().includes(needle));
      list.replaceChildren(
        ...(shown.length > 0
          ? shown.map((c) =>
              el("a", { className: "cmp-pick", href: compareLink("/compare/players", { a, boss: bossParam, b: c.playerId }) }, [
                el("span", { className: "cmp-pick-main" }, [iconLabel(c.className ? classIcon(c.className) : null, c.name)]),
                el("span", { className: "cmp-pick-sub", textContent: [c.serverName, c.guild, t("compare.runsCount", { count: c.runs })].filter((x) => x).join(" · ") }),
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
  setBreadcrumb([...gameCrumbs(), link(bossName, gp(`/bosses/${data.boss.id}`)), t("compare.playersTitle")]);

  const head = (x) =>
    el("div", { className: "cmp-head" }, [
      el("strong", {}, [iconLabel(classIcon(x.best.className), x.player.name)]),
      el("a", { href: gp(`/players/${x.player.id}`), textContent: [x.player.serverName, x.player.guild].filter((v) => v).join(" · ") || t("compare.openProfile") }),
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
      link(`${A.player.name}: ${t("compare.bestRun")}`, gp(`/participants/${A.best.participantId}`)),
      link(`${B.player.name}: ${t("compare.bestRun")}`, gp(`/participants/${B.best.participantId}`)),
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

function isAppPath(pathname) {
  return pathname === "/" || APP_SECTIONS.includes(pathname.split("/")[1]);
}

async function route() {
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
    } else if (section === "feedback") {
      renderFeedback();
    } else if (section === "privacy") {
      await renderPrivacy();
    } else if (section === "terms") {
      await renderTerms();
    } else if (section === "worldbosses" && !param) {
      await renderWorldBosses();
    } else if (section === "worldbosses") {
      await renderBosses(param);
    } else if (section === "instances" && (!param || INSTANCE_TAB_ORDER.includes(param))) {
      await renderInstances(param, segments[2]);
    } else if (section === "instances") {
      await renderBosses(param);
    } else if (section === "bosses" && param) {
      await renderLeaderboard(param, params);
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
