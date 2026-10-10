// Character data (main attributes, lord values, wing, wing skin, active pet) from the official character page of NC.
// Pure mapping of the two JSON answers (info + equipment) onto our own profile format; the fetching lives in portraitService.ts.

/** NC's stat type -> the attribute id of our profile format (profile.ts: attributes). ItemLevel (= gear score) is deliberately not taken. */
export const NC_STAT_IDS: Record<string, number> = {
  STR: 1, DEX: 2, INT: 3, CON: 4, AGI: 5, WIS: 6,
  Justice: 7, Freedom: 8, Illusion: 9, Life: 10, Time: 11, Destruction: 13, Death: 14, Wisdom: 15, Destiny: 16, Space: 17,
};

export type NcCharacterData = {
  attributes: Record<string, number>;
  wingId: number | null;
  wingSkinId: number | null;
  activePet: number | null;
  activePetLevel: number | null;
};

const asObject = (v: unknown): Record<string, unknown> | null => (v && typeof v === "object" && !Array.isArray(v) ? (v as Record<string, unknown>) : null);

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
  const petwing = asObject(asObject(equipment)?.petwing);
  const pet = asObject(petwing?.pet);
  const activePet = positiveInt(pet?.id, 2_000_000_000);
  return {
    attributes,
    wingId: positiveInt(asObject(petwing?.wing)?.id, 2_000_000_000),
    wingSkinId: positiveInt(asObject(petwing?.wingSkin)?.id, 2_000_000_000),
    activePet,
    activePetLevel: activePet ? positiveInt(pet?.level, 1000) : null,
  };
}
