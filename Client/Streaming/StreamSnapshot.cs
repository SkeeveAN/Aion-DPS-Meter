using System.Text.Json;

namespace AionDPS.Streaming;

/// <summary>One line of the stream overlay: a player with the numbers the current view shows.</summary>
public sealed record StreamRowData(int Rank, string Name, string ClassName, double Bar, bool IsSelf, IReadOnlyList<string> Cells, string? Sub = null);

/// <summary>The boss that is being fought: its name and health.</summary>
public sealed record StreamBoss(string Name, double Percent, string Text);

/// <summary>The state the browser source draws (see <see cref="StreamOverlayPage"/>), as the JSON text the server sends. Pure, so a test can check it.</summary>
public static class StreamSnapshot
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string ToJson(string modeLabel, IReadOnlyList<string> headers, IReadOnlyList<StreamRowData> rows, StreamBoss? boss, int maxRows) =>
        JsonSerializer.Serialize(new
        {
            v = 1,
            modeLabel,
            headers,
            rows = rows.Take(Math.Clamp(maxRows, 1, 24)).Select(r => new { rank = r.Rank, name = r.Name, cls = r.ClassName, bar = Math.Clamp(r.Bar, 0, 1), self = r.IsSelf, cells = r.Cells, sub = r.Sub }),
            boss,
            maxRows,
        }, Options);

    /// <summary>Without names, everyone is shown by their class ("Cleric", "Cleric 2", ...), you too: what is streamed is never more than the uploads carry by default.</summary>
    public static IReadOnlyList<StreamRowData> HideNames(IReadOnlyList<StreamRowData> rows)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<StreamRowData>(rows.Count);
        foreach (StreamRowData row in rows)
        {
            string cls = string.IsNullOrEmpty(row.ClassName) ? "?" : row.ClassName;
            int n = seen[cls] = seen.GetValueOrDefault(cls) + 1;
            result.Add(row with { Name = n == 1 ? cls : cls + " " + n });
        }

        return result;
    }
}
