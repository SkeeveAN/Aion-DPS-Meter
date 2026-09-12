import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

let trashMobNames: Set<string> | null = null;

function load(): Set<string> {
  if (!trashMobNames) {
    const filePath = path.join(__dirname, "..", "data", "npc_trash_mob_names.json");
    const names: string[] = JSON.parse(fs.readFileSync(filePath, "utf8"));
    trashMobNames = new Set(names);
  }
  return trashMobNames;
}

/**
 * Per the user: an everyday trash mob (e.g. "Kobold Peon", killed in passing on the way to a real
 * boss) is not a boss fight and must never even be accepted as an upload, unlike the existing
 * manual `bosses.is_trash_mob` flag (see the backend README's "Trash-Mobs ausblenden" section)
 * which exists for judgment calls this can't make automatically (an Elite mob the user has
 * decided doesn't matter for a specific instance, or a name aioncodex's own catalog doesn't cover).
 *
 * npc_trash_mob_names.json is a derived copy of the client's own
 * `assets/npcs/npcs_en_4x.json` (aioncodex's 4x NPC/monster catalog, same source as the client's
 * Data/NpcDatabase.cs): every name whose EVERY cataloged occurrence is rank "Normal", nothing else.
 * Deliberately excludes any name that ALSO occurs at Elite/Heroic/Legendary rank somewhere else in
 * the catalog (481 of 18.726 unique names, e.g. "Boreas" is a Normal spawn in one zone and a
 * Heroic named unique in another) - rejecting those on name alone would risk turning away a real
 * boss fight just because a same-named trash spawn exists somewhere else. An unrecognized name is
 * never rejected either - this dataset doesn't cover a private server's own custom content, and a
 * false "not a boss" rejection is worse than an unfiltered trash-mob upload (which the manual flag
 * above can still clean up after the fact).
 */
export function isTrashMobName(name: string): boolean {
  return load().has(name);
}
