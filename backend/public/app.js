import { LOCALES, getLocale, setLocale, t, formatNumber, formatDate, translateGameName } from "./i18n.js";

const app = document.getElementById("app");
const breadcrumb = document.getElementById("breadcrumb");
const serverIndicator = document.getElementById("server-indicator");

// Which server's data is being browsed - required for the leaderboard endpoint and used to scope
// player search, since two different servers can have a player of the same name (per the user:
// their gear levels are nowhere near comparable, so their runs must never share a leaderboard
// either). Persisted across reloads so switching pages doesn't ask again every time; explicitly
// changeable via the indicator link in the header.
let currentServerId = localStorage.getItem("dpsmeter.serverId");
let currentServerName = localStorage.getItem("dpsmeter.serverName");

function setCurrentServer(id, name) {
  currentServerId = String(id);
  currentServerName = name;
  localStorage.setItem("dpsmeter.serverId", currentServerId);
  localStorage.setItem("dpsmeter.serverName", name ?? "");
  updateServerIndicator();
}

function updateServerIndicator() {
  serverIndicator.replaceChildren();
  if (currentServerId) {
    serverIndicator.append(
      currentServerName || t("serverIndicator.number", { id: currentServerId }),
      link(t("serverIndicator.switch"), "#/servers"),
    );
  }
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

function link(text, hash) {
  return el("a", { href: hash, textContent: text });
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

function classIcon(className) {
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

function setBreadcrumb(parts) {
  breadcrumb.replaceChildren();
  parts.forEach((part, i) => {
    if (i > 0) {
      breadcrumb.append(" › ");
    }
    breadcrumb.append(typeof part === "string" ? part : part);
  });
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

/**
 * Per the user: the DPS Meter client itself should be offered for download right here, with an
 * explanation - reachable without picking a server first (see route()), since the client works
 * the same regardless of which server it's pointed at.
 *
 * The download link/version comes from GitHub's own releases list, fetched client-side (GitHub's
 * API sends CORS headers for this, no backend proxy needed) - NOT /releases/latest, which 404s for
 * this repo because every release here is marked prerelease (same trap the client's own
 * Update/UpdateService.cs works around); releases[0] is the newest one regardless of that flag.
 */
async function renderDownload() {
  setBreadcrumb([t("breadcrumb.download")]);
  app.replaceChildren(el("p", { textContent: t("loading.version") }));

  const hero = el("div", { className: "download-hero" }, [
    el("img", { src: "/logo.ico", alt: "" }),
    el("div", {}, [
      el("h2", { textContent: "Aion DPS-Meter" }),
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

  app.replaceChildren(hero, el("p", { textContent: t("loading.version") }));

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
  setBreadcrumb([t("breadcrumb.servers")]);
  app.replaceChildren(el("p", { textContent: t("loading.servers") }));

  const servers = await fetchJson("/api/servers");
  if (servers.length === 0) {
    app.replaceChildren(el("p", { className: "empty", textContent: t("servers.emptyNoServers") }));
    return;
  }

  const list = el(
    "ul",
    { className: "plain" },
    servers.map((s) => {
      const label = s.displayName || s.fingerprint;
      const a = el("a", { href: "#/", textContent: label });
      a.addEventListener("click", () => setCurrentServer(s.id, s.displayName));
      return el("li", {}, [a]);
    }),
  );
  app.replaceChildren(
    el("h2", { textContent: t("servers.title") }),
    el("p", { className: "empty", textContent: t("servers.chooseHint") }),
    list,
  );
}

// Per the user: real client loading-screen art (see backend/public/images/instances/, sourced from
// this project's own AION client - Textures/loading/loading_<zone>.dds, decoded/cropped/re-encoded,
// not fabricated) instead of the plain text list this used to be. Keyed by the literal instance
// name, same convention as i18n.js's GAME_NAME_TRANSLATIONS - an instance with no entry here just
// renders without a photo (icon()'s own onerror-remove handles a bad path the same way), it's never
// guessed. Steel Rose's two tracked sub-instances share one image on purpose: the client itself only
// ships a single loading screen for the whole ship (see the 3 identical loading_IDShulack_rose_0N.dds
// files - checked by hash, not assumed).
const INSTANCE_IMAGES = {
  "Sauro-Kriegsdepot": "/images/instances/sauro.jpg",
  Tahmes: "/images/instances/tahmes.jpg",
  "Stahlrose: Anlegestelle": "/images/instances/steelrose.jpg",
  "Stahlrose: Kabine": "/images/instances/steelrose.jpg",
  "Stahlrose: Deck": "/images/instances/steelrose.jpg",
  // The rest are pre-staged the same way as the Sauro/Tahmes boss-name translations in i18n.js -
  // none of these instances have ever been uploaded yet, so the key (the exact German name a real
  // upload would carry, per assets/places/instances_multilang.json's own "de" field) is provisional
  // until a real row confirms it. "Ruhnadium"/"Jormungand-Marschroute" are the client's real German
  // names, not "Danuar Reliquary"/"Ophidan Bridge" translated - same per-language-name-drift pattern
  // documented throughout i18n.js.
  "Beshmundirs Tempel": "/images/instances/beshmundir.jpg",
  Ruhnadium: "/images/instances/danuar_reliquary.jpg",
  "Schutzturm der Ruhn": "/images/instances/illuminary_obelisk.jpg",
  Katalamize: "/images/instances/infinity_shard.jpg",
  Stahlmauerbastion: "/images/instances/eternal_bastion.jpg",
  "Schlachtfeld der Stahlmauerbastion": "/images/instances/iron_wall_warfront.jpg",
  "Jormungand-Marschroute": "/images/instances/ophidan_bridge.jpg",
  // The PVE variant (the one actually curated so far - a mage plus two named turrets, not the
  // War/PVP siege fight above) - reuses the same loading-screen art, checked by eye (a generic
  // icy-cavern bridge shot, nothing War/PVP-specific in it), same "one photo, several
  // sub-instances" reasoning as Steel Rose's two decks above.
  "Ophidan Bridge": "/images/instances/ophidan_bridge.jpg",
  "Rentus-Basis": "/images/instances/rentus_base.jpg",
  "Tiamats Festung": "/images/instances/tiamat_fortress.jpg",
  "Tiamats Unterschlupf": "/images/instances/tiamat_fortress.jpg",
  // These two keyed by English name instead (like "Raksha Boilheart" above) - found via
  // origincdx.com's own map list (IDLDF5Re_03 / IDLDF5_Under_02), but not present under either name
  // in this client's own client_strings_dic_place.xml, so the real German name a German-client
  // upload would actually carry is unconfirmed - fix the key once a real row shows it.
  "Void Cube": "/images/instances/void_cube.jpg",
  "Danuar Sanctuary": "/images/instances/danuar_sanctuary.jpg",
};

// Per the user: real per-boss art, not the instance's own photo reused - found on aion.fandom.com,
// which turns out to keep one dedicated character-model render per named Sauro/Tahmes boss (found
// via its own MediaWiki API, allimages with the boss's exact English title as the filename prefix -
// e.g. "Guard_Captain_Ahuradim.png" - not a guess, confirmed present before use). Keyed the same way
// as INSTANCE_IMAGES: the literal boss name a real upload carries. An entry with no image here falls
// back to the instance photo via BOSS_IMAGES[name] ?? INSTANCE_IMAGES[instanceName] below.
const BOSS_IMAGES = {
  "Wachhauptmann Rohuka": "/images/bosses/rohuka.jpg",
  "Chefkanonierin Kurmata": "/images/bosses/kurmata.jpg",
  "Dunkelverschlinger Derakanak": "/images/bosses/derakanak.jpg",
  "Stabschef Moriata": "/images/bosses/moriata.jpg",
  "Forscherin Teselik": "/images/bosses/teselik.jpg",
  "Versorgungskommandant Ranodim": "/images/bosses/ranodim.jpg",
  "Torwächter Slurt": "/images/bosses/stranir.jpg",
  "Inspektionsoffizier Obanuka": "/images/bosses/ovanuka.jpg",
  "Inspektionsoffizier Sayahum": "/images/bosses/sayahum.jpg",
  "Gardenführer Achradim": "/images/bosses/ahuradim.jpg",
  "Wartungsleiterin Notakiki": "/images/bosses/notakiki.jpg",
  "Brigade General Sheba": "/images/bosses/sheba.jpg",
  // From the user directly (a real screenshot, not the wiki - Raksha Boilheart has no page there).
  "Raksha Boilheart": "/images/bosses/raksha_boilheart.jpg",
};

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

async function renderInstances() {
  setBreadcrumb([t("breadcrumb.instances")]);
  app.replaceChildren(el("p", { textContent: t("loading.instances") }));

  const instances = await fetchJson("/api/instances");
  if (instances.length === 0) {
    app.replaceChildren(el("p", { className: "empty", textContent: t("instances.emptyNoInstances") }));
    return;
  }

  const grid = el(
    "div",
    { className: "poster-grid" },
    instances.map((i) =>
      posterCard(`#/instances/${i.id}`, INSTANCE_IMAGES[i.name], translateGameName(i.name)),
    ),
  );
  app.replaceChildren(el("h2", { textContent: t("instances.heading") }), grid);
}

async function renderBosses(instanceId) {
  setBreadcrumb([link(t("breadcrumb.instances"), "#/"), t("breadcrumb.bosses")]);
  app.replaceChildren(el("p", { textContent: t("loading.bosses") }));

  // The bosses endpoint doesn't carry the instance's own name (see backend/src/routes/instances.ts)
  // - fetched separately (the instances list is tiny) rather than adding a field there just for
  // this. Falls back to the instance's own photo only for a boss BOSS_IMAGES has no dedicated
  // portrait for (see that const's own remarks) - better than no image at all, but a real per-boss
  // photo always wins when one exists.
  const [bosses, instances] = await Promise.all([
    fetchJson(`/api/instances/${instanceId}/bosses`),
    fetchJson("/api/instances"),
  ]);
  if (bosses.length === 0) {
    app.replaceChildren(el("p", { className: "empty", textContent: t("bosses.emptyNoBosses") }));
    return;
  }

  const instance = instances.find((i) => String(i.id) === String(instanceId));
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
    sortedBosses.map((b) =>
      posterCard(`#/bosses/${b.id}`, BOSS_IMAGES[b.name] ?? instancePhoto, translateGameName(b.name), "top"),
    ),
  );
  app.replaceChildren(el("h2", { textContent: t("bosses.heading") }), grid);
}

// Faction + class icon + name in one cell - matches myaion.eu's own combined "Player" column
// rather than the three separate ones the old accordion-based rosterTable used. Used for a single
// person (an encounter's own roster, or one solo attempt) - see groupPlayersCell below for the
// boss leaderboard's group rows, which need to show every member, not just one.
function playerCell(faction, className, name, href) {
  return el(
    "span",
    { className: "icon-label" },
    [factionIcon(faction), classIcon(className), href ? link(name, href) : name].filter((x) => x != null),
  );
}

// Per the user: a top-10-groups row must show every group member, not just one "face of this
// run" - each name still links to the same encounter (there's no single-player page a group row
// could point at instead).
function groupPlayersCell(roster, encounterId) {
  return el(
    "span",
    { className: "group-players" },
    (roster ?? []).map((p) => playerCell(p.faction, p.className, p.playerName, `#/encounters/${encounterId}`)),
  );
}

// Real reinforcements (see the client's ChatLog/BuffCastEvent and backend/src/skills/topBuffs.ts)
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
// showBuffs is false on the two leaderboard listings (top 10 groups, top 10 per class) - per the
// user, that column is redundant there since clicking through to the encounter/participant details
// page already shows it; kept true (the default) for that details page's own roster table, which
// is the one place it actually belongs.
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

async function renderLeaderboard(bossId) {
  setBreadcrumb([link(t("breadcrumb.instances"), "#/"), t("breadcrumb.leaderboard")]);
  app.replaceChildren(el("p", { textContent: t("loading.leaderboard") }));

  // instances fetched alongside the leaderboard itself so the breadcrumb can link back to THIS
  // boss's own instance page (#/instances/:id), not just all the way out to the top-level instance
  // picker - per the user, there was previously no way back to the boss list of the instance you
  // came from without using the browser's own back button.
  const [data, instances] = await Promise.all([
    fetchJson(`/api/bosses/${bossId}/leaderboard?serverId=${encodeURIComponent(currentServerId)}`),
    fetchJson("/api/instances"),
  ]);
  const instance = instances.find((i) => i.id === data.boss.instanceId);
  setBreadcrumb([
    link(t("breadcrumb.instances"), "#/"),
    ...(instance ? [link(translateGameName(instance.name), `#/instances/${instance.id}`)] : []),
    translateGameName(data.boss.name),
  ]);

  // Per the user: a real group fight and a solo practice target (e.g. Training Dummy) are never
  // both at once, so the page shows exactly one of these two rankings, never both - "top 10 per
  // class" only makes sense (and only appears) for a solo boss.
  if (data.boss.isSolo) {
    const classNames = Object.keys(data.topByClass).sort();
    if (classNames.length === 0) {
      app.replaceChildren(el("p", { className: "empty", textContent: t("leaderboard.emptyNoFights") }));
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
              playerCell(p.faction, className, p.playerName, `#/encounters/${p.encounterId}`),
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

    app.replaceChildren(classSection);
    return;
  }

  if (data.topGroups.length === 0) {
    app.replaceChildren(el("p", { className: "empty", textContent: t("leaderboard.emptyNoFights") }));
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
    el("h2", {}, [
      t("leaderboard.topGroupsHeading", { n: data.topGroups.length }),
      ...(lootBadge ? [" ", lootBadge] : []),
    ]),
    rankedTable(rows, false),
  ]);

  app.replaceChildren(groupsSection);
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
  setBreadcrumb([link(t("breadcrumb.instances"), "#/"), t("loading.encounter")]);
  app.replaceChildren(el("p", { textContent: t("loading.encounter") }));

  const data = await fetchJson(`/api/encounters/${encounterId}`);
  setBreadcrumb([link(t("breadcrumb.instances"), "#/"), link(translateGameName(data.encounter.bossName), `#/bosses/${data.encounter.bossId}`)]);

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
      playerCell(p.faction, p.className, p.playerName, `#/participants/${p.participantId}`),
      p.idps,
      p.totalDamage,
      p.totalHealing,
      p.topBuffs,
    ),
  );

  app.replaceChildren(
    el("h2", { textContent: translateGameName(data.encounter.bossName) }),
    metaTable,
    el("h3", { textContent: t("encounter.groupMembersHeading") }),
    rankedTable(rosterRows),
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
  setBreadcrumb([link(t("breadcrumb.instances"), "#/"), t("loading.participant")]);
  app.replaceChildren(el("p", { textContent: t("loading.participant") }));

  const data = await fetchJson(`/api/participants/${participantId}`);
  setBreadcrumb([
    link(t("breadcrumb.instances"), "#/"),
    link(translateGameName(data.encounter.bossName), `#/bosses/${data.encounter.bossId}`),
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
  setBreadcrumb([link(t("breadcrumb.instances"), "#/"), t("breadcrumb.playerProfile")]);
  app.replaceChildren(el("p", { textContent: t("loading.playerProfile") }));

  const data = await fetchJson(`/api/players/${playerId}`);
  setBreadcrumb([link(t("breadcrumb.instances"), "#/"), data.player.name]);

  if (data.history.length === 0) {
    app.replaceChildren(el("p", { className: "empty", textContent: t("player.emptyNoFights") }));
    return;
  }

  const rows = data.history.map((h) =>
    el("tr", {}, [
      el("td", { textContent: formatDate(new Date(h.startedAt)) }),
      el("td", {}, [link(translateGameName(h.bossName), `#/bosses/${h.bossId}`)]),
      el("td", {}, [iconLabel(classIcon(h.className), h.className)]),
      el("td", { textContent: formatNumber(h.totalDamage) }),
      el("td", { textContent: formatNumber(h.idps) }),
      el("td", { textContent: `${h.critRatePercent.toFixed(1)}%` }),
      el("td", {}, [link(t("table.details"), `#/participants/${h.participantId}`)]),
    ]),
  );

  app.replaceChildren(
    el("h2", { textContent: data.player.name }),
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
  setBreadcrumb([link(t("breadcrumb.instances"), "#/"), t("breadcrumb.search", { query })]);
  app.replaceChildren(el("p", { textContent: t("loading.search") }));

  const results = await fetchJson(
    `/api/players/search?q=${encodeURIComponent(query)}&serverId=${encodeURIComponent(currentServerId)}`,
  );
  if (results.length === 1) {
    // Not location.hash = ... : that pushes a NEW history entry on top of this search - pressing
    // Back from the profile then lands back on this exact search, which (still one result) just
    // redirects forward again immediately. Feels like Back is broken, since it visibly does
    // nothing. replaceState swaps this entry in place instead, so Back skips past the search
    // straight to whatever came before it - what the user was actually navigating away from.
    history.replaceState(null, "", `#/players/${results[0].id}`);
    await renderPlayerProfile(String(results[0].id));
    return;
  }
  if (results.length === 0) {
    app.replaceChildren(el("p", { className: "empty", textContent: t("search.noResults", { query }) }));
    return;
  }

  const list = el(
    "ul",
    { className: "plain" },
    results.map((p) => el("li", {}, [link(p.name, `#/players/${p.id}`)])),
  );
  app.replaceChildren(el("h2", { textContent: t("search.multipleResultsHeading") }), list);
}

async function route() {
  const hash = location.hash.replace(/^#\/?/, "");
  const [section, param] = hash.split("/");

  updateServerIndicator();

  try {
    // Every other view either needs a server (leaderboard, search) or is meaningless to browse
    // before one is even picked (the whole point of a "which instance/boss" drill-down is to reach
    // a server-specific leaderboard) - so nothing else renders until one is chosen, once. The
    // download page is the one exception: it's server-agnostic (the client works the same
    // regardless of which server it's pointed at), so it must stay reachable even before anyone
    // has picked one.
    if (section !== "servers" && section !== "download" && !currentServerId) {
      await renderServerPicker();
    } else if (section === "servers") {
      await renderServerPicker();
    } else if (section === "download") {
      await renderDownload();
    } else if (!section) {
      await renderInstances();
    } else if (section === "instances" && param) {
      await renderBosses(param);
    } else if (section === "bosses" && param) {
      await renderLeaderboard(param);
    } else if (section === "encounters" && param) {
      await renderEncounter(param);
    } else if (section === "participants" && param) {
      await renderParticipant(param);
    } else if (section === "players" && param) {
      await renderPlayerProfile(param);
    } else if (section === "search" && param) {
      await renderSearchResults(decodeURIComponent(param));
    } else {
      await renderInstances();
    }
  } catch (err) {
    app.replaceChildren(el("p", { className: "error", textContent: t("general.error", { msg: err.message }) }));
  }
}

document.getElementById("search-form").addEventListener("submit", (e) => {
  e.preventDefault();
  const query = document.getElementById("search-input").value.trim();
  if (query) {
    location.hash = `#/search/${encodeURIComponent(query)}`;
  }
});

window.addEventListener("hashchange", route);
setupLanguageSwitcher();
applyStaticTranslations();
route();
