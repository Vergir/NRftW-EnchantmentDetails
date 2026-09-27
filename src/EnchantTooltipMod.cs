using EnchantTooltip;
using MelonLoader;

[assembly: MelonInfo(typeof(EnchantTooltipMod), "EnchantTooltip", "0.2.0", "vergir")]
[assembly: MelonGame("Moon Studios", "NoRestForTheWicked")]

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
    }
}
