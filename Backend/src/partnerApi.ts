import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

// The data of the partner API (routes/partner.ts): what the game itself says - names, places, times - in the language asked for, read from the
// files the site and the client already ship (src/data/aion2 and Client/assets/aion2). Nothing here is about players, fights or uploads.

export const LANGUAGES = ["en", "de", "fr", "es", "ru", "pt", "ja", "ko", "zh"] as const;
export type Lang = (typeof LANGUAGES)[number];
export const DEFAULT_LANG: Lang = "en";

export function parseLang(value: unknown): Lang | null {
  if (value === undefined || value === "") {
    return DEFAULT_LANG;
  }
  const base = String(value).toLowerCase().split("-")[0];
  return (LANGUAGES as readonly string[]).includes(base) ? (base as Lang) : null;
}

type Names = Record<string, string>;

/** The name in the language asked for, else English, else whatever the game has. */
export function pick(names: Names | undefined | null, lang: Lang): string {
  if (!names) {
    return "";
  }
  return names[lang] ?? names.en ?? Object.values(names)[0] ?? "";
}

const dataDir = (...p: string[]) => fileURLToPath(new URL(`./data/aion2/${p.join("/")}`, import.meta.url));
const assetDir = (...p: string[]) => fileURLToPath(new URL(`../../Client/assets/aion2/${p.join("/")}`, import.meta.url));

const files = new Map<string, unknown>();
function load<T>(path: string): T {
  let value = files.get(path);
  if (value === undefined) {
    value = JSON.parse(readFileSync(path, "utf8"));
    files.set(path, value);
  }
  return value as T;
}

// ---- classes, instances, bosses ----

export function classes() {
  return load<{ items: { name: string; slug: string; abbreviation: string }[] }>(dataDir("classes.json")).items.map((c) => ({ slug: c.slug, name: c.name, abbreviation: c.abbreviation }));
}

type InstanceFile = { items: { key: string; slug: string; name: Names; category: string; sortOrder: number; modes: Record<string, unknown>; hidden?: boolean }[] };
export function instances(lang: Lang) {
  return load<InstanceFile>(dataDir("instances.json"))
    .items.filter((i) => !i.hidden)
    .map((i) => ({ slug: i.slug, name: pick(i.name, lang), category: i.category, modes: i.modes }));
}

type BossFile = { items: { key: string; instanceKey: string; slug: string; name: Names; npcIds: number[]; level?: number }[] };
export function bosses(lang: Lang, instanceSlug?: string) {
  const instanceOfKey = new Map(load<InstanceFile>(dataDir("instances.json")).items.map((i) => [i.key, i.slug]));
  return load<BossFile>(dataDir("bosses.json"))
    .items.map((b) => ({ slug: b.slug, instance: instanceOfKey.get(b.instanceKey) ?? null, name: pick(b.name, lang), level: b.level ?? null, npcIds: b.npcIds }))
    .filter((b) => instanceSlug === undefined || b.instance === instanceSlug);
}

// ---- world bosses and maps ----

type FieldFile = { maps: { key: string; id: number; names: Names; bosses: { npc: number; x: number; y: number; names: Names }[] }[] };
type WorldBossFile = { _meta: { groups: Record<string, string> }; items: { group: string; npcId: number; names: Names }[] };

export function worldBosses(lang: Lang) {
  const file = load<WorldBossFile>(dataDir("worldbosses.json"));
  const where = new Map<number, { map: string; x: number; y: number }>();
  for (const m of load<FieldFile>(assetDir("bosses", "field_bosses.json")).maps) {
    for (const b of m.bosses) {
      where.set(b.npc, { map: m.key, x: b.x, y: b.y });
    }
  }
  return file.items.map((b) => ({ npcId: b.npcId, group: b.group, name: pick(b.names, lang), ...(where.get(b.npcId) ?? { map: null, x: null, y: null }) }));
}

export function maps(lang: Lang) {
  return load<FieldFile>(assetDir("bosses", "field_bosses.json")).maps.map((m) => ({
    key: m.key,
    id: m.id,
    name: pick(m.names, lang),
    bosses: m.bosses.map((b) => ({ npcId: b.npc, name: pick(b.names, lang), x: b.x, y: b.y })),
  }));
}

// ---- events ----

type EventFile = { timeZone: string; events: { id: string; kind: string; players?: string; names: Names; windows: { days: number[]; start: string; end: string }[] }[] };
/** Days are 0 = Sunday ... 6 = Saturday, times in the zone named by timeZone (UTC). */
export function events(lang: Lang) {
  const file = load<EventFile>(assetDir("schedule", "events.json"));
  return { timeZone: file.timeZone, events: file.events.map((e) => ({ id: e.id, kind: e.kind, players: e.players ?? null, name: pick(e.names, lang), windows: e.windows })) };
}

// ---- pets ----

type PetsFile = { pets: Record<string, { key: string; category: string; names: Names }>; monsters: Record<string, number>; souls: Record<string, number>; shop: Record<string, number> };
type RegionsFile = { groups: { id: string; regions: { key: string; names: Names; pets: number[] }[] }[] };
type SpawnsFile = { maps: Record<string, Record<string, number[][]>> };
type PetMapNames = { maps: Record<string, Names> };

/** The pictures are the site's own files (the pets as 256 px portraits, the wings as 128 px icons). */
const IMAGE_BASE = "https://aiondps.com/images/aion2";

const petFile = () => load<PetsFile>(assetDir("pets", "pets.json"));

export function pets(lang: Lang, category?: string) {
  const regionsOf = new Map<number, { group: string; key: string; name: string }[]>();
  for (const g of load<RegionsFile>(assetDir("pets", "regions.json")).groups) {
    for (const r of g.regions) {
      for (const id of r.pets) {
        (regionsOf.get(id) ?? regionsOf.set(id, []).get(id)!).push({ group: g.id, key: r.key, name: pick(r.names, lang) });
      }
    }
  }
  return Object.entries(petFile().pets)
    .map(([id, p]) => ({ id: Number(id), key: p.key, category: p.category, name: pick(p.names, lang), image: `${IMAGE_BASE}/pets/${p.key}.webp`, regions: regionsOf.get(Number(id)) ?? [] }))
    .filter((p) => category === undefined || p.category.toLowerCase() === category.toLowerCase());
}

type SpawnPlace = { name: Names; points: number[][]; monsters: Map<number, number[][]> };
let spawnIndex: Map<number, Map<string, SpawnPlace>> | null = null;
/** For every pet the named maps with the places of the monsters that drop it (maps the game gives no name are left out; the difficulties of one instance share their places). The open-world maps (Verteron, Altgard, Reshanta) take the names of the field boss table. */
function spawnsOfPets() {
  if (spawnIndex) {
    return spawnIndex;
  }
  const names = { ...load<PetMapNames>(assetDir("pets", "instances.json")).maps };
  for (const m of load<FieldFile>(assetDir("bosses", "field_bosses.json")).maps) {
    for (const table of Object.keys(load<SpawnsFile>(assetDir("pets", "spawns.json")).maps)) {
      if (table.endsWith(`/${m.key}`) && Object.keys(names[table] ?? {}).length === 0) {
        names[table] = m.names;
      }
    }
  }
  const monsterPet = new Map(Object.entries(petFile().monsters).map(([npc, pet]) => [Number(npc), pet]));
  spawnIndex = new Map();
  for (const [table, npcs] of Object.entries(load<SpawnsFile>(assetDir("pets", "spawns.json")).maps)) {
    const mapNames = names[table];
    if (!mapNames || Object.keys(mapNames).length === 0) {
      continue;
    }
    for (const [npc, points] of Object.entries(npcs)) {
      const pet = monsterPet.get(Number(npc));
      if (pet === undefined) {
        continue;
      }
      const perMap = spawnIndex.get(pet) ?? spawnIndex.set(pet, new Map()).get(pet)!;
      const mapKey = mapNames.en ?? table;
      const entry = perMap.get(mapKey) ?? perMap.set(mapKey, { name: mapNames, points: [], monsters: new Map() }).get(mapKey)!;
      const own = entry.monsters.get(Number(npc)) ?? entry.monsters.set(Number(npc), []).get(Number(npc))!;
      const seen = new Set(entry.points.map((p) => `${p[0]},${p[1]}`));
      for (const p of points) {
        if (!seen.has(`${p[0]},${p[1]}`)) {
          seen.add(`${p[0]},${p[1]}`);
          entry.points.push(p);
          own.push(p);
        }
      }
    }
  }
  return spawnIndex;
}

let instanceBossSet: Set<number> | null = null;
const instanceBossIds = () => (instanceBossSet ??= new Set(load<BossFile>(dataDir("bosses.json")).items.flatMap((b) => b.npcIds)));
let worldBossSet: Set<number> | null = null;
const worldBossIds = () =>
  (worldBossSet ??= new Set([
    ...load<FieldFile>(assetDir("bosses", "field_bosses.json")).maps.flatMap((m) => m.bosses.map((b) => b.npc)),
    ...load<WorldBossFile>(dataDir("worldbosses.json")).items.map((b) => b.npcId),
  ]));

export function pet(id: number, lang: Lang) {
  const found = pets(lang).find((p) => p.id === id);
  if (!found) {
    return null;
  }
  const file = petFile();
  const npcNames = load<Record<string, Names>>(assetDir("npcs", "npc_names.json"));
  const idsOf = (table: Record<string, number>) => Object.entries(table).filter(([, pid]) => pid === id).map(([k]) => Number(k));
  return {
    ...found,
    // x and y (and z) are the game's own coordinates
    monsters: idsOf(file.monsters).map((npcId) => ({ npcId, name: pick(npcNames[String(npcId)], lang) })),
    soulItemIds: idsOf(file.souls),
    shopItemIds: idsOf(file.shop),
    // monsters: the same points by monster; boss = a boss of an instance, worldBoss = a field or world boss, neither = a normal mob that drops the soul
    spawns: [...(spawnsOfPets().get(id)?.values() ?? [])].map((s) => ({
      map: pick(s.name, lang),
      points: s.points,
      monsters: [...s.monsters].map(([npcId, points]) => ({ npcId, name: pick(npcNames[String(npcId)], lang), boss: instanceBossIds().has(npcId), worldBoss: worldBossIds().has(npcId), points })),
    })),
  };
}

// ---- titles, wings ----

export function titles(lang: Lang) {
  return Object.entries(load<Record<string, { g: string; n: Names }>>(dataDir("titles.json"))).map(([id, t]) => ({ id: Number(id), grade: t.g, name: pick(t.n, lang) }));
}

export function wings(lang: Lang) {
  return Object.entries(load<Record<string, { names: Names; icon: string; grade: string }>>(dataDir("wings.json"))).map(([id, w]) => ({ id: Number(id), grade: w.grade, icon: w.icon, image: `${IMAGE_BASE}/icons/item/${w.icon}.webp`, name: pick(w.names, lang) }));
}
