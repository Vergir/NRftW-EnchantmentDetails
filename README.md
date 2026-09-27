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
the same template the game's text is kept. Calls with `extractRanges = true` (the game's own "max-min" view) are skipped.

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
| NearbyEnemiesScaler (not a packet) | `max 5, within 7.1m` (cap from the scaler curve, radius from BalanceConfig) | the cap on "no Enemies nearby" curves |
| ModifierData / target Health-Focus-Stamina ratio threshold | `<50%` when the text says Low/High, else `only below 30% Focus` | 0% / 100% and absolute (Barrier, "Current") conditions |

Data behind it: `analysis/enchant_hidden_numbers.md` (tools/enchant_extract.py).

Research (roll encoding, distribution, RVAs) is in the workspace root README, "Enchantment tooltip roll range".

## Preferences (`UserData/MelonPreferences.cfg`, `[EnchantTooltip]`)

| Key | Default | |
|---|---|---|
| `Enabled` | `true` | Master switch. |
| `Format` | `{value} <color=#9A9A9A>({worst}–{best})</color>` | Replaces each rolled number. Placeholders `{value}` (as the game prints it), `{worst}` `{best}` (no sign, no `%`), `{roll}` (0-100, 100 = best roll). TMP rich text works. |
| `ShowRanges` | `true` | Show the worst–best range after rolled numbers. |
| `ShowHiddenNumbers` | `true` | Append numbers the text leaves out (see above). |
| `HiddenFormat` | ` <color=#9A9A9A>({extra})</color>` | Appended to lines with hidden numbers. |
| `Debug` | `false` | Log rolled / best / worst / merged text of every enchantment line. |

## Build

`dotnet build -c Release` (post-build copies the DLL to `<game>/Mods`, `-p:DeployToGame=false` to skip;
`-p:GameDir=...` for another install). With HotReload in `<game>/Plugins` the running game picks up new builds.
