import { eq, sql } from "drizzle-orm";
import { encounterParticipants, encounters } from "./db/schema.js";

/**
 * Damage of a participant can be ESTIMATED (hit counts x typical hit, see `estimated` in uploadSchema.ts): a crowded world boss never
 * sends the damage of the others. Such rows are shown in the encounter detail only - never in a record, ranking or average.
 */

/** Participant rows with real damage figures. */
export const EXACT_PARTICIPANT = eq(encounterParticipants.estimated, false);

/** Encounters without any estimated row: their group damage is a real figure, so they may enter group rankings. */
export const EXACT_ENCOUNTER = sql`not exists (select 1 from encounter_participants ep_est where ep_est.encounter_id = ${encounters.id} and ep_est.estimated = 1)`;


export const ESTIMATED_NOTE = "Damage of players marked ~ is estimated from their hit counts: the game does not send the damage of other players on a crowded world boss.";
