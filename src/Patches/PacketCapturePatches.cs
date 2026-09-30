using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppMoon.Forsaken;

namespace EnchantmentDetails.Patches;

/// <summary>
/// The value processors run on every packet in slot order, used or not. While a capture is open they record each
/// packet's Source and text and return a sentinel instead (docs/internal.md, "Hidden numbers").
/// </summary>
internal static class PacketCapture
{
    internal readonly record struct Packet(string Source, string Text);

    [ThreadStatic] private static List<Packet>? _packets;

    public static bool Active => _packets != null;

    public static void Begin() => _packets = new List<Packet>();

    public static List<Packet> End()
    {
        var list = _packets ?? new List<Packet>();
        _packets = null;
        return list;
    }

    public static string Sentinel(int index) => "\u0001ET" + index + "\u0001";

    internal static void Record(DescriptionPacket description, ref string __result)
    {
        var list = _packets;
        if (list == null) return;
        list.Add(new Packet(description.Source ?? "", __result ?? ""));
        __result = Sentinel(list.Count - 1);
    }
}

[HarmonyPatch(typeof(EnchantmentDescriptionExtension), nameof(EnchantmentDescriptionExtension.ProcessValue))]
internal static class ProcessValuePatch
{
    static void Postfix(DescriptionPacket description, ref string __result) => PacketCapture.Record(description, ref __result);
}

[HarmonyPatch(typeof(EnchantmentDescriptionExtension), nameof(EnchantmentDescriptionExtension.ProcessExalted))]
internal static class ProcessExaltedPatch
{
    static void Postfix(DescriptionPacket description, ref string __result) => PacketCapture.Record(description, ref __result);
}
