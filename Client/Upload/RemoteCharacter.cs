using System.Net.Http;
using System.Text.Json;
using AionDPS.Aion2;

namespace AionDPS.Upload;

/// <summary>
/// What aiondps.com holds about a character (the profile the player's own client uploaded and/or the data NC's official page gave the server): the
/// equipment with enchants, stones and god stones, the attributes, wings and the active pet. The character window shows it for the other characters of the
/// account, whose own login the meter has not seen: the meter only knows their basics from the character list.
/// </summary>
public sealed record RemoteCharacter(
    string Name, int Level, string? ClassName, string? Faction, string? Guild, int ItemLevel, int CombatPower, double AverageItemLevel,
    IReadOnlyList<Aion2EquippedItem> Gear, IReadOnlyDictionary<int, int>? Attributes, int WingId, int WingSkinId, int ActivePetId, int ActivePetLevel,
    IReadOnlyList<Aion2TitleSlot> Titles, string? UpdatedAt,
    string? Slug = null, IReadOnlyList<Aion2SkillEntry>? Skills = null, IReadOnlyList<Aion2SpeciesKnowledge>? Species = null, IReadOnlyList<Aion2Pet>? Pets = null,
    int DaevanionPoints = 0);

/// <summary>The answer of a lookup: found, not on the server (yet), or the server could not be reached.</summary>
public sealed record RemoteLookup(RemoteLookupStatus Status, RemoteCharacter? Character = null);

public enum RemoteLookupStatus { Found, NotFound, Unreachable }

/// <summary>
/// Asks the server for a character by name and server (the "aion2:..." key the uploads are filed under), the same way the portrait is fetched: only when
/// the caller says the profile upload is on (the character is only on the server then), never redirected, and never throwing. The server is asked at
/// most once a minute per character.
/// </summary>
public static class RemoteCharacterClient
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly Dictionary<string, (DateTime At, RemoteLookup Result)> Cache = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    public static async Task<RemoteLookup> GetAsync(string name, string serverFingerprint)
    {
        string key = serverFingerprint + "/" + name.ToLowerInvariant();
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var known) && DateTime.UtcNow - known.At < TimeSpan.FromMinutes(1))
            {
                return known.Result;
            }
        }

        RemoteLookup result;
        try
        {
            result = await FetchAsync(name, serverFingerprint);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            result = new RemoteLookup(RemoteLookupStatus.Unreachable);
        }

        lock (Gate)
        {
            Cache[key] = (DateTime.UtcNow, result);
        }

        return result;
    }

    private static async Task<RemoteLookup> FetchAsync(string name, string serverFingerprint)
    {
        using JsonDocument search = JsonDocument.Parse(await Http.GetStringAsync($"{UploadClient.ApiBaseUrl}/api/players/search?q={Uri.EscapeDataString(name)}"));
        if (search.RootElement.ValueKind != JsonValueKind.Array)
        {
            return new RemoteLookup(RemoteLookupStatus.NotFound);
        }

        int? id = null;
        foreach (JsonElement row in search.RootElement.EnumerateArray())
        {
            if (row.TryGetProperty("serverFingerprint", out JsonElement fp) && fp.GetString() == serverFingerprint
                && row.TryGetProperty("name", out JsonElement n) && string.Equals(n.GetString(), name, StringComparison.OrdinalIgnoreCase)
                && row.TryGetProperty("id", out JsonElement i) && i.TryGetInt32(out int value))
            {
                id = value;
                break;
            }
        }

        if (id is null)
        {
            return new RemoteLookup(RemoteLookupStatus.NotFound);
        }

        using JsonDocument detail = JsonDocument.Parse(await Http.GetStringAsync($"{UploadClient.ApiBaseUrl}/api/players/{id}"));
        return Parse(detail.RootElement) is { } character ? new RemoteLookup(RemoteLookupStatus.Found, character) : new RemoteLookup(RemoteLookupStatus.NotFound);
    }

    /// <summary>Reads the answer of <c>GET /api/players/:id</c> (player, character, profile). The profile wins where both have a value, the character view
    /// (NC's page) fills the rest; null when the answer has no player.</summary>
    public static RemoteCharacter? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("player", out JsonElement player) || player.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        JsonElement character = Obj(root, "character"), profile = Obj(root, "profile");
        string name = Str(player, "name") ?? "";
        IReadOnlyDictionary<int, int>? attributes = Attributes(Obj(profile, "attributes")) ?? Attributes(Obj(character, "attributes"));
        var gear = new List<Aion2EquippedItem>();
        if (profile.ValueKind == JsonValueKind.Object && profile.TryGetProperty("gear", out JsonElement gearList) && gearList.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in gearList.EnumerateArray())
            {
                if (Int(item, "slot") is int slot && Int(item, "itemId") is int itemId and > 0)
                {
                    gear.Add(new Aion2EquippedItem(slot, itemId, Int(item, "enchant") ?? 0, Stones(item), Stats(item), Godstone(item), Bonuses(item)));
                }
            }
        }

        var titles = new List<Aion2TitleSlot>();
        if (profile.ValueKind == JsonValueKind.Object && profile.TryGetProperty("titles", out JsonElement titleList) && titleList.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement t in titleList.EnumerateArray())
            {
                if (Int(t, "slot") is int slot && Int(t, "titleId") is int titleId and > 0)
                {
                    titles.Add(new Aion2TitleSlot(slot, titleId));
                }
            }
        }

        JsonElement pet = Obj(profile, "activePet").ValueKind == JsonValueKind.Object ? Obj(profile, "activePet") : Obj(character, "activePet");
        return new RemoteCharacter(
            name,
            Int(profile, "level") ?? 0,
            Str(profile, "className"),
            Str(player, "faction") ?? Str(profile, "faction"),
            Str(player, "guild"),
            Int(character, "itemLevel") ?? Int(profile, "itemLevel") ?? 0,
            Int(character, "combatPower") ?? Int(profile, "combatPower") ?? 0,
            Dbl(profile, "averageItemLevel") ?? 0,
            gear,
            attributes,
            Int(Obj(profile, "wing"), "id") ?? Int(Obj(character, "wing"), "id") ?? 0,
            Int(Obj(profile, "wingSkin"), "id") ?? Int(Obj(character, "wingSkin"), "id") ?? 0,
            Int(pet, "id") ?? 0,
            Int(pet, "level") ?? 0,
            titles,
            Str(profile, "updatedAt"),
            Str(player, "slug"),
            Skills(profile),
            Species(profile),
            Pets(profile),
            DaevanionPoints(profile));
    }

    private static IEnumerable<JsonElement> Items(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray() : Enumerable.Empty<JsonElement>();

    private static bool Flag(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    /// <summary>The skills of the profile (id, level, base level, stigma, on the skill bar, the stones chosen).</summary>
    private static IReadOnlyList<Aion2SkillEntry> Skills(JsonElement profile)
    {
        var skills = new List<Aion2SkillEntry>();
        foreach (JsonElement e in Items(profile, "skills"))
        {
            if (Int(e, "id") is int id and > 0)
            {
                List<int>? stones = null;
                if (e.TryGetProperty("stones", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
                {
                    stones = list.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out _)).Select(x => x.GetInt32()).ToList();
                }

                skills.Add(new Aion2SkillEntry(id, Int(e, "level") ?? 0, Int(e, "baseLevel") ?? 0, Flag(e, "stigma"), Flag(e, "equipped"), stones));
            }
        }

        return skills;
    }

    /// <summary>The species knowledge of the pet window (own character only): level, progress and the effect slots.</summary>
    private static IReadOnlyList<Aion2SpeciesKnowledge> Species(JsonElement profile)
    {
        var species = new List<Aion2SpeciesKnowledge>();
        foreach (JsonElement e in Items(profile, "species"))
        {
            if (Int(e, "id") is not int id)
            {
                continue;
            }

            var effects = new List<Aion2SpeciesEffect>();
            foreach (JsonElement f in Items(e, "effects"))
            {
                if (Int(f, "page") is int page && Int(f, "slot") is int slot && Int(f, "stat") is int stat)
                {
                    effects.Add(new Aion2SpeciesEffect(page, slot, stat, (long)(Dbl(f, "value") ?? 0), Int(f, "kind") ?? 0));
                }
            }

            long progress = (long)(Dbl(e, "progress") ?? 0);
            species.Add(new Aion2SpeciesKnowledge(id, Int(e, "level") ?? 0, progress, effects));
        }

        return species;
    }

    /// <summary>The pet circles: species, level and the quality of each effect slot.</summary>
    private static IReadOnlyList<Aion2Pet> Pets(JsonElement profile)
    {
        var pets = new List<Aion2Pet>();
        foreach (JsonElement e in Items(profile, "pets"))
        {
            if (Int(e, "species") is int species)
            {
                var kinds = Items(e, "kinds").Where(k => k.ValueKind == JsonValueKind.Number && k.TryGetInt32(out _)).Select(k => k.GetInt32()).ToList();
                pets.Add(new Aion2Pet(species, Int(e, "level") ?? 0, kinds));
            }
        }

        return pets;
    }

    /// <summary>The activated Daevanion points over all boards (the server sends counts per board, not the single nodes).</summary>
    private static int DaevanionPoints(JsonElement profile) => Items(profile, "daevanion").Sum(b => Int(b, "activePoints") ?? 0);

    private static JsonElement Obj(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Object ? value : default;

    private static string? Str(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : null;

    private static double? Dbl(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    /// <summary>The attributes of a profile or character view: <c>main</c> and <c>lords</c>, each a list of <c>{ id, value }</c>; null when there are none.</summary>
    private static IReadOnlyDictionary<int, int>? Attributes(JsonElement attributes)
    {
        if (attributes.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var values = new Dictionary<int, int>();
        foreach (string group in new[] { "main", "lords" })
        {
            if (attributes.TryGetProperty(group, out JsonElement list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in list.EnumerateArray())
                {
                    if (Int(entry, "id") is int id && Int(entry, "value") is int value)
                    {
                        values[id] = value;
                    }
                }
            }
        }

        return values.Count > 0 ? values : null;
    }

    /// <summary>The god stone of a piece: the server sends it as an object with the item id (and the names); a plain number is accepted too.</summary>
    private static int Godstone(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("godstone", out JsonElement stone))
        {
            return 0;
        }

        return stone.ValueKind == JsonValueKind.Object ? Int(stone, "itemId") ?? 0 : stone.ValueKind == JsonValueKind.Number && stone.TryGetInt32(out int id) ? id : 0;
    }

    private static IReadOnlyList<Aion2Stone>? Stones(JsonElement item) => Array(item, "stones", e => Int(e, "stat") is int stat ? new Aion2Stone(stat, Int(e, "tier") ?? 0) : null);

    private static IReadOnlyList<Aion2RolledStat>? Stats(JsonElement item) =>
        Array(item, "stats", e => Int(e, "stat") is int stat && Dbl(e, "value") is double value ? new Aion2RolledStat(stat, (long)value) : null);

    private static IReadOnlyList<Aion2SkillBonus>? Bonuses(JsonElement item) => Array(item, "skillBonuses", e => Int(e, "skill") is int skill ? new Aion2SkillBonus(skill, Int(e, "level") ?? 1) : null);

    private static IReadOnlyList<T>? Array<T>(JsonElement item, string name, Func<JsonElement, T?> read) where T : class
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out JsonElement list) || list.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = new List<T>();
        foreach (JsonElement e in list.EnumerateArray())
        {
            if (read(e) is { } value)
            {
                values.Add(value);
            }
        }

        return values;
    }
}
