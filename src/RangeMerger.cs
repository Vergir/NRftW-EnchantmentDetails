using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace EnchantmentDetails;

/// <summary>
/// Pure string logic: zips the numbers of the rolled, best-roll and worst-roll renders of one line and appends the
/// range after every number that differs. Returns null on any structural mismatch (the game's text is kept).
/// </summary>
internal static class RangeMerger
{
    // Rich-text tags are literals (their hex colours hold digits); numbers may carry a sign, decimals and a '%'.
    private static readonly Regex Token = new(@"<[^>]*>|[+\-]?\d+(?:[.,]\d+)?%?", RegexOptions.Compiled);

    /// <param name="format">Placeholders: {value} {worst} {best} (unsigned, no '%') {roll} (roll = 0-100 quality of this enchantment).</param>
    public static string? Merge(string rolled, string best, string worst, string format, int rollPercent)
    {
        if (best == worst) return null; // nothing rolls on this line
        var r = Split(rolled);
        var b = Split(best);
        var w = Split(worst);
        if (r.Count != b.Count || r.Count != w.Count) return null;

        var sb = new StringBuilder(rolled.Length + 32);
        bool changed = false;
        for (int i = 0; i < r.Count; i++)
        {
            var (text, isNumber) = r[i];
            if (!isNumber)
            {
                // Literal text and tags must match exactly, else the three strings are not the same template.
                if (b[i].Text != text || w[i].Text != text || b[i].IsNumber || w[i].IsNumber) return null;
                sb.Append(text);
                continue;
            }
            if (!b[i].IsNumber || !w[i].IsNumber) return null;
            if (b[i].Text == w[i].Text)
            {
                sb.Append(text); // fixed value (duration, stack count...)
                continue;
            }
            sb.Append(format
                .Replace("{value}", text)
                .Replace("{worst}", Bare(w[i].Text))
                .Replace("{best}", Bare(b[i].Text))
                .Replace("{roll}", rollPercent.ToString()));
            changed = true;
        }
        return changed ? sb.ToString() : null;
    }

    /// <summary>Range ends are shown without sign and '%': "-6% (3-10)" instead of "-6% (-3%--10%)".</summary>
    private static string Bare(string number) => number.TrimStart('+', '-').TrimEnd('%');

    private static List<(string Text, bool IsNumber)> Split(string s)
    {
        var list = new List<(string, bool)>();
        int pos = 0;
        foreach (Match m in Token.Matches(s))
        {
            if (m.Index > pos) list.Add((s.Substring(pos, m.Index - pos), false));
            list.Add((m.Value, m.Value[0] != '<'));
            pos = m.Index + m.Length;
        }
        if (pos < s.Length) list.Add((s.Substring(pos), false));
        return list;
    }
}
