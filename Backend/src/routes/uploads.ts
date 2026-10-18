import type { FastifyInstance } from "fastify";
import { CROWD_PENDING, crowdLookupEnabled, crowdToLookUp } from "../crowdResolve.js";
import { isCompletePlayer, profilesUploadSchema, uploadSchema, withoutIncompletePlayers } from "../uploadSchema.js";
import { processProfilesUpload, processUpload } from "../matching/merge.js";
import { db } from "../db/client.js";
import { bossNpcIds, bosses, instances, uploads } from "../db/schema.js";
import { eq } from "drizzle-orm";
import { gateUpload, isWorldBossNpc, NC_PENDING, PLAYER_CHECK_FAILED } from "../ncGate.js";
import { clearPageCache } from "../seo/cache.js";
import { clientPolicy, requireCurrentClient } from "../clientPolicy.js";

import { hashIp } from "../ipHash.js";
import { MAX_INSTANCE_PARTICIPANTS, MIN_KILL_DAMAGE_SHARE, MIN_WORLD_BOSS_DAMAGE_SHARE } from "../constants.js";
import { env } from "../env.js";
import { assessKill, referenceFor, structuralProblems, type ReviewFinding } from "../uploadReview.js";
import type { UploadPayload } from "../uploadSchema.js";

/** The NC gate is off in tests (no NC there) unless a test sets NC_GATE=on. */
const gateEnabled = () => (process.env.NC_GATE ?? (process.env.NODE_TEST_CONTEXT ? "off" : "on")).toLowerCase() !== "off";

/** Keeps what was turned away (and why) in the audit log instead of only in the process log. */
function recordTurnedAway(payload: UploadPayload, ipHash: string, findings: ReviewFinding[]) {
  db.insert(uploads)
    .values({
      clientVersion: payload.clientVersion,
      uploaderReportedName: payload.participants.find((p) => p.isSelf)?.name ?? "?",
      ipHash,
      status: "rejected",
      reviewReason: JSON.stringify(findings),
      rawPayloadJson: JSON.stringify(payload),
    })
    .run();
}

type UploadOutcome = { status: number; body: unknown };
type Log = FastifyInstance["log"];

/**
 * Judges and files one upload: the whole way from the "exactly one self" check to the merge. `crowdChecked` is set when the unknown players of a
 * world boss crowd have been looked up already (see crowdResolve.ts) - else such an upload waits for that first.
 */
export async function acceptUpload(log: Log, payload: UploadPayload, ipHash: string, crowdChecked = false): Promise<UploadOutcome> {
    const selfCount = payload.participants.filter((p) => p.isSelf).length;
    if (selfCount !== 1) {
      log.warn({ selfCount, bossNpcName: payload.bossNpcName }, "upload rejected: not exactly one self participant");
      return { status: 400, body: { error: "exactly_one_self_participant_required" } };
    }

    // The kill is judged on everybody who dealt damage, the players kept are those with name, server AND faction (top rule: the rest is not accepted).
    const groupDamage = payload.participants.reduce((sum, p) => sum + p.totalDamage, 0);
    if (!isCompletePlayer(payload.participants.find((p) => p.isSelf)!)) {
      log.warn({ clientVersion: payload.clientVersion, bossNpcName: payload.bossNpcName }, "upload rejected: own player lacks name, server or faction");
      return { status: 400, body: { error: "player_incomplete", message: "Name, server and faction of the uploader are required." } };
    }
    // A world boss crowd: the players the client knows no server or faction of are looked up at NC (both servers of the pairing) before the upload is
    // filed - nobody is given a server or faction by a guess. The lookup takes minutes (NC is asked slowly), so the upload waits (see crowdResolve.ts).
    if (!crowdChecked && crowdLookupEnabled() && isWorldBossNpc(payload.bossNpcId) && crowdToLookUp(payload).length > 0) {
      db.insert(uploads)
        .values({
          clientVersion: payload.clientVersion,
          uploaderReportedName: payload.participants.find((p) => p.isSelf)!.name,
          ipHash,
          status: "pending",
          reviewReason: JSON.stringify([{ code: CROWD_PENDING, detail: `${crowdToLookUp(payload).length} players of the crowd are looked up at NC` }]),
          rawPayloadJson: JSON.stringify(payload),
        })
        .run();
      return { status: 200, body: { status: "pending_review", reasons: [CROWD_PENDING] } };
    }
    const cut = withoutIncompletePlayers(payload.participants);
    if (cut.dropped.length > 0) {
      log.info({ dropped: cut.dropped.length, clientVersion: payload.clientVersion, bossNpcName: payload.bossNpcName }, "upload: players without name, server or faction left out");
      payload.participants = cut.kept;
    }

    // An instance holds at most an alliance; only a world boss has more people on it.
    const worldBoss =
      payload.bossNpcId !== undefined &&
      db
        .select({ category: instances.category })
        .from(bossNpcIds)
        .innerJoin(bosses, eq(bosses.id, bossNpcIds.bossId))
        .innerJoin(instances, eq(instances.id, bosses.instanceId))
        .where(eq(bossNpcIds.npcId, payload.bossNpcId))
        .get()?.category === "worldboss";
    const people = new Set(payload.participants.map((p) => p.name)).size;
    if (!worldBoss && people > MAX_INSTANCE_PARTICIPANTS) {
      log.warn({ people, bossNpcName: payload.bossNpcName }, "upload rejected: too many participants for an instance boss");
      return { status: 400, body: { error: "too_many_participants", people, max: MAX_INSTANCE_PARTICIPANTS } };
    }

    // A fight counts only when the group took the boss down: the damage dealt has to add up to (nearly)
    // all of the boss's hit points. Otherwise two failed 50 % attempts - or an empty run - would pass
    // as one kill. The client sends the boss's highest hit-point reading; without it nothing proves
    // the fight was a whole one. Exception: a crowded world boss, where the client only knows hit counts and
    // estimates the damage of the others (`estimated` rows) - there the line is lower (MIN_WORLD_BOSS_DAMAGE_SHARE), because the estimate
    // never adds up to the whole.
    const hasEstimatedRows = payload.participants.some((p) => p.estimated);
    const minShare = worldBoss && hasEstimatedRows ? MIN_WORLD_BOSS_DAMAGE_SHARE : MIN_KILL_DAMAGE_SHARE;
    // On a world boss the line is held by the players that are kept: what the top rule cut out does not count for the kill, else 1 % of the damage could pass as a kill.
    const judgedDamage = worldBoss ? payload.participants.reduce((sum, p) => sum + p.totalDamage, 0) : groupDamage;
    if (payload.bossMaxHp === undefined || judgedDamage < payload.bossMaxHp * minShare) {
      log.warn({ groupDamage: judgedDamage, bossMaxHp: payload.bossMaxHp, bossNpcName: payload.bossNpcName, clientVersion: payload.clientVersion }, "upload rejected: damage does not cover the boss's hit points");
      recordTurnedAway(payload, ipHash, [{ code: "boss_not_killed", detail: `${judgedDamage} damage on ${payload.bossMaxHp ?? "unknown"} hit points` }]);
      return { status: 400, body: { error: "boss_not_killed", groupDamage: judgedDamage, bossMaxHp: payload.bossMaxHp ?? null } };
    }

    // Dates that cannot be a fight (end before start, none or endless, in the future) are turned away.
    const problems = structuralProblems(payload, Date.now());
    if (problems.length > 0) {
      log.warn({ problems, bossNpcName: payload.bossNpcName, clientVersion: payload.clientVersion }, "upload rejected: implausible times");
      recordTurnedAway(payload, ipHash, problems);
      return { status: 400, body: { error: "implausible_upload", problems } };
    }

    // A kill that is formally fine but looks wrong beside other kills of the boss: in "log" mode it is only written to the log, in "on" mode it waits for review and stays off the leaderboards.
    if (env.UPLOAD_REVIEW !== "off") {
      const findings = assessKill(payload, referenceFor(payload));
      if (findings.length > 0) {
        log.warn({ findings, bossNpcName: payload.bossNpcName, mode: env.UPLOAD_REVIEW, clientVersion: payload.clientVersion }, "upload looks implausible");
        if (env.UPLOAD_REVIEW === "on") {
          db.insert(uploads)
            .values({
              clientVersion: payload.clientVersion,
              uploaderReportedName: payload.participants.find((p) => p.isSelf)!.name,
              ipHash,
              status: "pending",
              reviewReason: JSON.stringify(findings),
              rawPayloadJson: JSON.stringify(payload),
            })
            .run();
          return { status: 200, body: { status: "pending_review", reasons: findings.map((f) => f.code) } };
        }
      }
    }

    // The run counts only when NC has vouched for every player: one failed player fails the whole run, an open check holds the upload back (see ncGate.ts).
    if (gateEnabled()) {
      const gate = gateUpload(payload);
      if (gate === "fail") {
        log.warn({ bossNpcName: payload.bossNpcName, clientVersion: payload.clientVersion }, "upload rejected: a player failed the NC check");
        recordTurnedAway(payload, ipHash, [{ code: PLAYER_CHECK_FAILED, detail: "a player failed the NC check" }]);
        return { status: 400, body: { error: PLAYER_CHECK_FAILED } };
      }
      if (gate === "wait") {
        db.insert(uploads)
          .values({
            clientVersion: payload.clientVersion,
            uploaderReportedName: payload.participants.find((p) => p.isSelf)!.name,
            ipHash,
            status: "pending",
            reviewReason: JSON.stringify([{ code: NC_PENDING, detail: "waiting for the NC check of the players" }]),
            rawPayloadJson: JSON.stringify(payload),
          })
          .run();
        return { status: 200, body: { status: "pending_review", reasons: [NC_PENDING] } };
      }
    }

    const uploaderReportedName = payload.participants.find((p) => p.isSelf)!.name;
    
    let result;
    try {
      result = processUpload(payload);
    } catch (err) {
      log.error(err);
      db.insert(uploads)
        .values({
          clientVersion: payload.clientVersion,
          uploaderReportedName,
          ipHash,
          status: "rejected",
          rawPayloadJson: JSON.stringify(payload),
        })
        .run();
      return { status: 500, body: { error: "processing_failed" } };
    }

    db.insert(uploads)
      .values({
        clientVersion: payload.clientVersion,
        serverId: result.serverId,
        clientServerId: payload.serverId,
        gameServer: payload.gameServer,
        uploaderReportedName,
        ipHash,
        matchedEncounterId: result.encounterId,
        status: "merged",
        rawPayloadJson: JSON.stringify(payload),
      })
      .run();

    clearPageCache();
    return { status: 200, body: result };
}

export async function uploadRoutes(app: FastifyInstance) {
  // Aion 2 players without a boss fight (a client that read characters but killed nothing). The
  // path stays under /api/uploads on purpose: the rate limiter in server.ts keys on that prefix.
  // What the client asks at start-up: which version may upload (see clientPolicy.ts).
  app.get<{ Querystring: { version?: string } }>("/api/client-policy", async (request) => clientPolicy(Date.now(), request.query.version));

  app.post("/api/uploads/profiles", { preHandler: requireCurrentClient }, async (request, reply) => {
    const parseResult = profilesUploadSchema.safeParse(request.body);
    if (!parseResult.success) {
      app.log.warn({ details: parseResult.error.flatten(), issues: parseResult.error.issues.slice(0, 6).map((i) => `${i.path.join(".")}: ${i.message}`), clientVersion: (request.body as { clientVersion?: string } | null)?.clientVersion }, "profiles upload rejected: invalid payload");
      return reply.status(400).send({ error: "invalid_payload", details: parseResult.error.flatten() });
    }
    const payload = parseResult.data;
    if (payload.participants.filter((p) => p.isSelf).length !== 1) {
      return reply.status(400).send({ error: "exactly_one_self_participant_required" });
    }
    // No name, server or faction: the player is not accepted (the uploader's own row has to be complete, or the upload is turned away).
    if (!isCompletePlayer(payload.participants.find((p) => p.isSelf)!)) {
      app.log.warn({ clientVersion: payload.clientVersion }, "profiles upload rejected: own player lacks name, server or faction");
      return reply.status(400).send({ error: "player_incomplete", message: "Name, server and faction of the uploader are required." });
    }
    const cut = withoutIncompletePlayers(payload.participants);
    if (cut.dropped.length > 0) {
      app.log.info({ dropped: cut.dropped.length, clientVersion: payload.clientVersion }, "profiles upload: players without name, server or faction left out");
      payload.participants = cut.kept;
    }

    let result;
    try {
      result = processProfilesUpload(payload);
    } catch (err) {
      app.log.error(err);
      return reply.status(500).send({ error: "processing_failed" });
    }

    db.insert(uploads)
      .values({
        clientVersion: payload.clientVersion,
        serverId: result.serverId,
        clientServerId: payload.serverId,
        gameServer: payload.gameServer,
        uploaderReportedName: payload.participants.find((p) => p.isSelf)!.name,
        ipHash: hashIp(request.ip),
        status: "merged",
        rawPayloadJson: JSON.stringify(payload),
      })
      .run();
    clearPageCache();
    return reply.send({ status: "profiles", ...result });
  });

  // A world boss upload carries a thousand players (a few MB); nginx allows 40 MB.
  app.post("/api/uploads", { preHandler: requireCurrentClient, bodyLimit: 40 * 1024 * 1024 }, async (request, reply) => {
    const parseResult = uploadSchema.safeParse(request.body);
    if (!parseResult.success) {
      app.log.warn({ details: parseResult.error.flatten(), issues: parseResult.error.issues.slice(0, 6).map((i) => `${i.path.join(".")}: ${i.message}`), body: request.body }, "upload rejected: invalid payload");
      return reply.status(400).send({ error: "invalid_payload", details: parseResult.error.flatten() });
    }
    const payload = parseResult.data;

    const out = await acceptUpload(app.log, payload, hashIp(request.ip));
    return reply.status(out.status).send(out.body);
  });
}
