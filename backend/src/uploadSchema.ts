import { z } from "zod";

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

export const participantSchema = z.object({
  name: z.string().trim().min(1).max(64),
  className: z.string().trim().min(1).max(40),
  faction: z.string().trim().max(20).default(""),
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
});

export const uploadSchema = z.object({
  clientVersion: z.string().max(40).default(""),
  bossNpcName: z.string().trim().min(1).max(80),
  startedAt: z.string().min(1),
  endedAt: z.string().min(1),
  // 6-man groups up to 24-man alliance instances.
  participants: z.array(participantSchema).min(1).max(24),
  // The client's bin64\config.ini [ServerAddr] IP:port (see the client's Server/ServerIdentity.cs)
  // - required, not optional: without it there is no way to keep this upload's runs from being
  // merged or leaderboarded against a different, incompatible server's (per the user, EuroAion's
  // gear standard is nothing like this server's).
  serverFingerprint: z.string().trim().min(1).max(64),
  // Cosmetic label for the fingerprint above ("Origin Aion", "EuroAion") - optional, since the
  // fingerprint alone is already enough to keep servers apart correctly.
  serverName: z.string().trim().max(60).optional(),
});

export type UploadPayload = z.infer<typeof uploadSchema>;
export type ParticipantUpload = z.infer<typeof participantSchema>;
