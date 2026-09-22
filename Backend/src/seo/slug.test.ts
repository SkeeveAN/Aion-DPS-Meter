import { test } from "node:test";
import assert from "node:assert/strict";
import { parseIdOrSlug, slugify, uniqueSlug } from "./slug.js";

test("slugify keeps German names readable and strips other diacritics", () => {
  assert.equal(slugify("Sauro-Kriegsdepot"), "sauro-kriegsdepot");
  assert.equal(slugify("Gardenführer Achradim"), "gardenfuehrer-achradim");
  assert.equal(slugify("Stahlrose: Anlegestelle"), "stahlrose-anlegestelle");
  assert.equal(slugify("Dépôt de guerre"), "depot-de-guerre");
  assert.equal(slugify("Guard Captain Ahuradim (1 Key)"), "guard-captain-ahuradim-1-key");
  assert.equal(slugify("Chantra  Fighter"), "chantra-fighter");
  assert.equal(slugify("Tiamat's Fortress"), "tiamats-fortress");
  assert.equal(slugify("Jormungand’s Bridge"), "jormungands-bridge");
});

test("slugify returns an empty string for names without Latin letters", () => {
  assert.equal(slugify("塔梅斯"), "");
  assert.equal(slugify("Тиамат"), "");
});

test("uniqueSlug appends a counter only on collision", () => {
  const taken = new Set(["tiamat", "tiamat-2"]);
  assert.equal(uniqueSlug("stormwing", (s) => taken.has(s)), "stormwing");
  assert.equal(uniqueSlug("tiamat", (s) => taken.has(s)), "tiamat-3");
});

test("parseIdOrSlug tells numeric ids from slugs and rejects junk", () => {
  assert.deepEqual(parseIdOrSlug("17"), { id: 17 });
  assert.deepEqual(parseIdOrSlug("fire-temple"), { slug: "fire-temple" });
  assert.equal(parseIdOrSlug("Fire Temple"), null);
  assert.equal(parseIdOrSlug("-bad-"), null);
});
