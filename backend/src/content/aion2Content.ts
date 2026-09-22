import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

// Aion 2 reference content as checked into src/data/aion2/*.json. Produced by
// scripts/derive-aion2-content.ts from a third-party dataset (facts only: names, ids, roles,
// trigger/severity), then hand-curated in place (action/detail prose, translations). Synced into
// the database by content/syncAion2.ts - see backend/README.md "Aion 2 Content".

export interface LocalizedText {
  en: string;
  ko?: string;
  zh?: string;
  de?: string;
  fr?: string;
}

export interface Aion2Instance {
  key: string;
  slug: string;
  name: LocalizedText;
  category: "expedition" | "transcendence" | "sanctuary" | "hideout" | "stronghold" | "awakening" | null;
  sortOrder: number;
}

export interface Aion2Boss {
  key: string;
  instanceKey: string;
  slug: string;
  name: LocalizedText;
  npcIds: number[];
  level: number | null;
  internalName: string | null;
  /** True when the source dataset documents mechanics for this boss (i.e. it is a real encounter, not a named elite). */
  hasMechanics: boolean;
}

export interface Aion2Mechanic {
  bossKey?: string;
  instanceKey?: string;
  key: string;
  sortOrder: number;
  trigger: { type: "phase" | "hp"; pct?: number; label: string };
  severity: "wipe" | "wipe_avoidable" | "mechanic";
  /** Our own wording - empty until a human writes it (never copied from the source dataset). */
  action: string;
  detail: string;
  positionSheet: unknown | null;
}

export interface Aion2Class {
  name: string;
  slug: string;
  abbreviation: string;
}

export interface Aion2Content {
  instances: Aion2Instance[];
  bosses: Aion2Boss[];
  mechanics: Aion2Mechanic[];
  classes: Aion2Class[];
}

const __dirname = path.dirname(fileURLToPath(import.meta.url));
export const AION2_DATA_DIR = path.join(__dirname, "..", "data", "aion2");

function readJson<T>(file: string, fallback: T): T {
  const full = path.join(AION2_DATA_DIR, file);
  if (!fs.existsSync(full)) {
    return fallback;
  }
  const parsed = JSON.parse(fs.readFileSync(full, "utf8")) as { items?: T } | T;
  return (parsed as { items?: T }).items ?? (parsed as T);
}

export function loadAion2Content(): Aion2Content {
  return {
    instances: readJson<Aion2Instance[]>("instances.json", []),
    bosses: readJson<Aion2Boss[]>("bosses.json", []),
    mechanics: readJson<Aion2Mechanic[]>("mechanics.json", []),
    classes: readJson<Aion2Class[]>("classes.json", []),
  };
}
