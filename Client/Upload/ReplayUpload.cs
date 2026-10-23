using AionDPS.Combat;

namespace AionDPS.Upload;

/// <summary>One path for the replay: the player, boss or other monster, and where it was (centimetres) at moments in milliseconds after <see cref="StartedAt"/> (UTC).</summary>
public sealed record ReplayTrackUpload(string Kind, string Name, string? ClassName, bool IsSelf, DateTime StartedAt, IReadOnlyList<int> T, IReadOnlyList<int> X, IReadOnlyList<int> Y);

/// <summary>The "Upload for the replay": the paths of one boss fight. <see cref="EncounterId"/> is the fight on the server as the boss upload answered it.</summary>
public sealed record ReplayUploadRequest(
    string ClientVersion,
    string ServerFingerprint,
    int? EncounterId,
    int? BossNpcId,
    DateTime StartedAt,
    DateTime EndedAt,
    IReadOnlyList<ReplayTrackUpload> Tracks,
    string Game = "aion2");

/// <summary>A path as the server hands it out, the paths of every client of the fight merged: <see cref="StartMs"/> is the first point's time (UTC, ms since 1970), the times count from it.</summary>
public sealed record ServerReplayTrack(string Kind, string Name, string? ClassName, long StartMs, int[] T, int[] X, int[] Y, int Sources);

/// <summary>Turns the replay map's data into the upload.</summary>
public static class ReplayUploadBuilder
{
    /// <summary>The most points one path carries (the backend takes 6,000).</summary>
    public const int MaxPoints = 4000;

    /// <summary>Readings closer in time than this are one for the upload.</summary>
    private const int MinGapMs = 500;

    /// <summary>
    /// The paths of the replay. The own character's path and the monsters' always go; another player's path only with <paramref name="includeOthers"/> (the switch
    /// "data of other players" of the settings), so without it nothing of anybody else's position leaves the PC.
    /// </summary>
    public static IReadOnlyList<ReplayTrackUpload> Tracks(ReplayData data, bool includeOthers)
    {
        var list = new List<ReplayTrackUpload>();
        foreach (ReplayEntity e in data.Entities)
        {
            if (e.Role == ReplayRole.Player && !e.IsSelf && !includeOthers)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(e.Name) || e.Track.T.Length == 0)
            {
                continue;
            }

            var t = new List<int>();
            var x = new List<int>();
            var y = new List<int>();
            for (int i = 0; i < e.Track.T.Length && t.Count < MaxPoints; i++)
            {
                if (t.Count > 0 && e.Track.T[i] - t[^1] < MinGapMs)
                {
                    continue;
                }

                t.Add(e.Track.T[i]);
                x.Add(e.Track.X[i]);
                y.Add(e.Track.Y[i]);
            }

            list.Add(new ReplayTrackUpload(e.Role switch { ReplayRole.Player => "player", ReplayRole.Add => "add", _ => "boss" }, e.Name, e.Role == ReplayRole.Player ? e.ClassName : null, e.IsSelf,
                e.Track.Start.ToUniversalTime(), t, x, y));
        }

        return list;
    }
}
