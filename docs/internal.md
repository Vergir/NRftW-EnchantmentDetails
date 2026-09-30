# Enchantment Details: how it works

These are research notes for maintainers. Addresses are RVAs in Steam build 29466 (`GameAssembly.dll` plus an
Il2CppDumper `dump.cs`). They move with every game update, but the class, method and field names usually do not.
"Verified" means read from the disassembly or the game data (`quantumDatabase.bin`, the `qdb_assets_all` bundle) and,
where stated, confirmed in game.

## One hub for every enchantment line

`EnchantmentDescriptionExtension.GetDescription(IAssetResolutionContext ctx, EnchantmentData data, ScalingMeta meta,
bool extractRanges, bool isExalted)` is **private** and lives at 0x8B65EE0. Every enchantment line goes through it.
Gems and facets are enchantments too.

| Caller | extractRanges | What it shows |
|---|---|---|
| `InventoryItemInfoElement.PopulateEnchantmentsInfo` (public `GetDescription(Enchantment, Frame, EntityRef, bool)`) | false | item tooltip |
| `PopulateStoredEnchantmentInfoElement` | false | stored enchantments (embers) |
| `UpdateRadiantEmberBoostPreview`, `EnchantmentConfig.Populate`, `…OnItemExalted` (inventory, vendor) | false | previews, exalt pop-ups |
| `OverrideEnchantsData` (reroll screen), `PopulateEnchantmentSourceInfoElement>g__PopulateGemSourceSlot` (gem tooltip) | **true** | the game's own "max–min" text |

The mod patches only this method (a postfix), the two value
processors (see [Hidden numbers](#hidden-numbers)), the keyword pop-up and the settings tab. None of these hooks takes
a struct by `ref`/`in`/`out`; see [Pitfalls](#pitfalls).

The rendered text is `DescriptionDataProviderExtensions.ResolveDescription` (0x8B644F0):
1. `ExtractSortedDescriptionData` (0x8B651E0) → `ModifierDescriptionExtension.ExtractDescriptionData` (0x8B6A680)
   builds a list of `DescriptionPacket`s (value text plus a `Source` label).
2. `ValidateOverrides` (0x8B65410) maps packet *k* to placeholder `{OrderOverrideList[k]}`.
3. `String.Format(template, packets)` fills the localized `DescriptionData.Description`.

## Rolls and ranges

An enchantment instance is `Enchantment { short ExaltStacks @0x0, AssetRef Asset @0x8, ulong Quality @0x10 }`.
`ScalingDataUtility.GetScalingData(Enchantment)` (0x5DCDCB0) turns it into
`ScalingMeta { CustomScaler = 1, Interval, Level = ExaltStacks }`.

The value of every scaled modifier is:

```
value = Scaling.Evaluate(Level) × (1 − clamp(ScalingData.Interval × meta.Interval, 0, 1)) × CustomScaler
```

(`ScaledModifier.GetProbe`, 0x5DBF160.) So `meta.Interval = 0` is the **best** roll and `1.0` the **worst**.
`ScalingData.Interval` is the weight: 0.7, 0.5, or 0 for lines that don't roll.

**Ranges (`RangeMerger`):** the postfix renders the line two more times with `meta.Interval` forced to 0 and to 1.0.
It then zips the numbers of the three strings:
- Tokens are rich-text tags (literal), numbers (`[+-]?\d+([.,]\d+)?%?`) and plain text.
- Every number that differs between the best and worst renders gets the `Format` suffix.
- Numbers equal in both (durations, stacks) are left alone.
- If the three strings don't share the same shape (a different token count or text), the game's text is kept.

Range ends are printed unsigned and without `%`: `-6% (3–10)`, not `-6% (-3%–-10%)`.

Calls with `extractRanges = true` get no range merge; the game already prints its own range there. They still get the
hidden numbers.

Roll-distribution notes, for the curious: `EnchantmentsAPI.GetBiasedRoll` (0x5D6BCF0) biases by item level through
`BalanceConfig.Modifiers.EnchantmentRollBias` (keys (1, 0.5), (9, 1.0), (16, 2.0)). Quality is stored as
`FP.One − i × 0xFFFF00010000`. The best bucket (n = 0) wraps to "quality 0%", so about 25% of high-level rolls land on
the worst value. Exalt stacks raise `Level` (up to `Constants.MaxExaltStacks = 4` per item); item level does not.

## Hidden numbers

Many enchantment texts leave out numbers the game has already computed, for example "Drain Health in Combat" drains
1 HP/s. The packet for that number exists; the template just never references it, and `String.Format` ignores unused
arguments.

**Capturing the packets.** Right after `ExtractSortedDescriptionData`, `ResolveDescription` runs the value processor
delegate on **every** packet in slot order (loop at 0x08B647F0). The processor is `EnchantmentDescriptionExtension.
ProcessValue` or `ProcessExalted`, and packet *k* is `{k}`. The mod postfixes both processors (`PacketCapture`).
While a capture is open, they record `(Source, text)` and return a sentinel instead. The hub is then rendered once more
(the "probe"): every sentinel missing from the output is a packet the template dropped. This works in every language
without reading the template.

**Adding context.** `ModifierInfoReader` reads the enchantment's Quantum `ModifierData`, found with
`ctx.FindAsset<ModifierData>(data.ModifierDataRef.Id)`:
- the stat types and signs of `StatModifier`s, then of `ItemStatModifier`s (their packets come out in that order);
- `EntityCondition` (`EntityStatsCondition`: Health/Focus/Stamina, `AmountType`, `ComparisonType`, `Threshold`);
- target conditions on `EventEffectModifier.Event` (`ModifierDamageEvent.MetaConditions.TargetConditions`) and on
  `CustomDamageModifier`;
- `PeriodicModifier.PeriodType`;
- `ApplyStatusOnSprintModifier.SprintDurationToActivate`;
- a `NearbyEnemiesScaler` in `CustomScalerData`.

**Rules (`HiddenNumbers`).** Everything appended goes into `HiddenFormat`, which puts it in one pair of grey
parentheses, so the fragments themselves never contain parentheses.

| Dropped packet (`Source`) | Shown | Not shown |
|---|---|---|
| `StatModifier` / `ItemStatModifier` on a line with **no** placeholder | the value; `/s` for HealthDrain, FocusDrain, HealthRegen; facets label each stat and take the **sign from the data** (`+75 Durability, -10% Focus Gain`); `XDamageTaken` stats read as the game says them, `X Resistance` with the sign flipped (`+10% Fire Resistance`) | lines that have a `{N}`: extra stat packets there are dummy status markers (`Durability +1`) |
| `PayloadData*`, `InterModifier`, `BarrierModifier`, `CustomDamage*` on a line with no placeholder | the value (`Refill Stamina (100%)`) | lines with a target condition (an execute's "100% of target Health") |
| `PeriodicModifier` | `every 1s` | distance-based periods (Proud Lance: a buff while moving) |
| `StatusDuration` | `5s cooldown` | 60 s or longer (the fight is over by then), and durations equal to one already in the text. In this build every one left is an internal cooldown (Cinder & Stone, Lacquered Bow). |
| `DamageEventCondition` | `10% chance` | |
| `EntityStatsCondition` (dropped) + a Ratio condition strictly between 0 and 100% | `<50%` if the line says Low/High (English; `<20% HP` when it is the target's Health), else `only below 30% Focus` | Full/100%, 0%, and absolute conditions ("if have Barrier") |
| (not a packet) facet infusion (`DamageSchoolOverrideModifier`) | `Plague Infusion` first in the facet note; enum Heat/Cold/Electric shown as Fire/Ice/Lightning | gem lines ("Gain Plague Infusion" is already their text) |
| (not a packet) sprint time | `after 2s of sprinting` | |
| (not a packet) nearby enemies | `max 5, within 7m`: the cap is the scaler curve's end value, the radius is √`BalanceConfig.Modifiers.NearbyEnemiesScaler.CheckRadiusSquared` rounded to whole metres | the cap on "no Enemies nearby" curves |

The game prints magnitudes only ("reduced by {0}"). That is why facet signs must come from
`ScalingData.Scaling.Evaluate(0)`.

A packet `Source` the rules don't know is logged once as `Unhandled dropped number`.

**Verified examples:**
- **Attack Stamina Cost increased by X** (the tradeoff on weapons, bows and gloves) only applies below 30% Focus. Its
  `ModifierData.EntityCondition` is Focus `LessThan` 0.3 (Ratio). `ModifierSystem.UpdateConditionalModifiers`
  (0x5DB2B20) evaluates `IsMet` for every instance flagged `HasConditions` and calls `SetEnabled`. This is the same
  mechanism as every "at Low Health" line.
- **Black Pearl** "Enchantment Power increased by X at Low Health": Health < 50%.
- **Drain Health / Drain Focus in Combat:** 1 / 1.5 per second. The ExpectedHeroHealth scaler saturates at 1.0.

## Facet keyword pop-up

`InventoryItemInfoElement.PopulateKeywordTooltips()` (0x8F1FC60, no parameters) activates `KeywordTooltips[i]` and
fills it from `m_data[i]` for every keyword (loop at 0x08F1FD50). With Show Facet Numbers on, a postfix deactivates the
entries whose `InventoryKeywordData.keywordType == KeywordType.Trait`, because the facet line already shows the numbers.
Status keywords (Frozen, Infected) keep their pop-ups.

`InventoryItemKeywordTooltip.Populate(data)` is not a good hook: its caller calls `SetActive(true)` after it.

## Settings rows

These are checkboxes at the end of Options > Gameplay, after a blank divider, and they work the same way as in the
author's other mods:
- The game's own toggles need a `PlayerSetting<bool>`, whose getter is a `ref T` delegate that a mod cannot supply.
  `SettingsScreenControls.AddKeyboardAndMouseSchemeToggleItem(category, name, initial, Action<bool>, style, desc,
  invokeOnStart)` builds the same `ToggleSettingsItemGUI` from a plain callback.
- Style `Alternate1` avoids arming the "preview keyboard scheme" button while hovered.
- Rows are added in a postfix on `GameplaySettingsTab.Initialize` (once per scene load) and on init for screens that
  already exist (hot reload).
- They are named `ED_*` and removed on unload.
- `LocalizedMessage`s are created at runtime with the same text in every language.

## Pitfalls

- **Never Harmony-patch an IL2CPP method that takes a struct by `ref`/`in`/`out`.** Treat large by-value structs as
  suspect too. A parameterless prefix on `PopulateEnchantmentsInfo(Frame, ref ItemDescription)` corrupted the argument
  in Il2CppInterop's trampoline. `EnchantmentsAPI.GetGemSlotCount` then threw, and **no** enchantment showed in any
  tooltip. The broken trampoline survives unpatching until the game restarts.
- **MelonLoader applies every `[HarmonyPatch]` in a mod by itself.** The mod has `[assembly: HarmonyDontPatchAll]` and
  calls `PatchAll` only when `Enabled` is on, so `Enabled = false` really means off.
- **Re-entrancy:** the postfix calls the hub itself (best, worst and probe renders), guarded by a `[ThreadStatic]` flag.
  The processor capture is also thread-static.
- **Interop names:** Quantum types live under `Il2CppQuantum`, `Moon.Forsaken` under `Il2CppMoon.Forsaken`, and
  namespace-less types under `Il2Cpp`. `Math` is ambiguous with `Il2CppMoon.Forsaken.Math`.

## After a game update

1. Regenerate `dump.cs` (Il2CppDumper) and check that these still exist with the same signatures:
   - `EnchantmentDescriptionExtension.GetDescription` (5 parameters), `ProcessValue`, `ProcessExalted`
   - `InventoryItemInfoElement.PopulateKeywordTooltips`, `GameplaySettingsTab.Initialize`
   - the `DescriptionPacket.Source` labels listed above
2. Set `Debug = true`. Every rendered line is then logged with its rolled, best and worst renders and its captured
   packets, which shows quickly whether the pipeline changed.
3. Watch the log for `Unhandled dropped number`. It means a new packet type appeared.
