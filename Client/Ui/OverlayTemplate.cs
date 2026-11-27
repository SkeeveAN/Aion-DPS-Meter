using System.Globalization;
using System.Text;

namespace AionDPS.Ui;

/// <summary>One mode of the look "Own" of the DPS overlay: a name, the template of a row and what the list is sorted by.</summary>
public sealed class OwnMode
{
    /// <summary>Everything behind the tab of a line stands flush at the right edge of the row (a block of its own), the rest stays at the left.</summary>
    public bool RightBlock { get; set; }

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "";

    /// <summary>The row as text: %VARIABLE%(options), \t for a tab stop, \n (or a line break) for a new line.</summary>
    public string Template { get; set; } = "";

    /// <summary>"Damage", "Dps", "Healing", "Taken" or "Power": the key the list is sorted by, highest first.</summary>
    public string SortBy { get; set; } = "Damage";

    public OwnMode Copy() => new() { Id = Id, Name = Name, Template = Template, SortBy = SortBy, RightBlock = RightBlock };
}

/// <summary>The variables of a template (English).</summary>
public enum TemplateVar { Rank, Name, Class, Faction, Level, Damage, Dps, Share, Taken, Heal, Power, Deaths }

/// <summary>A variable of a template with its options, and where it stands in the text.</summary>
public sealed class TemplateField
{
    public TemplateVar Var { get; init; }

    /// <summary>The name as it was written (kept, so the text of the template stays as the player typed it).</summary>
    public string Written { get; init; } = "";

    /// <summary>Width in characters (0 = as wide as the text); the text is filled up or cut to it.</summary>
    public int Width { get; set; }

    public bool Bold { get; set; }

    /// <summary>Font size in steps of one point from the normal size (-6 to +12).</summary>
    public int Size { get; set; }

    public bool Short { get; set; }

    /// <summary>The side the text stands on. Numbers stand on the right unless "l" says otherwise, so the numbers of all rows end under each other.</summary>
    public bool Right { get; set; }

    /// <summary>Whether the template said l or r itself (else numbers stand on the right).</summary>
    public bool SideSet { get; set; }

    /// <summary>An opening bracket written directly in front of the field, "(%DPS%)": it belongs to the field, so the width and the side count it in and the brackets stay at the number.</summary>
    public string Prefix { get; set; } = "";

    /// <summary>The closing bracket written directly behind the field.</summary>
    public string Suffix { get; set; } = "";

    /// <summary>Leaves out the symbol that belongs to the variable (the swords of the combat power, the arrow of the damage taken ...).</summary>
    public bool NoSymbol { get; set; }

    public int Start { get; init; }

    public int Length { get; init; }

    /// <summary>The text of the field as it is written into a template: %NAME%(16,b).</summary>
    public string ToText()
    {
        var options = new List<string>();
        if (Width > 0)
        {
            options.Add(Width.ToString(CultureInfo.InvariantCulture));
        }

        if (Bold)
        {
            options.Add("b");
        }

        if (Size != 0)
        {
            options.Add((Size > 0 ? "+" : "-") + Math.Abs(Size).ToString(CultureInfo.InvariantCulture));
        }

        if (Short)
        {
            options.Add("short");
        }

        if (OverlayTemplate.IsNumber(Var) ? !Right : Right)
        {
            options.Add(Right ? "r" : "l");
        }

        if (NoSymbol)
        {
            options.Add("n");
        }

        return "%" + Written + "%" + (options.Count > 0 ? "(" + string.Join(",", options) + ")" : "");
    }
}

/// <summary>A piece of a template: some text, a field, a tab stop or a line break.</summary>
public sealed class TemplateItem
{
    public string? Literal { get; init; }

    public TemplateField? Field { get; init; }

    public bool NewLine { get; init; }

    public bool Tab { get; init; }
}

/// <summary>What is wrong at a place of the template: the kind ("Variable", "Option", "Bracket") and what was meant.</summary>
public sealed record TemplateError(int Start, int Length, string Kind, string Arg);

public sealed class ParsedTemplate
{
    public List<TemplateItem> Items { get; } = new();

    public List<TemplateError> Errors { get; } = new();

    public IEnumerable<TemplateField> Fields => Items.Where(i => i.Field is not null).Select(i => i.Field!);

    /// <summary>The field the cursor stands in (or touches at its end), null between the fields.</summary>
    public TemplateField? FieldAt(int caret) => Fields.FirstOrDefault(f => caret > f.Start && caret <= f.Start + f.Length);
}

/// <summary>
/// The template of the look "Own" of the DPS overlay, read like a printf format: %NAME%(16,b)  %DPS%(short,r8) \t \n.
/// Options go in brackets behind the variable, separated by commas: a number is the width in characters, b bold, +2 / -1 the font size
/// in steps, long / short the number format (2.514.236 or 2,514 M; combat power 212.145 or 212,1 k), l / r the side the text stands on,
/// n no symbol. A variable that has a symbol brings it along (the swords of %POWER%, the arrow of %TAKEN%, the skull of %DEATHS%).
/// </summary>
public static class OverlayTemplate
{
    private static readonly Dictionary<string, TemplateVar> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RANK"] = TemplateVar.Rank,
        ["NAME"] = TemplateVar.Name,
        ["CLASS"] = TemplateVar.Class,
        ["FACTION"] = TemplateVar.Faction,
        ["LEVEL"] = TemplateVar.Level,
        ["DAMAGE"] = TemplateVar.Damage,
        ["DPS"] = TemplateVar.Dps,
        ["PERCENT"] = TemplateVar.Share,
        ["SHARE"] = TemplateVar.Share, // the older name, templates that already use it keep working
        ["TAKEN"] = TemplateVar.Taken,
        ["HEAL"] = TemplateVar.Heal,
        ["POWER"] = TemplateVar.Power,
        ["DEATHS"] = TemplateVar.Deaths,
    };

    /// <summary>Variables that show a number (they stand on the right, so the numbers of all rows end under each other).</summary>
    public static bool IsNumber(TemplateVar v) => v is not (TemplateVar.Name or TemplateVar.Class or TemplateVar.Faction);

    /// <summary>The variables as they are offered to the player (English in every language).</summary>
    public static readonly string[] Offered = { "RANK", "NAME", "CLASS", "FACTION", "LEVEL", "DAMAGE", "DPS", "PERCENT", "TAKEN", "HEAL", "POWER", "DEATHS" };

    /// <summary>The sort keys of a mode: the key as it is saved and the property of the row it means.</summary>
    public static readonly (string Key, string Property)[] SortKeys =
    {
        ("Damage", nameof(PlayerRow.Damage)), ("Dps", nameof(PlayerRow.Dps)), ("Healing", nameof(PlayerRow.Healing)),
        ("Taken", nameof(PlayerRow.DamageTaken)), ("Power", nameof(PlayerRow.CombatPower)),
    };

    public static string SortProperty(string key) => SortKeys.FirstOrDefault(k => k.Key == key).Property ?? nameof(PlayerRow.Damage);

    /// <summary>What a row without a mode (none made yet) shows: rank, name, DPS.</summary>
    public const string DefaultText = "%RANK%  %NAME%(16,b)  %DPS%(short,r8)";

    public static ParsedTemplate Parse(string? text)
    {
        var result = new ParsedTemplate();
        text ??= "";
        var literal = new StringBuilder();

        void Flush()
        {
            if (literal.Length > 0)
            {
                result.Items.Add(new TemplateItem { Literal = literal.ToString() });
                literal.Clear();
            }
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                continue;
            }

            if (c == '\n')
            {
                Flush();
                result.Items.Add(new TemplateItem { NewLine = true });
                continue;
            }

            if (c == '\\' && i + 1 < text.Length)
            {
                char next = text[i + 1];
                if (next == 'n')
                {
                    Flush();
                    result.Items.Add(new TemplateItem { NewLine = true });
                    i++;
                    continue;
                }

                if (next == 't')
                {
                    Flush();
                    result.Items.Add(new TemplateItem { Tab = true });
                    i++;
                    continue;
                }

                if (next == '\\')
                {
                    literal.Append('\\');
                    i++;
                    continue;
                }
            }

            if (c == '%')
            {
                int end = i + 1;
                while (end < text.Length && char.IsLetter(text[end]))
                {
                    end++;
                }

                if (end < text.Length && text[end] == '%' && end > i + 1)
                {
                    string prefix = "";
                    if (literal.Length > 0 && literal[^1] is '(' or '[')
                    {
                        prefix = literal[^1].ToString();
                        literal.Length--;
                    }

                    Flush();
                    string name = text[(i + 1)..end];
                    int fieldEnd = end + 1;
                    string? options = null;
                    int skip = 0;
                    if (fieldEnd < text.Length && text[fieldEnd] == '(')
                    {
                        int close = text.IndexOf(')', fieldEnd);
                        if (close < 0)
                        {
                            result.Errors.Add(new TemplateError(fieldEnd, text.Length - fieldEnd, "Bracket", ""));
                            fieldEnd = text.Length;
                        }
                        else
                        {
                            options = text[(fieldEnd + 1)..close];
                            fieldEnd = close + 1;
                        }
                    }

                    if (!Names.TryGetValue(name, out TemplateVar variable))
                    {
                        result.Errors.Add(new TemplateError(i, end + 1 - i, "Variable", name));
                    }
                    else
                    {
                        var field = new TemplateField { Var = variable, Written = name.ToUpperInvariant(), Start = i, Length = fieldEnd - i };
                        if (options is not null)
                        {
                            ReadOptions(field, options, i, fieldEnd - i, result.Errors);
                        }

                        if (!field.SideSet && IsNumber(variable))
                        {
                            field.Right = true;
                        }

                        field.Prefix = prefix;
                        if (prefix.Length > 0 && fieldEnd < text.Length && text[fieldEnd] == (prefix == "(" ? ')' : ']'))
                        {
                            field.Suffix = text[fieldEnd].ToString();
                            skip = 1;
                        }

                        result.Items.Add(new TemplateItem { Field = field });
                    }

                    if (!Names.ContainsKey(name))
                    {
                        literal.Append(prefix); // the bracket stays text when the variable is not known
                    }

                    i = fieldEnd - 1 + skip;
                    continue;
                }
            }

            literal.Append(c);
        }

        Flush();
        return result;
    }

    private static void ReadOptions(TemplateField field, string options, int start, int length, List<TemplateError> errors)
    {
        foreach (string raw in options.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string o = raw.ToLowerInvariant();
            if (int.TryParse(o, NumberStyles.None, CultureInfo.InvariantCulture, out int width) && width is >= 1 and <= 100)
            {
                field.Width = width;
            }
            else if (o is "b")
            {
                field.Bold = true;
            }
            else if (o.Length >= 2 && (o[0] is '+' or '-') && int.TryParse(o, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int step) && step is >= -6 and <= 12)
            {
                field.Size = step;
            }
            else if (o is "long")
            {
                field.Short = false;
            }
            else if (o is "short")
            {
                field.Short = true;
            }
            else if (o.Length >= 2 && (o[0] is 'l' or 'r') && int.TryParse(o[1..], NumberStyles.None, CultureInfo.InvariantCulture, out int sideWidth) && sideWidth is >= 1 and <= 100)
            {
                // "r8": the side and the width in one
                field.Right = o[0] == 'r';
                field.SideSet = true;
                field.Width = sideWidth;
            }
            else if (o is "l")
            {
                field.Right = false;
                field.SideSet = true;
            }
            else if (o is "r")
            {
                field.Right = true;
                field.SideSet = true;
            }
            else if (o is "n")
            {
                field.NoSymbol = true;
            }
            else
            {
                errors.Add(new TemplateError(start, length, "Option", raw));
            }
        }
    }

    // ---- values ----

    /// <summary>A number as the player asked: long (2.514.236) or short (2,514 M, 212,1 k).</summary>
    public static string Number(long value, bool shortForm)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        if (!shortForm)
        {
            return value.ToString("N0", culture);
        }

        long abs = Math.Abs(value);
        return abs >= 1_000_000_000 ? (value / 1e9).ToString("0.000", culture) + " B"
            : abs >= 1_000_000 ? (value / 1e6).ToString("0.000", culture) + " M"
            : abs >= 1_000 ? (value / 1e3).ToString("0.0", culture) + " k"
            : value.ToString("N0", culture);
    }

    /// <summary>The text of a field for a row.</summary>
    public static string ValueOf(TemplateField f, PlayerRow row)
    {
        string symbol(string s) => f.NoSymbol ? "" : s;
        switch (f.Var)
        {
            case TemplateVar.Rank:
                return row.Rank > 0 ? row.Rank.ToString(CultureInfo.CurrentCulture) : "";
            case TemplateVar.Name:
                return row.Name;
            case TemplateVar.Class:
                return ""; // the class is a picture, drawn by the row (the class icon)
            case TemplateVar.Faction:
                return ""; // the faction is a picture too (the emblem of Elyos or Asmodian), drawn by the row
            case TemplateVar.Level:
                return row.Level > 0 ? row.Level.ToString(CultureInfo.CurrentCulture) : "";
            case TemplateVar.Damage:
                return (row.IsEstimated ? "~" : "") + Number(row.Damage, f.Short);
            case TemplateVar.Dps: // the meter's own rate, by its rule: with a boss chosen the iDPS (the boss fight's time, the same for everyone), else the damage over the player's time from first to last hit
                return row.Dps is double d ? Number((long)Math.Round(d), f.Short) : "-";
            case TemplateVar.Share:
                return row.SharePercent.ToString("0.0", CultureInfo.CurrentCulture) + " %";
            case TemplateVar.Taken:
                return symbol("↓ ") + (row.DamageTaken > 0 ? Number(row.DamageTaken, f.Short) : "–");
            case TemplateVar.Heal:
                return symbol("+") + (row.Healing > 0 ? Number(row.Healing, f.Short) : "–");
            case TemplateVar.Power:
                return row.CombatPower is > 0 and int power ? symbol("⚔ ") + Number(power, f.Short) : "";
            case TemplateVar.Deaths:
                return row.Deaths > 0 ? symbol("☠ ") + row.Deaths.ToString(CultureInfo.CurrentCulture) : "";
            default:
                return "";
        }
    }
}
