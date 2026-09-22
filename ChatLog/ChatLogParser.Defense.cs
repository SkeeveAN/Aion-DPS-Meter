using System.Text.RegularExpressions;
using AionDPS.Combat.Sources;

namespace AionDPS.ChatLog;

/// <summary>
/// Avoided attacks and kills. Every pattern here was taken from real OriginAion Chat.logs (the
/// 2026-09 Sauro/Tahmes corpus - see Combat/SelfCheckDefense for the verbatim lines), English and
/// German only; the other six languages get nothing until a real log shows their wording, same
/// rule as the damage patterns. Results are collected per poll and drained by the combat source
/// into the batch, alongside the damage events Parse() returns.
/// </summary>
public sealed partial class ChatLogParser
{
    private readonly List<AvoidEvent> _pendingAvoids = new();
    private readonly List<KillEvent> _pendingKills = new();

    /// <summary>Local-player literals the defense/kill lines use as a bare subject ("You evaded",
    /// "Ihr pariert", "Your attack was blocked").</summary>
    private static readonly string[] DefenseSelfLiterals = { "You", "Your", "Ihr", "Euch", "Euer" };

    // ---- English --------------------------------------------------------------------------------
    // "You parried Kobold Peon's attack." / "Badigadi evaded Steel Rose Scout's attack." /
    // "You blocked Paladin's attack with the protective shield effect." / "Sardine evaded Ulgorn's Fang Strike."
    [GeneratedRegex(@"^(?<defender>.+?) (?<verb>evaded|parried|blocked) (?<attacker>.+?)'s (?:attack(?: with the protective shield effect)?|(?<skill>.+?))\.$")]
    private static partial Regex AvoidPattern();

    // "You resisted Steel Rose Scout's Poison Fluid."
    [GeneratedRegex(@"^(?<defender>.+?) resisted (?<attacker>.+?)'s (?<skill>.+?)\.$")]
    private static partial Regex ResistPattern();

    // "Ezzz's attack was blocked by the protective shield effect cast on Kiazaki."
    [GeneratedRegex(@"^(?:(?<attacker>Your)|(?<attacker>.+?)'s) attack was blocked by the protective shield effect cast on (?<defender>.+?)\.$")]
    private static partial Regex ShieldBlockPattern();

    // "You have defeated Xiaoeight."
    [GeneratedRegex(@"^You have defeated (?<victim>.+?)\.$")]
    private static partial Regex DefeatedPattern();

    // "Sniggy was killed by Ulgorn Raider's attack."
    [GeneratedRegex(@"^(?<victim>.+?) was killed by (?<killer>.+?)'s attack\.$")]
    private static partial Regex KilledByPattern();

    // "Ulgorn Raider has died."
    [GeneratedRegex(@"^(?<victim>.+?) has died\.$")]
    private static partial Regex DiedPattern();

    // ---- German ---------------------------------------------------------------------------------
    // "Ihr weicht dem Angriff von Troll-Beobachter aus."
    [GeneratedRegex(@"^(?<defender>.+?) weicht dem Angriff von (?<attacker>.+?) aus\.$")]
    private static partial Regex DodgePatternDe();

    // "Kiazaki pariert den Angriff von Söldner der Stahlrose."
    [GeneratedRegex(@"^(?<defender>.+?) pariert den Angriff von (?<attacker>.+?)\.$")]
    private static partial Regex ParryPatternDe();

    // "Ihr widersteht Eispfeil von Zauberer der Stahlrose."
    [GeneratedRegex(@"^(?<defender>.+?) widersteht (?<skill>.+?) von (?<attacker>.+?)\.$")]
    private static partial Regex ResistPatternDe();

    // "Ihr habt Rayth besiegt." / "Kiazaki hat Rayth im Abyss besiegt."
    [GeneratedRegex(@"^(?<killer>.+?) (?:habt|hat) (?<victim>.+?)(?: im .+?)? besiegt\.$")]
    private static partial Regex DefeatedPatternDe();

    // "Souky ist gestorben." / "Ihr seid gestorben."
    [GeneratedRegex(@"^(?<victim>.+?) (?:seid|ist) gestorben\.$")]
    private static partial Regex DiedPatternDe();

    public List<AvoidEvent> DrainAvoids()
    {
        var drained = new List<AvoidEvent>(_pendingAvoids);
        _pendingAvoids.Clear();
        return drained;
    }

    public List<KillEvent> DrainKills()
    {
        var drained = new List<KillEvent>(_pendingKills);
        _pendingKills.Clear();
        return drained;
    }

    /// <summary>True when the line was an avoided attack (and has been queued). Combat lines like
    /// these legitimately repeat within one second - two parries in a row are two parries.</summary>
    private bool TryParseAvoid(string message, DateTime timestamp)
    {
        Match m;
        if ((m = AvoidPattern().Match(message)).Success)
        {
            AvoidKind kind = m.Groups["verb"].Value switch { "parried" => AvoidKind.Parry, "blocked" => AvoidKind.Block, _ => AvoidKind.Dodge };
            QueueAvoid(timestamp, m.Groups["attacker"].Value, m.Groups["defender"].Value, kind, SkillOrNull(m));
            return true;
        }

        if ((m = ResistPattern().Match(message)).Success || (m = ResistPatternDe().Match(message)).Success)
        {
            QueueAvoid(timestamp, m.Groups["attacker"].Value, m.Groups["defender"].Value, AvoidKind.Resist, SkillOrNull(m));
            return true;
        }

        if ((m = ShieldBlockPattern().Match(message)).Success)
        {
            QueueAvoid(timestamp, m.Groups["attacker"].Value, m.Groups["defender"].Value, AvoidKind.Block, null);
            return true;
        }

        if ((m = DodgePatternDe().Match(message)).Success)
        {
            QueueAvoid(timestamp, m.Groups["attacker"].Value, m.Groups["defender"].Value, AvoidKind.Dodge, null);
            return true;
        }

        if ((m = ParryPatternDe().Match(message)).Success)
        {
            QueueAvoid(timestamp, m.Groups["attacker"].Value, m.Groups["defender"].Value, AvoidKind.Parry, null);
            return true;
        }

        return false;
    }

    /// <summary>Kill/death announcements. Whether the victim was a player is not knowable from the
    /// line itself, so VictimIsPlayer stays false here and the consumer decides by name.</summary>
    private void RaiseKillIfPresent(string message, DateTime timestamp)
    {
        Match m;
        if ((m = DefeatedPattern().Match(message)).Success)
        {
            _pendingKills.Add(new KillEvent(timestamp, Names.GetOrAssignId(YouName), CanonicalId(m.Groups["victim"].Value), false));
        }
        else if ((m = KilledByPattern().Match(message)).Success)
        {
            _pendingKills.Add(new KillEvent(timestamp, CanonicalId(m.Groups["killer"].Value), CanonicalId(m.Groups["victim"].Value), false));
        }
        else if ((m = DiedPattern().Match(message)).Success || (m = DiedPatternDe().Match(message)).Success)
        {
            _pendingKills.Add(new KillEvent(timestamp, null, CanonicalId(m.Groups["victim"].Value), false));
        }
        else if ((m = DefeatedPatternDe().Match(message)).Success)
        {
            _pendingKills.Add(new KillEvent(timestamp, CanonicalId(m.Groups["killer"].Value), CanonicalId(m.Groups["victim"].Value), false));
        }
    }

    private void QueueAvoid(DateTime timestamp, string attacker, string defender, AvoidKind kind, string? skill) =>
        _pendingAvoids.Add(new AvoidEvent(timestamp, CanonicalId(attacker), CanonicalId(defender), kind, skill));

    private static string? SkillOrNull(Match m) => m.Groups["skill"] is { Success: true, Value.Length: > 0 } g ? g.Value : null;

    /// <summary>"You"/"Your"/"Ihr"/… all mean the local player - same canonicalization the
    /// damage patterns apply through their LocalPlayerLiterals.</summary>
    private int CanonicalId(string name)
    {
        bool self = DefenseSelfLiterals.Any(literal => string.Equals(name, literal, StringComparison.OrdinalIgnoreCase));
        return Names.GetOrAssignId(self ? YouName : name);
    }
}
