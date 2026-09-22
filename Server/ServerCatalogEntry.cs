namespace AionDPS.Server;

/// <summary>One entry from the backend's curated GET /api/server-catalog list - name/version/kind
/// of a real Aion server (official or private), researched by hand rather than auto-discovered.
/// See backend/src/db/schema.ts's serverCatalog remarks for why this is separate from the
/// fingerprint-based identity in ServerIdentity.cs: that one is a technical value auto-detected
/// per install and used to keep uploads from mixing servers; this one is what a user picks to
/// declare which real server a character is on, before that character has ever uploaded anything.
/// <see cref="Game"/> is the backend's game token ("aion" | "aion2"); a backend from before the
/// field existed omits it, which deserializes to the default and can only mean classic Aion.</summary>
public sealed record ServerCatalogEntry(int Id, string Name, string Version, string Kind, string Game = "aion", string? Slug = null)
{
    public override string ToString() => $"{Name} ({Version})";
}
