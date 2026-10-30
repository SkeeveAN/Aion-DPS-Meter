import { and, asc, desc, eq, gte, inArray, lte } from "drizzle-orm";
import { ESTIMATED_NOTE, EXACT_PARTICIPANT } from "../estimated.js";
import { withServerFaction } from "../factions.js";
import type { FastifyInstance } from "fastify";
import { db } from "../db/client.js";
import { bosses, encounterParticipants, encounters, encounterSkillUsage, instances, playerNcCharacter, playerProfiles, players, servers, uploads } from "../db/schema.js";
import { profileBadge } from "../profileBadge.js";
import { ownSkillRows, skillIconByName, skillNamesByName } from "../profile.js";
import { topBuffsByParticipant } from "../skills/topBuffs.js";
import { encounterBuffDetails } from "../encounterBuffs.js";
import { hasTimeline, loadEncounterTimeline } from "../encounterTimeline.js";
import { loadEncounterSummary } from "../encounterSummary.js";
import { gameFromQuery } from "./instances.js";

/*
 * Partial fights ("Teilkampf"): an uploader who joined late saw only part of the boss's health; such a fight is stored and shown but never ranked.
 * Shapes the website relies on:
 *   GET /api/encounters/:id          -> encounter: { ..., bossMaxHp: number|null (highest reading),
 *                                       isPartial: boolean, hpSeenShare: number|null (0..1, null = not judged),
 *                                       bossRealMaxHp: number|null, bossSpawnMaxHp: number|null (raw, one player on a scaled boss) }
 *   GET /api/participants/:id        -> encounter.isPartial: boolean
 *   every list of single fights (/api/activity/recent, boss page recent runs / top groups / history, instance page runs and clears,
 *   player history, compare picker)  -> per row isPartial: boolean
 * Rankings, best lists, averages, kill counts and statistics leave partial fights out (is_partial = 0 in the query).
 *
 * Combat analysis (docs/plans/combat-analysis.md): clients from the analysis release on send timeline.analysis per player; every field below is absent/null for older uploads.
 *   GET /api/encounters/:id/timeline -> additionally analysis?: { bucketSeconds: 5, groupDamage: number[] (boss damage per bucket, encounter clock),
 *                                       coverage: { players, withAnalysis }, players: [{ participantId, name, className, damage: number[], addDamage, deadSeconds,
 *                                       aliveShare (0..1), deaths, taken: [{ from: "boss"|"add"|"other", skill, hits, total, max }] }] }
 *                                       and players[].buffs[].usable?: { seconds, dead, idle, active } (effect seconds on the player: dead / alive without a skill use within 3 s / rest)
 *   GET /api/encounters/:id/summary  -> { encounterId, bossId, durationSeconds, groupIDps, isPartial, bossDamage, addDamage|null, deaths|null, aliveShare|null,
 *                                       bossFocusShare|null, buffUsableShare|null, damageCurve|null (cumulative per 5 s), bossHp|null, coverage: { players, withTimeline, withAnalysis } }
 *                                       404 { error: "encounter_not_found" } for an unknown fight. Estimated rows (crowded world boss) are left out of every figure.
 */

// The fights of one run (3-4 bosses of the same instance and step), the same rule as the website's runs tab: same instance, step and server,
// at most 40 minutes between two fights, no boss twice, and at least one player in common with the opened fight. Start order; always holds the opened fight.
const RUN_GAP_MS = 40 * 60 * 1000;
function runOfEncounter(encounter: { id: number; bossId: number; startedAt: string; endedAt: string; mode: string; serverId: number | null; instanceName: string; game: "aion" | "aion2" }) {
  const center = Date.parse(encounter.startedAt);
  const own = db.select({ playerId: encounterParticipants.playerId }).from(encounterParticipants).where(eq(encounterParticipants.encounterId, encounter.id)).all().map((r) => r.playerId);
  const around = db
    .select({
      id: encounters.id,
      bossId: encounters.bossId,
      bossName: bosses.name,
      bossNameEn: bosses.nameEn,
      bossSlug: bosses.slug,
      startedAt: encounters.startedAt,
      endedAt: encounters.endedAt,
      durationSeconds: encounters.durationSeconds,
      groupIDps: encounters.groupIDps,
      isPartial: encounters.isPartial,
      mode: encounters.mode,
      serverId: encounters.serverId,
    })
    .from(encounters)
    .innerJoin(bosses, eq(encounters.bossId, bosses.id))
    .innerJoin(instances, eq(bosses.instanceId, instances.id))
    .where(and(eq(instances.name, encounter.instanceName), eq(instances.game, encounter.game), gte(encounters.startedAt, new Date(center - 4 * 3600_000).toISOString()), lte(encounters.startedAt, new Date(center + 4 * 3600_000).toISOString())))
    .orderBy(asc(encounters.startedAt))
    .all()
    .filter((e) => e.mode === encounter.mode && e.serverId === encounter.serverId);
  const shared = new Set(
    own.length === 0 || around.length === 0
      ? []
      : db
          .select({ encounterId: encounterParticipants.encounterId })
          .from(encounterParticipants)
          .where(and(inArray(encounterParticipants.encounterId, around.map((e) => e.id)), inArray(encounterParticipants.playerId, own)))
          .all()
          .map((r) => r.encounterId),
  );
  const sessions: (typeof around)[] = [];
  for (const e of around.filter((x) => x.id === encounter.id || shared.has(x.id))) {
    const cur = sessions.at(-1);
    if (cur && Date.parse(e.startedAt) - Date.parse(cur.at(-1)!.endedAt) <= RUN_GAP_MS && !cur.some((x) => x.bossId === e.bossId)) {
      cur.push(e);
    } else {
      sessions.push([e]);
    }
  }
  const mine = sessions.find((s) => s.some((x) => x.id === encounter.id)) ?? [];
  return mine.map(({ id, bossName, bossNameEn, bossSlug, startedAt, durationSeconds, groupIDps, isPartial }) => ({ encounterId: id, bossName, bossNameEn, bossSlug, startedAt, durationSeconds, groupIDps, isPartial }));
}

// One specific fight's full group roster (mirrors myaion.eu's PvESession) - reachable from the
// leaderboard's "top groups" list so a run can be linked to directly, not just expanded inline.
export async function encounterRoutes(app: FastifyInstance) {
  // Homepage "recent activity" feed - the most recently merged real fights, newest first, each
  // with its top damage dealer as the row's "face" (same representative-picking idea as
  // bosses.ts's topGroups, just one person instead of a whole roster). Never a fabricated event -
  // an empty game just returns an empty list.
  app.get<{ Querystring: { game?: string; limit?: string } }>("/api/activity/recent", async (request, reply) => {
    const game = gameFromQuery(request.query.game, reply);
    if (game === null) {
      return;
    }
    const limit = Math.min(Math.max(Number(request.query.limit ?? 8) || 8, 1), 20);

    const rows = db
      .select({
        encounterId: encounters.id,
        createdAt: encounters.createdAt,
        groupIDps: encounters.groupIDps,
        durationSeconds: encounters.durationSeconds,
        mode: encounters.mode,
        isPartial: encounters.isPartial,
        bossName: bosses.name,
        bossNameEn: bosses.nameEn,
        bossSlug: bosses.slug,
        instanceName: instances.name,
        instanceNameEn: instances.nameEn,
        instanceSlug: instances.slug,
        instanceCategory: instances.category,
      })
      .from(encounters)
      .innerJoin(bosses, eq(encounters.bossId, bosses.id))
      .innerJoin(instances, eq(bosses.instanceId, instances.id))
      .where(eq(instances.game, game))
      .orderBy(desc(encounters.createdAt))
      .limit(limit)
      .all();

    const withTopPlayer = rows.map((row) => {
      const top = db
        .select({ playerName: players.name, className: encounterParticipants.className })
        .from(encounterParticipants)
        .innerJoin(players, eq(encounterParticipants.playerId, players.id))
        .where(and(eq(encounterParticipants.encounterId, row.encounterId), EXACT_PARTICIPANT))
        .orderBy(desc(encounterParticipants.totalDamage))
        .get();
      return { ...row, topPlayerName: top?.playerName ?? null, topPlayerClassName: top?.className ?? null };
    });

    return reply.send(withTopPlayer);
  });

  // The time axis of a fight (skills used, effects and boss health over time) from clients that send it; 404 when none did.
  app.get<{ Params: { id: string } }>("/api/encounters/:id/timeline", async (request, reply) => {
    const encounterId = Number(request.params.id);
    if (!Number.isInteger(encounterId)) {
      return reply.status(400).send({ error: "invalid_encounter_id" });
    }
    const timeline = loadEncounterTimeline(encounterId);
    return timeline ? reply.send(timeline) : reply.status(404).send({ error: "no_timeline" });
  });

  // The figures two fights are compared by (see encounterSummary.ts); 404 for an unknown fight, null where no client sent the data.
  app.get<{ Params: { id: string } }>("/api/encounters/:id/summary", async (request, reply) => {
    const encounterId = Number(request.params.id);
    if (!Number.isInteger(encounterId)) {
      return reply.status(400).send({ error: "invalid_encounter_id" });
    }
    const summary = loadEncounterSummary(encounterId);
    return summary ? reply.send(summary) : reply.status(404).send({ error: "encounter_not_found" });
  });

  app.get<{ Params: { id: string } }>("/api/encounters/:id", async (request, reply) => {
    const encounterId = Number(request.params.id);
    if (!Number.isInteger(encounterId)) {
      return reply.status(400).send({ error: "invalid_encounter_id" });
    }

    const encounter = db
      .select({
        id: encounters.id,
        bossId: encounters.bossId,
        bossSlug: bosses.slug,
        bossName: bosses.name,
        lootRules: bosses.lootRules,
        startedAt: encounters.startedAt,
        endedAt: encounters.endedAt,
        durationSeconds: encounters.durationSeconds,
        groupIDps: encounters.groupIDps,
        mergedUploadCount: encounters.mergedUploadCount,
        bossMaxHp: encounters.bossMaxHp,
        isPartial: encounters.isPartial,
        hpSeenShare: encounters.hpSeenShare,
        bossRealMaxHp: encounters.bossRealMaxHp,
        bossSpawnMaxHp: encounters.bossSpawnMaxHp,
        mode: encounters.mode,
        // Which server this run actually happened on - so a page reached from a cross-server list
        // (homepage "recent activity"/"top players") can bring the visitor's server context back in
        // sync with the run they clicked, instead of leaving it pointed at whichever server they'd
        // picked before (see Web-Frontend app.js renderEncounter's server-override).
        serverId: encounters.serverId,
        serverName: servers.displayName,
        game: instances.game,
        instanceName: instances.name,
        instanceNameEn: instances.nameEn,
        instanceSlug: instances.slug,
        instanceCategory: instances.category,
      })
      .from(encounters)
      .innerJoin(bosses, eq(encounters.bossId, bosses.id))
      .innerJoin(instances, eq(bosses.instanceId, instances.id))
      .leftJoin(servers, eq(encounters.serverId, servers.id))
      .where(eq(encounters.id, encounterId))
      .get();
    if (!encounter) {
      return reply.status(404).send({ error: "encounter_not_found" });
    }

    // An encounter is merged from however many group members' own uploads (see
    // matching/merge.ts) - there is no single "the" client version for it, only whichever build
    // sent the most recent one, which is what actually matters for "is this run's data trustworthy
    // under the latest fixes" (see e.g. the totalDamage-scoping bug fixed in 0.7.13).
    const latestUpload = db
      .select({ clientVersion: uploads.clientVersion })
      .from(uploads)
      .where(eq(uploads.matchedEncounterId, encounterId))
      .orderBy(desc(uploads.receivedAt))
      .get();

    const roster = db
      .select({
        participantId: encounterParticipants.id,
        playerId: encounterParticipants.playerId,
        hasProfile: playerProfiles.playerId,
        profileSource: playerProfiles.source,
        ncCharacterId: playerNcCharacter.characterId,
        ncAttributes: playerNcCharacter.attributesJson,
        playerSlug: players.slug,
        playerName: players.name,
        serverName: servers.displayName,
        className: encounterParticipants.className,
        faction: encounterParticipants.faction,
        totalDamage: encounterParticipants.totalDamage,
        dps: encounterParticipants.dps,
        idps: encounterParticipants.idps,
        estimated: encounterParticipants.estimated,
        totalHealing: encounterParticipants.totalHealing,
        hps: encounterParticipants.hps,
        damageTaken: encounterParticipants.damageTaken,
        damageAbsorbed: encounterParticipants.damageAbsorbed,
        shieldsGiven: encounterParticipants.shieldsGiven,
        critRatePercent: encounterParticipants.critRatePercent,
      })
      .from(encounterParticipants)
      .innerJoin(players, eq(encounterParticipants.playerId, players.id))
      .leftJoin(servers, eq(players.serverId, servers.id))
      .leftJoin(playerProfiles, eq(playerProfiles.playerId, players.id))
      .leftJoin(playerNcCharacter, eq(playerNcCharacter.playerId, players.id))
      .where(eq(encounterParticipants.encounterId, encounterId))
      .orderBy(desc(encounterParticipants.totalDamage))
      .all();

    const topBuffs = topBuffsByParticipant(roster.map((r) => r.participantId));
    // The buffs that ran on each player (uptime, who gave them), sorted into own / group / consumable for the page's buff columns.
    const buffDetails = encounterBuffDetails(roster);
    // hasProfile: true when the player has a character profile page (see /api/players/:id).
    const rosterWithBuffs = roster.map(({ profileSource, ncCharacterId, ncAttributes, ...r }) => ({ ...withServerFaction(r), hasProfile: r.hasProfile != null, ...profileBadge(profileSource, ncCharacterId, ncAttributes), topBuffs: topBuffs.get(r.participantId) ?? [], buffs: buffDetails.get(r.participantId) ?? [] }));

    return reply.send({
      encounter: { ...encounter, appVersion: latestUpload?.clientVersion ?? null, hasTimeline: hasTimeline(encounterId) },
      // All fights of the same run (the opened one included) in start order, for the boss tabs above the lists.
      run: runOfEncounter(encounter),
      roster: rosterWithBuffs,
      // Set when some damage figures are estimated from hit counts (rows with estimated = true); the page shows it as a footnote.
      estimatedNote: rosterWithBuffs.some((r) => r.estimated) ? ESTIMATED_NOTE : null,
    });
  });

  // One player's skill breakdown for one specific fight (mirrors myaion.eu's PvEPlayerSession).
  app.get<{ Params: { id: string } }>("/api/participants/:id", async (request, reply) => {
    const participantId = Number(request.params.id);
    if (!Number.isInteger(participantId)) {
      return reply.status(400).send({ error: "invalid_participant_id" });
    }

    const participantRow = db
      .select({
        id: encounterParticipants.id,
        encounterId: encounterParticipants.encounterId,
        playerId: encounterParticipants.playerId,
        playerSlug: players.slug,
        playerName: players.name,
        serverName: servers.displayName,
        className: encounterParticipants.className,
        faction: encounterParticipants.faction,
        totalDamage: encounterParticipants.totalDamage,
        dps: encounterParticipants.dps,
        idps: encounterParticipants.idps,
        estimated: encounterParticipants.estimated,
        totalHealing: encounterParticipants.totalHealing,
        hps: encounterParticipants.hps,
        critRatePercent: encounterParticipants.critRatePercent,
        profileSource: playerProfiles.source,
        ncCharacterId: playerNcCharacter.characterId,
        ncAttributes: playerNcCharacter.attributesJson,
      })
      .from(encounterParticipants)
      .innerJoin(players, eq(encounterParticipants.playerId, players.id))
      .leftJoin(servers, eq(players.serverId, servers.id))
      .leftJoin(playerProfiles, eq(playerProfiles.playerId, players.id))
      .leftJoin(playerNcCharacter, eq(playerNcCharacter.playerId, players.id))
      .where(eq(encounterParticipants.id, participantId))
      .get();
    if (!participantRow) {
      return reply.status(404).send({ error: "participant_not_found" });
    }
    const { profileSource, ncCharacterId, ncAttributes, ...participantFields } = participantRow;
    const participant = { ...withServerFaction(participantFields), ...profileBadge(profileSource, ncCharacterId, ncAttributes) };

    const encounter = db
      .select({
        id: encounters.id,
        bossId: encounters.bossId,
        bossSlug: bosses.slug,
        bossName: bosses.name,
        startedAt: encounters.startedAt,
        durationSeconds: encounters.durationSeconds,
        mode: encounters.mode,
        isPartial: encounters.isPartial,
        game: instances.game,
        instanceName: instances.name,
        instanceNameEn: instances.nameEn,
        instanceSlug: instances.slug,
        instanceCategory: instances.category,
      })
      .from(encounters)
      .innerJoin(bosses, eq(encounters.bossId, bosses.id))
      .innerJoin(instances, eq(bosses.instanceId, instances.id))
      .where(eq(encounters.id, participant.encounterId))
      .get();

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
      .where(eq(encounterSkillUsage.participantId, participantId))
      .orderBy(asc(encounterSkillUsage.isHeal), desc(encounterSkillUsage.totalDamage))
      .all();


    const aion2 = encounter?.game === "aion2";
    const withIcon = (list: typeof skills) =>
      (aion2 ? ownSkillRows(participant.className, list) : list).map((s) => ({ ...s, icon: aion2 ? skillIconByName(s.skillName, participant.className) : null, names: aion2 ? skillNamesByName(s.skillName, participant.className) : undefined }));
    return reply.send({
      participant,
      encounter,
      damageSkills: withIcon(skills.filter((s) => !s.isHeal)),
      healSkills: withIcon(skills.filter((s) => s.isHeal)),
    });
  });
}
