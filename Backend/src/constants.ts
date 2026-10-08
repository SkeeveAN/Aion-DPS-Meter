import { GAMES } from "./db/schema.js";

// The bucket every unrecognized boss name falls into - see src/matching/merge.ts.
export const UNASSIGNED_INSTANCE_NAME = "Unbekannt / nicht zugeordnet";

// The bucket every row created before server tracking existed is backfilled into (see the
// add-servers migration) - deliberately NOT a guess at which real server ("70.0.0.150:10241" etc.)
// those old rows actually came from. Two different config.ini readings turned up for the same
// physical install (bin64 vs bin32), so picking either one as the "real" historical fingerprint
// risked silently splitting that server's own future uploads into two buckets, or worse, colliding
// with a genuinely different server that happens to reuse an IP. This placeholder can never equal a
// real detected fingerprint (see Server/ServerIdentity.cs on the client: a real one is always
// "IP:PORT"), so it can only ever match itself.
export const UNKNOWN_SERVER_FINGERPRINT = "unattributed-pre-server-tracking";

// Which game a server/instance belongs to. The column and the `?game=` parameter stay (URLs and
// stored rows keep working), but only Aion 2 is served: rows of the retired classic-Aion version
// may still sit in the database (their `game` is "aion") and are simply never returned.
export { GAMES, INSTANCE_CATEGORIES } from "./db/schema.js";
export type Game = (typeof GAMES)[number];
export const DEFAULT_GAME: Game = "aion2";

export function isGame(value: unknown): value is Game {
  return value === "aion2";
}

// The 9th class (skill prefix 19) is "Brawler" in the client's own string table; some sources call it
// "Fighter" - accepted on upload and mapped here. "Elementalist" is what versions up to 0.10.8 called the
// Spiritmaster (a name the game does not use).
export const AION2_CLASS_ALIASES: Record<string, string> = { Fighter: "Brawler", Elementalist: "Spiritmaster" };

// Which classes a server does NOT offer, by server_catalog region (Aion 2) - per the user, Europe
// and North America launched with the eight base classes; Korea and Taiwan already have Brawler.
// Asia and LATAM are unknown so far and therefore offer everything. Empty/missing = every class of
// the game (see ClassCatalog on the client for the game's roster).
export const REGION_EXCLUDED_CLASSES: Record<string, readonly string[]> = {
  Europe: ["Brawler"],
  "NA West": ["Brawler"],
  "NA East": ["Brawler"],
  // Legacy region rows from before the per-server catalog (migration 0026 deactivates them).
  "North America": ["Brawler"],
};


// Aion 2 class id (the numbering the game's own tables use: 1 = Gladiator ... 8 = Chanter), as the
// client reads it from a character record. The id is the class code divided by 4 - see the client's
// Aion2FrameDecoder.DecodeVarintNickname.
export const AION2_CLASS_BY_ID: Readonly<Record<number, string>> = {
  1: "Gladiator",
  2: "Templar",
  3: "Ranger",
  4: "Assassin",
  5: "Spiritmaster",
  6: "Sorcerer",
  7: "Cleric",
  8: "Chanter",
};

export const AION2_CLASSES = [
  "Assassin",
  "Chanter",
  "Cleric",
  "Spiritmaster",
  "Brawler",
  "Gladiator",
  "Ranger",
  "Sorcerer",
  "Templar",
] as const;

// Difficulty steps inside one boss, by instance category (the step is stored on the encounter as `mode`).
// Expeditions have none: their normal / hard split is two separate instances (instances.variant).
export const CATEGORY_MODES: Readonly<Record<string, readonly string[]>> = {
  nightmare: ["1", "2", "3", "4", "5", "6", "7", "8", "9", "10"],
  ascension: ["easy", "medium", "hard", "extreme"],
  transcendence: ["1", "2", "3", "4"],
};

/**
 * How much of the boss's hit points the uploaded damage has to cover for a fight to count as a kill.
 * Real kills in the data sit between 90 % and 110 % (overkill and the odd lost packet); nothing lies
 * between 80 % and 90 %, while failed attempts stay far below.
 */
export const MIN_KILL_DAMAGE_SHARE = 0.9;
