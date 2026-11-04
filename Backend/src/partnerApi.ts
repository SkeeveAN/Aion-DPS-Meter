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

/** The OpenAPI (Swagger) description of the partner API, docs/partner-api.openapi.json. */
export const openapi = () => load<object>(fileURLToPath(new URL("../docs/partner-api.openapi.json", import.meta.url)));

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
type LootRoll = { chance: number; times: number; grade: string; items: { itemId: number; grade: string; chance: number; count: [number, number] }[] };
type BossLoot = { dungeon: string; mode: string; role: string; npcId: number; odyleCost: number; rolls: LootRoll[]; ceiling: { after: number; rewards: number; items: { itemId: number }[] } | null };
type LootFile = { modes: Record<string, Names>; grades: Record<string, Names>; items: Record<string, { level: number | null; grade: string; icon: string | null; names: Names }>; bosses: Record<string, { portrait: string | null; loot: BossLoot[] }> };
const lootFile = () => load<LootFile>(dataDir("boss_loot.json"));
const portraitUrl = (file: string | null | undefined) => (file ? `${IMAGE_BASE}/bosses/portraits/${file}.webp` : null);

export function bosses(lang: Lang, instanceSlug?: string) {
  const instanceOfKey = new Map(load<InstanceFile>(dataDir("instances.json")).items.map((i) => [i.key, i.slug]));
  const extra = lootFile().bosses;
  return load<BossFile>(dataDir("bosses.json"))
    .items.map((b) => ({ slug: b.slug, instance: instanceOfKey.get(b.instanceKey) ?? null, name: pick(b.name, lang), level: b.level ?? null, npcIds: b.npcIds, portrait: portraitUrl(extra[b.key]?.portrait), hasLoot: (extra[b.key]?.loot.length ?? 0) > 0 }))
    .filter((b) => instanceSlug === undefined || b.instance === instanceSlug);
}

/**
 * One boss with its loot as the game's "possible reward" window shows it: per dungeon mode, the rolls ("random obtain 1..n": chance of the roll,
 * how many times it is made) with the items of each roll and their single chances (percent), plus the Odyle cost and the guaranteed items.
 * Every mode of an expedition is a boss of its own (kromedes-desire = exploration, -conquest, -conquest-hard) with its own loot: exploration drops low level
 * gear, conquest the level 86 pieces. itemLevel is set for equipment; mode/modeName say which step the loot is from.
 */
export function boss(slug: string, lang: Lang) {
  const keys = new Set(load<BossFile>(dataDir("bosses.json")).items.filter((b) => b.slug === slug).map((b) => b.key));
  const found = bosses(lang).find((b) => b.slug === slug);
  if (!found) {
    return null;
  }
  const file = lootFile();
  const image = (id: number) => {
    const def = file.items[String(id)];
    return def?.icon ? `${IMAGE_BASE}/icons/item/${def.icon}.webp` : null;
  };
  const line = (i: { itemId: number; chance: number; count: [number, number] }) => {
    const def = file.items[String(i.itemId)];
    return { itemId: i.itemId, name: pick(def?.names, lang), grade: def?.grade ?? null, itemLevel: def?.level ?? null, image: image(i.itemId), chance: i.chance, count: i.count };
  };
  const portrait = [...keys].map((k) => file.bosses[k]?.portrait).find(Boolean);
  const order = Object.keys(file.modes);
  const rank = (mode: string) => (order.includes(mode) ? order.indexOf(mode) : order.length);
  const loot = [...keys].flatMap((k) => file.bosses[k]?.loot ?? []).sort((a, b) => rank(a.mode) - rank(b.mode) || a.mode.localeCompare(b.mode)).map((l) => ({
    dungeon: l.dungeon,
    mode: l.mode,
    modeName: file.modes[l.mode] ? pick(file.modes[l.mode], lang) : null,
    role: l.role,
    npcId: l.npcId,
    odyleCost: l.odyleCost,
    rolls: l.rolls.map((r) => ({ chance: r.chance, times: r.times, grade: r.grade, gradeName: pick(file.grades[r.grade], lang), items: r.items.map(line) })),
    guaranteed: l.ceiling ? { after: l.ceiling.after, items: l.ceiling.items.map((i) => ({ itemId: i.itemId, name: pick(file.items[String(i.itemId)]?.names, lang), image: image(i.itemId) })) } : null,
  }));
  return { ...found, portrait: portraitUrl(portrait), loot };
}

type DetailStat = { names: Names; unit: "flat" | "percent" | "hundredths" };
type DetailItem = {
  names: Names; description?: Names; type: string; grade: string; tier?: string; itemLevel?: number; race: string; requiredLevel?: number; trade?: string; stack: number; sellPrice?: number; icon?: string;
  category?: string; mainStats?: [string, number][]; maxBlock?: [string, number][]; soulBind?: { firstBindStats: number; firstBindSkills: number; pool: [string, number, number][] };
  soulFuse?: boolean; manaStoneSlots?: number; godStoneSlots?: number; enchantable?: boolean; surpassable?: boolean; succession?: boolean; set?: string[];
};
type DetailFile = { labels: Record<string, Names>; grades: Record<string, Names>; categories: Record<string, Names>; stats: Record<string, DetailStat>; items: Record<string, DetailItem> };
const detailFile = () => load<DetailFile>(dataDir("item_details.json"));

/** The client stores percentages in hundredths (820 = 8.2 %) and flight power the same; the answer gives the number as the game shows it. */
const shown = (unit: DetailStat["unit"], value: number) => (unit === "percent" ? value / 100 : unit === "hundredths" ? Math.round(value / 100) : value);

/**
 * One item as the in-game tooltip shows it (the client's Item table): base stats, the stats the first soul binding rolls from (with the number of them),
 * required level, mana and god stone sockets, soul fuse, the long description. Items are those the dungeon loot lists name; unknown ids are null.
 * "labels" are the client's own tooltip captions in the language asked for.
 */
export function item(id: number, lang: Lang) {
  const file = detailFile();
  const d = file.items[String(id)];
  if (!d) {
    return null;
  }
  const stat = (token: string, value: number) => {
    const s = file.stats[token];
    return { stat: token, name: pick(s?.names, lang), unit: s?.unit ?? "flat", value: shown(s?.unit ?? "flat", value) };
  };
  const range = (token: string, lo: number, hi: number) => {
    const s = file.stats[token];
    return { stat: token, name: pick(s?.names, lang), unit: s?.unit ?? "flat", min: shown(s?.unit ?? "flat", lo), max: shown(s?.unit ?? "flat", hi) };
  };
  const icon = lootFile().items[String(id)]?.icon ?? d.icon;
  const equip = d.type === "equip";
  return {
    itemId: id,
    name: pick(d.names, lang),
    description: d.description ? pick(d.description, lang) : null,
    type: d.type,
    category: d.category ? { key: d.category, name: pick(file.categories[d.category], lang) } : null,
    grade: { key: d.grade, name: pick(file.grades[d.grade], lang) },
    itemLevel: d.itemLevel ?? null,
    requiredLevel: d.requiredLevel ?? null,
    race: d.race,
    image: icon ? `${IMAGE_BASE}/icons/item/${icon}.webp` : null,
    boundOnPickup: d.trade === "SoulBind_Acquire",
    stackSize: d.stack,
    sellPrice: d.sellPrice ?? null,
    ...(equip
      ? {
          mainStats: (d.mainStats ?? []).map(([t, v]) => stat(t, v)),
          maxBlock: (d.maxBlock ?? []).map(([t, v]) => stat(t, v)),
          soulBind: d.soulBind ? { firstBindStats: d.soulBind.firstBindStats, firstBindSkills: d.soulBind.firstBindSkills, possibleStats: d.soulBind.pool.map(([t, lo, hi]) => range(t, lo, hi)) } : null,
          soulFuse: d.soulFuse ?? false,
          manaStoneSlots: d.manaStoneSlots ?? 0,
          godStoneSlots: d.godStoneSlots ?? 0,
          enchantable: d.enchantable ?? false,
          surpassable: d.surpassable ?? false,
          inheritable: d.succession ?? false,
        }
      : {}),
    labels: Object.fromEntries(Object.entries(file.labels).map(([k, v]) => [k, pick(v, lang)])),
  };
}

// ---- world bosses and maps ----

type FixedRule = { kind: "weekly" | "cycle"; days?: number[]; at?: string; base?: string; everyMinutes?: number; openMinutes: number };
type FieldFile = { maps: { key: string; id: number; names: Names; bosses: { npc: number; x: number; y: number; names: Names; respawnHours?: number; fixed?: FixedRule }[] }[] };
/** What the game fixes about when a boss comes: hours until it is back after a kill (field bosses) and/or the times it appears at (Abyss bosses; days 0 = Sunday, European clock). */
const timing = (b: { respawnHours?: number; fixed?: FixedRule }) => ({ respawnHours: b.respawnHours ?? null, schedule: b.fixed ?? null });
type WorldBossFile = { _meta: { groups: Record<string, string> }; items: { group: string; npcId: number; names: Names }[] };

export function worldBosses(lang: Lang) {
  const file = load<WorldBossFile>(dataDir("worldbosses.json"));
  const where = new Map<number, { map: string; x: number; y: number; respawnHours: number | null; schedule: FixedRule | null }>();
  for (const m of load<FieldFile>(assetDir("bosses", "field_bosses.json")).maps) {
    for (const b of m.bosses) {
      where.set(b.npc, { map: m.key, x: b.x, y: b.y, ...timing(b) });
    }
  }
  return file.items.map((b) => ({ npcId: b.npcId, group: b.group, name: pick(b.names, lang), ...(where.get(b.npcId) ?? { map: null, x: null, y: null, respawnHours: null, schedule: null }) }));
}

export function maps(lang: Lang) {
  return load<FieldFile>(assetDir("bosses", "field_bosses.json")).maps.map((m) => ({
    key: m.key,
    id: m.id,
    name: pick(m.names, lang),
    bosses: m.bosses.map((b) => ({ npcId: b.npc, name: pick(b.names, lang), x: b.x, y: b.y, ...timing(b) })),
  }));
}

// ---- item exchanges ----

type ExchangeItem = { itemId: number; names: Names };
type ExchangeFile = { exchanges: { product: ExchangeItem & { count: number }; cost: (ExchangeItem & { count: number })[]; kina: number }[]; weaponChoice: ExchangeItem[] };

/** The Substance Morph recipes of the Abyssal Forge: Ludra (gear change vouchers -> Ludra's Bracelet, the weapon choice box, Normal -> Hard voucher), with the items the weapon box offers. */
export function exchanges(lang: Lang) {
  const file = load<ExchangeFile>(dataDir("exchanges.json"));
  const item = (i: ExchangeItem) => ({ itemId: i.itemId, name: pick(i.names, lang) });
  return {
    exchanges: file.exchanges.map((e) => ({ product: { ...item(e.product), count: e.product.count }, cost: e.cost.map((c) => ({ ...item(c), count: c.count })), kina: e.kina })),
    weaponChoice: file.weaponChoice.map(item),
  };
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

type PetDrop = { npcId: number; petId: number; soulItemId: number; scope: "instance" | "abyss" | "raid"; instanceKey: string | null; mode: string | null };
const petDrops = () => load<{ drops: PetDrop[] }>(dataDir("pet_instance_drops.json")).drops;

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
    .map(([id, p]) => ({ id: Number(id), key: p.key, category: p.category, name: pick(p.names, lang), image: `${IMAGE_BASE}/pets/${p.key}.webp`, regions: regionsOf.get(Number(id)) ?? [], instanceDropCount: new Set(petDrops().filter((d) => d.petId === Number(id)).map((d) => d.npcId)).size }))
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

const instanceByKey = () => new Map(load<InstanceFile>(dataDir("instances.json")).items.map((i) => [i.key, i.slug]));

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
    // The monsters of instances (expeditions by step, transcendence dungeons, sanctuary raids) and of the Abyss whose loot has this pet's soul; since the patch of 4 November 2026 also normal monsters, before only bosses.
    instanceDrops: petDrops().filter((d) => d.petId === id).map((d) => ({ npcId: d.npcId, name: pick(npcNames[String(d.npcId)], lang), scope: d.scope, instance: d.instanceKey ? instanceByKey().get(d.instanceKey) ?? null : null, mode: d.mode, soulItemId: d.soulItemId })),
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
