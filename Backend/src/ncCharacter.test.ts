import { test } from "node:test";
import assert from "node:assert/strict";
import { mapNcCharacter } from "./ncCharacter.js";

const stat = (type: string, value: unknown) => ({ name: type, type, value, statSecondList: [] });
const aahzInfo = {
  stat: {
    statList: [
      stat("STR", 10), stat("DEX", 19), stat("INT", 0), stat("CON", 23), stat("AGI", 67), stat("WIS", 8),
      stat("Justice", 50), stat("Freedom", 51), stat("Illusion", 27), stat("Life", 21), stat("Time", 53),
      stat("Destruction", 6), stat("Death", 38), stat("Wisdom", 17), stat("Destiny", 32), stat("Space", 38),
      stat("ItemLevel", 2146),
    ],
  },
};
const aahzEquipment = { petwing: { pet: { id: 1124, level: 3, name: "x", icon: "y" }, wing: { id: 30500200, name: "w", enchantLevel: 5 }, wingSkin: { id: 30400400, name: "s" } } };

test("Aahz example maps onto the profile attribute ids; ItemLevel is not taken", () => {
  assert.deepEqual(mapNcCharacter(aahzInfo, aahzEquipment), {
    attributes: { 1: 10, 2: 19, 3: 0, 4: 23, 5: 67, 6: 8, 7: 50, 8: 51, 9: 27, 10: 21, 11: 53, 13: 6, 14: 38, 15: 17, 16: 32, 17: 38 },
    wingId: 30500200,
    wingSkinId: 30400400,
    activePet: 1124,
    activePetLevel: 3,
  });
});

test("missing wing, skin or pet give null fields; unusable info gives null", () => {
  const bare = mapNcCharacter(aahzInfo, { petwing: { pet: null, wing: null } });
  assert.deepEqual([bare?.wingId, bare?.wingSkinId, bare?.activePet, bare?.activePetLevel], [null, null, null, null]);
  assert.equal(mapNcCharacter(aahzInfo, null)?.wingId, null);
  assert.equal(mapNcCharacter({}, aahzEquipment), null);
  assert.equal(mapNcCharacter({ stat: { statList: [stat("ItemLevel", 5)] } }, {}), null);
  assert.equal(mapNcCharacter("x", "y"), null);
});

test("untrusted values are dropped: unknown types, non-numbers, negatives, huge, null", () => {
  const out = mapNcCharacter({ stat: { statList: [stat("STR", "12"), stat("DEX", "abc"), stat("INT", -1), stat("CON", 1e9), stat("AGI", null), stat("Evil", 5), stat("__proto__", 1), null, stat("WIS", 1.5)] } }, {});
  assert.deepEqual(out?.attributes, { 1: 12 });
});
