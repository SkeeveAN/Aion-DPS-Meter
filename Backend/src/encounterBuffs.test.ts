import assert from "node:assert/strict";
import { test } from "node:test";
import { classifyEncounterBuffs, isFood, isPotion, buffIcon } from "./encounterBuffs.js";
import { mergeBuffLists } from "./matching/merge.js";

const row = (skill: string, skillId: number, givers: string[] = [], uptimePermille = 500, casts = 1) => ({ skill, skillId, casts, uptimePermille, givers });

test("a buff one player got from another is a group buff for everyone, also for the one who cast it", () => {
  const kinds = classifyEncounterBuffs([
    { id: 1, name: "Aahz", buffs: [row("Experienced Counterstrike", 11780007), row("Murderous Burst", 11800007)] },
    { id: 2, name: "Pencilgon", buffs: [row("Experienced Counterstrike", 11780007, ["Aahz"])] },
  ]);
  assert.deepEqual(kinds.get(1)!.map((b) => [b.skill, b.kind]), [["Experienced Counterstrike", "group"], ["Murderous Burst", "own"]]);
  assert.equal(kinds.get(2)![0]!.kind, "group");
});

test("a mantra the Chanter cast on themself is a group buff because others got it from the Chanter", () => {
  const kinds = classifyEncounterBuffs([
    { id: 1, name: "PaiN", buffs: [row("Sprint Mantra", 18160021)] },
    { id: 2, name: "Lillyth", buffs: [row("Sprint Mantra", 18160021, ["PaiN"])] },
  ]);
  assert.equal(kinds.get(1)![0]!.kind, "group");
});

test("what a monster gave, potions and rows without an id are dropped; scrolls and food are consumables", () => {
  const kinds = classifyEncounterBuffs([
    {
      id: 1,
      name: "Aahz",
      buffs: [row("Attack", 1801692, ["Talisra of the Void"]), row("Life Potion", 2011101), row("Odd", 20), row("Speed Scroll", 2210103), row("Halloween Cookie", 2700416)],
    },
  ]);
  assert.deepEqual(kinds.get(1)!.map((b) => [b.skill, b.kind]), [["Speed Scroll", "consumable"], ["Halloween Cookie", "consumable"]]);
});

test("givers who are not in the fight are removed from a group buff", () => {
  const kinds = classifyEncounterBuffs([
    { id: 1, name: "Aahz", buffs: [row("Light of Protection", 17410011, ["Namì", "Stranger"])] },
    { id: 2, name: "Namì", buffs: [] },
  ]);
  assert.deepEqual(kinds.get(1)![0]!.givers, ["Namì"]);
});

test("food and potions are told by the item id ranges", () => {
  assert.equal(isFood(2700416), true);
  assert.equal(isFood(2161601), true);
  assert.equal(isFood(2210103), false); // a scroll
  assert.equal(isPotion(2011101), true);
});

test("icons: a class skill gets its skill icon, a scroll the icon of its item", () => {
  assert.equal(buffIcon(11780007)?.iconKind, "skill");
  assert.equal(buffIcon(2210103)?.iconKind, "item");
  assert.equal(buffIcon(5), null);
});

test("two buff lists of one person: counts add, uptime is the longer, givers are united", () => {
  const merged = mergeBuffLists([row("A", 11, ["x"], 300, 2)], [row("A", 11, ["y"], 700, 3), row("B", 12)]);
  const a = merged.find((b) => b.skill === "A")!;
  assert.equal(a.casts, 5);
  assert.equal(a.uptimePermille, 700);
  assert.deepEqual(a.givers, ["x", "y"]);
  assert.equal(merged.length, 2);
});
