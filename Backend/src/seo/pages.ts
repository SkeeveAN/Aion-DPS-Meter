import { and, asc, eq, ne } from "drizzle-orm";
import { db } from "../db/client.js";
import { bosses, encounterParticipants, encounters, instances, players, servers } from "../db/schema.js";
import { GAMES, UNASSIGNED_INSTANCE_NAME, type Game } from "../constants.js";
import { findInstance, instanceColumns } from "../routes/instances.js";
import { findBoss, mechanicsFor, selectServer, serversWithEncounters, topByClass, topGroups } from "../routes/bosses.js";
import { formatInt, html, Raw } from "./html.js";
import { breadcrumbJsonLd, itemListJsonLd, softwareApplicationJsonLd, type PageMeta } from "./meta.js";

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

const GAME_LABEL: Record<Game, string> = { aion: "Aion", aion2: "Aion 2" };
const SITE = "Aion DPS";

export function displayName(row: { name: string; nameEn: string | null }): string {
  return row.nameEn ?? row.name;
}

export function homePage(): Page {
  return {
    status: 200,
    meta: {
      title: "Aion DPS Meter – Free Damage Meter & Boss Leaderboards",
      description:
        "Free open-source DPS/HPS meter for Aion (Chat.log-based, Origin Aion/Aion Riftshade/EuroAion) and Aion 2 (passive network packet capture) with community boss leaderboards per server. Never reads game memory, never hooks the client.",
      canonicalPath: "/",
      jsonLd: [softwareApplicationJsonLd()],
    },
    body: html`
      <h2>Aion DPS Meter</h2>
      <p>Free, open-source damage and healing meter for Aion, plus community boss leaderboards per server.</p>
      <ul class="plain">
        ${GAMES.map((g) => html`<li><a href="/${g}/instances">${GAME_LABEL[g]} – instances &amp; boss leaderboards</a></li>`)}
        <li><a href="/download">Download the Windows client</a></li>
      </ul>`,
  };
}

export function downloadPage(): Page {
  return {
    status: 200,
    meta: {
      title: "Download Aion DPS Meter for Windows – Free Damage Meter",
      description:
        "Free Aion and Aion 2 damage meter with live DPS/HPS per player, transparent click-through overlay and optional leaderboard upload. Aion via Chat.log, Aion 2 via passive packet capture. Windows installer, auto-updates.",
      canonicalPath: "/download",
      jsonLd: [softwareApplicationJsonLd(), breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: "Download", path: "/download" }])],
    },
    body: html`
      <h2>Download Aion DPS Meter</h2>
      <p>A damage/heal meter for Aion and Aion 2 - classic Aion by reading your client's own Chat.log, Aion 2 by passively capturing its network traffic. Neither reads game memory nor hooks the client. Runs beside the game as its own window or as a transparent overlay.</p>
      <p><a class="download-cta" href="https://github.com/SkeeveAN/Aion-DPS-Meter/releases">Latest release on GitHub</a></p>`,
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
      description: "What Aion DPS Meter's client and website collect, and why: local Chat.log reading, optional uploads, hashed IPs, no accounts, no tracking.",
      canonicalPath: "/privacy",
      jsonLd: [breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: "Privacy Policy", path: "/privacy" }])],
    },
    body: html`
      <h2>Privacy Policy</h2>
      <p>Aion DPS Meter is a free, open-source, hobby-run community project. The client only reads your own local Chat.log to compute stats locally; uploading a parse to the community leaderboards is optional. See the full policy on the site for details on what gets stored and your rights.</p>`,
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
    .where(and(eq(instances.game, game), ne(instances.name, UNASSIGNED_INSTANCE_NAME)))
    .orderBy(asc(instances.sortOrder), asc(instances.name))
    .all();
  const items = rows.map((r) => ({ name: displayName(r), path: `/${game}/instances/${r.slug}` }));
  const label = GAME_LABEL[game];
  return {
    status: 200,
    meta: {
      title: `${label} Instances & Boss DPS Leaderboards – ${SITE}`,
      description:
        rows.length > 0
          ? `Browse ${rows.length} ${label} dungeons with boss DPS rankings: ${items.slice(0, 6).map((i) => i.name).join(", ")}${rows.length > 6 ? ", …" : ""}.`
          : `${label} dungeons and boss DPS rankings on ${SITE}.`,
      canonicalPath: `/${game}/instances`,
      jsonLd: [breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: `${label} instances`, path: `/${game}/instances` }]), itemListJsonLd(`${label} instances`, items)],
    },
    body: html`
      <h2>${label} instances</h2>
      ${rows.length === 0 ? html`<p class="empty">No instances recorded yet.</p>` : html`<ul class="plain">${items.map((i) => html`<li><a href="${i.path}">${i.name}</a></li>`)}</ul>`}`,
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
  const items = bossRows.map((b) => ({ name: displayName(b), path: `/${game}/bosses/${b.slug}` }));
  return {
    status: 200,
    meta: {
      title: `${name} Bosses – DPS Leaderboard | ${SITE}`,
      description:
        items.length > 0
          ? `${items.length} bosses in ${name} (${label}): ${items.slice(0, 8).map((i) => i.name).join(", ")}. Top group DPS per server.`
          : `${name} (${label}) – boss DPS leaderboards on ${SITE}.`,
      canonicalPath: `/${game}/instances/${instance.slug}`,
      jsonLd: [
        breadcrumbJsonLd([
          { name: SITE, path: "/" },
          { name: `${label} instances`, path: `/${game}/instances` },
          { name, path: `/${game}/instances/${instance.slug}` },
        ]),
        itemListJsonLd(`${name} bosses`, items),
      ],
    },
    body: html`
      <h2>${name}</h2>
      <p>${label} instance – bosses with community DPS leaderboards:</p>
      ${items.length === 0 ? html`<p class="empty">No boss fights uploaded for this instance yet.</p>` : html`<ul class="plain">${items.map((i) => html`<li><a href="${i.path}">${i.name}</a></li>`)}</ul>`}`,
  };
}

export function bossPage(game: Game, idOrSlug: string, query: { server?: string }): Page | null {
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
  const basePath = `/${game}/bosses/${boss.slug}`;
  const canonicalPath = query.server && server?.slug === query.server ? `${basePath}?server=${server.slug}` : basePath;

  let table: Raw;
  let summary: string;
  if (scope === undefined) {
    table = html`<p class="empty">No fights uploaded for this boss yet.</p>`;
    summary = `${name} (${instanceName}, ${label}) – community DPS leaderboard. No fights uploaded yet.`;
  } else if (boss.isSolo) {
    const byClass = topByClass(boss.id, scope);
    const classes = Object.keys(byClass).sort();
    table = html`<table><thead><tr><th>Class</th><th>Player</th><th>iDPS</th><th>Damage</th></tr></thead><tbody>
      ${classes.map((c) => html`<tr><td>${c}</td><td>${tag(byClass[c][0])}</td><td>${formatInt(byClass[c][0].idps)}</td><td>${formatInt(byClass[c][0].totalDamage)}</td></tr>`)}
    </tbody></table>`;
    summary = `Best solo iDPS per class against ${name} on ${scopeLabel}: ${classes
      .slice(0, 4)
      .map((c) => `${c} ${formatInt(byClass[c][0].idps)}`)
      .join(", ")}.`;
  } else {
    const groups = topGroups(boss.id, scope);
    table = html`<table class="ranked-table"><thead><tr><th>#</th><th>Group</th><th>iDPS</th><th>Damage</th><th>Healing</th></tr></thead><tbody>
      ${groups.map((g, i) => html`<tr><td>${i + 1}</td><td>${g.roster.map(tag).join(", ")}</td><td>${formatInt(g.groupIDps)}</td><td>${formatInt(g.totalDamage)}</td><td>${formatInt(g.totalHealing)}</td></tr>`)}
    </tbody></table>`;
    const top = groups[0];
    summary = top
      ? `Top ${groups.length} groups vs ${name} on ${scopeLabel}: best ${formatInt(top.groupIDps)} iDPS by ${top.roster.map(tag).join(", ")}.`
      : `${name} (${instanceName}) – community DPS leaderboard on ${scopeLabel}.`;
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
      ? `${name} Mechanics Guide & DPS Leaderboard – ${instanceName} | ${SITE}`
      : `${name} DPS Leaderboard – ${instanceName}${server ? ` (${server.name})` : ""} | ${SITE}`;
  const description =
    mechanics.length > 0
      ? `How to beat ${name} in ${instanceName} (${label}): ${mechanics.length} mechanics, ${wipes} of them wipe-critical, plus the community DPS leaderboard.`
      : summary;

  return {
    status: 200,
    meta: {
      title,
      description,
      canonicalPath,
      jsonLd: [
        breadcrumbJsonLd([
          { name: SITE, path: "/" },
          { name: `${label} instances`, path: `/${game}/instances` },
          { name: instanceName, path: `/${game}/instances/${found.instanceSlug}` },
          { name, path: basePath },
        ]),
      ],
    },
    body: html`
      <h2>${name}</h2>
      <p>${instanceName} · ${label}${server ? html` · Leaderboard for <strong>${server.name}</strong>` : combined && scope === null ? html` · Leaderboard across all servers` : ""}</p>
      ${mechanicsBlock}
      ${mechanics.length > 0 ? html`<h3>Leaderboard</h3>` : ""}
      ${!combined && serverList.length > 1 ? html`<p>Servers: ${serverList.map((s) => html`<a href="${basePath}?server=${s.slug ?? ""}">${s.name}</a> `)}</p>` : ""}
      ${table}`,
  };
}

export function playerPage(game: Game, id: string): Page | null {
  const playerId = Number(id);
  if (!Number.isInteger(playerId)) {
    return null;
  }
  const player = db
    .select({ id: players.id, name: players.name, serverName: servers.displayName })
    .from(players)
    .leftJoin(servers, eq(players.serverId, servers.id))
    .where(eq(players.id, playerId))
    .get();
  if (!player) {
    return null;
  }
  return {
    status: 200,
    meta: {
      title: `${player.name}${player.serverName ? ` (${player.serverName})` : ""} – ${SITE}`,
      description: `Boss fight history of ${player.name} on ${SITE}.`,
      canonicalPath: `/${game}/players/${player.id}`,
      noindex: true,
    },
    body: html`<h2>${player.name}</h2><p>${player.serverName ?? ""}</p>`,
  };
}

/** One boss fight's link preview - per the user, a shared encounter link showed nothing but a
 * generic "Boss fight details" title/description regardless of which boss or server it actually
 * was. Boss name and server come first in the title (asked for explicitly); participant count and
 * group iDPS round out the description when there's anything to show. Deliberately still
 * noindex - this is a link-preview/crawler fragment, not a page meant to rank in search, same as
 * appOnlyPage's other kinds. */
export function encounterPage(game: Game, id: string): Page | null {
  const encounterId = Number(id);
  if (!Number.isInteger(encounterId)) {
    return null;
  }

  const row = db
    .select({
      id: encounters.id,
      durationSeconds: encounters.durationSeconds,
      groupIDps: encounters.groupIDps,
      bossName: bosses.name,
      bossNameEn: bosses.nameEn,
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

  const participantCount = db
    .select({ id: encounterParticipants.id })
    .from(encounterParticipants)
    .where(eq(encounterParticipants.encounterId, encounterId))
    .all().length;

  const name = displayName({ name: row.bossName, nameEn: row.bossNameEn });
  const server = row.serverName;
  const title = `${name}${server ? ` (${server})` : ""} – Boss Fight – ${SITE}`;
  const stats = [
    participantCount > 0 ? `${participantCount} player${participantCount === 1 ? "" : "s"}` : "",
    row.groupIDps ? `${formatInt(row.groupIDps)} group iDPS` : "",
  ].filter(Boolean);
  const description = `Boss fight against ${name}${server ? ` on ${server}` : ""}${stats.length > 0 ? `: ${stats.join(", ")}` : ""}. Aion DPS community leaderboards.`;

  return {
    status: 200,
    meta: { title, description, canonicalPath: `/${game}/encounters/${encounterId}`, noindex: true },
    body: html`<h2>${name}</h2><p>${server ?? ""}</p>`,
  };
}

/** Pages that exist only as the interactive app (server picker, search, single encounters). */
export function appOnlyPage(game: Game | null, kind: "servers" | "search" | "participant", path: string): Page {
  const label = game ? GAME_LABEL[game] : SITE;
  const titles = {
    servers: `Choose a server – ${label} | ${SITE}`,
    search: `Player search – ${SITE}`,
    participant: `Player fight details – ${SITE}`,
  };
  return {
    status: 200,
    meta: { title: titles[kind], description: `${SITE} – community boss DPS leaderboards for Aion.`, canonicalPath: path, noindex: true },
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
