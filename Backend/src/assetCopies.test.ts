import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "..");

// Tables the client ships (Client/assets/aion2) and the backend keeps for itself (Backend/src/data/aion2) must not drift apart: the client takes the server's
// data from Client/assets (see dataPack.ts), the backend reads its own copy. Each pair is one file in two places.
const COPIES: [string, string][] = [
  ["items/item_info.json", "item_info.json"],
  ["items/item_icons.json", "item_icons.json"],
  ["items/effect_icons.json", "effect_icons.json"],
  ["skills/skill_chains.json", "skill_chains.json"],
  ["skills/skill_icon_tokens.json", "skill_icon_tokens.json"],
  ["skills/skill_names.json", "skill_names.json"],
  ["skills/skill_names_i18n.json", "skill_names_i18n.json"],
  ["skills/skill_types.json", "skill_types.json"],
  ["species/species_stats.json", "species_stats.json"],
  ["titles/titles.json", "titles.json"],
  ["wings/wings.json", "wings.json"],
];

test("data copies: the client's table and the backend's copy are the same file", () => {
  for (const [client, backend] of COPIES) {
    const a = readFileSync(path.join(ROOT, "Client", "assets", "aion2", client), "utf8");
    const b = readFileSync(path.join(ROOT, "Backend", "src", "data", "aion2", backend), "utf8");
    assert.equal(a, b, `${client} differs from the backend's ${backend}: copy the newer one over the other`);
  }
});
