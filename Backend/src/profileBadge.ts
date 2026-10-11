/**
 * What the small profile icon behind a player's name shows (see profileIcon in the web frontend):
 * where the profile came from and whether NC's official character page added data to it.
 * "self": the player uploaded it himself; "seen": taken from another player's client (or only from NC);
 * null: no profile, so no icon.
 */
export type ProfileKind = "self" | "seen" | null;

export interface ProfileBadge {
  profileKind: ProfileKind;
  profileNc: boolean;
}

export function profileBadge(source: string | null, ncCharacterId: string | null, ncAttributesJson: string | null): ProfileBadge {
  if (source === null) {
    return { profileKind: null, profileNc: false };
  }
  // A profile built from NC alone is a "seen" one by definition; otherwise NC data counts once a character was found and fetched.
  const fetched = ncCharacterId !== null && ncAttributesJson !== null && ncAttributesJson !== "{}";
  return { profileKind: source === "self" ? "self" : "seen", profileNc: source === "nc" || fetched };
}
