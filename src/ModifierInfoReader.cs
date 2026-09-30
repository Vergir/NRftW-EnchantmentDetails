using System.Collections.Generic;
using System.Text.RegularExpressions;
using Il2CppQuantum;

namespace EnchantmentDetails;

/// <summary>Reads the structural facts HiddenNumbers needs from an enchantment's Quantum ModifierData.</summary>
internal static class ModifierInfoReader
{
    private const float RawOne = 65536f;
    private static readonly Regex CamelSplit = new("(?<=[a-z])(?=[A-Z])", RegexOptions.Compiled);

    // Stat labels where the enum name reads badly (the rest is the enum name split at capitals).
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["FocusGainOnHit"] = "Focus on Hit",
        ["FocusGainOnBlock"] = "Focus on Block",
        ["PoiseDamageOnBlock"] = "Poise on Block",
        ["StaminaRegen"] = "Stamina Recovery",
    };

    private static readonly HashSet<StatType> RateStats = new() { StatType.HealthDrain, StatType.FocusDrain, StatType.HealthRegen };

    public static ModifierInfo? Read(IAssetResolutionContext ctx, EnchantmentData data)
    {
        var md = ctx.FindAsset<ModifierData>(data.ModifierDataRef.Id);
        if (md == null) return null;

        var info = new ModifierInfo { IsTrait = data.Type == EnchantmentType.Trait };
        info.SelfCondition = RatioCondition(md.EntityCondition);
        ReadNearbyScaler(ctx, md, info);

        var itemStats = new List<string>();
        var itemSigns = new List<int>();
        var mods = md.Modifiers;
        if (mods != null)
        {
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                if (m == null) continue;
                var stat = m.TryCast<StatModifier>();
                if (stat != null)
                {
                    if (RateStats.Contains(stat.StatType)) info.RateStatIndices.Add(info.StatNames.Count);
                    info.StatNames.Add(Label(stat.StatType.ToString()));
                    info.StatSigns.Add(Sign(stat.ScalingData) * Flip(stat.StatType.ToString()));
                    continue;
                }
                var itemStat = m.TryCast<ItemStatModifier>();
                if (itemStat != null)
                {
                    itemStats.Add(Label(itemStat.StatType.ToString()));
                    itemSigns.Add(Sign(itemStat.ScalingData) * Flip(itemStat.StatType.ToString()));
                    continue;
                }
                var periodic = m.TryCast<PeriodicModifier>();
                if (periodic != null)
                {
                    if (periodic.PeriodType == PeriodType.Distance) info.HasDistancePeriod = true;
                    continue;
                }
                var evt = m.TryCast<EventEffectModifier>();
                var damageEvent = evt?.Event?.TryCast<ModifierDamageEvent>();
                if (damageEvent != null)
                {
                    info.TargetCondition ??= FirstRatioCondition(damageEvent.MetaConditions.TargetConditions);
                    continue;
                }
                var infusion = m.TryCast<DamageSchoolOverrideModifier>();
                if (infusion != null)
                {
                    info.Infusion = SchoolName(infusion.DamageSchool) + " Infusion";
                    continue;
                }
                var sprint = m.TryCast<ApplyStatusOnSprintModifier>();
                if (sprint != null)
                {
                    info.SprintSeconds = sprint.SprintDurationToActivate.RawValue / RawOne;
                    continue;
                }
                var custom = m.TryCast<CustomDamageModifier>();
                if (custom != null)
                    info.TargetCondition ??= FirstRatioCondition(custom.MetaConditions.TargetConditions);
            }
        }
        // ExtractDescriptionData emits all StatModifier packets, then all ItemStatModifier packets.
        info.StatNames.AddRange(itemStats);
        info.StatSigns.AddRange(itemSigns);
        return info;
    }

    /// <summary>"for each Nearby Enemy": radius from BalanceConfig, cap = the scaler curve's end value.</summary>
    private static void ReadNearbyScaler(IAssetResolutionContext ctx, ModifierData md, ModifierInfo info)
    {
        var scalers = md.CustomScalerData.Collection;
        if (scalers == null) return;
        for (int i = 0; i < scalers.Count; i++)
        {
            var nearby = scalers[i]?.TryCast<NearbyEnemiesScaler>();
            if (nearby == null) continue;
            var config = ctx.FindAsset<BalanceConfigData>(BalanceConfigAccess.BalanceConfigData.Id);
            if (config == null) return;
            float r2 = config.Modifiers.NearbyEnemiesScaler.CheckRadiusSquared.RawValue / RawOne;
            info.NearbyRadius = (float)System.Math.Sqrt(r2);
            var curve = nearby.Scaling;
            if (curve != null)
            {
                float atEnd = curve.Evaluate(curve.EndTime).RawValue / RawOne;
                if (atEnd > 1f) info.NearbyMaxEnemies = (int)System.Math.Round(atEnd);
            }
            return;
        }
    }

    /// <summary>Sign of a modifier's value at level 0 (traits and enchant stats never change sign with level).</summary>
    private static int Sign(ScalingData data)
    {
        var curve = data.Scaling;
        if (curve == null) return 0;
        long raw = curve.Evaluate(new Il2CppPhoton.Deterministic.FP { RawValue = 0 }).RawValue;
        return raw < 0 ? -1 : raw > 0 ? 1 : 0;
    }

    private static Condition? FirstRatioCondition(Il2CppSystem.Collections.Generic.List<EntityCondition>? list)
    {
        if (list == null) return null;
        for (int i = 0; i < list.Count; i++)
            if (RatioCondition(list[i]) is { } c) return c;
        return null;
    }

    /// <summary>Health/Focus/Stamina ratio thresholds strictly between 0 and 100%; others add nothing.</summary>
    private static Condition? RatioCondition(EntityCondition? condition)
    {
        var c = condition?.TryCast<EntityStatsCondition>();
        if (c == null || c.AmountType != AmountType.Ratio) return null;
        float t = c.Threshold.RawValue / RawOne;
        if (t <= 0f || t >= 1f) return null;
        string? stat = c.Type switch
        {
            EntityStatsCurrentValueType.Health => "Health",
            EntityStatsCurrentValueType.Focus => "Focus",
            EntityStatsCurrentValueType.Stamina => "Stamina",
            _ => null,
        };
        return stat == null ? null : new Condition(stat, c.ComparisonType == ComparisonType.LessThan, t);
    }

    /// <summary>"XDamageTaken" reads as the game says it, "X Resistance", with the sign flipped (-10% taken = +10%).</summary>
    internal static string Label(string enumName)
    {
        if (Labels.TryGetValue(enumName, out var l)) return l;
        if (enumName.EndsWith("DamageTaken"))
        {
            string school = CamelSplit.Replace(enumName.Substring(0, enumName.Length - "DamageTaken".Length), " ");
            return school.Length == 0 ? "Damage Resistance" : school + " Resistance";
        }
        return CamelSplit.Replace(enumName, " ");
    }

    /// <summary>The game's player-facing school names (the enum says Heat / Cold / Electric).</summary>
    private static string SchoolName(DamageSchool school) => school switch
    {
        DamageSchool.Heat => "Fire",
        DamageSchool.Cold => "Ice",
        DamageSchool.Electric => "Lightning",
        _ => school.ToString(),
    };

    private static int Flip(string enumName) => enumName.EndsWith("DamageTaken") ? -1 : 1;
}
