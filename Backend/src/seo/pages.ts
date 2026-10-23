import { findGuild } from "../guilds.js";
import { and, asc, count, desc, eq, ne } from "drizzle-orm";
import { db } from "../db/client.js";
import { ESTIMATED_NOTE } from "../estimated.js";
import { bosses, encounterParticipants, encounterSkillUsage, encounters, instances, players, servers } from "../db/schema.js";
import { DEFAULT_GAME, UNASSIGNED_INSTANCE_NAME, type Game } from "../constants.js";
import { findInstance, instanceColumns } from "../routes/instances.js";
import { classBoxes, findBoss, mechanicsFor, modesFor, selectServer, UNKNOWN_MODE, serversWithEncounters, statsForBossIds, topByClass, topGroups } from "../routes/bosses.js";
import { instanceStatistics } from "../instanceStats.js";
import { buildProfileView, ownSkillRows } from "../profile.js";
import { INSTANCE_FACTS, INSTANCE_IMAGES, INSTANCE_MIN_LEVEL, BOSS_IMAGES, splitConquest } from "../../../Web-Frontend/game-data.js";
import { ARTICLE, articleSegments } from "../../../Web-Frontend/article-data.js";
import { GUIDE_ASOF, GUIDE_FAQ, GUIDE_OSS, GUIDE_ROWS, GUIDE_STRONG, GUIDE_TABLES, GUIDE_WEAK, GUIDE_WHAT } from "../../../Web-Frontend/guide-data.js";
import enStrings from "../../../Web-Frontend/locales/en.js";
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
  if (mode === "") {
    return "Unknown stage";
  }
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
  const conquest = splitConquest(name); // "Draupnir (Conquest)" is shown as "Draupnir (Conquest 1)", "(Conquest, Hard)" as "(Conquest 1, Hard)": the stage in words, as on the website
  return conquest ? `${conquest.base} (Conquest ${conquest.stars.length}${conquest.hard ? ", Hard" : ""})` : name;
}

/** Each main page has its own preview picture (Web-Frontend/og/pages/<slug>.jpg, 1200x630) with the text in the middle,
 *  because WhatsApp cuts a square out of the middle of the picture. */
function pageCard(slug: string): Pick<PageMeta, "ogImage" | "ogImageSize" | "ogImageAlt"> {
  return { ogImage: `/og/pages/${slug}.jpg?v=20261019`, ogImageSize: { width: 1200, height: 630 }, ogImageAlt: "Aion DPS Meter" };
}

export function homePage(): Page {
  return {
    status: 200,
    meta: {
      title: "Aion DPS Meter – Free Damage Meter & Boss Leaderboards",
      description:
        "Free open-source Aion 2 DPS meter: click-through overlay, boss leaderboards, character profiles, pet map, event timetable and world boss timers. No memory reading.",
      canonicalPath: "/",
      ...pageCard("home"),
      jsonLd: [websiteJsonLd(), softwareApplicationJsonLd()],
    },
    body: html`
      <h1>Aion 2 DPS Meter</h1>
      <p>Free, open-source damage and healing meter for Aion 2, plus community boss leaderboards and character profiles.</p>
      <h3>What it does</h3>
      <p>Aion DPS Meter shows live damage per second and healing per second for every player in your party or raid while you play. It runs beside the game as its own window or as a transparent click-through overlay, and keeps a full log of each fight so you can review skill breakdowns and group composition afterwards.</p>
      <p>The meter works by passive packet capture: it only reads the network traffic your own computer already receives. It never reads game memory, never injects code and never hooks the game client.</p>
      <h3>More than a damage meter</h3>
      <p>Besides DPS and HPS the client brings an interactive map with pet spawns and gathering spots, an event timetable and world boss timers with sound reminders, a network overlay with ping and packet loss, your character with gear and Daevanion boards, and shortcuts to the official AION 2 announcements and Twitch Drops. <a href="/features">See all features</a>.</p>
      <h3>Boss leaderboards and guides</h3>
      <p>Players can optionally upload their encounters. The site turns them into community leaderboards for every instance, boss and difficulty step, from Expeditions and Nightmare levels to Ascension Rite and Transcendence stages. Each boss page also lists its mechanics, the fastest clears, the strongest groups, the best results per class and the skills each class uses, which you can narrow by gear score. A single run shows the buffs on every player and, from client 1.9.0 on, a group timeline of every skill use and which effects ran at each one, so you can compare your own run with what others achieve.</p>
      <h3>Character profiles</h3>
      <p>Every uploading character gets a public profile with gear, skills, stigmas and recent encounters. Legions have their own pages that list their members on each server. Uploads are off by default and you decide what is shared.</p>
      <h3>Get started</h3>
      <p>Install the Windows client, start Aion 2 and the meter picks up your fights automatically. It works with the global release of AION 2; Korea and Taiwan run a few versions ahead, so skill recognition may be incomplete there. Updates are installed by the client itself, and every release is documented in the changelog.</p>
      <ul class="plain">
        ${[DEFAULT_GAME].map((g) => html`<li><a href="/instances">${GAME_LABEL[g]} – instances &amp; boss leaderboards</a></li>`)}
        <li><a href="/features">Features of the Aion 2 DPS meter</a></li>
        <li><a href="/aion-2-dps-meter">Aion 2 DPS meter: features, installation and comparison</a></li>
        <li><a href="/legions">Legions</a></li>
        <li><a href="/status">AION 2 server status</a></li>
        <li><a href="/announcements">AION 2 announcements</a> and <a href="/patchnotes">patch notes</a></li>
        <li><a href="/twitch-drops">AION 2 Twitch Drops</a></li>
        <li><a href="/download">Download the Windows client</a></li>
        <li><a href="/changelog">Changelog</a></li>
      </ul>`,
  };
}

export function downloadPage(): Page {
  return {
    status: 200,
    meta: {
      title: "AION 2 DPS Meter Download – Free Windows Damage Meter",
      description:
        "Free Aion 2 damage meter with live DPS/HPS per player, transparent click-through overlay and optional leaderboard upload, via passive packet capture. Windows installer, auto-updates.",
      canonicalPath: "/download",
      ...pageCard("download"),
      jsonLd: [softwareApplicationJsonLd(), breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: "Download", path: "/download" }]),
        {"@context": "https://schema.org", "@type": "FAQPage", "mainEntity": [{"@type": "Question", "name": "Is it safe to use?", "acceptedAnswer": {"@type": "Answer", "text": "The meter only reads the network traffic your own computer already receives, through the Npcap driver. It never reads game memory, never injects code and never hooks the game client."}}, {"@type": "Question", "name": "Does it cost anything?", "acceptedAnswer": {"@type": "Answer", "text": "No. Aion DPS Meter is free and open source under the MIT license."}}, {"@type": "Question", "name": "Which AION 2 regions does it support?", "acceptedAnswer": {"@type": "Answer", "text": "The global release of AION 2, every region NCSOFT launched on 5 October 2026. Korea and Taiwan run a few versions ahead, so skill recognition may be incomplete there."}}, {"@type": "Question", "name": "Do I need administrator rights?", "acceptedAnswer": {"@type": "Answer", "text": "Not for the meter: it installs per user. Only the installer of the Npcap driver asks for them."}}, {"@type": "Question", "name": "Are my fights uploaded automatically?", "acceptedAnswer": {"@type": "Answer", "text": "No. Uploads to the leaderboards are off by default and you decide in the settings whether to switch them on."}}]}],
    },
    body: html`
      <h1>AION 2 DPS Meter Download</h1>
      <p>A damage/heal meter for Aion 2 that passively captures your own network traffic. It never reads game memory and never hooks the client. Runs beside the game as its own window or as a transparent overlay.</p>
      <p><a class="download-cta" href="/download/latest">Download the latest release</a></p>
      <p>Free Windows installer, no admin rights needed. Works with the global release of AION 2 (every region NCSOFT launched on 5 October 2026); Korea and Taiwan run a few versions ahead, so skill recognition may be incomplete there.</p>
      <p>Code signing: free code signing provided by SignPath.io, certificate by the SignPath Foundation (being set up, releases are unsigned until then). See the <a href="/code-signing-policy">code signing policy</a>.</p>
      <h3>Requirements</h3>
      <ul class="plain">
        <li>Windows 10 or 11</li>
        <li>The <a href="https://npcap.com" rel="noopener">Npcap</a> driver, which the meter uses to see the game's network traffic. It is not part of the installer.</li>
      </ul>
      <h3>Installation</h3>
      <ol class="steps">
        <li>Download and run the installer (installs per user).</li>
        <li>Install the Npcap driver from npcap.com.</li>
        <li>Start the meter, then log in with your character.</li>
        <li>Optional: switch on the profile upload in the settings (off by default) to appear in the leaderboards.</li>
        <li>Done. The meter updates itself whenever a new version is released.</li>
      </ol>
      <h3>Screenshots</h3>
      <p><img src="/images/features/meter.webp" alt="Aion 2 DPS meter window showing live damage per second for every group member" width="600" loading="lazy" />
      <img src="/images/features/overlay.webp" alt="Transparent click-through in-game overlay of the Aion 2 DPS meter" width="600" loading="lazy" />
      <img src="/images/features/details.webp" alt="Aion 2 combat analysis with skill breakdown and crit rates" width="600" loading="lazy" /></p>
      <h3>FAQ</h3>
      <h4>Is it safe to use?</h4>
      <p>The meter only reads the network traffic your own computer already receives, through the Npcap driver. It never reads game memory, never injects code and never hooks the game client.</p>
      <h4>Does it cost anything?</h4>
      <p>No. Aion DPS Meter is free and open source under the MIT license.</p>
      <h4>Which AION 2 regions does it support?</h4>
      <p>The global release of AION 2, every region NCSOFT launched on 5 October 2026. Korea and Taiwan run a few versions ahead, so skill recognition may be incomplete there.</p>
      <h4>Do I need administrator rights?</h4>
      <p>Not for the meter: it installs per user. Only the installer of the Npcap driver asks for them.</p>
      <h4>Are my fights uploaded automatically?</h4>
      <p>No. Uploads to the leaderboards are off by default and you decide in the settings whether to switch them on.</p>
      <p><a href="/features">All features</a> · <a href="/changelog">Changelog</a> · <a href="https://github.com/SkeeveAN/Aion-DPS-Meter">Source code on GitHub</a></p>`,
  };
}

// Web-Frontend has no package.json, so at run time Node may hand the default export over wrapped in { default }.
const en = ((enStrings as unknown as { default?: Record<string, string> }).default ?? enStrings) as unknown as Record<string, string>;

/** The technical article; text and markup come from Web-Frontend/article-data.js, the same file the app renders. */
export function articlePage(): Page {
  const inline = (text: string) => articleSegments(text).map((s) => (s.href ? html`<a href="${s.href}">${s.text}</a>` : s.bold ? html`<strong>${s.text}</strong>` : s.text));
  const block = ([type, value]: [string, string | string[]]) => {
    if (type === "h2") return html`<h2>${value as string}</h2>`;
    if (type === "ul") return html`<ul class="plain">${(value as string[]).map((i) => html`<li>${inline(i)}</li>`)}</ul>`;
    if (type === "ol") return html`<ol class="steps">${(value as string[]).map((i) => html`<li>${inline(i)}</li>`)}</ol>`;
    return html`<p>${inline(value as string)}</p>`;
  };
  return {
    status: 200,
    meta: {
      title: `${ARTICLE.title} – ${SITE}`,
      description: ARTICLE.description,
      canonicalPath: ARTICLE.path,
      ...pageCard("how-it-works"),
      ogType: "article",
      jsonLd: [breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: "Technical background", path: ARTICLE.path }])],
    },
    body: html`<h1>${ARTICLE.title}</h1>${ARTICLE.blocks.map(block)}`,
  };
}

/** The comparison page for the searches "AION 2 DPS meter / damage meter / combat tracker". The text and tables come from
 *  the same files as the app's version (Web-Frontend/guide-data.js and the English strings), so there is one source. */
export function guidePage(): Page {
  const g = (key: string, params?: Record<string, string>) => (en[`guide.${key}`] ?? key).replace(/\{(\w+)\}/g, (m: string, n: string) => params?.[n] ?? m);
  const cell = (value: string) => (value.startsWith("$") ? g(`c.${value.slice(1)}`) : value);
  const list = (keys: string[], prefix: string) => html`<ul class="plain">${keys.map((k) => html`<li>${g(`${prefix}.${k}`)}</li>`)}</ul>`;
  const table = (spec: (typeof GUIDE_TABLES)[number]) => html`
    <h3>${g(`t.${spec.id}`)}</h3>
    <table>
      <thead><tr><th scope="col">${g("rowHead")}</th>${spec.columns.map((c) => html`<th scope="col">${c.url ? html`<a href="${c.url}" rel="noopener">${c.name}</a>` : c.name}</th>`)}</tr></thead>
      <tbody>${GUIDE_ROWS.map((row) => html`<tr><th scope="row">${g(`r.${row}`)}</th>${spec.columns.map((c) => html`<td>${cell(c.cells[row])}</td>`)}</tr>`)}</tbody>
    </table>`;
  const faq = GUIDE_FAQ.map((k) => ({ q: g(`faq.${k}.q`), a: g(`faq.${k}.a`) }));
  return {
    status: 200,
    meta: {
      title: g("title"),
      description: g("metaDesc"),
      canonicalPath: "/aion-2-dps-meter",
      // WhatsApp cuts a square out of the middle of the preview picture, so this one keeps its text there.
      ogImage: "/og/guide.png?v=20261019",
      ogImageAlt: "AION 2 DPS Meter – Features, Installation & Comparison",
      ogImageSize: { width: 1200, height: 630 },
      jsonLd: [
        softwareApplicationJsonLd(),
        breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: g("breadcrumb"), path: "/aion-2-dps-meter" }]),
        { "@context": "https://schema.org", "@type": "FAQPage", mainEntity: faq.map((f) => ({ "@type": "Question", name: f.q, acceptedAnswer: { "@type": "Answer", text: f.a } })) },
      ],
    },
    body: html`
      <h1>${g("title")}</h1>
      <p>${g("intro")}</p>
      <p><a class="download-cta" href="/download">${g("download")}</a> · <a href="/features">${g("allFeatures")}</a></p>
      <h2>${g("h.what")}</h2>
      ${list(GUIDE_WHAT, "what")}
      <h2>${g("h.install")}</h2>
      <p>${g("installShort")} <a href="/download">${g("download")}</a></p>
      <p>${g("installNote")}</p>
      <h2>${g("h.measure")}</h2>
      <p>${g("measure.p1")}</p><p>${g("measure.p2")}</p><p>${g("measure.p3")}</p>
      <h2>${g("h.compare")}</h2>
      <p>${g("compare.intro", { date: GUIDE_ASOF })}</p>
      ${GUIDE_TABLES.map(table)}
      <p>${g("lago")}</p><p>${g("rdpsWhy")}</p><p>${g("also")}</p>
      <h2>${g("h.weak")}</h2>
      ${list(GUIDE_WEAK, "weak")}
      <h2>${g("h.strong")}</h2>
      ${list(GUIDE_STRONG, "strong")}
      <h2>${g("h.oss")}</h2>
      ${list(GUIDE_OSS, "oss")}
      <h2>${g("h.faq")}</h2>
      ${faq.map((f) => html`<h4>${f.q}</h4><p>${f.a}</p>`)}
      <p><a href="/download">Download</a> · <a href="/features">Features</a> · <a href="/changelog">Changelog</a> · <a href="https://github.com/SkeeveAN/Aion-DPS-Meter">Source code on GitHub</a></p>`,
  };
}

export function featuresPage(): Page {
  return {
    status: 200,
    meta: {
      title: "Aion 2 DPS Meter Features – Overlay, Pet Map, Timetable, Boss Timers",
      description:
        "All features of the free Aion 2 DPS meter: live DPS/HPS, overlay, skill breakdown, character view, pet map, event timetable, world boss timers, network overlay.",
      canonicalPath: "/features",
      ...pageCard("features"),
      jsonLd: [softwareApplicationJsonLd(), breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: "Features", path: "/features" }])],
    },
    body: html`
      <h1>Aion 2 DPS Meter Toolkit – everything it does</h1>
      <p>A free, open-source damage and heal meter and toolkit for Aion 2. It only reads the network traffic your own computer already receives: no memory reading, no injection, no packets sent.</p>
      <p>It works with the global release of AION 2, every region NCSOFT launched on 5 October 2026. Korea and Taiwan run a few versions ahead, so skill recognition may be incomplete there.</p>
      <h2>Live damage and healing meter</h2>
      <p>Every player in your group or raid with name, total damage, DPS and a share bar; faction and class as icons. Filter by class, group or target. The Mob/Boss filter switches between overall DPS and true per-target iDPS, a PvE/PvP switch separates the two, and every wipe on a boss gets an entry of its own.</p>
      <h3>Five views</h3>
      <p>Damage, Heal, Damage taken and two All in One views, switched by hotkey or click.</p>
      <h3>Skill breakdown and DPS curve</h3>
      <p>Double-click a player to see every skill with hits, crit rate, total, min, max and average, split by target, plus a DPS curve over the whole fight. A rotation tab shows the skills on a timeline with the DPS behind them, the uptime of every effect and the stagger phases, and back, front, perfect, double and multi hits are counted per skill.</p>
      <h3>Copy to chat or Discord</h3>
      <p>One click puts a chat-ready one-line ranking on the clipboard, or a Discord table of the whole group.</p>
      <h2>Sessions and combat analyzer</h2>
      <p>Every map and instance is recorded as its own session on a timeline. The session manager and a combat analyzer share one window with a movable splitter: pick a session, then a player by damage, healing or damage taken, and see every detail on the right, limited to that session, with deaths and the time dead. For boss fights a plausibility check compares the counted damage with the hit points the boss really lost and shows the boss rage, and a replay map shows the fight from above. Sessions can be saved to a file, optionally with the rotation, effects and replay data. The dummy test in the Tools menu times runs at a training dummy: the clock starts with your first hit, every run is kept, and a comparison puts the damage numbers and the skills and effects of chosen runs on one timeline.</p>
      <h2>Click-through overlay</h2>
      <p>The window turns into small chips or a compact panel on top of the game that never takes a click, with adjustable opacity and share bars, drawn only while Aion 2 is in front. Toggle it with a system-wide hotkey of your choice.</p>
      <h2>Boss fight overlay</h2>
      <p>The boss's casts with a countdown, whom they hit and when a hit would kill you, plus the target of the target with its picture, name, health bar and debuffs for healers. Every part moves and scales on its own.</p>
      <h2>Streaming for OBS</h2>
      <p>A browser source for OBS or Streamlabs shows the meter live, also from a second streaming PC in your own network. Names are off by default.</p>
      <h2>Your character</h2>
      <p>Gear with item level and enchant (including the Amplify stage), arcana, species knowledge, all skills and the full Daevanion boards. The meter keeps your last login, so the window is never empty.</p>
      <h2>Interactive map: pets and gathering</h2>
      <p>A map overlay with your position, the spawn points of the pets you are after, and the gathering spots you switch on – herbs, ores, wood, gems and more – for Verteron, Altgard and the Abyss. Edge arrows point to the nearest pet or gatherable, and a Rare tab lists pets with few spawn points.</p>
      <h2>Event timetable and world boss timers</h2>
      <p>An overlay lists the next events – Battlefield, Abyss sieges, Shugo Festival, Beritra Air Raid, Spacetime Rift and more – with a sound and volume per event and a reminder minutes before the start. A second overlay shows when each field boss is back, taken from the game itself, with its own sound.</p>
      <h2>Network overlay</h2>
      <p>Ping, jitter, traffic, packet loss and online time of your connection to the game server, measured from the traffic your PC already receives.</p>
      <h2>Announcements and Twitch Drops</h2>
      <p>One click from the meter: the official AION 2 announcements and patch notes with kind filters, and while a campaign runs, the Twitch Drops rewards, watch times and the streams with Drops enabled right now.</p>
      <h2>Aion 2 checklist</h2>
      <p>Per character, read from the game at login: the Odyle energy (basic and charged), the entries and attempts of Shugo Festival, Nightmare, Ascension rite, Dimensional Invasion and the daily dungeon, the five mandatory daily tasks and the time left in the lower Abyss. Counters you have not reached the level for are greyed out at 0.</p>
      <h2>Design editor</h2>
      <p>Pick your own colours for the window, panels, buttons and bars with a colour picker, one colour per class if you like, and save them as a design you can export and import. The preview shows every change at once.</p>
      <h2>Stat calculator (beta)</h2>
      <p>See what a change of stats does to your damage before you swap a piece: your own stats from the game, the side you hit from and your crit rate, plus an expert mode with buffs and boss resistance. It is an estimate; you can record a fight and send it to help make it better.</p>
      <h2>Network report</h2>
      <p>Save the last 15 minutes of your connection as a JSON file, or send it: ping, packet loss, pauses and what caused them, without IP address or names. You see exactly what is sent before it goes out.</p>
      <h2>Hotkeys, languages and updates</h2>
      <p>Sixteen system-wide hotkeys that work while the game has focus, all rebindable. Many languages including English, German, French, Spanish, Russian, Chinese, Portuguese, Japanese and Korean, dark and light look, and automatic background updates without an installer or admin rights.</p>
      <h2>Community leaderboards and profiles</h2>
      <p>Uploads are optional and off by default. If you switch them on, your boss fights feed the <a href="/instances">boss leaderboards</a> and your character gets a public profile; <a href="/legions">legions</a> list their members per server.</p>
      <p><a class="download-cta" href="/download">Download the Windows client</a></p>`,
  };
}

export function changelogPage(): Page {
  return {
    status: 200,
    meta: {
      title: "Changelog – Aion DPS Meter",
      description: "What changed in each release of the Aion DPS Meter Windows client, newest first.",
      canonicalPath: "/changelog",
      ...pageCard("changelog"),
      jsonLd: [breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: "Changelog", path: "/changelog" }])],
    },
    body: html`
      <h1>Changelog</h1>
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
      ...pageCard("legions"),
      jsonLd: [breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: "Legions", path: "/legions" }])],
    },
    body: html`
      <h1>Legions</h1>
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
      <h1>${guild.name}</h1>
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
      ...pageCard("stats"),
      jsonLd: [breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: "Statistics", path: "/stats" }])],
    },
    body: html`
      <h1>Statistics</h1>
      <p>Live numbers of the Aion DPS community leaderboards: players, full profiles, boss fights and downloads.</p>`,
  };
}

export function serverStatusPage(): Page {
  return {
    status: 200,
    meta: {
      title: "Server Status – Aion DPS Meter",
      description: "Are the Aion 2 login and game servers reachable right now? Live checks every minute, uptime of the last 24 hours and the worlds on each game server address.",
      canonicalPath: "/status",
      ...pageCard("status"),
      jsonLd: [breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: "Server Status", path: "/status" }])],
    },
    body: html`
      <h1>Server Status</h1>
      <p>Live reachability of the Aion 2 login servers and game servers, checked every minute.</p>`,
  };
}

export function announcementsPage(): Page {
  return {
    status: 200,
    meta: {
      title: "AION 2 Announcements – Aion DPS Meter",
      description: "The official AION 2 announcements in one place: maintenance, outages, new servers, server transfers, compensation and account notices, in your language and with colored labels.",
      canonicalPath: "/announcements",
      ...pageCard("announcements"),
      jsonLd: [breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: "AION 2 Announcements", path: "/announcements" }])],
    },
    body: html`
      <h1>AION 2 Announcements</h1>
      <p>The official AION 2 announcements: maintenance, outages, new servers, server transfers, compensation and account notices.</p>`,
  };
}

export function patchnotesPage(): Page {
  return {
    status: 200,
    meta: {
      title: "AION 2 Patch Notes – Aion DPS Meter",
      description: "The official AION 2 patch notes in full text, in your language: improvements, drop rates, merchants and fixes, newest first.",
      canonicalPath: "/patchnotes",
      ...pageCard("patchnotes"),
      jsonLd: [breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: "AION 2 Patch Notes", path: "/patchnotes" }])],
    },
    body: html`
      <h1>AION 2 Patch Notes</h1>
      <p>The official AION 2 patch notes in full text: improvements, drop rates, merchants and fixes.</p>`,
  };
}

export function twitchDropsPage(): Page {
  return {
    status: 200,
    meta: {
      title: "AION 2 Twitch Drops – Aion DPS Meter",
      description: "AION 2 Twitch Drops: the running campaign with every reward and its watch time, how to link your accounts and claim, and the earlier campaigns.",
      canonicalPath: "/twitch-drops",
      ...pageCard("twitch-drops"),
      jsonLd: [breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: "AION 2 Twitch Drops", path: "/twitch-drops" }])],
    },
    body: html`
      <h1>AION 2 Twitch Drops</h1>
      <p>Watch AION 2 streams on Twitch with Drops enabled and earn in-game rewards. This page lists the running campaign with every reward and its watch time, how to link your NC and Twitch accounts, and the earlier campaigns.</p>`,
  };
}

/** The operator page behind the secret address: an empty app shell, never indexed, never cached. */
export function privateStatsPage(path: string): Page {
  return {
    status: 200,
    meta: { title: "Statistics", description: "Statistics", canonicalPath: path, noindex: true },
    body: html`<h1>Statistics</h1>`,
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
      <h1>Privacy Policy</h1>
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
      <h1>Problem / Improvement</h1>
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
      <h1>Terms of Service</h1>
      <p>Aion DPS Meter is a free, fan-made community project, not affiliated with or endorsed by NCSoft or any official Aion publisher. It is provided "as is", without warranty. See the full terms on the site for acceptable use and liability details.</p>`,
  };
}

export function codeSigningPage(): Page {
  return {
    status: 200,
    meta: {
      title: "Code Signing Policy – Aion DPS Meter",
      description: "How the Aion DPS Meter installer is built from the public source code and signed: provider, team roles and privacy statement.",
      canonicalPath: "/code-signing-policy",
      jsonLd: [breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: "Code Signing Policy", path: "/code-signing-policy" }])],
    },
    body: html`
      <h1>Code Signing Policy</h1>
      <p>Releases of the Aion DPS Meter client are built automatically by GitHub Actions from the public source code (MIT License). Free code signing is provided by SignPath.io, with a certificate issued by the SignPath Foundation. Every signing request is approved manually by the maintainer. This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it; see the Privacy Policy.</p>`,
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
    if (r.variant === "explore" || r.variant === null) {
      byCategory.set(r.category ?? "other", (byCategory.get(r.category ?? "other") ?? 0) + 1);
    }
  }
  const parts = [...byCategory].map(([c, n]) => `${CATEGORY_LABEL[c] ?? "Other"} (${n}${c === "expedition" ? ", Exploration & Conquest (Normal, Hard)" : c === "nightmare" ? ", 10 levels each" : ""})`);
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
      <h1>${label} instances</h1>
      ${rows.length === 0 ? html`<p class="empty">No instances recorded yet.</p>` : html`<ul class="plain">${items.map((i) => html`<li><a href="${i.path}">${i.name}</a></li>`)}</ul>`}`,
  };
}

export const INSTANCE_CATEGORY_SLUGS = ["expedition", "nightmare", "ascension", "transcendence"] as const;

const CATEGORY_INTRO: Record<string, string> = {
  expedition: "Expeditions in two difficulties: Exploration and Conquest, which has the steps Normal and Hard",
  nightmare: "Nightmare bosses, each with ten challenge levels that are ranked separately",
  ascension: "Ascension Rite instances with four difficulties: Easy, Medium, Hard and Extreme",
  transcendence: "Transcendence dungeons with four stages",
  worldboss: "World bosses of Verteron, Altgard and the Abyss: field bosses that roam the open world, ranked like any other boss",
};

/** One category of the instance overview (/instances/nightmare, /instances/expedition/hard ...) - its own address so it can be found and shared. */
export function instanceCategoryPage(game: Game, category: string, variant: "normal" | "conquest" | "conquest-hard" | null): Page | null {
  if (!(INSTANCE_CATEGORY_SLUGS as readonly string[]).includes(category)) {
    return null;
  }
  const label = GAME_LABEL[game];
  const wanted = category === "expedition" ? (variant === "conquest-hard" ? "advanced" : variant === "conquest" ? "conquest" : "explore") : null;
  const rows = db
    .select(instanceColumns)
    .from(instances)
    .where(and(eq(instances.game, game), eq(instances.category, category as never), eq(instances.hidden, false)))
    .orderBy(asc(instances.sortOrder))
    .all()
    .filter((r) => wanted === null || r.variant === wanted);
  const items = rows.map((r) => ({ name: displayName(r), path: `/instances/${r.slug}` }));
  const catLabel = CATEGORY_LABEL[category];
  const modeText = wanted === "advanced" ? " – Conquest, Hard" : wanted === "conquest" ? " – Conquest, Normal" : wanted === "explore" ? " – Exploration" : "";
  const path = `/instances/${category}${wanted === "advanced" ? "/conquest/hard" : wanted === "conquest" ? "/conquest" : ""}`;
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
      <h1>${label} ${catLabel}${modeText}</h1>
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
    body: html`<h1>World Bosses</h1><p>${CATEGORY_INTRO.worldboss}.</p><ul class="plain">${items.map((i) => html`<li><a href="${i.path}">${i.name}</a></li>`)}</ul>`,
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
  const mode = instance.variant === "advanced" ? "Conquest, Hard" : instance.variant === "conquest" ? "Conquest" : instance.variant === "explore" ? "Exploration" : "";
  const facts = INSTANCE_FACTS[instance.name] ?? {};
  const level = INSTANCE_MIN_LEVEL[instance.name];
  const factText = [
    level !== undefined ? `from level ${level}` : "",
    facts.players ? `${facts.players} players` : "",
    facts.itemLevel ? `item level ${formatInt(facts.itemLevel)}` : "",
    facts.stars ? `Conquest ${facts.stars}` : "",
  ].filter(Boolean);
  const photo = INSTANCE_IMAGES[instance.name];
  const kind = [category, mode].filter(Boolean).join(" · ");
  const worldBoss = instance.category === "worldboss";
  const base = worldBoss ? "/worldbosses" : "/instances";
  return {
    status: 200,
    meta: {
      title: `${name}${instance.variant === "explore" ? " (Exploration)" : ""} – ${label} ${worldBoss ? "World" : category || "Instance"} Bosses & DPS Leaderboard | ${SITE}`,
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
      <h1>${name}</h1>
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
  const requestedMode = modes.find((m) => m.mode === (query.mode === UNKNOWN_MODE ? "" : query.mode))?.mode;
  const mode = modes.length === 0 ? null : requestedMode ?? [...modes].sort((a, b) => b.runs - a.runs)[0].mode;
  const modeText = mode !== null ? modeLabelEn(found.instanceCategory, mode) : "";
  const canonicalParams = [
    query.server && server?.slug === query.server ? `server=${server.slug}` : "",
    requestedMode !== undefined ? `mode=${requestedMode || UNKNOWN_MODE}` : "",
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
      <h1>${name}</h1>
      <p>${instanceName} · ${label}${modeText ? ` · ${modeText}` : ""}${server ? html` · Leaderboard for <strong>${server.name}</strong>` : combined && scope === null ? html` · Leaderboard across all servers` : ""}</p>
      ${modes.length > 0 ? html`<p>Difficulty: ${modes.map((m) => html`<a href="${basePath}?mode=${m.mode}">${modeLabelEn(found.instanceCategory, m.mode)}</a> `)}</p>` : ""}
      ${mechanicsBlock}
      ${mechanics.length > 0 ? html`<h3>Leaderboard</h3>` : ""}
      ${!combined && serverList.length > 1 ? html`<p>Servers: ${serverList.map((s) => html`<a href="${basePath}?server=${s.slug ?? ""}">${s.name}</a> `)}</p>` : ""}
      ${table}`,
  };
}

/** The statistics view of a boss with an address of its own (/bosses/<boss>/statistics): the iDPS spread per class, as a table for crawlers; the app draws the box plot. */
export function bossStatisticsPage(game: Game, idOrSlug: string, query: { server?: string; mode?: string }): Page | null {
  const found = findBoss(idOrSlug, game);
  if (!found || found.game !== game) {
    return null;
  }
  const boss = found.boss;
  const name = displayName(boss);
  const instanceName = found.instanceNameEn ?? found.instanceName;
  const label = GAME_LABEL[game];
  const modes = modesFor(boss.id, found.instanceCategory);
  const requestedMode = modes.find((m) => m.mode === (query.mode === UNKNOWN_MODE ? "" : query.mode))?.mode;
  const mode = modes.length === 0 ? null : requestedMode ?? [...modes].sort((a, b) => b.runs - a.runs)[0].mode;
  const modeText = mode !== null ? modeLabelEn(found.instanceCategory, mode) : "";
  const basePath = `/bosses/${boss.slug}`;
  const statsPath = `${basePath}/statistics`;
  const canonicalPath = requestedMode !== undefined ? `${statsPath}?mode=${requestedMode || UNKNOWN_MODE}` : statsPath;
  const boxes = classBoxes(boss.id, null, mode);
  const stats = statsForBossIds([boss.id], null, mode);
  const top = boxes.slice(0, 4).map((b) => `${b.className} ${formatInt(b.median)}`).join(", ");
  const description =
    boxes.length > 0
      ? `${name} (${instanceName}${modeText ? `, ${modeText}` : ""}, ${label}): how the iDPS spreads per class over ${stats.runCount} community run${stats.runCount === 1 ? "" : "s"} – median iDPS ${top}${boxes.length > 4 ? " and more" : ""}${stats.avgDurationSeconds ? `, average kill time ${minutes(stats.avgDurationSeconds)}` : ""}.`
      : `${name} (${instanceName}${modeText ? `, ${modeText}` : ""}, ${label}) – iDPS statistics per class. No fights uploaded yet.`;
  const photo = bossImage(boss.name, found.instanceName);
  const worldBoss = found.instanceCategory === "worldboss";
  return {
    status: 200,
    meta: {
      title: `${name} iDPS Statistics per Class – ${instanceName}${modeText ? ` (${modeText})` : ""} | ${SITE}`,
      description,
      canonicalPath,
      ogImage: photo,
      ogImageAlt: photo ? `${name} – ${instanceName}` : undefined,
      jsonLd: [
        breadcrumbJsonLd([
          { name: SITE, path: "/" },
          worldBoss ? { name: `${label} world bosses`, path: `/worldbosses` } : { name: `${label} instances`, path: `/instances` },
          { name: instanceName, path: `${worldBoss ? "/worldbosses" : "/instances"}/${found.instanceSlug}` },
          { name, path: basePath },
          { name: "Statistics", path: statsPath },
        ]),
      ],
    },
    body: html`
      <h1>${name} – iDPS statistics per class</h1>
      <p>${instanceName} · ${label}${modeText ? ` · ${modeText}` : ""} · all servers · <a href="${basePath}">Leaderboard</a></p>
      ${boxes.length === 0
        ? html`<p class="empty">No fights uploaded for this boss yet.</p>`
        : html`<table><thead><tr><th>Class</th><th>Rows</th><th>Lowest</th><th>Lower quarter</th><th>Median</th><th>Upper quarter</th><th>Highest</th></tr></thead><tbody>
      ${boxes.map((b) => html`<tr><td>${b.className}</td><td>${b.samples}</td><td>${formatInt(b.min)}</td><td>${formatInt(b.q1)}</td><td>${formatInt(b.median)}</td><td>${formatInt(b.q3)}</td><td>${formatInt(b.max)}</td></tr>`)}
    </tbody></table>`}`,
  };
}

/** The statistics of a dungeon (/instances/<dungeon>/statistics): every boss with its runs, best group iDPS, average kill time and the median iDPS of the classes, plus what is played there. */
export function instanceStatisticsPage(game: Game, idOrSlug: string): Page | null {
  const instance = findInstance(idOrSlug, game);
  if (!instance || instance.name === UNASSIGNED_INSTANCE_NAME) {
    return null;
  }
  const name = displayName(instance);
  const label = GAME_LABEL[game];
  const worldBoss = instance.category === "worldboss";
  const base = worldBoss ? "/worldbosses" : "/instances";
  const statsPath = `${base}/${instance.slug}/statistics`;
  const data = instanceStatistics(instance.id, null, null);
  const runs = data.bosses.reduce((n, b) => n + b.runCount, 0);
  const fought = data.bosses.filter((b) => b.runCount > 0);
  const description =
    runs > 0
      ? `${name} (${label}): kill times and best group iDPS of ${fought.length} boss${fought.length === 1 ? "" : "es"} from ${runs} community run${runs === 1 ? "" : "s"}, the median iDPS of every class on each boss and which classes are played${data.classShare.length > 0 ? ` (most: ${data.classShare.slice(0, 3).map((c) => c.className).join(", ")})` : ""}.`
      : `${name} (${label}) – boss statistics. No fights uploaded yet.`;
  const photo = INSTANCE_IMAGES[instance.name];
  const totalRows = data.classShare.reduce((n, c) => n + c.samples, 0);
  return {
    status: 200,
    meta: {
      title: `${name} Statistics – Kill Times, Best iDPS and Classes per Boss | ${SITE}`,
      description,
      canonicalPath: statsPath,
      ogImage: photo,
      ogImageAlt: photo ? `${name} – ${label}` : undefined,
      jsonLd: [
        breadcrumbJsonLd([
          { name: SITE, path: "/" },
          { name: worldBoss ? `${label} world bosses` : `${label} instances`, path: worldBoss ? `/worldbosses` : `/instances` },
          { name, path: `${base}/${instance.slug}` },
          { name: "Statistics", path: statsPath },
        ]),
      ],
    },
    body: html`
      <h1>${name} – statistics</h1>
      <p>${label} · all servers · <a href="${base}/${instance.slug}">Bosses and leaderboards</a></p>
      ${runs === 0
        ? html`<p class="empty">No boss fights uploaded for this instance yet.</p>`
        : html`<table><thead><tr><th>Boss</th><th>Runs</th><th>Best group iDPS</th><th>Average kill time</th><th>Median iDPS per class</th></tr></thead><tbody>
      ${data.bosses.map((b) => html`<tr><td>${b.slug ? html`<a href="/bosses/${b.slug}/statistics">${displayName(b)}</a>` : displayName(b)}</td><td>${b.runCount}</td><td>${b.bestIdps ? formatInt(b.bestIdps) : "–"}</td><td>${b.avgDurationSeconds ? minutes(b.avgDurationSeconds) : "–"}</td><td>${b.classes.slice(0, 5).map((c) => `${c.className} ${formatInt(c.median)}`).join(", ")}</td></tr>`)}
    </tbody></table>
      ${data.classShare.length > 0 ? html`<h2>Classes played here</h2><p>${data.classShare.map((c) => `${c.className} ${((c.samples / Math.max(1, totalRows)) * 100).toFixed(1)} %`).join(", ")}</p>` : ""}`}`,
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
      estimated: encounterParticipants.estimated,
      startedAt: encounters.startedAt,
      isPartial: encounters.isPartial,
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
  // Estimated damage and a partial fight never make a personal best.
  const best = runs.filter((r) => !r.estimated && !r.isPartial).reduce<(typeof runs)[number] | null>((m, r) => (m === null || r.idps > m.idps ? r : m), null);
  const path = `/players/${player.slug ?? player.id}`;
  const serverShort = (player.serverName ?? "").split(" - ").pop() ?? "";
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
  const gear = [...(profile?.gear ?? [])].sort((a, b) => a.slot - b.slot);
  const attributes = profile?.attributes?.main.filter((a) => a.value > 0) ?? [];
  const boards = (profile?.boards ?? []).filter((b) => b.count > 0);
  const titles = profile?.titles ?? [];
  // A character with neither a boss run nor any gear read is just a name and a level: keep it out of the index.
  const thin = runs.length === 0 && gear.length === 0;
  return {
    status: 200,
    meta: {
      title: `${player.name} – ${classLine || "Aion 2 Character"} | ${SITE}`,
      description,
      canonicalPath: path,
      noindex: thin,
      ogType: "profile",
      ogImage: `/og/players/${player.slug ?? player.id}.png`,
      ogImageAlt: `${player.name}${className ? `, ${className}` : ""} on ${SITE}`,
      twitterCard: "summary_large_image",
      jsonLd: [
        breadcrumbJsonLd([{ name: SITE, path: "/" }, { name: player.name, path }]),
        profileJsonLd({ name: player.name, path, description, image: image ?? "/og/default.png", guild: player.guild, server: player.serverName }),
      ],
    },
    body: html`
      <h1>${player.name}</h1>
      <p>${facts.join(" · ")}</p>
      ${runs.length > 0
        ? html`<p>${runText}.</p>
            <ul class="plain">
              ${recent.map((r) => html`<li><a href="/bosses/${r.bossSlug}">${displayName({ name: r.bossName, nameEn: r.bossNameEn })}</a> – ${formatInt(r.idps)} DPS, ${r.startedAt.slice(0, 10)}</li>`)}
            </ul>`
        : html``}
      ${gear.length > 0
        ? html`<h2>Equipment</h2>
            <ul class="plain">
              ${gear.map((g) => html`<li>${g.slotName ? `${g.slotName}: ` : ""}${g.name}${g.itemLevel ? ` (item level ${g.itemLevel})` : ""}${g.enchant > 0 ? ` +${Math.min(g.enchant, 15)}` : ""}${g.zenit > 0 ? ` Zenit ${g.zenit}` : ""}${g.godstone ? ` · ${g.godstone.name}` : ""}</li>`)}
            </ul>`
        : html``}
      ${attributes.length > 0
        ? html`<h2>Main attributes</h2><p>${attributes.map((a) => `${a.key} ${formatInt(a.value)}`).join(", ")}</p>`
        : html``}
      ${profile?.wing || profile?.activePet || titles.length > 0
        ? html`<h2>Wings, pet and titles</h2>
            <ul class="plain">
              ${profile?.wing ? html`<li>Wings: ${profile.wing.name}</li>` : ""}
              ${profile?.activePet ? html`<li>Pet: ${profile.activePet.name}${profile.activePet.level ? ` (level ${profile.activePet.level})` : ""}</li>` : ""}
              ${titles.map((t) => html`<li>Title: ${t.name}</li>`)}
            </ul>`
        : html``}
      ${boards.length > 0
        ? html`<h2>Daevanion boards</h2><p>${boards.map((b) => `${b.name} ${b.count}/${b.total}`).join(", ")}</p>`
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
    .select({ name: players.name, className: encounterParticipants.className, damage: encounterParticipants.totalDamage, estimated: encounterParticipants.estimated, healing: encounterParticipants.totalHealing })
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
  const top = roster.slice(0, 3).map((p) => `${p.name} (${p.className}) ${p.estimated ? "~" : ""}${compact(p.damage)}`);
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
    body: html`<h1>${name}</h1><p>${server ?? ""}${modeText}</p>${roster.length > 0 ? html`<ul class="plain">${roster.map((p) => html`<li>${p.name} (${p.className}) – ${p.estimated ? "~" : ""}${formatInt(p.damage)} damage</li>`)}</ul>` : ""}${roster.some((p) => p.estimated) ? html`<p class="empty">${ESTIMATED_NOTE}</p>` : ""}`,
  };
}

/** One player's skill breakdown in one fight: who, against what, how much, with which skills. */
export function participantPage(game: Game, id: string): Page | null {
  // "<player slug>-<participant id>" or a bare id: the trailing number picks the fight.
  const participantId = Number(/(\d+)$/.exec(id)?.[1]);
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
      estimated: encounterParticipants.estimated,
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
  const description = `${row.playerName} (${row.className}) vs ${boss}${modeText}${row.serverName ? ` on ${row.serverName}` : ""}: ${row.estimated ? "~" : ""}${formatInt(row.totalDamage)} damage, ${row.estimated ? "~" : ""}${formatInt(row.dps)} DPS${row.estimated ? "" : `, ${row.crit.toFixed(1)}% crit`}${row.totalHealing > 0 ? `, ${formatInt(row.totalHealing)} healing` : ""}${row.durationSeconds ? `, ${minutes(row.durationSeconds)} fight` : ""}.${topSkills.length > 0 ? ` Top skills: ${topSkills.join(", ")}.` : ""}`;
  const photo = bossImage(row.bossName, row.instanceName);
  return {
    status: 200,
    meta: {
      title: `${row.playerName} vs ${boss}${modeText} – Skill Breakdown – ${SITE}`,
      description,
      canonicalPath: `/participants/${row.playerSlug ? `${row.playerSlug}-` : ""}${participantId}`,
      noindex: true,
      ogImage: photo,
      ogImageAlt: photo ? boss : undefined,
    },
    body: html`<h1>${row.playerName}</h1><p>${row.className} · ${boss}${modeText}</p><p>${row.estimated ? "~" : ""}${formatInt(row.totalDamage)} damage · ${row.estimated ? "~" : ""}${formatInt(row.dps)} DPS</p>${row.estimated ? html`<p class="empty">${ESTIMATED_NOTE}</p>` : ""}`,
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
    body: html`<h1>${title}</h1><p>${description}</p>`,
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

/** A player or legion that is no longer (or never was) in the database: 410 tells Google to drop the address sooner than a 404 does. */
export function gonePage(path: string): Page {
  return {
    status: 410,
    meta: { title: `Page removed – ${SITE}`, description: "This page has been removed.", canonicalPath: path, noindex: true },
    body: html`<h1>Page removed</h1><p>This player or legion is no longer listed. <a href="/">Back to the start page</a>.</p>`,
  };
}

export function notFoundPage(path: string): Page {
  return {
    status: 404,
    meta: { title: `Page not found – ${SITE}`, description: "This page does not exist.", canonicalPath: path, noindex: true },
    body: html`<h1>Page not found</h1><p>There is nothing at this address. <a href="/">Back to the start page</a>.</p>`,
  };
}
