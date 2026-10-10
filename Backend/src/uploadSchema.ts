import { z } from "zod";
import { profileSchema } from "./profile.js";
import { AION2_CLASS_ALIASES, AION2_CLASSES, GAMES } from "./constants.js";

// Generous but real ceilings - these guard against garbage/abuse, not against
// a legitimately long or hard-hitting fight, so they're deliberately loose.
const skillUsageSchema = z.object({
  skill: z.string().min(1).max(120),
  hits: z.number().int().positive().max(100_000),
  critHits: z.number().int().min(0).max(100_000),
  total: z.number().int().min(0).max(2_000_000_000),
  min: z.number().int().min(0).max(2_000_000_000),
  max: z.number().int().min(0).max(2_000_000_000),
});

// A real reinforcement (buff) cast - see the client's ChatLog/BuffCastEvent. No damage/crit/min/
// max here on purpose: a buff cast has none of those to report, unlike skillUsageSchema above.
const buffUsageSchema = z.object({
  skill: z.string().min(1).max(120),
  casts: z.number().int().positive().max(100_000),
});

export const participantSchema = z.object({
  name: z.string().trim().min(1).max(64),
  className: z
    .string()
    .trim()
    .min(1)
    .max(40)
    .transform((name) => AION2_CLASS_ALIASES[name] ?? name),
  faction: z.string().trim().max(20).default(""),
  // The numeric server id this player announced himself with (his OWN server: a matched world mixes Elyos and Asmodian
  // servers, so it is not the uploader's). Optional for older clients, which put everybody on the uploader's server.
  serverId: z.number().int().min(1000).max(99999).optional(),
  // Optional: only Aion 2 clients know a player's guild. Empty counts as absent.
  guild: z
    .string()
    .trim()
    .max(40)
    .optional()
    .transform((value) => (value ? value : undefined)),
  // Optional, Aion 2 only: what the client read about this character (see profile.ts).
  profile: profileSchema.optional(),
  // Mirrors the client's `_chatLogParser.Names.NameFor(id) == "You"` check -
  // true for exactly one participant per upload, the uploader themselves.
  isSelf: z.boolean(),
  totalDamage: z.number().int().min(0).max(2_000_000_000),
  dps: z.number().min(0).max(5_000_000),
  idps: z.number().min(0).max(5_000_000),
  // Per the user: AP/Kinah/EXP/loot are never uploaded, only combat performance - damage AND heal.
  totalHealing: z.number().int().min(0).max(2_000_000_000),
  hps: z.number().min(0).max(5_000_000),
  skills: z.array(skillUsageSchema).max(80),
  healSkills: z.array(skillUsageSchema).max(80),
  // Opposite direction from totalDamage (dealt TO the boss) - how much of the boss's own damage
  // this row ate, for the frontend's damage-distribution chart (see routes/encounters.ts).
  // Defaulted, not required: an older client that predates this field must keep uploading
  // successfully during the rollout window before everyone has auto-updated (see Update/
  // UpdateService.cs - the client is Velopack-managed, not instant), just without this number yet.
  damageTaken: z.number().int().min(0).max(2_000_000_000).default(0),
  // Group shields: damage the shields on this player absorbed, and (for a Chanter/Templar) how much
  // each shielded player got. Defaulted - clients do not report them yet.
  damageAbsorbed: z.number().int().min(0).max(2_000_000_000).default(0),
  shieldsGiven: z.array(z.object({ playerName: z.string().trim().min(1).max(40), amount: z.number().int().min(0).max(2_000_000_000) })).max(40).default([]),
  // Real reinforcements this row RECEIVED (see buffUsageSchema) - what the web frontend's "Buffs"
  // column actually shows now, not a damage/heal skill. A Cleric/Chanter group buff lands on every
  // recipient's own row, not only the caster's. Defaulted for the same rollout reason as
  // damageTaken above.
  buffs: z.array(buffUsageSchema).max(80).default([]),
});

export const uploadSchema = z
  .object({
  clientVersion: z.string().max(40).default(""),
  // Which game this fight is from - only Aion 2 is served (see constants.ts).
  game: z.literal("aion2").default("aion2"),
  bossNpcName: z.string().trim().min(1).max(80),
  // Difficulty step inside the boss (see constants.ts CATEGORY_MODES); empty when the client does not know it.
  mode: z.string().trim().toLowerCase().regex(/^[a-z0-9]{0,10}$/).default(""),
  // The boss's numeric NPC id from the game's own traffic - unambiguous where the name alone is not
  // (the same name recurs across dungeons, see boss_npc_ids). Optional for older clients.
  bossNpcId: z.number().int().positive().optional(),
  // The boss's highest hit-point reading the client saw (the game's HP frame); a lower bound when the
  // meter joined mid-fight. Optional for older clients.
  bossMaxHp: z.number().int().positive().max(100_000_000_000).optional(),
  startedAt: z.string().min(1),
  endedAt: z.string().min(1),
  // Up to 300 for a world boss (the whole map is on it); the route cuts instance bosses down to MAX_INSTANCE_PARTICIPANTS.
  participants: z.array(participantSchema).min(1).max(300),
  // The server the uploader plays on, "aion2:<server slug>" (derived from the server id in the
  // game's own character record) - required: without it runs of different servers could not be
  // told apart.
  serverFingerprint: z.string().trim().min(1).max(64),
  // Label for the fingerprint above ("Europe - Kaisinel"); upsertServer (see matching/merge.ts)
  // matches on both, so two servers that shared a fingerprint stay apart.
  serverName: z.string().trim().max(60).optional(),
  // The own character's numeric server id and the game server's address (IP:port). A server id is reused
  // by every region, the address is what tells them apart. Optional for older clients.
  serverId: z.number().int().positive().max(99999).optional(),
  gameServer: z.string().trim().max(64).optional(),
  })
  .superRefine((payload, ctx) => {
    // Aion 2 has a fixed, small class roster; a class outside it is a client bug worth rejecting
    // loudly rather than storing as a phantom class.
    payload.participants.forEach((p, i) => {
      if (!(AION2_CLASSES as readonly string[]).includes(p.className)) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["participants", i, "className"],
          message: `unknown Aion 2 class: ${p.className}`,
        });
      }
    });
  });

export type UploadPayload = z.infer<typeof uploadSchema>;
export type ParticipantUpload = z.infer<typeof participantSchema>;

/**
 * Aion 2 players without a boss fight: a client that has read characters off the network (its own
 * and the visible ones around it) but has no boss kill to attach them to. Only profiles are stored -
 * no encounter, no damage. Same participant shape as a fight upload, with the profile required.
 */
export const profilesUploadSchema = z
  .object({
    clientVersion: z.string().max(40).default(""),
    game: z.literal("aion2"),
    serverFingerprint: z.string().trim().min(1).max(64),
    serverName: z.string().trim().max(60).optional(),
    serverId: z.number().int().positive().max(99999).optional(),
    gameServer: z.string().trim().max(64).optional(),
    participants: z
      .array(
        participantSchema
          .pick({ name: true, className: true, faction: true, serverId: true, guild: true, isSelf: true })
          .extend({ profile: profileSchema }),
      )
      .min(1)
      .max(60),
  })
  .superRefine((payload, ctx) => {
    payload.participants.forEach((p, i) => {
      if (!(AION2_CLASSES as readonly string[]).includes(p.className)) {
        ctx.addIssue({ code: z.ZodIssueCode.custom, path: ["participants", i, "className"], message: `unknown Aion 2 class: ${p.className}` });
      }
    });
  });

export type ProfilesUploadPayload = z.infer<typeof profilesUploadSchema>;
