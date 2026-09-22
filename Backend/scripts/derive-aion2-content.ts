/**
 * Derives our Aion 2 reference content (src/data/aion2/*.json) from a third-party dataset that is
 * NOT part of this repository. Only facts cross over: dungeon and boss names as the game client
 * shows them, NPC ids, roles, levels, which boss has which mechanics, trigger type/percentage and
 * severity. Everything that is somebody's writing - trigger labels, action and detail prose,
 * position sheets, images - is left empty here and, when --review-out is given, dumped to a file
 * outside the repo as reading material for writing our own text.
 *
 *   pnpm content:derive -- --source /path/to/dataset [--out src/data/aion2] [--review-out ../aion2.review.json]
 *
 * Re-runs are safe: fields a human has filled in the existing output (action, detail, trigger
 * labels, de/fr names, category, extra npcIds) are kept; only derived fields are refreshed.
 */
import fs from "node:fs";
import path from "node:path";
import { slugify } from "../src/seo/slug.js";
import type { Aion2Boss, Aion2Class, Aion2Instance, Aion2Mechanic, LocalizedText } from "../src/content/aion2Content.js";

interface SourceNpc {
  en?: string;
  ko?: string;
  zh?: string;
  role: string;
  level: number;
  internalName: string;
}
interface SourceMobs {
  dungeons: Record<string, { name: { en?: string; ko?: string; zh?: string }; npcs: Record<string, SourceNpc> }>;
}
interface SourceIcons {
  dungeons: Record<string, string>;
}
interface SourceMechanic {
  id: string;
  trigger: { type: "phase" | "hp"; pct?: number; label?: Record<string, string> };
  severity: "wipe" | "wipe-avoidable" | "mechanic";
  action?: Record<string, string>;
  detail?: Record<string, string>;
  sheet?: unknown;
}
interface SourceSheets {
  dungeons: { key: string; category?: string; order?: number; bosses: { id: string; npcIds: number[]; name: { en?: string; ko?: string; zh?: string }; mechanics: SourceMechanic[] }[] }[];
}

// The dataset flags far more NPCs as bosses than a player would call one (named elites, add
// waves); this key is a known mash-up of unrelated fights and is skipped for bosses entirely.
const SKIP_BOSSES_FOR = new Set(["Nightmare"]);
const SKIP_DUNGEONS = new Set(["GenericDungeon"]);
const CATEGORY_BY_ICON: Record<string, Aion2Instance["category"]> = { "hideout.png": "hideout", "awaken.png": "awakening", "stronghold.png": "stronghold" };
const VALID_CATEGORIES = new Set(["expedition", "transcendence", "sanctuary", "hideout", "stronghold", "awakening"]);

const AION2_CLASSES: Aion2Class[] = [
  { name: "Assassin", slug: "assassin", abbreviation: "ASN" },
  { name: "Chanter", slug: "chanter", abbreviation: "CHA" },
  { name: "Cleric", slug: "cleric", abbreviation: "CLR" },
  { name: "Elementalist", slug: "elementalist", abbreviation: "ELE" },
  { name: "Brawler", slug: "brawler", abbreviation: "BRW" },
  { name: "Gladiator", slug: "gladiator", abbreviation: "GLA" },
  { name: "Ranger", slug: "ranger", abbreviation: "RNG" },
  { name: "Sorcerer", slug: "sorcerer", abbreviation: "SOR" },
  { name: "Templar", slug: "templar", abbreviation: "TPL" },
];

function arg(name: string): string | undefined {
  const i = process.argv.indexOf(name);
  return i >= 0 ? process.argv[i + 1] : undefined;
}

const sourceDir = arg("--source");
if (!sourceDir) {
  console.error("usage: derive-aion2-content --source <dataset dir> [--out <dir>] [--review-out <file>]");
  process.exit(2);
}
const outDir = arg("--out") ?? path.join("src", "data", "aion2");
const reviewOut = arg("--review-out");

const readSource = <T>(file: string): T => JSON.parse(fs.readFileSync(path.join(sourceDir, file), "utf8")) as T;
const mobs = readSource<SourceMobs>("mobs.json");
const icons = readSource<SourceIcons>("dungeon_icons.json");
const sheets = readSource<SourceSheets>("mechanics_sheets.json");

const readExisting = <T>(file: string): T[] => {
  const full = path.join(outDir, file);
  if (!fs.existsSync(full)) {
    return [];
  }
  const parsed = JSON.parse(fs.readFileSync(full, "utf8")) as { items?: T[] } | T[];
  return Array.isArray(parsed) ? parsed : parsed.items ?? [];
};
const existingInstances = new Map(readExisting<Aion2Instance>("instances.json").map((i) => [i.key, i]));
const existingBosses = new Map(readExisting<Aion2Boss>("bosses.json").map((b) => [b.key, b]));
const existingMechanics = new Map(readExisting<Aion2Mechanic>("mechanics.json").map((m) => [`${m.bossKey ?? m.instanceKey}#${m.key}`, m]));

function localized(name: { en?: string; ko?: string; zh?: string }, previous?: LocalizedText): LocalizedText {
  const out: LocalizedText = { en: name.en ?? previous?.en ?? "" };
  if (name.ko) out.ko = name.ko;
  if (name.zh) out.zh = name.zh;
  // Translations are authored here (the dataset has none), so they survive a re-run.
  if (previous?.de) out.de = previous.de;
  if (previous?.fr) out.fr = previous.fr;
  return out;
}

// ---- instances -------------------------------------------------------------------------------
const sheetByKey = new Map(sheets.dungeons.map((d) => [d.key, d]));
const instances: Aion2Instance[] = [];
Object.keys(icons.dungeons).forEach((key, index) => {
  if (SKIP_DUNGEONS.has(key)) return;
  const mob = mobs.dungeons[key];
  if (!mob?.name.en) {
    console.warn(`skip ${key}: no English dungeon name in dataset`);
    return;
  }
  const previous = existingInstances.get(key);
  const sheet = sheetByKey.get(key);
  const derivedCategory = sheet?.category && VALID_CATEGORIES.has(sheet.category) ? (sheet.category as Aion2Instance["category"]) : CATEGORY_BY_ICON[icons.dungeons[key]] ?? null;
  instances.push({
    key,
    slug: previous?.slug ?? slugify(mob.name.en),
    name: localized(mob.name, previous?.name),
    category: previous?.category ?? derivedCategory,
    sortOrder: sheet?.order ?? 100 + index,
  });
});
const instanceByKey = new Map(instances.map((i) => [i.key, i]));

// ---- bosses ----------------------------------------------------------------------------------
// One boss = one English name within a dungeon; the dataset lists difficulty variants as separate
// NPC ids with the same name, which is exactly what boss_npc_ids is for.
interface BossDraft {
  key: string;
  instanceKey: string;
  name: LocalizedText;
  npcIds: Set<number>;
  level: number | null;
  internalName: string | null;
  hasMechanics: boolean;
}
const drafts = new Map<string, BossDraft>();
const draftByNpcId = new Map<number, BossDraft>();

function draftFor(instanceKey: string, name: { en?: string; ko?: string; zh?: string }): BossDraft | null {
  if (!name.en || name.en.startsWith("M_")) return null;
  const key = `${instanceKey}/${slugify(name.en)}`;
  let draft = drafts.get(key);
  if (!draft) {
    draft = { key, instanceKey, name: localized(name, existingBosses.get(key)?.name), npcIds: new Set(), level: null, internalName: null, hasMechanics: false };
    drafts.set(key, draft);
  }
  return draft;
}

for (const instance of instances) {
  if (SKIP_BOSSES_FOR.has(instance.key)) continue;
  for (const [id, npc] of Object.entries(mobs.dungeons[instance.key].npcs)) {
    if (npc.role !== "boss") continue;
    const draft = draftFor(instance.key, npc);
    if (!draft) continue;
    const npcId = Number(id);
    draft.npcIds.add(npcId);
    draftByNpcId.set(npcId, draft);
    draft.level ??= npc.level;
    draft.internalName ??= npc.internalName;
  }
}

// ---- mechanics -------------------------------------------------------------------------------
const mechanics: Aion2Mechanic[] = [];
const review: Record<string, unknown> = {};
for (const dungeon of sheets.dungeons) {
  if (!instanceByKey.has(dungeon.key)) {
    console.warn(`skip mechanics for ${dungeon.key}: not a listed dungeon`);
    continue;
  }
  for (const sheetBoss of dungeon.bosses) {
    // No NPC ids = a dungeon-wide rule ("throughout the dungeon"), attached to the instance.
    let owner: { bossKey?: string; instanceKey?: string };
    if (sheetBoss.npcIds.length === 0) {
      owner = { instanceKey: dungeon.key };
    } else {
      let draft = sheetBoss.npcIds.map((id) => draftByNpcId.get(id)).find((d) => d !== undefined);
      draft ??= draftFor(dungeon.key, sheetBoss.name) ?? undefined;
      if (!draft) {
        console.warn(`skip mechanics for ${dungeon.key}/${sheetBoss.id}: no usable boss name`);
        continue;
      }
      for (const id of sheetBoss.npcIds) {
        draft.npcIds.add(id);
        draftByNpcId.set(id, draft);
      }
      draft.hasMechanics = true;
      owner = { bossKey: draft.key };
    }

    sheetBoss.mechanics.forEach((m, index) => {
      const ownerKey = owner.bossKey ?? owner.instanceKey!;
      const previous = existingMechanics.get(`${ownerKey}#${m.id}`);
      mechanics.push({
        ...owner,
        key: m.id,
        sortOrder: index,
        trigger: { type: m.trigger.type, ...(m.trigger.pct !== undefined ? { pct: m.trigger.pct } : {}), label: previous?.trigger.label ?? "" },
        severity: m.severity === "wipe-avoidable" ? "wipe_avoidable" : m.severity,
        action: previous?.action ?? "",
        detail: previous?.detail ?? "",
        positionSheet: previous?.positionSheet ?? null,
      });
      review[`${ownerKey}#${m.id}`] = { label: m.trigger.label?.en, action: m.action?.en, detail: m.detail?.en, hasSheet: m.sheet !== undefined };
    });
  }
}

// ---- boss slugs: unique per game, qualified by dungeon on a name clash ------------------------
const bosses: Aion2Boss[] = [];
const nameCount = new Map<string, number>();
for (const d of drafts.values()) {
  nameCount.set(d.name.en, (nameCount.get(d.name.en) ?? 0) + 1);
}
for (const d of [...drafts.values()].sort((a, b) => a.key.localeCompare(b.key))) {
  const previous = existingBosses.get(d.key);
  const base = slugify(d.name.en);
  const instanceSlug = instanceByKey.get(d.instanceKey)!.slug;
  const slug = previous?.slug ?? ((nameCount.get(d.name.en) ?? 0) > 1 ? `${base}-${instanceSlug}` : base);
  const npcIds = new Set([...d.npcIds, ...(previous?.npcIds ?? [])]);
  bosses.push({
    key: d.key,
    instanceKey: d.instanceKey,
    slug,
    name: d.name,
    npcIds: [...npcIds].sort((a, b) => a - b),
    level: d.level,
    internalName: d.internalName,
    hasMechanics: d.hasMechanics,
  });
}

// ---- write -----------------------------------------------------------------------------------
fs.mkdirSync(outDir, { recursive: true });
const meta = {
  source: "derived",
  sourceKind: "third-party-dataset",
  derivedAt: new Date().toISOString().slice(0, 10),
  note: "Facts only (names as shown by the game client, NPC ids, roles, trigger/severity). Prose fields are ours and start empty; edit them here, then run content:sync.",
};
const write = (file: string, items: unknown[]) => fs.writeFileSync(path.join(outDir, file), JSON.stringify({ _meta: meta, items }, null, 2) + "\n");
write("instances.json", instances);
write("bosses.json", bosses);
write("mechanics.json", mechanics);
write("classes.json", AION2_CLASSES);
if (reviewOut) {
  fs.writeFileSync(reviewOut, JSON.stringify(review, null, 2) + "\n");
}

const withMechanics = bosses.filter((b) => b.hasMechanics).length;
console.log(
  `instances ${instances.length}, bosses ${bosses.length} (${withMechanics} with mechanics), mechanics ${mechanics.length} ` +
    `(${mechanics.filter((m) => m.instanceKey).length} dungeon-wide), authored actions ${mechanics.filter((m) => m.action).length}` +
    (reviewOut ? `, review notes -> ${reviewOut}` : ""),
);
