using System.Collections.Generic;
using Il2CppQuantum;

namespace EnchantTooltip;

/// <summary>
/// Screenshot helper (F10). Each enchantment line of a tooltip is swapped for another enchantment that could really roll
/// in its place, so screenshots stay believable:
///  - same EnchantmentType (Blue / Purple / PurpleTradeoff / Trait); Golden (unique-item) lines are kept,
///  - valid on every item type the real line is valid on (candidate ItemType flags contain the real ones),
///  - no enchantment twice and no two from the same EnchantmentGroupType in one tooltip.
/// The real line's roll and exalt level are kept. Candidates come from ShowcasePool (most interesting first); each F10
/// press starts the search at a different point of the pool. Variant 3 also exalts the first swapped line x4
/// (the per-item exalt cap). The swap happens in the GetDescription hub prefix: the text is the game's own rendering.
/// </summary>
internal static class Showcase
{
    private const int Variants = 3;
    public const int ExaltStacks = 4; // Quantum Constants.MaxExaltStacks (per item)

    private sealed record Candidate(long Guid, EnchantmentData Data, int Type, int ItemType, int Group);

    private static int _variant; // 0 = off
    private static List<Candidate>? _pool;
    private static int _frame = -1;
    private static readonly HashSet<long> _used = new();
    private static readonly HashSet<int> _usedGroups = new();
    private static bool _exaltGiven;

    public static bool Active => _variant > 0;

    public static string Cycle()
    {
        _variant = (_variant + 1) % (Variants + 1);
        _frame = -1;
        return _variant == 0 ? "Showcase off."
            : $"Showcase set {_variant}/{Variants}{(_variant == Variants ? " (one line exalted x4)" : "")}: enchantments are swapped for others that can roll in the same place.";
    }

    /// <summary>The replacement for one real line (null = keep it). A new frame means a new tooltip.</summary>
    public static EnchantmentData? Swap(IAssetResolutionContext ctx, EnchantmentData real, out bool exalt)
    {
        exalt = false;
        if (!Active) return null;
        int frame = UnityEngine.Time.frameCount;
        if (frame != _frame)
        {
            _frame = frame;
            _used.Clear();
            _usedGroups.Clear();
            _exaltGiven = false;
        }

        int type = (int)real.Type;
        if (real.Type == EnchantmentType.Golden) return null;
        var pool = Pool(ctx);
        if (pool.Count == 0) return null;
        int realItems = (int)real.ItemType;

        int start = (_variant - 1) * pool.Count / Variants;
        for (int i = 0; i < pool.Count; i++)
        {
            var c = pool[(start + i) % pool.Count];
            if (c.Type != type || (c.ItemType & realItems) != realItems) continue;
            if (_used.Contains(c.Guid) || (c.Group != 0 && _usedGroups.Contains(c.Group))) continue;
            _used.Add(c.Guid);
            if (c.Group != 0) _usedGroups.Add(c.Group);
            if (_variant == Variants && !_exaltGiven && real.Type != EnchantmentType.Trait)
            {
                exalt = true;
                _exaltGiven = true;
            }
            return c.Data;
        }
        return null;
    }

    private static List<Candidate> Pool(IAssetResolutionContext ctx)
    {
        if (_pool != null) return _pool;
        var list = new List<Candidate>();
        foreach (long guid in ShowcasePool.Guids)
        {
            var d = ctx.FindAsset<EnchantmentData>(new AssetGuid { Value = guid });
            if (d != null) list.Add(new Candidate(guid, d, (int)d.Type, (int)d.ItemType, (int)d.GroupType));
        }
        EnchantTooltipMod.Log.Msg($"Showcase pool: {list.Count}/{ShowcasePool.Guids.Length} enchantments found.");
        return _pool = list;
    }
}
