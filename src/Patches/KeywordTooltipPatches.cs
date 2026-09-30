using HarmonyLib;
using Il2CppMoon.Forsaken;

namespace EnchantmentDetails.Patches;

/// <summary>With Show Facet Numbers on, hide the facet keyword pop-ups; the facet line already shows the numbers.</summary>
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
            EnchantmentDetailsMod.Log.Warning("Keyword tooltip postfix: " + e.Message);
        }
    }
}
