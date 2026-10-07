import { findGuild } from "../guilds.js";
import { and, asc, count, desc, eq, ne } from "drizzle-orm";
import { db } from "../db/client.js";
import { bosses, encounterParticipants, encounterSkillUsage, encounters, instances, players, servers } from "../db/schema.js";
import { DEFAULT_GAME, UNASSIGNED_INSTANCE_NAME, type Game } from "../constants.js";
import { findInstance, instanceColumns } from "../routes/instances.js";
import { findBoss, mechanicsFor, modesFor, selectServer, serversWithEncounters, statsForBossIds, topByClass, topGroups } from "../routes/bosses.js";
import { buildProfileView, ownSkillRows } from "../profile.js";
import { INSTANCE_FACTS, INSTANCE_IMAGES, INSTANCE_MIN_LEVEL, BOSS_IMAGES, splitConquest } from "../../../Web-Frontend/game-data.js";
import { parseIdOrSlug } from "./slug.js";
import { formatInt, html, Raw } from "./html.js";
import { breadcrumbJsonLd, itemListJsonLd, profileJsonLd, softwareApplicationJsonLd, websiteJsonLd, type PageMeta } from "./meta.js";

/**
 * Server-rendered page: <head> metadata plus a minimal content fragment (headline, one or two
 * sentences, real links, a plain table). The browser app replaces the fragment once it has loaded
 * - see app.js's hydration remarks - so this only needs to carry what a crawler or a link preview
 * should see, never the full interactive view.
 */
export interface Page {
  status: number;
  meta: PageMeta;
  body: Raw;
}

const GAME_LABEL: Record<Game, string> = { aion: "Aion", aion2: "Aion 2" }; // "aion" only labels rows of the retired classic version
const SITE = "Aion DPS";

const CATEGORY_LABEL: Record<string, string> = {
  expedition: "Expedition",
  nightmare: "Nightmare",
  ascension: "Ascension Rite",
  transcendence: "Transcendence",
  sanctuary: "Sanctuary",
  hideout: "Hideout",
  stronghold: "Stronghold",
  awakening: "Awakening",
  worldboss: "World Bosses",
};
const CLASS_OG_FILE: Record<string, string> = { Gladiator: "gladiator", Templar: "templar", Ranger: "ranger", Assassin: "assassin", Spiritmaster: "elementalist", Sorcerer: "sorcerer", Cleric: "cleric", Chanter: "chanter", Brawler: "fighter" };

/** Preview picture of a boss (its own portrait, else its instance's photo), if the site has one. */
export function bossImage(bossName: string | null | undefined, instanceName: string | null | undefined): string | undefined {
  return (bossName ? BOSS_IMAGES[bossName] : undefined) ?? (instanceName ? INSTANCE_IMAGES[instanceName] : undefined);
}

export function classImage(className: string | null | undefined): string | undefined {
  const file = className ? CLASS_OG_FILE[className] : undefined;
  return file ? `/og/classes/${file}.png` : undefined;
}

/** How a difficulty step reads in a title: Nightmare "Level 10", Ascension "Hard", Transcendence "Stage 2". */
function modeLabelEn(category: string | null, mode: string): string {
  if (/^\d+$/.test(mode)) {
    return category === "nightmare" ? `Level ${mode}` : `Stage ${mode}`;
  }
  return mode.charAt(0).toUpperCase() + mode.slice(1);
}

function minutes(seconds: number): string {
  return `${Math.floor(seconds / 60)}:${String(Math.round(seconds % 60)).padStart(2, "0")}`;
}

/** 1,479,654 -> "1.48M" - compact numbers for the short preview texts. */
function compact(n: number): string {
  return n >= 1_000_000 ? `${(n / 1_000_000).toFixed(2)}M` : n >= 10_000 ? `${(n / 1000).toFixed(1)}k` : formatInt(n);
}

export function displayName(row: { name: string; nameEn: string | null }): string {
  const name = row.nameEn ?? row.name;
  const conquest = splitConquest(name); // "Draupnir (Conquest)" is shown as "Draupnir ★"
  return conquest ? `${conquest.base} ${conquest.stars}` : name;
}

export function homePage(): Page {
  return {
    status: 200,
    meta: {
      title: "Aion DPS Meter – Free Damage Meter & Boss Leaderboards",
      description:
        "Free open-source Aion 2 DPS/HPS meter with boss leaderboards, mechanics guides and character profiles. Passive packet capture, never reads game memory.",
      canonicalPath: "/",
      jsonLd: [websiteJsonLd(), softwareApplicationJsonLd()],
    },
    body: html`
      <h2>Aion DPS Meter</h2>
      <p>Free, open-source damage and healing meter for Aion 2, plus community boss leaderboards and character profiles.</p>
      <h3>What it does</h3>
      <p>Aion DPS Meter shows live damage per second and healing per second for every player in your party or raid while you play. It runs beside the game as its own window or as a transparent click-through overlay, and keeps a full log of each fight so you can review skill breakdowns and group composition afterwards.</p>
      <p>The meter works by passive packet capture: it only reads the network traffic your own computer already receives. It never reads game memory, never injects code and never hooks the game client.</p>
      <h3>Boss leaderboards and guides</h3>
      <p>Players can optionally upload their encounters. The site turns them into community leaderboards for every instance, boss and difficulty step, from Expeditions and Nightmare levels to Ascension Rite and Transcendence stages. Each boss page also lists its mechanics, the fastest clears, the strongest groups and the best results per class, so you can compare your own run with what others achieve.</p>
      <h3>Character profiles</h3>
      <p>Every uploading character gets a public profile with gear, skills, stigmas and recent encounters. Legions have their own pages that list their members on each server. Uploads are off by default and you decide what is shared.</p>
      <h3>Get started</h3>
      <p>Install the Windows client, start Aion 2 and the meter picks up your fights automatically. Updates are installed by the client itself, and every release is documented in the changelog.</p>
      <ul class="plain">
        ${[DEFAULT_GAME].map((g) => html`<li><a href="/instances">${GAME_LABEL[g]} – instances &amp; boss leaderboards</a></li>`)}
        <li><a href="/legions">Legions</a></li>
        <li><a href="/download">Download the Windows client</a></li>
        <li><a href="/changelog">Changelog</a></li>
      </ul>`,
  };
}

export function downloadPage(): Page {
  return {
    status: 200,
    meta: {
      title: "Download Aion DPS Meter for Windows – Free Damage Meter",
      description:
        "Free Aion 2 damage meter with live DPS/HPS per player, transparent click-through overlay and optional leaderboard upload, via passive packet capture. Windows installer, auto-updates.",
      canonicalPath: "/download",
      jsonLd: [softwareApplicationJsonLd(), breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: "Download", path: "/download" }])],
    },
    body: html`
      <h2>Download Aion DPS Meter</h2>
      <p>A damage/heal meter for Aion 2 that passively captures your own network traffic. It never reads game memory and never hooks the client. Runs beside the game as its own window or as a transparent overlay.</p>
      <p><a class="download-cta" href="/download/latest">Download the latest release</a></p>`,
  };
}

export function changelogPage(): Page {
  return {
    status: 200,
    meta: {
      title: "Changelog – Aion DPS Meter",
      description: "What changed in each release of the Aion DPS Meter Windows client, newest first.",
      canonicalPath: "/changelog",
      jsonLd: [breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: "Changelog", path: "/changelog" }])],
    },
    body: html`
      <h2>Changelog</h2>
      <p>What changed in each release of the Aion DPS Meter Windows client. <a href="/download">Download</a></p>`,
  };
}

export function guildsPage(): Page {
  return {
    status: 200,
    meta: {
      title: "Legions – Aion DPS Meter",
      description: "The legions of the Aion 2 players known to Aion DPS, per server, with the members we have seen.",
      canonicalPath: "/legions",
      jsonLd: [breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: "Legions", path: "/legions" }])],
    },
    body: html`
      <h2>Legions</h2>
      <p>The legions of the Aion 2 players known to Aion DPS, per server, with the members we have seen.</p>`,
  };
}

export function guildPage(slug: string): Page | null {
  const found = findGuild(slug);
  if (!found) {
    return null;
  }
  const { guild, members } = found;
  const path = `/legions/${guild.slug}`;
  return {
    status: 200,
    meta: {
      title: `${guild.name} (${guild.serverName}) – Legion members | ${SITE}`,
      description: `${members.length} known member${members.length === 1 ? "" : "s"} of the legion ${guild.name} on ${guild.serverName}: class, level and boss runs.`,
      canonicalPath: path,
      jsonLd: [breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: "Legions", path: "/legions" }, { name: guild.name, path }])],
    },
    body: html`
      <h2>${guild.name}</h2>
      <p>${guild.serverName} · ${members.length} known member${members.length === 1 ? "" : "s"}</p>
      <ul class="plain">${members.map((m) => html`<li><a href="/players/${m.slug ?? m.id}">${m.name}</a>${m.className ? html` · ${m.className}` : ""}</li>`)}</ul>`,
  };
}

export function statsPage(): Page {
  return {
    status: 200,
    meta: {
      title: "Statistics – Aion DPS Meter",
      description: "How many players, characters with full profiles, boss fights and downloads Aion DPS has - live numbers from the community leaderboards.",
      canonicalPath: "/stats",
      jsonLd: [breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: "Statistics", path: "/stats" }])],
    },
    body: html`
      <h2>Statistics</h2>
      <p>Live numbers of the Aion DPS community leaderboards: players, full profiles, boss fights and downloads.</p>`,
  };
}

/** The operator page behind the secret address: an empty app shell, never indexed, never cached. */
export function privateStatsPage(path: string): Page {
  return {
    status: 200,
    meta: { title: "Statistics", description: "Statistics", canonicalPath: path, noindex: true },
    body: html`<h2>Statistics</h2>`,
  };
}

// Minimal English fallback fragment, like every other SSR page here - app.js's renderPrivacy/
// renderTerms replace this with the full, localized version (Web-Frontend/i18n.js "legal.*" keys)
// once the client hydrates.
export function privacyPage(): Page {
  return {
    status: 200,
    meta: {
      title: "Privacy Policy – Aion DPS Meter",
      description:
        "What Aion DPS Meter's client and website collect, and why: passive network capture, optional uploads, hashed IPs, no accounts, no tracking.",
      canonicalPath: "/privacy",
      jsonLd: [breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: "Privacy Policy", path: "/privacy" }])],
    },
    body: html`
      <h2>Privacy Policy</h2>
      <p>Aion DPS Meter is a free, open-source, hobby-run community project. The client passively observes your own network traffic to compute stats locally; uploading a parse to the community leaderboards is optional, and your own character profile is only uploaded automatically after login if you switch that on in the settings (off by default). See the full policy on the site for details on what gets stored and your rights.</p>`,
  };
}

export function feedbackPage(): Page {
  return {
    status: 200,
    meta: {
      title: "Report a problem or suggest an improvement – Aion DPS Meter",
      description: "Report a bug, ask for a feature or suggest an improvement for Aion DPS Meter; your message becomes a GitHub issue.",
      canonicalPath: "/feedback",
      jsonLd: [breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: "Feedback", path: "/feedback" }])],
    },
    body: html`
      <h2>Problem / Improvement</h2>
      <p>Report a bug, ask for a feature or suggest an improvement. Your message becomes a public issue on GitHub; your e-mail address is never published.</p>`,
  };
}

export function termsPage(): Page {
  return {
    status: 200,
    meta: {
      title: "Terms of Service – Aion DPS Meter",
      description: "Terms for using Aion DPS Meter's free client and website: fan-made project, provided as-is, acceptable use of the upload feature.",
      canonicalPath: "/terms",
      jsonLd: [breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: "Terms of Service", path: "/terms" }])],
    },
    body: html`
      <h2>Terms of Service</h2>
      <p>Aion DPS Meter is a free, fan-made community project, not affiliated with or endorsed by NCSoft or any official Aion publisher. It is provided "as is", without warranty. See the full terms on the site for acceptable use and liability details.</p>`,
  };
}

export function instancesPage(game: Game): Page {
  const rows = db
    .select(instanceColumns)
    .from(instances)
    .where(and(eq(instances.game, game), ne(instances.name, UNASSIGNED_INSTANCE_NAME), eq(instances.hidden, false)))
    .orderBy(asc(instances.sortOrder), asc(instances.name))
    .all()
    .filter((r) => r.category !== "worldboss"); // world bosses have their own area (/worldbosses)
  const items = rows.map((r) => ({ name: displayName(r), path: `/instances/${r.slug}` }));
  const label = GAME_LABEL[game];
  // "Expedition (6 dungeons, normal & hard), Nightmare (7 bosses), ..." - what the page really offers.
  const byCategory = new Map<string, number>();
  for (const r of rows) {
    if (r.variant !== "conquest") {
      byCategory.set(r.category ?? "other", (byCategory.get(r.category ?? "other") ?? 0) + 1);
    }
  }
  const parts = [...byCategory].map(([c, n]) => `${CATEGORY_LABEL[c] ?? "Other"} (${n}${c === "expedition" ? ", Normal & Hard" : c === "nightmare" ? ", 10 levels each" : ""})`);
  return {
    status: 200,
    meta: {
      title: `${label} Instances, Bosses & DPS Leaderboards – ${SITE}`,
      description:
        rows.length > 0
          ? `All ${label} instances with boss DPS rankings per difficulty: ${parts.join(", ")}. Mechanics guides and top groups for every boss.`
          : `${label} dungeons and boss DPS rankings on ${SITE}.`,
      canonicalPath: `/instances`,
      jsonLd: [breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: `${label} instances`, path: `/instances` }]), itemListJsonLd(`${label} instances`, items)],
    },
    body: html`
      <h2>${label} instances</h2>
      ${rows.length === 0 ? html`<p class="empty">No instances recorded yet.</p>` : html`<ul class="plain">${items.map((i) => html`<li><a href="${i.path}">${i.name}</a></li>`)}</ul>`}`,
  };
}

export const INSTANCE_CATEGORY_SLUGS = ["expedition", "nightmare", "ascension", "transcendence"] as const;

const CATEGORY_INTRO: Record<string, string> = {
  expedition: "Expeditions in two difficulties: Exploration (normal) and Conquest (hard)",
  nightmare: "Nightmare bosses, each with ten challenge levels that are ranked separately",
  ascension: "Ascension Rite instances with four difficulties: Easy, Medium, Hard and Extreme",
  transcendence: "Transcendence dungeons with four stages",
  worldboss: "World bosses of Verteron, Altgard and the Abyss: field bosses that roam the open world, ranked like any other boss",
};

/** One category of the instance overview (/instances/nightmare, /instances/expedition/hard ...) - its own address so it can be found and shared. */
export function instanceCategoryPage(game: Game, category: string, variant: "normal" | "hard" | null): Page | null {
  if (!(INSTANCE_CATEGORY_SLUGS as readonly string[]).includes(category)) {
    return null;
  }
  const label = GAME_LABEL[game];
  const wanted = category === "expedition" ? (variant === "hard" ? "conquest" : "explore") : null;
  const rows = db
    .select(instanceColumns)
    .from(instances)
    .where(and(eq(instances.game, game), eq(instances.category, category as never), eq(instances.hidden, false)))
    .orderBy(asc(instances.sortOrder))
    .all()
    .filter((r) => wanted === null || r.variant === wanted);
  const items = rows.map((r) => ({ name: displayName(r), path: `/instances/${r.slug}` }));
  const catLabel = CATEGORY_LABEL[category];
  const modeText = wanted === "conquest" ? " – Hard Mode (Conquest)" : wanted === "explore" ? " – Normal Mode (Exploration)" : "";
  const path = `/instances/${category}${variant === "hard" ? "/hard" : ""}`;
  return {
    status: 200,
    meta: {
      title: `${label} ${catLabel}${modeText} – Bosses & DPS Leaderboards | ${SITE}`,
      description: `${CATEGORY_INTRO[category]}. ${items.length} ${items.length === 1 ? "entry" : "entries"}: ${items.slice(0, 7).map((i) => i.name).join(", ")}. Mechanics guides and community DPS rankings.`,
      canonicalPath: path,
      ogImage: rows[0] ? INSTANCE_IMAGES[rows[0].name] : undefined,
      jsonLd: [
        breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: `${label} instances`, path: "/instances" }, { name: catLabel, path }]),
        itemListJsonLd(`${label} ${catLabel}`, items),
      ],
    },
    body: html`
      <h2>${label} ${catLabel}${modeText}</h2>
      <p>${CATEGORY_INTRO[category]}.</p>
      <ul class="plain">${items.map((i) => html`<li><a href="${i.path}">${i.name}</a></li>`)}</ul>`,
  };
}

/** The world boss overview (/worldbosses): Verteron, Altgard and the Abyss. */
export function worldBossesPage(game: Game): Page {
  const rows = db
    .select(instanceColumns)
    .from(instances)
    .where(and(eq(instances.game, game), eq(instances.category, "worldboss"), eq(instances.hidden, false)))
    .orderBy(asc(instances.sortOrder))
    .all();
  const items = rows.map((r) => ({ name: displayName(r), path: `/worldbosses/${r.slug}` }));
  const label = GAME_LABEL[game];
  return {
    status: 200,
    meta: {
      title: `${label} World Bosses – DPS Leaderboards | ${SITE}`,
      description: `${CATEGORY_INTRO.worldboss}. ${items.map((i) => i.name).join(", ")}.`,
      canonicalPath: `/worldbosses`,
      ogImage: rows[0] ? INSTANCE_IMAGES[rows[0].name] : undefined,
      jsonLd: [breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: `${label} world bosses`, path: `/worldbosses` }]), itemListJsonLd(`${label} world bosses`, items)],
    },
    body: html`<h2>World Bosses</h2><p>${CATEGORY_INTRO.worldboss}.</p><ul class="plain">${items.map((i) => html`<li><a href="${i.path}">${i.name}</a></li>`)}</ul>`,
  };
}

export function instancePage(game: Game, idOrSlug: string): Page | null {
  const instance = findInstance(idOrSlug, game);
  if (!instance || instance.name === UNASSIGNED_INSTANCE_NAME) {
    return null;
  }
  const bossRows = db
    .select({ id: bosses.id, name: bosses.name, nameEn: bosses.nameEn, slug: bosses.slug })
    .from(bosses)
    .where(and(eq(bosses.instanceId, instance.id), eq(bosses.isTrashMob, false)))
    .orderBy(asc(bosses.name))
    .all();
  const name = displayName(instance);
  const label = GAME_LABEL[game];
  const items = bossRows.map((b) => ({ name: displayName(b), path: `/bosses/${b.slug}` }));
  const category = CATEGORY_LABEL[instance.category ?? ""] ?? "";
  const mode = instance.variant === "conquest" ? "Hard mode (Conquest)" : instance.variant === "explore" ? "Normal mode (Exploration)" : "";
  const facts = INSTANCE_FACTS[instance.name] ?? {};
  const level = INSTANCE_MIN_LEVEL[instance.name];
  const factText = [
    level !== undefined ? `from level ${level}` : "",
    facts.players ? `${facts.players} players` : "",
    facts.itemLevel ? `item level ${formatInt(facts.itemLevel)}` : "",
    facts.stars ? `${"★".repeat(facts.stars)} difficulty` : "",
  ].filter(Boolean);
  const photo = INSTANCE_IMAGES[instance.name];
  const kind = [category, mode].filter(Boolean).join(" · ");
  const worldBoss = instance.category === "worldboss";
  const base = worldBoss ? "/worldbosses" : "/instances";
  return {
    status: 200,
    meta: {
      title: `${name}${instance.variant === "conquest" ? "" : mode ? " (Normal)" : ""} – ${label} ${worldBoss ? "World" : category || "Instance"} Bosses & DPS Leaderboard | ${SITE}`,
      description:
        items.length > 0
          ? `${name} (${label}${kind ? `, ${kind}` : ""}${factText.length > 0 ? `; ${factText.join(", ")}` : ""}): ${items.length} boss${items.length === 1 ? "" : "es"} – ${items.slice(0, 6).map((i) => i.name).join(", ")}. Mechanics guides and top group DPS.`
          : `${name} (${label}) – boss DPS leaderboards on ${SITE}.`,
      canonicalPath: `${base}/${instance.slug}`,
      ogImage: photo,
      ogImageAlt: photo ? `${name} – ${label}` : undefined,
      jsonLd: [
        breadcrumbJsonLd([
          { name: SITE, path: "/" },
          { name: worldBoss ? `${label} world bosses` : `${label} instances`, path: worldBoss ? `/worldbosses` : `/instances` },
          { name, path: `${base}/${instance.slug}` },
        ]),
        itemListJsonLd(`${name} bosses`, items),
      ],
    },
    body: html`
      <h2>${name}</h2>
      <p>${label} ${kind || "instance"}${factText.length > 0 ? ` · ${factText.join(" · ")}` : ""} – bosses with community DPS leaderboards:</p>
      ${items.length === 0 ? html`<p class="empty">No boss fights uploaded for this instance yet.</p>` : html`<ul class="plain">${items.map((i) => html`<li><a href="${i.path}">${i.name}</a></li>`)}</ul>`}`,
  };
}

export function bossPage(game: Game, idOrSlug: string, query: { server?: string; mode?: string }): Page | null {
  const found = findBoss(idOrSlug, game);
  if (!found || found.game !== game) {
    return null;
  }
  const boss = found.boss;
  const name = displayName(boss);
  const instanceName = found.instanceNameEn ?? found.instanceName;
  const label = GAME_LABEL[game];
  const serverList = serversWithEncounters(boss.id);
  // Aion 2: one ranking across its official servers unless a server is asked for (see bosses.ts).
  const combined = game === "aion2" && !query.server;
  const selected = combined ? null : selectServer(serverList, query);
  const server = selected !== null && selected !== "invalid" ? serverList.find((s) => s.id === selected.id) ?? null : null;
  const scope: number | null | undefined = combined ? (serverList.length > 0 ? null : undefined) : server?.id;
  const scopeLabel = combined ? "all servers" : server?.name ?? "";
  const tag = (p: { playerName: string; serverName: string | null }) => (combined && p.serverName ? `${p.playerName} [${p.serverName}]` : p.playerName);
  const basePath = `/bosses/${boss.slug}`;
  // Difficulty steps (Nightmare level ...): each is ranked on its own and has its own address.
  const modes = modesFor(boss.id, found.instanceCategory);
  const requestedMode = modes.find((m) => m.mode === query.mode)?.mode;
  const mode = modes.length === 0 ? null : requestedMode ?? [...modes].sort((a, b) => b.runs - a.runs)[0].mode;
  const modeText = mode !== null ? modeLabelEn(found.instanceCategory, mode) : "";
  const canonicalParams = [
    query.server && server?.slug === query.server ? `server=${server.slug}` : "",
    requestedMode ? `mode=${requestedMode}` : "",
  ].filter(Boolean);
  const canonicalPath = canonicalParams.length > 0 ? `${basePath}?${canonicalParams.join("&")}` : basePath;

  let table: Raw;
  let summary: string;
  if (scope === undefined) {
    table = html`<p class="empty">No fights uploaded for this boss yet.</p>`;
    summary = `${name} (${instanceName}${modeText ? `, ${modeText}` : ""}, ${label}) – community DPS leaderboard. No fights uploaded yet.`;
  } else if (boss.isSolo) {
    const byClass = topByClass(boss.id, scope, game, mode);
    const classes = Object.keys(byClass).sort();
    table = html`<table><thead><tr><th>Class</th><th>Player</th><th>iDPS</th><th>Damage</th></tr></thead><tbody>
      ${classes.map((c) => html`<tr><td>${c}</td><td>${tag(byClass[c][0])}</td><td>${formatInt(byClass[c][0].idps)}</td><td>${formatInt(byClass[c][0].totalDamage)}</td></tr>`)}
    </tbody></table>`;
    summary = `Best solo iDPS per class against ${name} on ${scopeLabel}: ${classes
      .slice(0, 4)
      .map((c) => `${c} ${formatInt(byClass[c][0].idps)}`)
      .join(", ")}.`;
  } else {
    const groups = topGroups(boss.id, scope, game, mode);
    const stats = statsForBossIds([boss.id], scope, mode);
    table = html`<table class="ranked-table"><thead><tr><th>#</th><th>Group</th><th>iDPS</th><th>Damage</th><th>Healing</th></tr></thead><tbody>
      ${groups.map((g, i) => html`<tr><td>${i + 1}</td><td>${g.roster.map(tag).join(", ")}</td><td>${formatInt(g.groupIDps)}</td><td>${formatInt(g.totalDamage)}</td><td>${formatInt(g.totalHealing)}</td></tr>`)}
    </tbody></table>`;
    const top = groups[0];
    summary = top
      ? `${name} (${instanceName}${modeText ? `, ${modeText}` : ""}): ${stats.runCount} community run${stats.runCount === 1 ? "" : "s"}, best group iDPS ${formatInt(top.groupIDps)} by ${top.roster.map(tag).join(", ")}${stats.avgDurationSeconds ? `, average kill time ${minutes(stats.avgDurationSeconds)}` : ""} (${scopeLabel}).`
      : `${name} (${instanceName}${modeText ? `, ${modeText}` : ""}) – community DPS leaderboard on ${scopeLabel}.`;
  }

  // Aion 2 bosses double as mechanics guides - the guide is the part worth ranking for while no
  // leaderboard data exists yet, so it leads the title and the fragment.
  const { instanceWide, mechanics } = mechanicsFor(boss.id, boss.instanceId);
  const severityLabel: Record<string, string> = { wipe: "Wipe", wipe_avoidable: "Wipe (avoidable)", mechanic: "Mechanic" };
  const mechanicsRow = (m: (typeof mechanics)[number]) =>
    html`<tr><td>${m.triggerType === "hp" && m.triggerPct !== null ? `${m.triggerPct}% HP` : "Phase"}${m.triggerLabel ? ` ${m.triggerLabel}` : ""}</td><td>${severityLabel[m.severity]}</td><td>${m.action || html`<span class="empty">Description in progress</span>`}</td></tr>`;
  const mechanicsBlock =
    mechanics.length > 0
      ? html`<h3>Boss mechanics</h3>
      ${instanceWide.length > 0 ? html`<p>Throughout the dungeon:</p><table class="mechanics-table"><tbody>${instanceWide.map(mechanicsRow)}</tbody></table>` : ""}
      <table class="mechanics-table"><thead><tr><th>Trigger</th><th>Severity</th><th>What to do</th></tr></thead><tbody>${mechanics.map(mechanicsRow)}</tbody></table>`
      : html``;
  const wipes = mechanics.filter((m) => m.severity !== "mechanic").length;
  const title =
    mechanics.length > 0
      ? `${name} Mechanics Guide & DPS Leaderboard – ${instanceName}${modeText ? ` (${modeText})` : ""} | ${SITE}`
      : `${name} DPS Leaderboard – ${instanceName}${modeText ? ` (${modeText})` : ""}${server ? ` (${server.name})` : ""} | ${SITE}`;
  const description =
    mechanics.length > 0
      ? `How to beat ${name} in ${instanceName}${modeText ? ` (${modeText})` : ""} (${label}): ${mechanics.length} mechanics, ${wipes} of them wipe-critical. ${summary}`
      : summary;
  const photo = bossImage(boss.name, found.instanceName);

  return {
    status: 200,
    meta: {
      title,
      description,
      canonicalPath,
      ogImage: photo,
      ogImageAlt: photo ? `${name} – ${instanceName}` : undefined,
      jsonLd: [
        breadcrumbJsonLd([
          { name: SITE, path: "/" },
          found.instanceCategory === "worldboss" ? { name: `${label} world bosses`, path: `/worldbosses` } : { name: `${label} instances`, path: `/instances` },
          { name: instanceName, path: `${found.instanceCategory === "worldboss" ? "/worldbosses" : "/instances"}/${found.instanceSlug}` },
          { name, path: basePath },
        ]),
      ],
    },
    body: html`
      <h2>${name}</h2>
      <p>${instanceName} · ${label}${modeText ? ` · ${modeText}` : ""}${server ? html` · Leaderboard for <strong>${server.name}</strong>` : combined && scope === null ? html` · Leaderboard across all servers` : ""}</p>
      ${modes.length > 0 ? html`<p>Difficulty: ${modes.map((m) => html`<a href="${basePath}?mode=${m.mode}">${modeLabelEn(found.instanceCategory, m.mode)}</a> `)}</p>` : ""}
      ${mechanicsBlock}
      ${mechanics.length > 0 ? html`<h3>Leaderboard</h3>` : ""}
      ${!combined && serverList.length > 1 ? html`<p>Servers: ${serverList.map((s) => html`<a href="${basePath}?server=${s.slug ?? ""}">${s.name}</a> `)}</p>` : ""}
      ${table}`,
  };
}

export function playerPage(game: Game, idOrSlug: string): Page | null {
  const key = parseIdOrSlug(idOrSlug);
  if (!key) {
    return null;
  }
  const player = db
    .select({ id: players.id, slug: players.slug, name: players.name, guild: players.guild, serverName: servers.displayName, fingerprint: servers.fingerprint })
    .from(players)
    .leftJoin(servers, eq(players.serverId, servers.id))
    .where("id" in key ? eq(players.id, key.id) : eq(players.slug, key.slug))
    .get();
  // Only Aion 2 characters are served (clients file them under "aion2:<server>").
  if (!player || !(player.fingerprint ?? "").startsWith("aion2:")) {
    return null;
  }

  const runs = db
    .select({
      bossName: bosses.name,
      bossNameEn: bosses.nameEn,
      bossSlug: bosses.slug,
      instanceName: instances.name,
      className: encounterParticipants.className,
      faction: encounterParticipants.faction,
      idps: encounterParticipants.idps,
      startedAt: encounters.startedAt,
    })
    .from(encounterParticipants)
    .innerJoin(encounters, eq(encounterParticipants.encounterId, encounters.id))
    .innerJoin(bosses, eq(encounters.bossId, bosses.id))
    .innerJoin(instances, eq(bosses.instanceId, instances.id))
    .where(and(eq(encounterParticipants.playerId, player.id), ne(encounterParticipants.className, "?")))
    .orderBy(desc(encounters.startedAt))
    .all();
  const profile = buildProfileView(player.id);
  const className = profile?.className ?? runs[0]?.className ?? null;
  const faction = profile?.faction ?? runs[0]?.faction ?? null;
  const best = runs.reduce<(typeof runs)[number] | null>((m, r) => (m === null || r.idps > m.idps ? r : m), null);
  const path = `/players/${player.slug ?? player.id}`;
  const serverShort = (player.serverName ?? "").split(" - ").pop() ?? "";
  const where = player.serverName ? ` (${player.serverName})` : "";
  const classLine = [profile?.level ? `Lv. ${profile.level}` : "", className ?? "", faction ? `(${faction})` : ""].filter(Boolean).join(" ");
  const gearText = profile?.averageItemLevel ? `average item level ${profile.averageItemLevel}` : "";
  const runText =
    runs.length > 0
      ? `${runs.length} recorded boss fight${runs.length === 1 ? "" : "s"}, best personal DPS ${formatInt(best?.idps ?? 0)}${best ? ` vs ${displayName({ name: best.bossName, nameEn: best.bossNameEn })}` : ""}`
      : "";
  const facts = [
    classLine || "Aion 2 character",
    serverShort ? `on ${serverShort}` : "",
    player.guild ? `guild ${player.guild}` : "",
    gearText,
  ].filter(Boolean);
  const description = `${player.name}: ${facts.join(", ")}${runText ? `. ${runText}` : ""}. Gear, skills, stigmas, Daevanion boards and run history on ${SITE}.`;
  const image = classImage(className);
  const recent = runs.slice(0, 10);
  return {
    status: 200,
    meta: {
      title: `${player.name}${where} – ${classLine || "Aion 2 Character"} – Boss Runs & Gear | ${SITE}`,
      description,
      canonicalPath: path,
      ogType: "profile",
      ogImage: image,
      ogImageAlt: className ? `${className} emblem` : undefined,
      twitterCard: image ? "summary" : "summary_large_image",
      jsonLd: [
        breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: player.name, path }]),
        profileJsonLd({ name: player.name, path, description, image: image ?? "/og/default.png", guild: player.guild, server: player.serverName }),
      ],
    },
    body: html`
      <h2>${player.name}</h2>
      <p>${facts.join(" · ")}</p>
      ${runs.length > 0
        ? html`<p>${runText}.</p>
            <ul class="plain">
              ${recent.map((r) => html`<li><a href="/bosses/${r.bossSlug}">${displayName({ name: r.bossName, nameEn: r.bossNameEn })}</a> – ${formatInt(r.idps)} DPS, ${r.startedAt.slice(0, 10)}</li>`)}
            </ul>`
        : html``}`,
  };
}

/** One boss fight's link preview - boss, server, difficulty, group result and the top damage dealers,
 * with the boss's picture. Deliberately noindex: it is a link-preview/crawler fragment (Discord, Slack),
 * not a page meant to rank in search. */
export function encounterPage(game: Game, id: string): Page | null {
  const encounterId = Number(id);
  if (!Number.isInteger(encounterId)) {
    return null;
  }

  const row = db
    .select({
      id: encounters.id,
      startedAt: encounters.startedAt,
      mode: encounters.mode,
      durationSeconds: encounters.durationSeconds,
      groupIDps: encounters.groupIDps,
      bossName: bosses.name,
      bossNameEn: bosses.nameEn,
      instanceName: instances.name,
      instanceNameEn: instances.nameEn,
      instanceCategory: instances.category,
      instanceGame: instances.game,
      serverName: servers.displayName,
    })
    .from(encounters)
    .innerJoin(bosses, eq(encounters.bossId, bosses.id))
    .innerJoin(instances, eq(bosses.instanceId, instances.id))
    .leftJoin(servers, eq(encounters.serverId, servers.id))
    .where(eq(encounters.id, encounterId))
    .get();
  if (!row || row.instanceGame !== game) {
    return null;
  }

  // Excludes pets/summons (Water Spirit, Coyote, ...) - Chat.log never narrates who owns someone
  // ELSE's pet, so the client uploads them as their own participant row with className "?" (a
  // real player's class is always resolved by upload time).
  const roster = db
    .select({ name: players.name, className: encounterParticipants.className, damage: encounterParticipants.totalDamage, healing: encounterParticipants.totalHealing })
    .from(encounterParticipants)
    .innerJoin(players, eq(encounterParticipants.playerId, players.id))
    .where(and(eq(encounterParticipants.encounterId, encounterId), ne(encounterParticipants.className, "?")))
    .orderBy(desc(encounterParticipants.totalDamage))
    .all();

  const name = displayName({ name: row.bossName, nameEn: row.bossNameEn });
  const instanceName = displayName({ name: row.instanceName, nameEn: row.instanceNameEn });
  const server = row.serverName;
  const modeText = row.mode ? ` · ${modeLabelEn(row.instanceCategory, row.mode)}` : "";
  const title = `${name}${row.mode ? ` (${modeLabelEn(row.instanceCategory, row.mode)})` : ""}${server ? ` – ${server}` : ""} – Boss Fight – ${SITE}`;
  const top = roster.slice(0, 3).map((p) => `${p.name} (${p.className}) ${compact(p.damage)}`);
  const stats = [
    roster.length > 0 ? `${roster.length} player${roster.length === 1 ? "" : "s"}` : "",
    row.groupIDps ? `${formatInt(row.groupIDps)} group iDPS` : "",
    row.durationSeconds ? `cleared in ${minutes(row.durationSeconds)}` : "",
  ].filter(Boolean);
  const description = `${name} (${instanceName}${modeText}) on ${server ?? "Aion 2"}: ${stats.join(", ")}.${top.length > 0 ? ` Top damage: ${top.join(", ")}.` : ""} ${row.startedAt.slice(0, 10)}`;
  const photo = bossImage(row.bossName, row.instanceName);
  // With a roster the preview is the rendered fight table (seo/ogEncounter.ts), else just the boss photo.
  const rendered = roster.length > 0 && game === DEFAULT_GAME;

  return {
    status: 200,
    meta: {
      title,
      description,
      canonicalPath: `/encounters/${encounterId}`,
      noindex: true,
      ogImage: rendered ? `/og/encounters/${encounterId}.png` : photo,
      ogImageAlt: rendered ? `${name}: damage, healing and damage taken per player` : photo ? name : undefined,
      ogImageSize: rendered ? { width: 1200, height: 630 } : undefined,
    },
    body: html`<h2>${name}</h2><p>${server ?? ""}${modeText}</p>${roster.length > 0 ? html`<ul class="plain">${roster.map((p) => html`<li>${p.name} (${p.className}) – ${formatInt(p.damage)} damage</li>`)}</ul>` : ""}`,
  };
}

/** One player's skill breakdown in one fight: who, against what, how much, with which skills. */
export function participantPage(game: Game, id: string): Page | null {
  const participantId = Number(id);
  if (!Number.isInteger(participantId)) {
    return null;
  }
  const row = db
    .select({
      encounterId: encounters.id,
      playerName: players.name,
      playerSlug: players.slug,
      serverName: servers.displayName,
      className: encounterParticipants.className,
      totalDamage: encounterParticipants.totalDamage,
      dps: encounterParticipants.dps,
      totalHealing: encounterParticipants.totalHealing,
      crit: encounterParticipants.critRatePercent,
      mode: encounters.mode,
      durationSeconds: encounters.durationSeconds,
      bossName: bosses.name,
      bossNameEn: bosses.nameEn,
      instanceName: instances.name,
      instanceNameEn: instances.nameEn,
      instanceCategory: instances.category,
      instanceGame: instances.game,
    })
    .from(encounterParticipants)
    .innerJoin(players, eq(encounterParticipants.playerId, players.id))
    .innerJoin(encounters, eq(encounterParticipants.encounterId, encounters.id))
    .innerJoin(bosses, eq(encounters.bossId, bosses.id))
    .innerJoin(instances, eq(bosses.instanceId, instances.id))
    .leftJoin(servers, eq(encounters.serverId, servers.id))
    .where(eq(encounterParticipants.id, participantId))
    .get();
  if (!row || row.instanceGame !== game) {
    return null;
  }
  const skills = db
    .select({
      skillName: encounterSkillUsage.skillName,
      hits: encounterSkillUsage.hits,
      critHits: encounterSkillUsage.critHits,
      totalDamage: encounterSkillUsage.totalDamage,
      minHit: encounterSkillUsage.minHit,
      maxHit: encounterSkillUsage.maxHit,
      isHeal: encounterSkillUsage.isHeal,
    })
    .from(encounterSkillUsage)
    .where(and(eq(encounterSkillUsage.participantId, participantId), eq(encounterSkillUsage.isHeal, false)))
    .all();
  const own = ownSkillRows(row.className, skills);
  const total = own.reduce((sum, sk) => sum + sk.totalDamage, 0);
  const topSkills = own.slice(0, 3).map((sk) => `${sk.skillName} ${total > 0 ? Math.round((sk.totalDamage / total) * 100) : 0}%`);
  const boss = displayName({ name: row.bossName, nameEn: row.bossNameEn });
  const modeText = row.mode ? ` (${modeLabelEn(row.instanceCategory, row.mode)})` : "";
  const description = `${row.playerName} (${row.className}) vs ${boss}${modeText}${row.serverName ? ` on ${row.serverName}` : ""}: ${formatInt(row.totalDamage)} damage, ${formatInt(row.dps)} DPS, ${row.crit.toFixed(1)}% crit${row.totalHealing > 0 ? `, ${formatInt(row.totalHealing)} healing` : ""}${row.durationSeconds ? `, ${minutes(row.durationSeconds)} fight` : ""}.${topSkills.length > 0 ? ` Top skills: ${topSkills.join(", ")}.` : ""}`;
  const photo = bossImage(row.bossName, row.instanceName);
  return {
    status: 200,
    meta: {
      title: `${row.playerName} vs ${boss}${modeText} – Skill Breakdown – ${SITE}`,
      description,
      canonicalPath: `/participants/${participantId}`,
      noindex: true,
      ogImage: photo,
      ogImageAlt: photo ? boss : undefined,
    },
    body: html`<h2>${row.playerName}</h2><p>${row.className} · ${boss}${modeText}</p><p>${formatInt(row.totalDamage)} damage · ${formatInt(row.dps)} DPS</p>`,
  };
}

/** The comparison pages carry the compared names in the title/description so a shared link says what it compares. */
export function comparePage(kind: "players" | "runs", query: { a?: string; b?: string; boss?: string }, path: string): Page {
  const nameOfPlayer = (raw?: string) => {
    const n = Number(raw);
    return Number.isInteger(n) ? db.select({ name: players.name }).from(players).where(eq(players.id, n)).get()?.name : undefined;
  };
  const encounterOf = (raw?: string) => {
    const n = Number(raw);
    return Number.isInteger(n)
      ? db
          .select({ boss: bosses.name, bossEn: bosses.nameEn, server: servers.displayName, idps: encounters.groupIDps })
          .from(encounters)
          .innerJoin(bosses, eq(encounters.bossId, bosses.id))
          .leftJoin(servers, eq(encounters.serverId, servers.id))
          .where(eq(encounters.id, n))
          .get()
      : undefined;
  };
  let title = `Compare – ${SITE}`;
  let description = `Compare two Aion 2 players or two boss runs side by side on ${SITE}: best and average iDPS, damage, crit rate and the skill split.`;
  if (kind === "players") {
    const a = nameOfPlayer(query.a);
    const b = nameOfPlayer(query.b);
    const bossRow = query.boss ? findBoss(query.boss, DEFAULT_GAME) : null;
    const boss = bossRow ? displayName(bossRow.boss) : "";
    if (a && b) {
      title = `${a} vs ${b}${boss ? ` – ${boss}` : ""} – Player Comparison – ${SITE}`;
      description = `${a} and ${b}${boss ? ` against ${boss}` : ""}: best and average iDPS, total damage, crit rate, healing and the skill-by-skill damage split.`;
    } else if (a) {
      title = `Compare ${a} with another player – ${SITE}`;
      description = `Pick an opponent for ${a}${boss ? ` on ${boss}` : ""} and compare iDPS, damage, crit rate and skills side by side.`;
    } else {
      title = `Player Comparison – ${SITE}`;
    }
  } else {
    const a = encounterOf(query.a);
    const b = encounterOf(query.b);
    if (a && b) {
      const boss = displayName({ name: a.boss, nameEn: a.bossEn });
      title = `${boss} – Run Comparison – ${SITE}`;
      description = `Two ${boss} runs side by side: ${formatInt(a.idps)} vs ${formatInt(b.idps)} group iDPS${a.server ? ` (${a.server})` : ""}, duration, damage and the roster of each run.`;
    } else if (a) {
      const boss = displayName({ name: a.boss, nameEn: a.bossEn });
      title = `Compare this ${boss} run with another – ${SITE}`;
      description = `Pick a second ${boss} run and compare group iDPS, duration, damage and rosters.`;
    } else {
      title = `Run Comparison – ${SITE}`;
    }
  }
  return {
    status: 200,
    meta: { title, description, canonicalPath: path, noindex: true },
    body: html`<h2>${title}</h2><p>${description}</p>`,
  };
}

/** Pages that exist only as the interactive app (server picker, search). */
export function appOnlyPage(game: Game | null, kind: "servers" | "search" | "participant" | "compare", path: string): Page {
  const label = game ? GAME_LABEL[game] : SITE;
  const titles = {
    servers: `Choose a server – ${label} | ${SITE}`,
    search: `Search Aion 2 Players – Characters, Gear & Boss Runs | ${SITE}`,
    participant: `Player fight details – ${SITE}`,
    compare: `Compare – ${SITE}`,
  };
  const descriptions = {
    servers: `${SITE} – community boss DPS leaderboards for Aion 2.`,
    search: `Find any Aion 2 character by name across all servers: class, level, item level, gear, skills, stigmas and every uploaded boss run.`,
    participant: `${SITE} – community boss DPS leaderboards for Aion 2.`,
    compare: `Compare two Aion 2 players or boss runs side by side on ${SITE}.`,
  };
  return {
    status: 200,
    meta: { title: titles[kind], description: descriptions[kind], canonicalPath: path, noindex: true },
    body: html`<p class="empty">Loading…</p>`,
  };
}

export function notFoundPage(path: string): Page {
  return {
    status: 404,
    meta: { title: `Page not found – ${SITE}`, description: "This page does not exist.", canonicalPath: path, noindex: true },
    body: html`<h2>Page not found</h2><p>There is nothing at this address. <a href="/">Back to the start page</a>.</p>`,
  };
}
