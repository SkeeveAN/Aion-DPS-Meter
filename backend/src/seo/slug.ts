const GERMAN_TRANSLITERATIONS: Record<string, string> = { ä: "ae", ö: "oe", ü: "ue", ß: "ss" };

/**
 * URL slug from a display name: lowercase ASCII words joined by "-". Umlauts become their
 * two-letter spellings (so "Sauro-Kriegsdepot" and "Stahlmauerbastion" stay readable), every other
 * diacritic is stripped. Returns "" for a name with no Latin letters at all (Cyrillic/CJK) - the
 * caller falls back to an id-based slug then, never guesses a transliteration.
 */
export function slugify(name: string): string {
  return name
    .toLowerCase()
    // "Tiamat's Fortress" → tiamats-fortress, not tiamat-s-fortress.
    .replace(/['’`]/g, "")
    .replace(/[äöüß]/g, (ch) => GERMAN_TRANSLITERATIONS[ch] ?? ch)
    .normalize("NFKD")
    .replace(/[̀-ͯ]/g, "")
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
}

/** First of base, base-2, base-3, … that isTaken() rejects nothing for. */
export function uniqueSlug(base: string, isTaken: (candidate: string) => boolean): string {
  if (!isTaken(base)) {
    return base;
  }
  for (let n = 2; ; n++) {
    const candidate = `${base}-${n}`;
    if (!isTaken(candidate)) {
      return candidate;
    }
  }
}

/** Numeric ids and slugs share one URL segment; a slug never consists of digits only. */
export function parseIdOrSlug(raw: string): { id: number } | { slug: string } | null {
  if (/^\d+$/.test(raw)) {
    return { id: Number(raw) };
  }
  if (/^[a-z0-9]+(-[a-z0-9]+)*$/.test(raw)) {
    return { slug: raw };
  }
  return null;
}
