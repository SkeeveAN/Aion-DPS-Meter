using System.Text.Json.Serialization;

namespace AionDPS.Game;

/// <summary>
/// Whether <see cref="MeterSettings.Game"/> is kept in sync with whichever client is actually
/// running (see <see cref="GameDetector"/>) or is a deliberate, fixed pick from Settings' Game
/// dropdown. Automatic is the default: per the user, Aion and Aion 2 should be told apart clearly,
/// but without having to remember to flip a dropdown every time the played game changes. Manual
/// exists for whoever wants Settings' pick to stick regardless of what happens to be running (e.g.
/// testing one game's UI while the other's client is open, or captures/replays run with neither
/// client open at all). Missing in settings files predating this feature defaults to Automatic --
/// harmless, since detection only ever moves <see cref="MeterSettings.Game"/> to match a client
/// that is actually running, never away from a correct manual pick with nothing else open.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GameDetectionMode>))]
public enum GameDetectionMode
{
    [JsonStringEnumMemberName("automatic")]
    Automatic,

    [JsonStringEnumMemberName("manual")]
    Manual,
}
