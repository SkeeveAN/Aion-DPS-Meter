import { test } from "node:test";
import assert from "node:assert/strict";
import { mapDaevanionBoard, mapNcCharacter, openBoardIds } from "./ncCharacter.js";

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

test("Aahz example maps onto the profile attribute ids; ItemLevel is kept apart from the attributes", () => {
  const out = mapNcCharacter(aahzInfo, aahzEquipment);
  assert.deepEqual(out?.attributes, { 1: 10, 2: 19, 3: 0, 4: 23, 5: 67, 6: 8, 7: 50, 8: 51, 9: 27, 10: 21, 11: 53, 13: 6, 14: 38, 15: 17, 16: 32, 17: 38 });
  assert.deepEqual([out?.wingId, out?.wingSkinId, out?.activePet, out?.activePetLevel], [30500200, 30400400, 1124, 3]);
  assert.equal(out?.itemLevel, 2146);
});

test("profile, gear, skills and Daevanion boards of a real answer", () => {
  const info = {
    ...aahzInfo,
    profile: { characterLevel: 45, combatPower: 109480, pcId: 6, regionName: "Akatsuki", titleId: 13050017 },
    daevanion: { boardList: [{ id: 11, openNodeCount: 63 }, { id: 12, openNodeCount: 0 }, { id: 13, openNodeCount: 59 }] },
  };
  const equipment = {
    equipment: { equipmentList: [{ id: 110130050, slotPos: 1, enchantLevel: 15, exceedLevel: 2 }, { id: 215230001, slotPos: 17, enchantLevel: 9, exceedLevel: 0 }, { id: "x", slotPos: 3 }] },
    skill: { skillList: [{ id: 11020000, skillLevel: 14, equip: 1, acquired: 1, category: "Active" }, { id: 11340000, skillLevel: 10, equip: 1, acquired: 1, category: "Dp" }, { id: 11130000, skillLevel: 0, equip: 0, acquired: 0, category: "Dp" }] },
  };
  const out = mapNcCharacter(info, equipment);
  assert.deepEqual([out?.level, out?.combatPower, out?.classId, out?.guild, out?.titleId], [45, 109480, 1, "Akatsuki", 13050017]);
  assert.deepEqual(out?.gear, [{ slot: 1, itemId: 110130050, enchant: 17 }, { slot: 17, itemId: 215230001, enchant: 9 }], "the exceed steps count into the game's enchant number; a piece without a usable id is dropped");
  assert.deepEqual(out?.skills, [{ id: 11020000, level: 14, baseLevel: 14, stigma: false, equipped: true }, { id: 11340000, level: 10, baseLevel: 10, stigma: true, equipped: true }], "only acquired skills");
  assert.deepEqual(out?.boardCounts, [{ board: 11, count: 63 }, { board: 13, count: 59 }]);
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

test("a Daevanion board: the open nodes plus the start node, in our own id space", () => {
  const detail = {
    nodeList: [
      { boardId: 11, nodeId: 110113, type: "Start", open: 0 },
      { boardId: 11, nodeId: 110033, type: "Stat", open: 1 },
      { boardId: 11, nodeId: 110034, type: "Stat", open: 0 },
      { boardId: 11, nodeId: 110035, type: "SkillLevel", open: 1 },
      { boardId: 12, nodeId: 120001, type: "Stat", open: 1 },
      { boardId: 11, nodeId: "x", type: "Stat", open: 1 },
    ],
  };
  assert.deepEqual(mapDaevanionBoard(11, detail), { board: 11, nodes: [110113, 110033, 110035] });
  assert.equal(mapDaevanionBoard(11, { nodeList: [{ boardId: 11, nodeId: 110113, type: "Start", open: 0 }] }), null, "nothing open");
  assert.equal(mapDaevanionBoard(11, "x"), null);
  assert.deepEqual(openBoardIds({ daevanion: { boardList: [{ id: 11, open: 1, openNodeCount: 63 }, { id: 12, open: 1, openNodeCount: 0 }, { id: 13, open: 0, openNodeCount: 5 }] } }), [11]);
});
