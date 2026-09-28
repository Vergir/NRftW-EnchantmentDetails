using HarmonyLib;
using Il2CppMoon.Forsaken;

namespace EnchantTooltip.Patches;

/// <summary>
/// With Show Facet Numbers on, the facet line already says what the facet does in numbers, so the game's facet keyword
/// pop-up ("Durable / Item Facet / Durability increased...") is hidden. PopulateKeywordTooltips (no parameters) activates
/// KeywordTooltips[i] and fills it from m_data[i] for every keyword (loop @0x08F1FD50, build 29466); other keyword types
/// (statuses like Frozen) keep their pop-ups.
/// </summary>
[HarmonyPatch(typeof(InventoryItemInfoElement), nameof(InventoryItemInfoElement.PopulateKeywordTooltips))]
internal static class PopulateKeywordTooltipsPatch
{
    static void Postfix(InventoryItemInfoElement __instance)
    {
        if (!Prefs.Enabled.Value || !Prefs.ShowFacetNumbers.Value) return;
        try
        {
            var tooltips = __instance.KeywordTooltips;
            var data = __instance.m_data;
            if (tooltips == null || data == null) return;
            int n = System.Math.Min(tooltips.Count, data.Count);
            for (int i = 0; i < n; i++)
            {
                var d = data[i];
                var t = tooltips[i];
                if (d != null && t != null && d.keywordType == KeywordType.Trait)
                    t.gameObject.SetActive(false);
            }
        }
        catch (System.Exception e)
        {
            EnchantTooltipMod.Log.Warning("Keyword tooltip postfix: " + e.Message);
        }
    }
}
