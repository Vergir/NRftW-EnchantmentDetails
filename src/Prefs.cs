using MelonLoader;

namespace EnchantmentDetails;

/// <summary>All user-tunable values. Stored in UserData/MelonPreferences.cfg under [EnchantmentDetails].</summary>
internal static class Prefs
{
    public const string DefaultFormat = "{value} <color=#9A9A9A>({worst}–{best})</color>";

    public const string DefaultHiddenFormat = " <color=#9A9A9A>({extra})</color>";

    private static MelonPreferences_Category _cat = null!;

    public static MelonPreferences_Entry<bool> Enabled = null!;
    public static MelonPreferences_Entry<string> Format = null!;
    public static MelonPreferences_Entry<bool> ShowRanges = null!;
    public static MelonPreferences_Entry<bool> ShowFacetNumbers = null!;
    public static MelonPreferences_Entry<bool> ShowDetailedInfo = null!;
    public static MelonPreferences_Entry<bool> AddSettingsRows = null!;
    public static MelonPreferences_Entry<string> HiddenFormat = null!;
    public static MelonPreferences_Entry<bool> Debug = null!;

    public static void Init()
    {
        _cat = MelonPreferences.CreateCategory("EnchantmentDetails", "Enchantment Details");

        Enabled = _cat.CreateEntry("Enabled", true, description: "Master switch.");
        Format = _cat.CreateEntry("Format", DefaultFormat,
            description: "Replaces every rolled number in an enchantment line. Placeholders: {value} (rolled), {worst}, {best} (both without sign and %), "
                + "{roll} (0-100 roll quality, 100 = best). TextMeshPro rich text works, e.g. <color=#9A9A9A>...</color>.");
        ShowRanges = _cat.CreateEntry("ShowRanges", true,
            description: "Show the worst-best range after every rolled number. Also in Options > Gameplay.");
        ShowFacetNumbers = _cat.CreateEntry("ShowFacetNumbers", true,
            description: "Show the values of facets (traits), e.g. Heavy (+20% Damage, +25% Attack Stamina Cost), and hide the game's facet pop-up that says the same without numbers. Also in Options > Gameplay.");
        ShowDetailedInfo = _cat.CreateEntry("ShowDetailedInfo", true,
            description: "Append numbers the enchantment text leaves out: drain rates, tick intervals, cooldowns, Low Health/Focus "
                + "thresholds, sprint time, nearby-enemy cap and radius. Also in Options > Gameplay.");
        HiddenFormat = _cat.CreateEntry("HiddenFormat", DefaultHiddenFormat,
            description: "Appended to lines with hidden numbers. {extra} = the numbers, e.g. \"1/s\" or \"<50%\".");
        AddSettingsRows = _cat.CreateEntry("AddSettingsRows", true,
            description: "Add the three toggles above to Options > Gameplay.");
        Debug = _cat.CreateEntry("Debug", false,
            description: "Log the rolled / best / worst renderings of every enchantment line (MelonLoader console).");
    }
}
