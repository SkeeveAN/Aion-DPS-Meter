using System.Text.Json.Serialization;

namespace AionDPS.Game;

/// <summary>
/// Which game a character/install belongs to. Serialized as the same lowercase strings the
/// backend uses everywhere ("aion" | "aion2" - see Backend/src/constants.ts), so settings files,
/// upload payloads and the server catalog all agree. A settings file from before this existed
/// simply lacks the field and deserializes to <see cref="Aion"/>, the only game the meter knew.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GameKind>))]
public enum GameKind
{
    [JsonStringEnumMemberName("aion")]
    Aion,

    [JsonStringEnumMemberName("aion2")]
    Aion2,
}

public static class GameKindExtensions
{
    /// <summary>The wire/URL token ("aion", "aion2").</summary>
    public static string ToToken(this GameKind game) => game == GameKind.Aion2 ? "aion2" : "aion";

    public static string DisplayName(this GameKind game) => game == GameKind.Aion2 ? "Aion 2" : "Aion";

    public static GameKind ParseToken(string? token) =>
        string.Equals(token, "aion2", StringComparison.OrdinalIgnoreCase) ? GameKind.Aion2 : GameKind.Aion;
}
