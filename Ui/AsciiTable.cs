using System.Text;

namespace AionSniffer.Ui;

/// <summary>
/// Fixed-width text tables wrapped in a Markdown code fence, for pasting into Discord.
///
/// Discord does not render Markdown tables -- a "| Person | Item |" table arrives as the literal
/// pipes and dashes, one long unaligned line per row, which is what the loot export used to
/// produce. Inside a fenced block Discord switches to a monospace font and leaves the text alone,
/// so columns padded to a common width actually line up for everyone reading it.
/// </summary>
public static class AsciiTable
{
    /// <summary>
    /// <paramref name="rightAlign"/> marks the numeric columns; numbers read wrong when
    /// left-aligned, since the digit that matters no longer sits in the same place down the
    /// column. Returns an empty string for no rows, so callers keep their "nothing to copy"
    /// behaviour instead of pasting an empty frame.
    /// </summary>
    public static string Render(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, IReadOnlyList<bool> rightAlign)
    {
        if (rows.Count == 0)
        {
            return "";
        }

        var width = new int[headers.Count];
        for (int c = 0; c < headers.Count; c++)
        {
            width[c] = headers[c].Length;
            foreach (IReadOnlyList<string> row in rows)
            {
                width[c] = Math.Max(width[c], Cell(row, c).Length);
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("```");
        AppendRow(sb, headers, width, rightAlign);

        // A dashed rule rather than the Markdown "|---|" separator: inside a code fence nothing is
        // interpreted, so the line only has to look like a rule to a human.
        sb.AppendLine(string.Join("  ", width.Select(w => new string('-', w))));

        foreach (IReadOnlyList<string> row in rows)
        {
            AppendRow(sb, row, width, rightAlign);
        }

        sb.AppendLine("```");
        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, IReadOnlyList<string> row, int[] width, IReadOnlyList<bool> rightAlign)
    {
        var cells = new List<string>(width.Length);
        for (int c = 0; c < width.Length; c++)
        {
            string value = Cell(row, c);
            cells.Add(c < rightAlign.Count && rightAlign[c] ? value.PadLeft(width[c]) : value.PadRight(width[c]));
        }

        // Trailing padding on the last column is invisible but still bytes on the clipboard, and
        // Discord preserves it inside a fence.
        sb.AppendLine(string.Join("  ", cells).TrimEnd());
    }

    private static string Cell(IReadOnlyList<string> row, int index) => index < row.Count ? row[index] ?? "" : "";
}
