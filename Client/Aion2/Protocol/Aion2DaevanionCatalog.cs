using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>One Daevanion board node: where it sits and what it gives. <see cref="Key"/> is a stat
/// token (HPMax, Critical ...) for Stat nodes, the skill id for SkillLevel nodes.</summary>
public sealed record Aion2DaevanionNode(int Id, int Board, int Row, int Col, string Grade, string Type, string Key, int Value);

/// <summary>
/// Daevanion node table (assets/aion2/daevanion/nodes.json, see its README). The login packet only
/// lists activated node ids per board; this turns them into "+100 HP max" / "+1 Rending Blow".
/// </summary>
public static class Aion2DaevanionCatalog
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "daevanion", "nodes.json");
    private static IReadOnlyDictionary<int, Aion2DaevanionNode>? _nodes;
    private static IReadOnlyDictionary<int, (string Name, string ClassName)>? _boards;

    private static void EnsureLoaded()
    {
        if (_nodes is not null)
        {
            return;
        }

        var nodes = new Dictionary<int, Aion2DaevanionNode>();
        var boards = new Dictionary<int, (string, string)>();
        try
        {
            if (File.Exists(FilePath))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                foreach (JsonProperty b in doc.RootElement.GetProperty("boards").EnumerateObject())
                {
                    boards[int.Parse(b.Name)] = (b.Value[0].GetString() ?? "", b.Value[1].GetString() ?? "");
                }

                foreach (JsonProperty n in doc.RootElement.GetProperty("nodes").EnumerateObject())
                {
                    JsonElement v = n.Value;
                    int id = int.Parse(n.Name);
                    nodes[id] = new Aion2DaevanionNode(id, v[0].GetInt32(), v[1].GetInt32(), v[2].GetInt32(), v[3].GetString() ?? "", v[4].GetString() ?? "", v[5].GetString() ?? "", v[6].ValueKind == JsonValueKind.Number ? v[6].GetInt32() : 0);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            // Missing or broken table: boards show only their activated-node counts.
        }

        _boards = boards;
        _nodes = nodes;
    }

    public static Aion2DaevanionNode? Find(int nodeId)
    {
        EnsureLoaded();
        return _nodes!.GetValueOrDefault(nodeId);
    }

    /// <summary>What one board's activated nodes add up to: stat totals by token, and skill-level
    /// bonuses by skill id. <c>ActiveNodes</c> counts every activated node except the start node;
    /// <c>KnownNodes</c> is how many of them the node table describes - the table lacks some real
    /// nodes, so the totals are a lower bound.</summary>
    public sealed record BoardSummary(int BoardId, string Name, int ActiveNodes, int KnownNodes, IReadOnlyDictionary<string, int> Stats, IReadOnlyDictionary<int, int> SkillBonuses);

    public static BoardSummary Summarize(int boardId, IEnumerable<int> activeNodeIds)
    {
        var stats = new Dictionary<string, int>();
        var skills = new Dictionary<int, int>();
        int active = 0;
        int known = 0;
        foreach (int id in activeNodeIds)
        {
            Aion2DaevanionNode? node = Find(id);
            if (node is { Type: "Start" })
            {
                continue;
            }

            active++;
            if (node is null)
            {
                continue;
            }

            known++;
            if (node.Type == "SkillLevel" && int.TryParse(node.Key, out int skillId))
            {
                skills[skillId] = skills.GetValueOrDefault(skillId) + node.Value;
            }
            else if (node.Type == "Stat")
            {
                stats[node.Key] = stats.GetValueOrDefault(node.Key) + node.Value;
            }
        }

        return new BoardSummary(boardId, BoardName(boardId), active, known, stats, skills);
    }

    /// <summary>Every node of one board (the unlocked ones and the rest), for drawing its map.</summary>
    public static IReadOnlyList<Aion2DaevanionNode> NodesOfBoard(int boardId)
    {
        EnsureLoaded();
        return _nodes!.Values.Where(n => n.Board == boardId).ToList();
    }

    public static string BoardName(int boardId)
    {
        EnsureLoaded();
        return _boards!.TryGetValue(boardId, out var b) ? b.Name : $"Board {boardId}";
    }
}
