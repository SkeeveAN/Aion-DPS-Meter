// Character data (main attributes, lord values, wing, wing skin, active pet) from the official character page of NC.
// Pure mapping of the two JSON answers (info + equipment) onto our own profile format; the fetching lives in portraitService.ts.

/** NC's stat type -> the attribute id of our profile format (profile.ts: attributes). ItemLevel (= gear score) is deliberately not taken. */
export const NC_STAT_IDS: Record<string, number> = {
  STR: 1, DEX: 2, INT: 3, CON: 4, AGI: 5, WIS: 6,
  Justice: 7, Freedom: 8, Illusion: 9, Life: 10, Time: 11, Destruction: 13, Death: 14, Wisdom: 15, Destiny: 16, Space: 17,
};

export type NcGear = { slot: number; itemId: number; enchant: number };
export type NcSkill = { id: number; level: number; baseLevel: number; stigma: boolean; equipped: boolean };

export type NcCharacterData = {
  attributes: Record<string, number>;
  combatPower: number | null;
  /** The website's "item level" stat (not the in-game gear score). */
  itemLevel: number | null;
  /** The legion NC files the character under (its `regionName`), "" when none. */
  guild: string;
  level: number | null;
  /** Our class id ((pcId - 1) / 4), null when unknown. */
  classId: number | null;
  titleId: number | null;
  gear: NcGear[];
  skills: NcSkill[];
  /** Activated node count per Daevanion board. */
  boardCounts: { board: number; count: number }[];
  wingId: number | null;
  wingSkinId: number | null;
  activePet: number | null;
  activePetLevel: number | null;
};

const asObject = (v: unknown): Record<string, unknown> | null => (v && typeof v === "object" && !Array.isArray(v) ? (v as Record<string, unknown>) : null);

const asArray = (v: unknown): Record<string, unknown>[] => (Array.isArray(v) ? v.map((x) => asObject(x) ?? {}) : []);

const nonNegativeInt = (v: unknown, max: number): number | null => {
  const n = Number(v);
  return v !== null && v !== "" && Number.isInteger(n) && n >= 0 && n <= max ? n : null;
};

const positiveInt = (v: unknown, max: number): number | null => {
  const n = Number(v);
  return Number.isInteger(n) && n > 0 && n <= max ? n : null;
};

/**
 * Maps the answers of /api/character/info and /api/character/equipment. Untrusted data: unknown stat types, non-numeric
 * values and implausible ids are dropped. Returns null when the info answer has no attribute at all (an unusable answer);
 * a missing wing or pet is fine (null fields).
 */
export function mapNcCharacter(info: unknown, equipment: unknown): NcCharacterData | null {
  const statList = asObject(asObject(info)?.stat)?.statList;
  if (!Array.isArray(statList)) {
    return null;
  }
  const attributes: Record<string, number> = {};
  for (const entry of statList) {
    const stat = asObject(entry);
    const id = typeof stat?.type === "string" && Object.hasOwn(NC_STAT_IDS, stat.type) ? NC_STAT_IDS[stat.type] : undefined;
    const value = Number(stat?.value);
    if (id !== undefined && stat?.value !== null && stat?.value !== "" && Number.isInteger(value) && value >= 0 && value <= 1_000_000) {
      attributes[String(id)] = value;
    }
  }
  if (Object.keys(attributes).length === 0) {
    return null;
  }
  const profile = asObject(asObject(info)?.profile);
  const itemLevelStat = statList.map(asObject).find((st) => st?.type === "ItemLevel");
  const pcId = Number(profile?.pcId);
  const classId = Number.isInteger(pcId) && pcId >= 1 ? Math.floor((pcId - 1) / 4) : null;
  const gear: NcGear[] = [];
  for (const e of asArray(asObject(asObject(equipment)?.equipment)?.equipmentList)) {
    const slot = Number(e?.slotPos);
    const itemId = positiveInt(e?.id, 2_000_000_000);
    if (itemId && Number.isInteger(slot) && slot >= 0 && slot <= 255 && gear.length < 64) {
      // The game's own enchant number counts the exceed steps on top of the 15 normal ones.
      gear.push({ slot, itemId, enchant: Math.min(30, (nonNegativeInt(e?.enchantLevel, 30) ?? 0) + (nonNegativeInt(e?.exceedLevel, 30) ?? 0)) });
    }
  }
  const skills: NcSkill[] = [];
  for (const e of asArray(asObject(asObject(equipment)?.skill)?.skillList)) {
    const id = positiveInt(e?.id, 2_000_000_000);
    const level = nonNegativeInt(e?.skillLevel, 60);
    if (id && level !== null && Number(e?.acquired) === 1 && skills.length < 400) {
      // NC does not say how much of the level is a bonus: no bonus is shown.
      skills.push({ id, level, baseLevel: level, stigma: e?.category === "Dp", equipped: Number(e?.equip) === 1 });
    }
  }
  const boardCounts: { board: number; count: number }[] = [];
  for (const b of asArray(asObject(asObject(info)?.daevanion)?.boardList)) {
    const board = positiveInt(b?.id, 10_000);
    const count = nonNegativeInt(b?.openNodeCount, 1000);
    if (board && count && boardCounts.length < 12) {
      boardCounts.push({ board, count });
    }
  }
  const petwing = asObject(asObject(equipment)?.petwing);
  const pet = asObject(petwing?.pet);
  const activePet = positiveInt(pet?.id, 2_000_000_000);
  return {
    attributes,
    combatPower: positiveInt(profile?.combatPower, 100_000_000),
    itemLevel: positiveInt(itemLevelStat?.value, 100_000_000),
    guild: typeof profile?.regionName === "string" ? profile.regionName.replace(/<[^>]*>/g, "").slice(0, 60) : "",
    level: positiveInt(profile?.characterLevel, 200),
    classId: classId !== null && classId >= 1 && classId <= 9 ? classId : null,
    titleId: positiveInt(profile?.titleId, 2_000_000_000),
    gear,
    skills,
    boardCounts,
    wingId: positiveInt(asObject(petwing?.wing)?.id, 2_000_000_000),
    wingSkinId: positiveInt(asObject(petwing?.wingSkin)?.id, 2_000_000_000),
    activePet,
    activePetLevel: activePet ? positiveInt(pet?.level, 1000) : null,
  };
}
