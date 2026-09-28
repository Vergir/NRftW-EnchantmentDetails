# EnchantTooltip

MelonLoader mod for No Rest for the Wicked: shows the possible range next to every rolled enchantment value,
e.g. `Damage increased by 7% (3–10)`, `-6% Stamina cost (3–10)`. Display only; the Quantum simulation is untouched.

## How it works

`EnchantmentDescriptionExtension.GetDescription(IAssetResolutionContext, EnchantmentData, ScalingMeta, bool extractRanges, bool isExalted)`
(private, RVA 0x8B65EE0 in build 29466) is the hub for every enchantment line: all public overloads (item tooltip,
stored enchantments), the Radiant Ember boost preview, `EnchantmentConfig.Populate` and the exalt pop-ups call it.

A Harmony postfix renders the same line twice more with `ScalingMeta.Interval` forced to 0 (best roll) and 1.0
(worst roll), then `RangeMerger` zips the numbers of the three strings. Numbers that differ between best and worst
get the range; fixed numbers (durations, stacks) and rich-text tags are left alone. If the three strings do not share
the same template the game's text is kept. Calls with `extractRanges = true` (the game's own "max-min" view: gem
tooltips, the enchant reroll screen) get no range merge, but still get the hidden numbers below.

### Hidden numbers

Many lines drop numbers the game has already computed ("Drain Health in Combat" = 1/s). `ResolveDescription` runs the value
processor (`ProcessValue` / `ProcessExalted`) on every packet in slot order before `String.Format` ignores the ones the
template never references. The mod renders the line once more with those processors returning sentinels, so it knows
which packets were dropped (any language), reads the enchantment's `ModifierData` for context, and appends
` (…)` via `HiddenNumbers`:

| Dropped packet | Shown | Skipped |
|---|---|---|
| stat value on a line with no placeholder | `1/s` (drain/regen), `100%`, traits `+20% Damage, +25% Attack Stamina Cost` | lines with a `{N}`: extra stats there are dummy status markers |
| periodic tick | `every 1s` | distance-based ticks (Proud Lance) |
| status duration | `5s cooldown` | 60s+ debuffs, durations equal to one already in the text |
| sprint time before an "after Sprinting" status (not a packet) | `after 2s of sprinting` | |
| NearbyEnemiesScaler (not a packet) | `max 5, within 7m` (radius rounded to whole metres) (cap from the scaler curve, radius from BalanceConfig) | the cap on "no Enemies nearby" curves |
| ModifierData / target Health-Focus-Stamina ratio threshold | `<50%` when the text says Low/High, else `only below 30% Focus` | 0% / 100% and absolute (Barrier, "Current") conditions |

Data behind it: `analysis/enchant_hidden_numbers.md` (tools/enchant_extract.py).

### Rune details

Rune texts are fixed strings without numbers (`HeroItemDataAsset.GetDescription()`, no packets). A postfix on that
parameterless method appends what the rune's `HeroRuneData.Actions[0]` really does, walked at runtime with the game's
context-free resolver `AssetBase.Resolve` (`RuneDetails`, mirrors `tools/rune_extract.py`):

| Rune kind | Shown |
|---|---|
| instant heal / restore | `Heals 40 HP`, `Restores 25 Durability` |
| channelled aura | `Heals 30 HP/s to you and allies; drains 40 Focus/s while channelling` |
| self buff | `+20% Overall Damage Dealt for 120s`, or `lasts 60s` for infusions |
| melee rune attack | `350% weapon damage`, `4 hits × 100% weapon damage`, `3 hits, 150–200% each, 500% total weapon damage` |
| projectiles / spells | `130/150/200% weapon damage by charge`, `320–800% weapon damage by charge`, `1100% weapon damage in 6m`, `3–10 shots × 80% weapon damage` (ammo fired) |
| damage over time | `150% weapon damage every 0.1s for 10s` (Fire Wall); beams `20% weapon damage every 0.15s; drains 20 Focus/s while channelling` |
| no damage | `knockdown, no damage` (Scream) |
| kicks (Swipe/Turnback/Frontflip Kick, Dropkick) | `≈161 dmg, grows with weapon LVL, not weapon DMG` with a weapon drawn; `≈136–161 dmg, …` in town (weapons put away: one value per main-hand weapon set); `3100% base dmg, …` outside a game |

Heals are before your Healing stat and damage is a multiple of the weapon's Damage stat (runes have no level). The
rune screen shows only the paragraph after the first line break, so the text is appended in-line.
Details: empty charge curves count as ×1 and real ones are sampled over the spell's Min..MaxCharge; `DamageBalanceData`
arrays are read from native memory (0x58-byte stride; the interop struct is smaller); only `IsContinuousDamage`
cascades show "every Xs" (other repeats just move the area, each enemy is hit once - inferred from the flag).
Audit: put `name guid` lines (from `analysis/rune_inventory.csv`) in `UserData/EnchantTooltip.selftest.txt`; on load the
mod writes every rune's text to `EnchantTooltip.selftest.out.txt` (2026-09-28: 262 runes, 251 with details, none odd).
Kicks: their `DamageConfig.CustomDamageProvider` is `ExpectedWeaponDamageAmountProviderNode`; `ResolveOverlapResult`
(@0x05B8C659) adds its amount to BaseDamage and sets `DamageFlags.IgnoreEntityBaseDamage`, so the hit is
multiplier × `StatsSystem.ExpectedStats.GetExpectedWeaponDamage(frame, hero)` = 2 × (1 + 5.8 × (weaponItemLevel − 1) / 29)
(`BalanceConfigData.Weapon.CoreStatScaling[Damage]`). The weapon's Damage stat, facets, attributes and upgrades don't
apply. Verified in game 2026-09-28: Swipe Kick 88 vs 32 for a 60-Damage item-level-9 spear (161 / 60 = 2.7x).
The live value comes from the local `HeroView` (`IsLocalPlayer`: `EntityRef` + `VerifiedFrame`), no hook. In town the
game reports no mainhand (weapons put away) and its own function would fall back to character level (1760 for Frontflip
Kick at level 19), so the mod then evaluates `ItemStatsSystem.GetExpectedWeaponDamage(ctx, itemLevel)` for the items in
`EquipmentSlot.RightHand1..3` and shows the range.
Research: `analysis/rune_numbers.md`.

Research (roll encoding, distribution, RVAs) is in the workspace root README, "Enchantment tooltip roll range".

## Preferences (`UserData/MelonPreferences.cfg`, `[EnchantTooltip]`)

| Key | Default | |
|---|---|---|
| `Enabled` | `true` | Master switch. |
| `Format` | `{value} <color=#9A9A9A>({worst}–{best})</color>` | Replaces each rolled number. Placeholders `{value}` (as the game prints it), `{worst}` `{best}` (no sign, no `%`), `{roll}` (0-100, 100 = best roll). TMP rich text works. |
| `ShowRanges` | `true` | In game: Options > Gameplay > **Show Enchantment Ranges**. |
| `ShowFacetNumbers` | `true` | **Show Facet Numbers**: trait values, e.g. `Heavy (+20% Damage, +25% Attack Stamina Cost)`; also hides the game's facet keyword pop-up (`InventoryItemInfoElement.PopulateKeywordTooltips` postfix). |
| `ShowDetailedInfo` | `true` | **Show Detailed Enchantment Info**: every other hidden number (see above). |
| `ShowRuneDetails` | `true` | **Show Rune Details** (see above). |
| `AddSettingsRows` | `true` | Add the four toggles to Options > Gameplay (after a divider, rows named `ET_*`). |
| `HiddenFormat` | ` <color=#9A9A9A>({extra})</color>` | Appended to lines with hidden numbers. |
| `ShowcaseKey` | `F10` | Screenshot helper, cycles 3 sets then off: each enchantment line is swapped for another that could roll in the same place (same colour, valid on every item type the original is, no duplicates or same-group pairs, unique lines kept, real roll and exalt kept; set 3 exalts one line x4). Display only. |
| `Debug` | `false` | Log rolled / best / worst / merged text of every enchantment line. |

## Build

`dotnet build -c Release` (post-build copies the DLL to `<game>/Mods`, `-p:DeployToGame=false` to skip;
`-p:GameDir=...` for another install). With HotReload in `<game>/Plugins` the running game picks up new builds.
