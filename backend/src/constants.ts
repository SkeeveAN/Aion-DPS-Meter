import { GAMES } from "./db/schema.js";

// The bucket every unrecognized boss name falls into - see src/matching/merge.ts. One such row
// exists per game (see instances.game).
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

// Which game a server/instance belongs to (values live in schema.ts, see its remarks). Anything
// that predates this distinction (old clients, old URLs) means "aion".
export { GAMES, INSTANCE_CATEGORIES } from "./db/schema.js";
export type Game = (typeof GAMES)[number];
export const DEFAULT_GAME: Game = "aion";

export function isGame(value: unknown): value is Game {
  return typeof value === "string" && (GAMES as readonly string[]).includes(value);
}

// The 9th class (skill prefix 19) is "Brawler" in the client's own string table; some sources call it
// "Fighter" - accepted on upload and mapped here.
export const AION2_CLASS_ALIASES: Record<string, string> = { Fighter: "Brawler" };

// Which classes a server does NOT offer, by server_catalog slug. Per the user: Aion 2 in Europe
// (and NA) launched with the eight base classes; Korea and Taiwan already have Brawler. Empty/
// missing = every class of the game (see ClassCatalog on the client for the game's roster).
export const SERVER_EXCLUDED_CLASSES: Record<string, readonly string[]> = {
  "aion-2-europe": ["Brawler"],
  "aion-2-north-america": ["Brawler"],
};

export const AION2_CLASSES = [
  "Assassin",
  "Chanter",
  "Cleric",
  "Elementalist",
  "Brawler",
  "Gladiator",
  "Ranger",
  "Sorcerer",
  "Templar",
] as const;
