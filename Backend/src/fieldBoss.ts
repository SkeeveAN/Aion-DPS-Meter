import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

/**
 * The field bosses of the home maps (Verteron: Elyos, Altgard: Asmodians; the Abyss bosses are left out, both factions fight there).
 * A thousand strangers are on such a boss and the client knows neither their server nor their faction - but the map says whose they are:
 * on a map of the uploader's own faction the crowd is his faction and, with the server pairing (Kaisinel/Lumiel), his server.
 */
const FACTION_OF_GROUP: Record<string, "Elyos" | "Asmodian"> = { verteron: "Elyos", altgard: "Asmodian" };

let npcFaction: Map<number, "Elyos" | "Asmodian"> | null = null;

function load(): Map<number, "Elyos" | "Asmodian"> {
  if (npcFaction === null) {
    npcFaction = new Map();
    try {
      const file = join(dirname(fileURLToPath(import.meta.url)), "data", "aion2", "worldbosses.json");
      const items = (JSON.parse(readFileSync(file, "utf8")) as { items: { group: string; npcId: number }[] }).items;
      for (const item of items) {
        const faction = FACTION_OF_GROUP[item.group];
        if (faction !== undefined) {
          npcFaction.set(item.npcId, faction);
        }
      }
    } catch {
      // no list: nothing is filled in, the top rule stays as it is
    }
  }
  return npcFaction;
}

/** The faction whose home-map field boss this is, or null (no field boss). */
export function fieldBossFaction(npcId: number | undefined): "Elyos" | "Asmodian" | null {
  return npcId === undefined ? null : (load().get(npcId) ?? null);
}

/**
 * Fills the gaps of the crowd on a field boss: a player without faction or server gets the map's faction and the uploader's server -
 * only when the uploader himself is of that faction (else the map says nothing about the strangers). Returns how many were filled.
 */
export function fillFieldBossCrowd<T extends { isSelf: boolean; faction?: string; serverId?: number }>(npcId: number | undefined, participants: T[]): number {
  const faction = fieldBossFaction(npcId);
  const self = participants.find((p) => p.isSelf);
  if (faction === null || self === undefined || self.faction !== faction || self.serverId === undefined) {
    return 0;
  }
  let filled = 0;
  for (const p of participants) {
    if (p.isSelf || (p.faction === faction && p.serverId !== undefined)) {
      continue;
    }
    if (p.faction !== undefined && p.faction !== "" && p.faction !== faction) {
      continue; // a known player of the other faction stays as the client read him
    }
    p.faction = faction;
    p.serverId ??= self.serverId;
    filled++;
  }
  return filled;
}
