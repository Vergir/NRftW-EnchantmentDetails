using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EnchantTooltip;

/// <summary>What HiddenNumbers needs to know about an enchantment's ModifierData (read by ModifierInfoReader).</summary>
internal sealed class ModifierInfo
{
    public bool IsTrait;
    /// <summary>Stat names of the StatModifiers then ItemStatModifiers, in the order their packets are emitted.</summary>
    public List<string> StatNames = new();
    /// <summary>StatModifier stats that are per-second rates (HealthDrain, FocusDrain, HealthRegen).</summary>
    public HashSet<int> RateStatIndices = new();
    /// <summary>A PeriodicModifier ticking per distance moved (Proud Lance): its period and status are not shown.</summary>
    public bool HasDistancePeriod;
    /// <summary>ModifierData.EntityCondition, when it is a Ratio threshold strictly between 0 and 100%.</summary>
    public Condition? SelfCondition;
    /// <summary>Health/Focus/Stamina threshold on the event's or custom damage's TARGET (execute, "against Low Health").</summary>
    public Condition? TargetCondition;
    /// <summary>ApplyStatusOnSprintModifier.SprintDurationToActivate: seconds of sprinting before the status applies.</summary>
    public float? SprintSeconds;
    /// <summary>NearbyEnemiesScaler: the check radius (m), and the enemy count where the per-enemy curve stops growing
    /// (null for "no enemies nearby" style curves that fall to 0).</summary>
    public float? NearbyRadius;
    public int? NearbyMaxEnemies;
}

internal readonly record struct Condition(string Stat, bool LessThan, float Threshold);

/// <summary>
/// Pure logic (no game types): picks the packets the template dropped and turns them into a short suffix, e.g.
/// "1/s", "every 1s", "5s cooldown", "&lt;50%", "+20% Damage, +25% Attack Stamina Cost".
/// Rules agreed with the user (2026-09-27): no 60s+ debuff durations, no meaningless 0%/100% thresholds, no meters or
/// durations for distance-based periodic effects, trait values labelled by stat.
/// </summary>
internal static class HiddenNumbers
{
    private static readonly Regex Tags = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex ThresholdWord = new(@"\b(Low|High)\b", RegexOptions.Compiled);

    /// <summary>Packet Source labels this class does not handle, reported once each (hook for logging).</summary>
    public static readonly HashSet<string> UnknownSources = new();
    public static System.Action<string, string>? UnknownSource;

    /// <param name="packets">(Source, text) of every packet, packet k = placeholder {k}.</param>
    /// <param name="used">Whether packet k's text made it into the rendered line.</param>
    /// <param name="line">The rendered line (for the English "Low/High" keyword check).</param>
    public static string? Describe(IReadOnlyList<(string Source, string Text)> packets, IReadOnlyList<bool> used,
        ModifierInfo info, string line)
    {
        bool anyUsed = false;
        var usedTexts = new HashSet<string>();
        for (int k = 0; k < packets.Count; k++)
            if (used[k]) { anyUsed = true; usedTexts.Add(Plain(packets[k].Text)); }

        var parts = new List<string>();
        var seenDurations = new HashSet<string>();
        bool conditionPacketDropped = false;
        int statIndex = 0;
        for (int k = 0; k < packets.Count; k++)
        {
            var (source, text) = packets[k];
            bool isStat = source.Contains("StatModifier");
            int thisStat = isStat ? statIndex++ : -1;
            if (used[k]) continue;
            string value = Plain(text);
            if (value.Length == 0) continue;

            if (isStat)
            {
                // Lines with a {N} that carry extra stat packets are status "markers" (dummy Durability +1).
                if (anyUsed) continue;
                if (info.IsTrait && thisStat < info.StatNames.Count)
                    parts.Add(Signed(value) + " " + info.StatNames[thisStat]);
                else if (info.RateStatIndices.Contains(thisStat))
                    parts.Add(value + "/s");
                else
                    parts.Add(value);
            }
            else if (source.StartsWith("PayloadData") || source == "InterModifier" || source == "BarrierModifier"
                     || source.StartsWith("CustomDamage"))
            {
                // An execute's "100% of target Health" says nothing; its threshold is shown instead.
                if (anyUsed || info.TargetCondition != null) continue;
                parts.Add(value);
            }
            else if (source == "PeriodicModifier")
            {
                if (!info.HasDistancePeriod) parts.Add("every " + value + "s");
            }
            else if (source == "StatusDuration")
            {
                if (info.HasDistancePeriod) continue;
                if (double.TryParse(value.TrimEnd('s'), NumberStyles.Float, CultureInfo.InvariantCulture, out var secs) && secs >= 60) continue;
                if (usedTexts.Contains(value) || !seenDurations.Add(value)) continue; // same as a duration already in the text
                // In this build every dropped status duration left after the rules above is an internal cooldown
                // (Cinder & Stone's Furnace, Lacquered Bow's per-element Hunter's Mark lockout).
                parts.Add(value + "s cooldown");
            }
            else if (source == "DamageEventCondition")
            {
                parts.Add(value + " chance");
            }
            else if (source == "EntityStatsCondition")
            {
                conditionPacketDropped = true;
            }
            else if (UnknownSources.Add(source))
            {
                UnknownSource?.Invoke(source, value);
            }
        }

        if (info.SprintSeconds is { } sprint && sprint > 0)
            parts.Add($"after {Num(sprint)}s of sprinting");
        if (info.NearbyRadius is { } radius)
            parts.Add(info.NearbyMaxEnemies is { } max ? $"max {max}, within {Num(radius)}m" : $"within {Num(radius)}m");

        string plainLine = Plain(line);
        if (conditionPacketDropped && info.SelfCondition is { } self) parts.Add(ConditionText(self, plainLine));
        if (info.TargetCondition is { } target) parts.Add(ConditionText(target, plainLine));
        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    /// <summary>"&lt;50%" when the line already says Low/High Health etc., else the whole condition
    /// (the Attack Stamina Cost tradeoff never mentions that it only applies below 30% Focus).</summary>
    private static string ConditionText(Condition c, string line)
    {
        int pct = (int)System.Math.Round(c.Threshold * 100);
        if (ThresholdWord.IsMatch(line))
            return c.LessThan ? "<" + pct + "%" : pct + "%+";
        return c.LessThan ? $"only below {pct}% {c.Stat}" : $"only at {pct}%+ {c.Stat}";
    }

    private static string Num(float v) => System.Math.Round(v, 1).ToString(CultureInfo.InvariantCulture);

    private static string Plain(string s) => Tags.Replace(s, "").Trim();

    private static string Signed(string v) => v.Length > 0 && char.IsDigit(v[0]) ? "+" + v : v;
}
