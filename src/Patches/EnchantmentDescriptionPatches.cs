using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppMoon.Forsaken;
using Il2CppPhoton.Deterministic;
using Il2CppQuantum;

namespace EnchantmentDetails.Patches;

/// <summary>
/// Postfix on the private GetDescription hub every enchantment line goes through: adds the roll range (RangeMerger)
/// and the numbers the text leaves out (HiddenNumbers). See docs/internal.md.
/// </summary>
[HarmonyPatch]
internal static class GetDescriptionPatch
{
    private const long RawOne = 65536; // FP.RAW_ONE
    [ThreadStatic] private static bool _inside;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(EnchantmentDescriptionExtension), nameof(EnchantmentDescriptionExtension.GetDescription),
            new[] { typeof(IAssetResolutionContext), typeof(EnchantmentData), typeof(ScalingMeta), typeof(bool), typeof(bool) });

    static void Postfix(IAssetResolutionContext f, EnchantmentData enchantmentData, ScalingMeta scalingMeta,
        bool extractRanges, bool isExalted, ref EnchantmentDescriptionPiece __result)
    {
        // extractRanges=true callers (gem tooltips, reroll screen) print the game's own range: hidden numbers only.
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
                    EnchantmentDetailsMod.Log.Msg($"interval={scalingMeta.Interval.RawValue} roll={roll}%\n  rolled: {rolled}\n  best:   {best}\n  worst:  {worst}\n  out:    {merged ?? "(unchanged)"}");
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
            EnchantmentDetailsMod.Log.Warning("GetDescription postfix: " + e);
        }
        finally
        {
            _inside = false;
        }
    }

    /// <summary>Probe render with every packet replaced by a sentinel: missing sentinels are the dropped numbers.</summary>
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
            EnchantmentDetailsMod.Log.Msg($"hidden: {rolled}\n  packets: {dump}\n  used: {string.Join(",", used)}\n  extra: {extra ?? "(none)"}");
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

    /// <summary>Interval 0 is the best roll, 1 the worst.</summary>
    private static int RollPercent(long intervalRaw)
    {
        if (intervalRaw < 0) intervalRaw = 0;
        if (intervalRaw > RawOne) intervalRaw = RawOne;
        return (int)System.Math.Round(100.0 * (RawOne - intervalRaw) / RawOne);
    }
}

