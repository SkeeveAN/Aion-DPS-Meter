import { LOCALES, getLocale, setLocale, t, formatNumber, formatDate, translateGameName } from "./i18n.js";
import { INSTANCE_IMAGES, BOSS_IMAGES, INSTANCE_MIN_LEVEL } from "./game-data.js";

const app = document.getElementById("app");
const breadcrumb = document.getElementById("breadcrumb");
const serverIndicator = document.getElementById("server-indicator");
const gameTabs = document.getElementById("game-tabs");

// Real path URLs (/aion/bosses/raksha-boilheart) - one address per page, so search engines and
// Discord previews see distinct pages (the old #/... hash routes all looked like one URL to them).
// The first path segment names the game; everything a page fetches is scoped to it.
const GAMES = ["aion", "aion2"];
const DEFAULT_GAME = "aion";
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
    target = `/${DEFAULT_GAME}/instances`;
  } else if (section === "download") {
    target = "/download";
  } else if (section === "servers") {
    target = `/${DEFAULT_GAME}/servers`;
  } else if (["instances", "bosses", "players", "encounters", "participants"].includes(section) && param) {
    target = `/${DEFAULT_GAME}/${section}/${param}`;
  } else if (section === "search" && param) {
    target = `/${DEFAULT_GAME}/search?q=${param}`;
  }
  if (target) {
    location.replace(target);
  }
})();

// Which server's data is being browsed - scopes leaderboards and player search, since two servers
// can each have a player of the same name (per the user: their gear levels are nowhere near
// comparable, so their runs must never share a leaderboard either). Remembered per game so picking
// an Aion 2 server never hides the classic-Aion one. No server picked is fine now: instance lists
// show everything, a leaderboard defaults to the busiest server for that boss and offers the others
// as tabs, and player search spans every server (each hit says which one it is from).
//
// currentServerId can be null even after a server is picked - a server_catalog entry nobody has
// ever uploaded from yet has no real `servers` row (see servers.ts's own remarks), so there's no
// numeric id to store. currentServerCatalogId is the OTHER id (server_catalog.id) - always present
// once picked, and what GET /api/instances filters on (which instances even exist differs by server).
let currentServerId = null;
let currentServerName = null;
let currentServerCatalogId = null;
let currentServerPicked = false;

(function migrateLegacyServerKeys() {
  for (const key of ["serverId", "serverName", "serverCatalogId", "serverPicked"]) {
    const value = localStorage.getItem(`dpsmeter.${key}`);
    if (value !== null) {
      localStorage.setItem(`dpsmeter.aion.${key}`, value);
      localStorage.removeItem(`dpsmeter.${key}`);
    }
  }
})();

function storageKey(name) {
  return `dpsmeter.${currentGame}.${name}`;
}

function loadServerState() {
  currentServerId = localStorage.getItem(storageKey("serverId"));
  currentServerName = localStorage.getItem(storageKey("serverName"));
  currentServerCatalogId = localStorage.getItem(storageKey("serverCatalogId"));
  currentServerPicked = localStorage.getItem(storageKey("serverPicked")) === "1" && currentServerCatalogId !== null;
}

function setCurrentServer(id, name, serverCatalogId) {
  currentServerId = id === null ? null : String(id);
  currentServerName = name;
  currentServerCatalogId = serverCatalogId === null ? null : String(serverCatalogId);
  currentServerPicked = true;
  if (currentServerId === null) {
    localStorage.removeItem(storageKey("serverId"));
  } else {
    localStorage.setItem(storageKey("serverId"), currentServerId);
  }
  if (currentServerCatalogId === null) {
    localStorage.removeItem(storageKey("serverCatalogId"));
  } else {
    localStorage.setItem(storageKey("serverCatalogId"), currentServerCatalogId);
  }
  localStorage.setItem(storageKey("serverName"), name ?? "");
  localStorage.setItem(storageKey("serverPicked"), "1");
  updateServerIndicator();
}

function gp(path) {
  return `/${currentGame}${path}`;
}

function gameLabel(game) {
  return t(`game.${game}`);
}

function updateServerIndicator() {
  serverIndicator.replaceChildren();
  // Aion 2 has official, same-standard servers whose groups span them: no server to pick, the
  // rankings are combined and each player carries their server as a tag instead (see playerCell).
  if (currentGame === "aion2") {
    return;
  }
  if (currentServerPicked) {
    serverIndicator.append(
      currentServerName || t("serverIndicator.number", { id: currentServerId }),
      link(t("serverIndicator.switch"), gp("/servers")),
    );
  } else {
    serverIndicator.append(link(t("serverIndicator.choose"), gp("/servers")));
  }
}

function updateGameTabs() {
  gameTabs.replaceChildren(
    ...GAMES.map((g) => {
      const a = link(gameLabel(g), `/${g}/instances`);
      if (g === currentGame) {
        a.className = "active";
      }
      return a;
    }),
  );
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

function link(text, href) {
  return el("a", { href, textContent: text });
}

// Icons are best-effort - a class/faction/skill name with no matching file (an unmapped class,
// an unknown faction, a skill the collected dataset doesn't cover) just renders without one,
// mirroring the desktop client's own Convert() returning null for a missing asset rather than
// erroring.
function icon(src, className) {
  const img = el("img", { src, alt: "", className });
  img.addEventListener("error", () => img.remove(), { once: true });
  return img;
}

// Aion 2's nine classes have no icon files yet - a short text badge stands in until we have our
// own artwork (mirrors src/data/aion2/classes.json).
const AION2_CLASS_ABBREVIATIONS = {
  Assassin: "ASN",
  Chanter: "CHA",
  Cleric: "CLR",
  Elementalist: "ELE",
  Brawler: "BRW",
  Gladiator: "GLA",
  Ranger: "RNG",
  Sorcerer: "SOR",
  Templar: "TPL",
};

function classIcon(className) {
  if (currentGame === "aion2") {
    return el("span", { className: "class-badge", title: className, textContent: AION2_CLASS_ABBREVIATIONS[className] ?? className.slice(0, 3).toUpperCase() });
  }
  return icon(`/icons/classes/${encodeURIComponent(className)}.png`, "class-icon");
}

function factionIcon(faction) {
  return faction ? icon(`/icons/races/${encodeURIComponent(faction)}.png`, "faction-icon") : null;
}

function skillIcon(iconFile) {
  return iconFile ? icon(`/icons/skills/${encodeURIComponent(iconFile)}`, "skill-icon") : null;
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
    route();
  });
}

function applyStaticTranslations() {
  document.getElementById("search-input").placeholder = t("nav.searchPlaceholder");
  document.getElementById("search-button").textContent = t("nav.searchButton");
}

const GITHUB_REPO = "SkeeveAN/Aion-DPS-Meter";

function renderHome() {
  setBreadcrumb([]);
  document.title = HOME_TITLE;
  const hero = el("div", { className: "download-hero" }, [
    el("img", { src: "/logo.png", alt: "" }),
    el("div", {}, [el("h2", { textContent: SITE_TITLE }), el("p", { textContent: t("home.tagline") })]),
  ]);
  const games = el("div", { className: "feature-grid" }, [
    gameCard("aion", t("home.gameCardAion")),
    gameCard("aion2", t("home.gameCardAion2")),
  ]);
  const cta = el("p", {}, [el("a", { className: "download-cta", href: "/download", textContent: t("home.downloadCta") })]);
  app.replaceChildren(hero, el("h2", { textContent: t("home.chooseGame") }), games, cta);
}

function gameCard(game, text) {
  return el("a", { className: "feature-card game-card", href: `/${game}/instances` }, [
    el("h3", { textContent: gameLabel(game) }),
    el("p", { textContent: text }),
  ]);
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
async function renderDownload() {
  setBreadcrumb([link(t("breadcrumb.home"), "/"), t("breadcrumb.download")]);
  showLoading(t("loading.version"));

  const hero = el("div", { className: "download-hero" }, [
    el("img", { src: "/logo.png", alt: "" }),
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

  const step2Parts = t("download.step2").split("{code}");
  const stepsSection = el("div", {}, [
    el("h2", { textContent: t("download.installationHeading") }),
    el("ol", { className: "steps" }, [
      el("li", { textContent: t("download.step1") }),
      el("li", {}, [step2Parts[0], el("code", { textContent: "Chat.log" }), step2Parts[1]]),
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

async function renderServerPicker() {
  setBreadcrumb([...gameCrumbs(), t("breadcrumb.servers")]);
  showLoading(t("loading.servers"));

  const servers = await fetchJson(`/api/servers?game=${currentGame}`);
  if (servers.length === 0) {
    app.replaceChildren(el("p", { className: "empty", textContent: t("servers.emptyNoServers") }));
    return;
  }

  const list = el(
    "ul",
    { className: "plain" },
    servers.map((s) => {
      // s.id is null for a catalog server nobody has ever uploaded from yet (see servers.ts's own
      // remarks) - still clickable: instances/bosses are never server-scoped to begin with (see
      // instances.ts), so there's a real global list to browse even with no serverId at all. Only
      // a per-server leaderboard/player search has nothing to show yet, which the "no data" hint
      // still calls out.
      const a = el("a", { href: gp("/instances"), textContent: s.name });
      a.addEventListener("click", () => setCurrentServer(s.id, s.name, s.serverCatalogId));
      const children = [a];
      if (s.id === null) {
        children.push(` (${t("servers.noDataYet")})`);
      }
      return el("li", {}, children);
    }),
  );
  app.replaceChildren(
    el("h2", { textContent: t("servers.title") }),
    el("p", { className: "empty", textContent: t("servers.chooseHint") }),
    list,
  );
}

// Shared by the instance grid and the boss grid below - a "poster" tile is just a photo (optional),
// a dark scrim for legibility, and a light title overlaid on top, linking somewhere. objectPosition
// defaults to centered (right for a landscape instance photo) but a boss portrait is a tall
// character render - "top" keeps the face in frame instead of centering on the torso.
function posterCard(href, photo, title, objectPosition) {
  const children = [el("div", { className: "poster-scrim" })];
  if (photo) {
    const img = icon(photo, "poster-photo");
    if (objectPosition) {
      img.style.objectPosition = objectPosition;
    }
    children.unshift(img);
  }
  children.push(el("div", { className: "poster-title", textContent: title }));
  return el("a", { className: "poster-card", href }, children);
}

/** Display name as the UI translation table knows it, else the English name the DB carries, else the raw name. */
function displayName(row) {
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

async function renderInstances() {
  setBreadcrumb([link(t("breadcrumb.home"), "/"), t("breadcrumb.instances")]);
  showLoading(t("loading.instances"));

  // Per the user: which instances even exist differs by server (Origin/EuroAion share one list,
  // Riftshade's is wider) - filtered once a server is picked, otherwise the game's full list.
  const params = new URLSearchParams({ game: currentGame });
  if (currentServerPicked && currentServerCatalogId !== null) {
    params.set("serverCatalogId", currentServerCatalogId);
  }
  const instances = await fetchJson(`/api/instances?${params}`);
  if (instances.length === 0) {
    app.replaceChildren(el("p", { className: "empty", textContent: t("instances.emptyNoInstances") }));
    return;
  }

  // Aion 2 sorts its dungeons into kinds (expedition, transcendence, …) - one grid per kind, kept
  // in the order the API returns them (its own sortOrder) so category headings don't jump around;
  // only the instances WITHIN each grid are re-sorted, by level (see byMinLevelDescending above).
  // Classic Aion has no categories, so it stays one flat grid, itself level-sorted the same way.
  const groups = new Map();
  for (const i of instances) {
    const key = i.category ?? "other";
    if (!groups.has(key)) {
      groups.set(key, []);
    }
    groups.get(key).push(i);
  }
  const grid = (list) =>
    el(
      "div",
      { className: "poster-grid" },
      [...list]
        .sort(byMinLevelDescending)
        .map((i) => posterCard(gp(`/instances/${i.slug}`), INSTANCE_IMAGES[i.name], instanceCardLabel(i))),
    );
  const sections = [el("h2", { textContent: t("instances.heading") })];
  if (groups.size === 1 && groups.has("other")) {
    sections.push(grid(instances));
  } else {
    for (const [category, list] of groups) {
      sections.push(el("h3", { className: "category-heading", textContent: t(`category.${category}`) }), grid(list));
    }
  }
  if (currentGame === "aion2") {
    sections.push(el("p", { className: "derived-note", textContent: t("aion2.derivedNote") }));
  }
  app.replaceChildren(...sections);
}

async function renderBosses(instanceSlug) {
  setBreadcrumb([...gameCrumbs(), t("breadcrumb.bosses")]);
  showLoading(t("loading.bosses"));

  // The bosses endpoint doesn't carry the instance's own name (see Backend/src/routes/instances.ts)
  // - fetched separately (the instances list is tiny) rather than adding a field there just for
  // this. Falls back to the instance's own photo only for a boss BOSS_IMAGES has no dedicated
  // portrait for (see that const's own remarks) - better than no image at all, but a real per-boss
  // photo always wins when one exists.
  const [bosses, instances] = await Promise.all([
    fetchJson(`/api/instances/${encodeURIComponent(instanceSlug)}/bosses?game=${currentGame}`),
    fetchJson(`/api/instances?game=${currentGame}`),
  ]);
  const instance = instances.find((i) => i.slug === instanceSlug || String(i.id) === String(instanceSlug));
  if (instance) {
    setBreadcrumb([...gameCrumbs(), displayName(instance)]);
  }
  if (bosses.length === 0) {
    app.replaceChildren(el("p", { className: "empty", textContent: t("bosses.emptyNoBosses") }));
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
    sortedBosses.map((b) => posterCard(gp(`/bosses/${b.slug}`), BOSS_IMAGES[b.name] ?? instancePhoto, displayName(b), "top")),
  );
  app.replaceChildren(el("h2", { textContent: t("bosses.heading") }), grid);
}

// Faction + class icon + name in one cell - matches myaion.eu's own combined "Player" column
// rather than the three separate ones the old accordion-based rosterTable used. Used for a single
// person (an encounter's own roster, or one solo attempt) - see groupPlayersCell below for the
// boss leaderboard's group rows, which need to show every member, not just one.
function playerCell(faction, className, name, href, serverName) {
  // Aion 2 rankings mix servers (per the user: cross-server runs exist there), so the server rides
  // along as a tag; classic Aion's pages are always scoped to one server and need none.
  const tag = currentGame === "aion2" && serverName ? el("span", { className: "server-tag", textContent: serverName, title: serverName }) : null;
  return el(
    "span",
    { className: "icon-label" },
    [factionIcon(faction), classIcon(className), href ? link(name, href) : name, tag].filter((x) => x != null),
  );
}

// Per the user: a top-10-groups row must show every group member, not just one "face of this
// run" - each name still links to the same encounter (there's no single-player page a group row
// could point at instead).
function groupPlayersCell(roster, encounterId) {
  return el(
    "span",
    { className: "group-players" },
    (roster ?? []).map((p) => playerCell(p.faction, p.className, p.playerName, gp(`/encounters/${encounterId}`), p.serverName)),
  );
}

// Real reinforcements (see the client's ChatLog/BuffCastEvent and Backend/src/skills/topBuffs.ts)
// - icon + cast count per buff, ranked by how often it was cast. Per the user: this must be actual
// buffs, not a damage/heal skill breakdown (the roster table already has that).
function buffsCell(buffs) {
  return el(
    "span",
    { className: "buffs-row" },
    (buffs ?? []).map((b) => iconLabel(skillIcon(b.icon), String(b.casts))),
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
      }
      return a;
    }),
  ]);
}

async function renderLeaderboard(bossSlug, params) {
  setBreadcrumb([...gameCrumbs(), t("breadcrumb.leaderboard")]);
  showLoading(t("loading.leaderboard"));

  // Server precedence: an explicit ?server=/?serverId= in the address (a shared link or a tab
  // click), else the server this visitor picked, else the API's own default (the busiest server
  // for this boss). currentServerId is null for a catalog server with no uploads yet - omitted then.
  const query = new URLSearchParams({ game: currentGame });
  if (params.get("server")) {
    query.set("server", params.get("server"));
  } else if (params.get("serverId")) {
    query.set("serverId", params.get("serverId"));
  } else if (currentGame !== "aion2" && currentServerPicked && currentServerId !== null) {
    query.set("serverId", currentServerId);
  }
  const data = await fetchJson(`/api/bosses/${encodeURIComponent(bossSlug)}/leaderboard?${query}`);
  const bossPath = gp(`/bosses/${data.boss.slug}`);
  const bossName = displayName(data.boss);
  setBreadcrumb([
    ...gameCrumbs(),
    link(displayName({ name: data.boss.instanceName, nameEn: data.boss.instanceNameEn }), gp(`/instances/${data.boss.instanceSlug}`)),
    bossName,
  ]);

  const heading = el("h2", { textContent: bossName });
  const tabs = serverTabs(data, bossPath);
  const sections = [heading];
  if (data.boss.hasMechanics) {
    const mechanics = await fetchJson(`/api/bosses/${encodeURIComponent(bossSlug)}/mechanics?game=${currentGame}`);
    sections.push(...mechanicsSection(mechanics));
    sections.push(el("h2", { textContent: t("leaderboard.heading") }));
  }
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
              playerCell(p.faction, className, p.playerName, gp(`/encounters/${p.encounterId}`), p.serverName),
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

  const rows = data.topGroups.map((g, i) =>
    rankedRow(
      i + 1,
      groupPlayersCell(g.roster, g.encounterId),
      g.groupIDps,
      g.totalDamage,
      g.totalHealing,
      g.groupBuffs,
      false,
    ),
  );

  // Per the user: a small marker next to the group table's own heading when this boss has known
  // loot documented (bosses.lootRules - see lootTable's own remarks) - loot is never tied to one
  // specific encounter (it's deliberately not part of any upload), so this can only ever say
  // "loot is known for this BOSS", not which of the rows below actually saw it drop.
  const lootBadge =
    data.boss.lootRules && data.boss.lootRules.length > 0
      ? el("span", { className: "loot-known-badge", title: t("leaderboard.lootKnownTitle") }, ["💎"])
      : null;

  const groupsSection = el("section", {}, [
    el("h3", {}, [
      t("leaderboard.topGroupsHeading", { n: data.topGroups.length }),
      ...(lootBadge ? [" ", lootBadge] : []),
    ]),
    rankedTable(rows, false),
  ]);

  app.replaceChildren(...sections, groupsSection);
}

// Wipe-mechanics reference (see Backend/src/db/schema.ts bossMechanics): trigger badge, severity,
// what to do, optional detail. Facts (trigger, severity, ordering) come from game data; the prose
// is ours and may still be empty for a row - shown as "in progress" rather than hidden, so the
// mechanic itself is at least known to exist.
function mechanicsSection(data) {
  const row = (m) =>
    el("tr", { className: `sev-${m.severity.replace("_", "-")}` }, [
      el("td", {}, [el("span", { className: "trigger-badge", textContent: m.triggerType === "hp" && m.triggerPct != null ? t("mechanics.hp", { pct: m.triggerPct }) : t("mechanics.phase") }), m.triggerLabel ? ` ${m.triggerLabel}` : ""]),
      el("td", {}, [el("span", { className: "severity-badge", textContent: t(`mechanics.severity.${m.severity}`) })]),
      el("td", {}, [
        m.action ? m.action : el("span", { className: "empty", textContent: t("mechanics.pending") }),
        ...(m.detail ? [el("details", {}, [el("summary", { textContent: t("mechanics.detail") }), el("p", { textContent: m.detail })])] : []),
      ]),
    ]);
  const table = (rows) =>
    el("table", { className: "mechanics-table" }, [
      el("thead", {}, [el("tr", {}, [el("th", { textContent: t("mechanics.trigger") }), el("th", { textContent: t("mechanics.severity") }), el("th", { textContent: t("mechanics.action") })])]),
      el("tbody", {}, rows.map(row)),
    ]);

  const sections = [el("h2", { textContent: t("mechanics.heading") })];
  if (data.instanceWide.length > 0) {
    sections.push(el("h3", { textContent: t("mechanics.instanceWide") }), table(data.instanceWide));
  }
  if (data.mechanics.length > 0) {
    sections.push(table(data.mechanics));
  }
  sections.push(el("p", { className: "derived-note", textContent: t("aion2.derivedNote") }));
  return sections;
}

// m:ss - short enough to sit next to "Zeitpunkt"/"App Version" in a two-column meta table, unlike
// the full duration-implying-precision formatting a stopwatch library would produce.
function formatDuration(totalSeconds) {
  const rounded = Math.round(totalSeconds);
  const minutes = Math.floor(rounded / 60);
  const seconds = rounded % 60;
  return `${minutes}:${String(seconds).padStart(2, "0")}`;
}

function metaRow(label, value) {
  return el("tr", {}, [el("td", { textContent: label }), el("td", { textContent: value })]);
}

// Per the user: this chart is about the BOSS's damage, not the group's - who ate the boss's hits
// (damageTaken, an aggro/tank question), not who hit the boss (totalDamage, already shown in the
// roster table above). damageTaken is 0 for every row from a client older than the field itself
// (see uploadSchema.ts) - such an encounter just renders an all-zero chart rather than erroring.
function damageDistributionChart(roster) {
  const total = roster.reduce((sum, p) => sum + p.damageTaken, 0);
  const rows = [...roster]
    .sort((a, b) => b.damageTaken - a.damageTaken)
    .map((p) => {
      const pct = total > 0 ? (p.damageTaken / total) * 100 : 0;
      return el("div", { className: "contribution-row" }, [
        el("div", { className: "contribution-label", textContent: `${p.playerName} - ${pct.toFixed(1)}%` }),
        el("div", { className: "contribution-track" }, [
          el("div", { className: "contribution-bar", style: `width: ${pct.toFixed(1)}%` }),
        ]),
      ]);
    });
  return el("div", { className: "contribution-chart" }, rows);
}

// "bekannte Regeln" - curated reference text (see bosses.lootRules), never inferred from uploads:
// loot is deliberately never part of an upload payload at all.
function lootTable(lootRules) {
  if (!lootRules || lootRules.length === 0) {
    return el("p", { className: "empty", textContent: t("encounter.lootEmpty") });
  }

  const rows = lootRules.map((r) => el("tr", {}, [el("td", { textContent: r.item }), el("td", { textContent: r.rule })]));
  return el("table", {}, [
    el("thead", {}, [el("tr", {}, [el("th", { textContent: t("table.item") }), el("th", { textContent: t("table.rule") })])]),
    el("tbody", {}, rows),
  ]);
}

async function renderEncounter(encounterId) {
  setBreadcrumb([...gameCrumbs(), t("loading.encounter")]);
  showLoading(t("loading.encounter"));

  const data = await fetchJson(`/api/encounters/${encodeURIComponent(encounterId)}`);
  setBreadcrumb([...gameCrumbs(), link(translateGameName(data.encounter.bossName), gp(`/bosses/${data.encounter.bossId}`))]);

  const metaTable = el("table", { className: "meta-table" }, [
    el("tbody", {}, [
      metaRow(t("encounter.name"), translateGameName(data.encounter.bossName)),
      metaRow(t("encounter.timestamp"), formatDate(new Date(data.encounter.startedAt))),
      metaRow(t("encounter.duration"), formatDuration(data.encounter.durationSeconds)),
      metaRow(t("encounter.appVersion"), data.encounter.appVersion ?? t("encounter.appVersionUnknown")),
    ]),
  ]);

  const rosterRows = data.roster.map((p, i) =>
    rankedRow(
      i + 1,
      playerCell(p.faction, p.className, p.playerName, gp(`/participants/${p.participantId}`), p.serverName),
      p.idps,
      p.totalDamage,
      p.totalHealing,
      p.topBuffs,
      false,
    ),
  );

  app.replaceChildren(
    el("h2", { textContent: translateGameName(data.encounter.bossName) }),
    metaTable,
    el("h3", { textContent: t("encounter.groupMembersHeading") }),
    rankedTable(rosterRows, false),
    el("h3", { textContent: t("encounter.damageDistributionHeading") }),
    damageDistributionChart(data.roster),
    el("h3", { textContent: t("encounter.lootHeading") }),
    lootTable(data.encounter.lootRules),
  );
}

function skillTable(skills) {
  const rows = skills.map((s) =>
    el("tr", {}, [
      el("td", {}, [iconLabel(skillIcon(s.icon), s.skillName)]),
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

async function renderParticipant(participantId) {
  setBreadcrumb([...gameCrumbs(), t("loading.participant")]);
  showLoading(t("loading.participant"));

  const data = await fetchJson(`/api/participants/${encodeURIComponent(participantId)}`);
  setBreadcrumb([
    ...gameCrumbs(),
    link(translateGameName(data.encounter.bossName), gp(`/bosses/${data.encounter.bossId}`)),
    data.participant.playerName,
  ]);

  const p = data.participant;
  const statsTable = el("table", {}, [
    el("tbody", {}, [
      el("tr", {}, [el("td", { textContent: t("table.class") }), el("td", {}, [iconLabel(classIcon(p.className), p.className)])]),
      ...(p.faction ? [el("tr", {}, [el("td", { textContent: t("participant.faction") }), el("td", {}, [iconLabel(factionIcon(p.faction), p.faction)])])] : []),
      el("tr", {}, [el("td", { textContent: t("table.damage") }), el("td", { textContent: formatNumber(p.totalDamage) })]),
      el("tr", {}, [el("td", { textContent: t("participant.dps") }), el("td", { textContent: formatNumber(p.dps) })]),
      el("tr", {}, [el("td", { textContent: t("table.idps") }), el("td", { textContent: formatNumber(p.idps) })]),
      el("tr", {}, [el("td", { textContent: t("participant.totalHealing") }), el("td", { textContent: formatNumber(p.totalHealing) })]),
      el("tr", {}, [el("td", { textContent: t("participant.hps") }), el("td", { textContent: formatNumber(p.hps) })]),
      el("tr", {}, [el("td", { textContent: t("participant.critRate") }), el("td", { textContent: `${p.critRatePercent.toFixed(1)}%` })]),
    ]),
  ]);

  const sections = [el("h2", { textContent: p.playerName }), statsTable];
  if (data.damageSkills.length > 0) {
    sections.push(el("h3", { textContent: t("participant.damageSkillsHeading") }), skillTable(data.damageSkills));
  }
  if (data.healSkills.length > 0) {
    sections.push(el("h3", { textContent: t("participant.healSkillsHeading") }), skillTable(data.healSkills));
  }

  app.replaceChildren(...sections);
}

async function renderPlayerProfile(playerId) {
  setBreadcrumb([...gameCrumbs(), t("breadcrumb.playerProfile")]);
  showLoading(t("loading.playerProfile"));

  const data = await fetchJson(`/api/players/${encodeURIComponent(playerId)}`);
  setBreadcrumb([...gameCrumbs(), data.player.name]);

  if (data.history.length === 0) {
    app.replaceChildren(el("p", { className: "empty", textContent: t("player.emptyNoFights") }));
    return;
  }

  const rows = data.history.map((h) =>
    el("tr", {}, [
      el("td", { textContent: formatDate(new Date(h.startedAt)) }),
      el("td", {}, [link(translateGameName(h.bossName), gp(`/bosses/${h.bossId}`))]),
      el("td", {}, [iconLabel(classIcon(h.className), h.className)]),
      el("td", { textContent: formatNumber(h.totalDamage) }),
      el("td", { textContent: formatNumber(h.idps) }),
      el("td", { textContent: `${h.critRatePercent.toFixed(1)}%` }),
      el("td", {}, [link(t("table.details"), gp(`/participants/${h.participantId}`))]),
    ]),
  );

  app.replaceChildren(
    el("h2", { textContent: data.player.name + (data.player.serverName ? ` – ${data.player.serverName}` : "") }),
    el("table", {}, [
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
    ]),
  );
}

async function renderSearchResults(query) {
  setBreadcrumb([...gameCrumbs(), t("breadcrumb.search", { query })]);
  showLoading(t("loading.search"));

  // Scoped to the picked server when there is one; otherwise every server, with each hit labelled
  // (the API returns serverName per row exactly for that case).
  const params = new URLSearchParams({ q: query });
  if (currentServerPicked && currentServerId !== null) {
    params.set("serverId", currentServerId);
  }
  const results = await fetchJson(`/api/players/search?${params}`);
  if (results.length === 1) {
    // replaceState, not pushState: Back from the profile must not land on a search that would just
    // redirect forward again.
    navigate(gp(`/players/${results[0].id}`), { replace: true });
    return;
  }
  if (results.length === 0) {
    app.replaceChildren(el("p", { className: "empty", textContent: t("search.noResults", { query }) }));
    return;
  }

  const list = el(
    "ul",
    { className: "plain" },
    results.map((p) => el("li", {}, [link(p.serverName ? `${p.name} (${p.serverName})` : p.name, gp(`/players/${p.id}`))])),
  );
  app.replaceChildren(el("h2", { textContent: t("search.multipleResultsHeading") }), list);
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
  return pathname === "/" || pathname === "/download" || GAMES.some((g) => pathname === `/${g}` || pathname.startsWith(`/${g}/`));
}

async function route() {
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
  } else if (GAMES.includes(segments[0])) {
    currentGame = segments[0];
    section = segments[1] ?? "instances";
    param = segments[2];
  } else {
    section = "notfound";
  }

  loadServerState();
  updateServerIndicator();
  updateGameTabs();

  try {
    if (section === "home") {
      renderHome();
    } else if (section === "download") {
      await renderDownload();
    } else if (section === "servers") {
      await renderServerPicker();
    } else if (section === "instances" && !param) {
      await renderInstances();
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
applyStaticTranslations();
route();
