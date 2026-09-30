using EnchantmentDetails;
using System;
using MelonLoader;

[assembly: MelonInfo(typeof(EnchantmentDetailsMod), "Enchantment Details", "1.0.0", "vergir")]
[assembly: MelonGame("Moon Studios", "NoRestForTheWicked")]
// MelonLoader would otherwise apply every [HarmonyPatch] in this assembly by itself, ignoring Enabled (and the INERT build).
[assembly: HarmonyDontPatchAll]

namespace EnchantmentDetails;

/// <summary>
/// No Rest for the Wicked: roll ranges and the numbers the game leaves out, for every enchantment, gem and facet line.
/// Display only; the simulation is untouched. See docs/internal.md.
/// </summary>
public class EnchantmentDetailsMod : MelonMod
{
    public static EnchantmentDetailsMod Instance { get; private set; } = null!;
    public static MelonLogger.Instance Log => Instance.LoggerInstance;

    public override void OnInitializeMelon()
    {
        Instance = this;
#if INERT
        LoggerInstance.Msg("Inert build: no patches, the game's enchantment text is untouched.");
        return;
#endif
        Prefs.Init();
        if (!Prefs.Enabled.Value)
        {
            LoggerInstance.Msg("Disabled via preferences.");
            return;
        }

        HiddenNumbers.UnknownSource = (source, value) =>
            LoggerInstance.Msg($"Unhandled dropped number: Source '{source}' = '{value}' (please report)");
        HarmonyInstance.PatchAll(typeof(EnchantmentDetailsMod).Assembly);
        LoggerInstance.Msg("Patches applied.");

        // After a hot reload the settings screens already exist; the Initialize postfix will not run for them.
        if (Prefs.AddSettingsRows.Value)
        {
            try { SettingsRows.AddToLiveScreens(); }
            catch (Exception e) { LoggerInstance.Warning("Adding rows to live settings screens: " + e.Message); }
        }
    }


    /// <summary>Unload / hot reload: take our rows back off the game's settings screens.</summary>
    public override void OnDeinitializeMelon()
    {
        try { SettingsRows.RemoveAll(); }
        catch (Exception e) { LoggerInstance.Warning("SettingsRows.RemoveAll: " + e.Message); }
    }
}
