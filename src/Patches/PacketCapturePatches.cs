using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppMoon.Forsaken;

namespace EnchantTooltip.Patches;

/// <summary>
/// DescriptionDataProviderExtensions.ResolveDescription runs the value processor (EnchantmentDescriptionExtension
/// .ProcessValue / .ProcessExalted) on EVERY packet in slot order, packet k = placeholder {k}, before String.Format
/// drops the ones the template does not reference (loop @0x08B647F0, build 29466). While a capture is open these
/// postfixes record each packet's Source label and formatted text, and replace the text with a sentinel so the caller
/// can see which slots made it into the final string.
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
