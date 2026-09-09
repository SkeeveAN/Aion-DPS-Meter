import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

interface SkillRow {
  id: number;
  name: string;
  icon?: string;
  class?: string;
  slot?: string;
  levels?: number[];
  de?: string;
  fr?: string;
}

// Mirrors the client's own Data/SkillDatabase.cs FindByLocalizedName - a skill name uploaded by an
// English/German/French client must resolve to the same icon regardless of which rank was cast
// (Chat.log always names the actual rank, e.g. "Cleave IV", while the collected dataset only
// carries the rank-I name - see assets/README.md).
const RANK_SUFFIX = /\s+[IVXLCDM]+$/;

let rows: SkillRow[] | null = null;

function load(): SkillRow[] {
  if (rows) {
    return rows;
  }
  const filePath = path.join(__dirname, "..", "data", "skills_multilang_4x.json");
  rows = JSON.parse(fs.readFileSync(filePath, "utf8"));
  return rows!;
}

/** Icon filename (e.g. "cbt_fi_wingblade_g1.png") for a skill name as uploaded, or undefined if unknown. */
export function resolveSkillIcon(skillName: string): string | undefined {
  const all = load();

  const exact = all.find((s) => s.name === skillName || s.de === skillName || s.fr === skillName);
  if (exact) {
    return exact.icon;
  }

  const baseName = skillName.replace(RANK_SUFFIX, "");
  const normalized = all.find(
    (s) =>
      s.name.replace(RANK_SUFFIX, "") === baseName ||
      (s.de && s.de.replace(RANK_SUFFIX, "") === baseName) ||
      (s.fr && s.fr.replace(RANK_SUFFIX, "") === baseName),
  );
  return normalized?.icon;
}
