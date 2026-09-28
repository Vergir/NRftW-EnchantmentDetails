using EnchantTooltip;
using System;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(EnchantTooltipMod), "EnchantTooltip", "0.5.0", "vergir")]
[assembly: MelonGame("Moon Studios", "NoRestForTheWicked")]
// MelonLoader would otherwise apply every [HarmonyPatch] in this assembly by itself, ignoring Enabled (and the INERT build).
[assembly: HarmonyDontPatchAll]

namespace EnchantTooltip;

/// <summary>
/// No Rest for the Wicked: show the possible range next to every rolled enchantment value,
/// e.g. "Damage increased by 7% (3-10)". Display only; the simulation is untouched.
/// </summary>
public class EnchantTooltipMod : MelonMod
{
    public static EnchantTooltipMod Instance { get; private set; } = null!;
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
        HarmonyInstance.PatchAll(typeof(EnchantTooltipMod).Assembly);
        LoggerInstance.Msg("Patches applied.");

        // After a hot reload the settings screens already exist; the Initialize postfix will not run for them.
        if (Prefs.AddSettingsRows.Value)
        {
            try { SettingsRows.AddToLiveScreens(); }
            catch (Exception e) { LoggerInstance.Warning("Adding rows to live settings screens: " + e.Message); }
        }
    }

    public override void OnUpdate()
    {
        if (Prefs.Enabled is null) return; // inert build / not initialised
        if (!Prefs.Enabled.Value || !Enum.TryParse(Prefs.ShowcaseKey.Value, true, out KeyCode key) || key == KeyCode.None) return;
        if (Input.GetKeyDown(key)) LoggerInstance.Msg(Showcase.Cycle() + " Re-hover the item to refresh.");
    }

    /// <summary>Unload / hot reload: take our rows back off the game's settings screens.</summary>
    public override void OnDeinitializeMelon()
    {
        try { SettingsRows.RemoveAll(); }
        catch (Exception e) { LoggerInstance.Warning("SettingsRows.RemoveAll: " + e.Message); }
    }
}
