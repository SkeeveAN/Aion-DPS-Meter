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
  apTotal: z.number().int().min(0).max(100_000_000).optional(),
  skills: z.array(skillUsageSchema).max(80),
});

export const uploadSchema = z.object({
  clientVersion: z.string().max(40).default(""),
  bossNpcName: z.string().trim().min(1).max(80),
  startedAt: z.string().min(1),
  endedAt: z.string().min(1),
  // 6-man groups up to 24-man alliance instances.
  participants: z.array(participantSchema).min(1).max(24),
});

export type UploadPayload = z.infer<typeof uploadSchema>;
export type ParticipantUpload = z.infer<typeof participantSchema>;
