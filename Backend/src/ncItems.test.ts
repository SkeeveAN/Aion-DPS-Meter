import { test } from "node:test";
import assert from "node:assert/strict";
import { gearOfSlot, levelBonusOf, mapNcItem, ncLangOf } from "./ncItems.js";

// Aahz's main hand as NC answers (equipment/item) and the item's definition (gameconst/item), shortened.
const equipped = {
  id: 110130050, name: "Aulamus' Großschwert", grade: "Unique", gradeName: "Einzigartig", categoryName: "Großschwert", classNames: ["Gladiator"],
  level: 70, levelValue: 15, enchantLevel: 15, equipLevel: 45, tradable: false, soulBindRate: "100",
  mainStats: [
    { id: "WeaponFixingDamage", name: "Angriffskraft", minValue: "305", value: "413", extra: "119", exceed: false },
    { id: "Block", name: "Block", value: "150", extra: "0", exceed: false },
  ],
  subStats: [{ id: "AGI", name: "Präzision", value: "29" }, { id: "CombatSpeed", name: "Kampftempo", value: "11.3%" }],
  subSkills: [{ id: 11170000, level: 1, name: "Abwärtsschlag" }],
  magicStoneStat: [
    { name: "Angriffskraft", value: "+1", grade: "Common", slotPos: 0 }, { name: "Kritischer Treffer", value: "+7", grade: "Rare", slotPos: 1 },
    { name: "LP", value: "+30", grade: "Common", slotPos: 2 }, { name: "Zusatzpräzision", value: "+5", grade: "Common", slotPos: 3 },
  ],
  godStoneStat: [{ name: "Götterstein: Aulvicars Zauber", desc: "Fügt bei Angriffen <b>625</b> Wasserschaden zu.", grade: "Rare" }],
  sources: ["Belohnungstruhen", "Weihestätte"],
};
const definition = { subStats: [{ id: "AGI", minValue: "19", value: "29" }, { id: "CombatSpeed", minValue: "9.7%", value: "11.3%" }] };

test("a worn piece keeps its base stats with the enchant bonus and the rolled stats with their range", () => {
  const tip = mapNcItem(equipped, definition);
  assert.equal(tip?.name, "Aulamus' Großschwert");
  assert.deepEqual(tip?.mainStats[0], { name: "Angriffskraft", value: "413", min: "305", max: null, bonus: "119" });
  assert.equal(tip?.mainStats[1]?.bonus, null, "a zero bonus is not shown");
  assert.deepEqual(tip?.rolled[0], { name: "Präzision", value: "29", min: "19", max: "29", bonus: null });
  assert.equal(tip?.soulBind, 100);
  assert.equal(tip?.stones[1]?.value, "+7");
  assert.equal(tip?.godStones[0]?.desc, "Fügt bei Angriffen 625 Wasserschaden zu.", "markup is stripped");
});

test("without the definition the rolled stats come without a range, and a strange answer maps to nothing", () => {
  assert.equal(mapNcItem(equipped, null)?.rolled[0]?.max, null);
  assert.equal(mapNcItem({ id: 1 }, null), null);
  assert.equal(mapNcItem("<html>", null), null);
});

test("the site's languages map onto NC's; the others read English", () => {
  assert.equal(ncLangOf("de"), "de-DE");
  assert.equal(ncLangOf("pt"), "pt-BR");
  assert.equal(ncLangOf("zh-TW"), "en-US");
  assert.equal(ncLangOf(undefined), "en-US");
});

test("only a piece the stored profile lists is asked for; the Zenit enchant byte is +15 for NC", () => {
  const gear = JSON.stringify([{ slot: 1, itemId: 110130050, enchant: 17 }, { slot: 2, itemId: 115030052, enchant: 10 }]);
  assert.deepEqual(gearOfSlot(gear, 1), { slot: 1, itemId: 110130050, enchant: 15, exceed: 2 });
  assert.equal(gearOfSlot(gear, 9), null);
  assert.equal(gearOfSlot("not json", 1), null);
});

test("the item level bonus is the enchant, the stones by rarity, a god stone and five per step over +15 (all 15 measured pieces)", () => {
  // Aahz's main hand: +15 with 2 steps, stones white/green/white/white, one god stone -> 33 in the game
  assert.equal(mapNcItem(equipped, null, 2)?.levelBonus, 33);
  assert.equal(levelBonusOf(15, 1, ["Rare", "Rare", "Rare", "Common"], []), 27, "Sidy's mace");
  assert.equal(levelBonusOf(13, 0, ["Rare", "Rare", "Rare", "Common"], ["Rare"]), 23, "Caramelly's staff");
  assert.equal(levelBonusOf(14, 0, ["Rare", "Rare", "Common", "Rare"], ["Rare"]), 24, "Namitio's mace");
  assert.equal(levelBonusOf(5, 0, ["Common", "Rare", "Rare", "Rare"], []), 12, "Layenn's staff");
  assert.equal(levelBonusOf(10, 0, ["Legend", "Common", "Common", "Common"], ["Rare"]), 19, "the shield");
  assert.equal(levelBonusOf(10, 0, ["Rare", "Rare", "Rare", "Common"], []), 17, "the helm");
  assert.equal(levelBonusOf(9, 0, [], []), 9, "a belt has no stones");
  assert.equal(levelBonusOf(10, 0, ["Unique"], []), null, "a rarity whose worth is not known shows no bonus");
  assert.equal(levelBonusOf(10, 0, [], ["Legend"]), null);
});
