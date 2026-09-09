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

async function renderInstances() {
  setBreadcrumb([t("breadcrumb.instances")]);
  app.replaceChildren(el("p", { textContent: t("loading.instances") }));

  const instances = await fetchJson("/api/instances");
  if (instances.length === 0) {
    app.replaceChildren(el("p", { className: "empty", textContent: t("instances.emptyNoInstances") }));
    return;
  }

  const list = el(
    "ul",
    { className: "plain" },
    instances.map((i) => el("li", {}, [link(translateGameName(i.name), `#/instances/${i.id}`)])),
  );
  app.replaceChildren(el("h2", { textContent: t("instances.heading") }), list);
}

async function renderBosses(instanceId) {
  setBreadcrumb([link(t("breadcrumb.instances"), "#/"), t("breadcrumb.bosses")]);
  app.replaceChildren(el("p", { textContent: t("loading.bosses") }));

  const bosses = await fetchJson(`/api/instances/${instanceId}/bosses`);
  if (bosses.length === 0) {
    app.replaceChildren(el("p", { className: "empty", textContent: t("bosses.emptyNoBosses") }));
    return;
  }

  const list = el(
    "ul",
    { className: "plain" },
    bosses.map((b) => el("li", {}, [link(translateGameName(b.name), `#/bosses/${b.id}`)])),
  );
  app.replaceChildren(el("h2", { textContent: t("bosses.heading") }), list);
}

// Faction + class icon + name in one cell - matches myaion.eu's own combined "Player" column
// rather than the three separate ones the old accordion-based rosterTable used, which is also why
// this single helper now covers both the boss leaderboard's group rows AND an encounter's own
// roster (see rankedRow/rankedTable below), not two parallel almost-identical implementations.
function playerCell(faction, className, name, href) {
  return el(
    "span",
    { className: "icon-label" },
    [factionIcon(faction), classIcon(className), href ? link(name, href) : name].filter((x) => x != null),
  );
}

// The small icon+hit-count row myaion.eu labels "Buffs" next to a player - actually that
// player's own top-used (damage) skills, not real buffs (see topSkills.ts on the backend); kept
// under that name since that's the label the user asked for and what myaion.eu itself calls it.
function buffsCell(topSkills) {
  return el(
    "span",
    { className: "buffs-row" },
    (topSkills ?? []).map((s) => iconLabel(skillIcon(s.icon), String(s.hits))),
  );
}

// One ranked entry - either one of a boss's top 10 groups (represented by its top damage dealer)
// or one member of a single encounter's own roster; both need the same rank/player/DPS/DMG/
// Heal/Buffs shape, so both render through this one row and its table wrapper.
function rankedRow(rank, faction, className, name, href, dps, dmg, heal, topSkills) {
  return el("tr", {}, [
    el("td", { textContent: `${rank}` }),
    el("td", {}, [playerCell(faction, className, name, href)]),
    el("td", { textContent: formatNumber(dps) }),
    el("td", { textContent: formatNumber(dmg) }),
    el("td", { textContent: formatNumber(heal) }),
    el("td", {}, [buffsCell(topSkills)]),
  ]);
}

function rankedTable(rows) {
  return el("table", { className: "ranked-table" }, [
    el("thead", {}, [
      el("tr", {}, [
        el("th", { textContent: "#" }),
        el("th", { textContent: t("table.player") }),
        el("th", { textContent: t("participant.dps") }),
        el("th", { textContent: t("table.damage") }),
        el("th", { textContent: t("participant.totalHealing") }),
        el("th", { textContent: t("table.buffs") }),
      ]),
    ]),
    el("tbody", {}, rows),
  ]);
}

async function renderLeaderboard(bossId) {
  setBreadcrumb([link(t("breadcrumb.instances"), "#/"), t("breadcrumb.leaderboard")]);
  app.replaceChildren(el("p", { textContent: t("loading.leaderboard") }));

  const data = await fetchJson(`/api/bosses/${bossId}/leaderboard?serverId=${encodeURIComponent(currentServerId)}`);
  setBreadcrumb([link(t("breadcrumb.instances"), "#/"), translateGameName(data.boss.name)]);

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
              p.faction,
              className,
              p.playerName,
              `#/encounters/${p.encounterId}`,
              p.idps,
              p.totalDamage,
              p.totalHealing,
              p.topSkills,
            ),
          );
          return el("div", { className: "class-block" }, [
            el("h3", {}, [iconLabel(classIcon(className), className)]),
            rankedTable(rows),
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
      g.representative?.faction,
      g.representative?.className,
      g.representative?.playerName ?? "?",
      `#/encounters/${g.encounterId}`,
      g.groupIDps,
      g.totalDamage,
      g.totalHealing,
      g.representative?.topSkills,
    ),
  );

  const groupsSection = el("section", {}, [
    el("h2", { textContent: t("leaderboard.topGroupsHeading", { n: data.topGroups.length }) }),
    rankedTable(rows),
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

// Damage each roster member dealt to the boss, as a share of the group's combined damage - same
// idea as myaion.eu's "Damage Contribution" chart, computed straight from the roster this page
// already has (totalDamage is already scoped to hits on this specific boss, see the client's
// BuildEncounterUpload), no separate endpoint needed.
function damageDistributionChart(roster) {
  const total = roster.reduce((sum, p) => sum + p.totalDamage, 0);
  const rows = [...roster]
    .sort((a, b) => b.totalDamage - a.totalDamage)
    .map((p) => {
      const pct = total > 0 ? (p.totalDamage / total) * 100 : 0;
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
    rankedRow(i + 1, p.faction, p.className, p.playerName, `#/participants/${p.participantId}`, p.idps, p.totalDamage, p.totalHealing, p.topSkills),
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
    location.hash = `#/players/${results[0].id}`;
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
