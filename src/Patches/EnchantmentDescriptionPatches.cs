using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppMoon.Forsaken;
using Il2CppPhoton.Deterministic;
using Il2CppQuantum;

namespace EnchantTooltip.Patches;

/// <summary>
/// The private GetDescription(ctx, data, scalingMeta, extractRanges, isExalted) is the hub every enchantment line goes
/// through: all public overloads (item tooltip, stored enchantments, enchant pickers), the Radiant Ember boost preview
/// and the exalt pop-ups call it (checked with tools/xref.py, build 29466).
/// The postfix renders the line twice more with ScalingMeta.Interval forced to 0 (best roll) and 1 (worst roll) and
/// lets RangeMerger put the range next to each rolled number.
/// </summary>
[HarmonyPatch]
internal static class GetDescriptionPatch
{
    private const long RawOne = 65536; // FP.RAW_ONE
    [ThreadStatic] private static bool _inside;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(EnchantmentDescriptionExtension), nameof(EnchantmentDescriptionExtension.GetDescription),
            new[] { typeof(IAssetResolutionContext), typeof(EnchantmentData), typeof(ScalingMeta), typeof(bool), typeof(bool) });

    /// <summary>Showcase (F10): render another enchantment that could roll in this line's place (see Showcase).
    /// The real line's roll and exalt level are kept; one showcase set exalts one line x4.</summary>
    static void Prefix(IAssetResolutionContext f, ref EnchantmentData enchantmentData, ref ScalingMeta scalingMeta,
        bool extractRanges, ref bool isExalted)
    {
        if (_inside || extractRanges || !Showcase.Active || !Prefs.Enabled.Value || enchantmentData == null) return;
        try
        {
            var swap = Showcase.Swap(f, enchantmentData, out bool exalt);
            if (swap == null) return;
            enchantmentData = swap;
            if (exalt)
            {
                scalingMeta.Level = new FP { RawValue = Showcase.ExaltStacks * RawOne };
                isExalted = true;
            }
        }
        catch (Exception e)
        {
            EnchantTooltipMod.Log.Warning("Showcase swap: " + e.Message);
        }
    }

    static void Postfix(IAssetResolutionContext f, EnchantmentData enchantmentData, ScalingMeta scalingMeta,
        bool extractRanges, bool isExalted, ref EnchantmentDescriptionPiece __result)
    {
        // extractRanges=true callers (gem tooltips, the enchant reroll screen) already show the game's own "max-min"
        // text: no range merge there, but hidden numbers still apply (e.g. Black Pearl's "at Low Health" = <50%).
        if (_inside || !Prefs.Enabled.Value) return;
        var rolled = __result.Description;
        if (string.IsNullOrEmpty(rolled)) return;

        _inside = true;
        try
        {
            string text = rolled;
            if (Prefs.ShowRanges.Value && !extractRanges)
            {
                string best = Render(f, enchantmentData, WithInterval(scalingMeta, 0), isExalted);
                string worst = Render(f, enchantmentData, WithInterval(scalingMeta, RawOne), isExalted);
                int roll = RollPercent(scalingMeta.Interval.RawValue);
                var merged = RangeMerger.Merge(rolled, best, worst, Prefs.Format.Value, roll);
                if (Prefs.Debug.Value)
                    EnchantTooltipMod.Log.Msg($"interval={scalingMeta.Interval.RawValue} roll={roll}%\n  rolled: {rolled}\n  best:   {best}\n  worst:  {worst}\n  out:    {merged ?? "(unchanged)"}");
                if (merged != null) text = merged;
            }
            bool isFacet = enchantmentData.Type == EnchantmentType.Trait;
            if (isFacet ? Prefs.ShowFacetNumbers.Value : Prefs.ShowDetailedInfo.Value)
            {
                var extra = DescribeHidden(f, enchantmentData, scalingMeta, isExalted, rolled);
                if (extra != null) text += Prefs.HiddenFormat.Value.Replace("{extra}", extra);
            }
            if (!ReferenceEquals(text, rolled)) __result.Description = text;
        }
        catch (Exception e)
        {
            EnchantTooltipMod.Log.Warning("GetDescription postfix: " + e);
        }
        finally
        {
            _inside = false;
        }
    }

    /// <summary>One more render with every packet replaced by a sentinel (PacketCapture): sentinels missing from the
    /// output are the numbers the template dropped. Language independent, no need to read the localized template.</summary>
    private static string? DescribeHidden(IAssetResolutionContext f, EnchantmentData data, ScalingMeta meta, bool isExalted, string rolled)
    {
        string probe;
        List<PacketCapture.Packet> packets;
        PacketCapture.Begin();
        try { probe = Render(f, data, meta, isExalted); }
        finally { packets = PacketCapture.End(); }
        if (packets.Count == 0) return null;

        // No early exit when every packet is used: sprint time and nearby radius are not packets at all.
        var used = new bool[packets.Count];
        for (int k = 0; k < packets.Count; k++)
            used[k] = probe.Contains(PacketCapture.Sentinel(k));

        var info = ModifierInfoReader.Read(f, data);
        if (info == null) return null;
        var list = packets.ConvertAll(p => (p.Source, p.Text));
        var extra = HiddenNumbers.Describe(list, used, info, rolled);
        if (Prefs.Debug.Value)
        {
            var dump = string.Join(" | ", list.ConvertAll(p => p.Source + "=" + p.Text));
            EnchantTooltipMod.Log.Msg($"hidden: {rolled}\n  packets: {dump}\n  used: {string.Join(",", used)}\n  extra: {extra ?? "(none)"}");
        }
        return extra;
    }

    private static ScalingMeta WithInterval(ScalingMeta meta, long intervalRaw)
    {
        meta.Interval = new FP { RawValue = intervalRaw };
        return meta;
    }

    private static string Render(IAssetResolutionContext f, EnchantmentData data, ScalingMeta meta, bool isExalted) =>
        EnchantmentDescriptionExtension.GetDescription(f, data, meta, false, isExalted).Description ?? "";

    /// <summary>Value = base * (1 - weight * interval): interval 0 is the best roll, 1 the worst (see root README).</summary>
    private static int RollPercent(long intervalRaw)
    {
        if (intervalRaw < 0) intervalRaw = 0;
        if (intervalRaw > RawOne) intervalRaw = RawOne;
        return (int)System.Math.Round(100.0 * (RawOne - intervalRaw) / RawOne);
    }
}

