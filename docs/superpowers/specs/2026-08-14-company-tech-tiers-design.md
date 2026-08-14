# Company Tech Tiers — Design

**Date:** 2026-08-14
**Target:** Quasimorph 1.0.1.566s.7e4da55 (Unity 2022.3.62f2, Steam app 2059170, stable branch)
**Mod id:** `babyak_QM_CompanyTechTiers`

## Problem

Quasimorph's generic research datadisks come in three tiers that gate roughly by item tech level:

| Chip | Item `TechLevel` | Unlocks | Offered at faction level |
|------|------------------|---------|--------------------------|
| `low_chip` | 1 | 57 items, TL1–3 | 1–3 |
| `medium_chip` | 4 | 48 items, mostly TL4–6 | 4–5 |
| `high_chip` | 7 | 14 items, TL7+ | 7–8 |

Company (faction) tech has no such breakdown. Each company has exactly one monolithic chip, always
`TechLevel` 10, always offered only at faction tech level 10 — even though the arsenal it unlocks
spans the whole range. `anc_chip` unlocks items from TL2 to TL10. A company's entry-level pistol is
therefore gated behind maximum standing with that company.

Twelve company chips exist: `rwa_chip`, `sbn_chip`, `anc_chip`, `ddr_chip`, `sun_chip`, `cor_chip`,
`plb_chip`, `gra_chip`, `chu_chip`, `fra_chip`, `dil_chip`, `tia_chip`.

Company chips are also an upgrade currency: they appear in `ModifyItemsGrades` across **236 crafting
recipes** (e.g. `rwa_power_armor_1` costs `rwa_chip 40` to reach max grade).

## Goal

Give company tech the same three-tier breakdown as the generic chips, offered as faction rewards at
**faction tech level 3, 6 and 10**.

## Decisions

Settled during design:

1. **Three tiered company chips.** Each company's unlock list is split into three chips. Not "the
   same chip offered earlier".
2. **Upgrade costs are remapped** to the matching tier, so a TL2 item unlocked at level 3 can also be
   upgraded at level 3.
3. **Strict tech-level split**: tier 1 = TL1–3, tier 2 = TL4–6, tier 3 = TL7+. Boundaries mirror the
   generic chips exactly. Accepted consequence: three companies get a one-item tier-1 chip.
4. **Strict partition, not cumulative.** The existing `<x>_chip` keeps its id but unlocks only TL7+.
   This matches vanilla — `high_chip` does not re-unlock `low_chip`'s items. Lower tiers remain in
   the reward pool at every level above their threshold, so they stay obtainable at level 10.

## Approach

A single mod that performs a **pure data transform at load time**. No Harmony patching of game logic.

`UserModSystem.InitResourceHooks` runs inside `Bootstrap.InitMods()`, which completes before
`Data.Load()`. A `ModHookType.ResourcesLoad` hook therefore sees every config resource before the
game parses it, and can return a replacement `TextAsset`.

Everything is derived from the live config text at runtime. No item lists, faction names, chip ids or
tech levels are hardcoded. If a game patch adds a company, moves an item's tech level, or changes an
unlock list, the mod re-derives from the new data. This is the primary reason to transform text at
runtime rather than ship edited config files.

### Intercepted resources

| Resource | Change |
|---|---|
| `config_items` | Add 24 datadisk rows; rewrite the 12 existing company chips' `UnlockIds` to TL7+ only |
| `config_faction_drops` | Add tier-1 and tier-2 entries at levels 3 and 6 to every `*_rewardChips` table |
| `config_crafting` | Remap `ModifyItemsGrades` company-chip costs to the tier matching the output item |
| `DescriptorsCollections/datadisks_descriptors` | Clone each company chip's descriptor onto its two new ids |

The hook returns `null` for every other path, so the game falls through to `Resources.Load`
unchanged.

### Transform pipeline

1. **Discover** company chips — a row in the `#datadisks` section is a company chip if its
   `Categories` cell does **not** contain the token `Chip`. Every generic disk (`low_chip`,
   `medium_chip`, `high_chip`, `mercenary_chip`, `class_chip`, `augment_chip`) is tagged `Chip`;
   the twelve company chips are tagged only with faction ids. Finds 12 today.
2. **Index** item id → `TechLevel` across every section of `config_items` (1,381 items).
3. **Partition** each company chip's `UnlockIds` into TL1–3 / TL4–6 / TL7+.
4. **Emit** `<x>_chip_low` and `<x>_chip_mid` rows, copying the parent chip's `Categories`,
   `Weight` (item mass, 0.1), `InventoryWidthSize`, `ItemClass`, `CanPutInVest` and `UnlockType`.
   The new rows take `TechLevel` 1 and 4 respectively, matching `low_chip` and `medium_chip`; the
   parent keeps its `TechLevel` of 10. `Price` comes from `TierPrices`.
5. **Rewrite** the parent chip's `UnlockIds` to the TL7+ subset.
6. **Drops** — for every `*_rewardChips` table containing `<x>_chip@10`, insert `<x>_chip_low@3` and
   `<x>_chip_mid@6`. Each new entry's `Points` is set to that tier's `Price`, matching how the
   generic chips line up (`low_chip` 300/300, `medium_chip` 450/450). Drop `Weight` is covered
   under Configuration below.
7. **Costs** — in `ModifyItemsGrades`, replace `<x>_chip N` with the tier chip matching the output
   item's tech level. Recipes whose output is TL7+ are untouched.

Deriving drops from the existing table entries handles the irregular cases without special-casing:

- **UnchainedBelt** has no chip of its own but drops seven other companies' chips at level 10; it
  gains fourteen entries.
- **Hive** drops `anc_chip`; **SheduThousand** drops `rwa_chip`.
- **Sunlight** and **Emeraldlight** share `sun_chip`; **ChurchRevelation** and **ChurchGannix** share
  `chu_chip`.
- **Tezctlan** and **XiomaraMasks** have no `rewardChips` table at all and are correctly skipped.

### Tier sizes under the chosen split

Computed from live config data (tier1 / tier2 / tier3):

```
anc 4/6/6    rwa 4/2/5    sbn 2/3/3    ddr 3/9/4
sun 1/4/10   cor 3/6/3    plb 1/4/6    gra 2/3/5
chu 1/7/4    fra 3/5/6    dil 3/3/6    tia 4/6/6
```

`sun`, `plb` and `chu` get a single-item tier-1 chip. Accepted per decision 3.

### Icons

The two new tiers reuse the parent company chip's sprite, registered via
`DescriptorsCollection.AddDescriptor`. There is no art pipeline for this mod, and a mismatched custom
icon would be worse than a shared one.

### Names

`Localization.DuplicateKey` is public static but cannot alter a value — it only copies a key verbatim,
which does not let the new tiers carry distinguishing names. Instead, the `localization` table itself
is loaded through `CustomResources.Load("localization")` as resource #529, comfortably after the
`ResourcesLoad` hook is armed, so it is intercepted and rewritten the same way as the other config
resources. For each new id, the rewriter duplicates `item.<parent>.name`, `.shortdesc` and `.desc` in
every language, then appends a language-neutral roman numeral: `I`, `II`, `III`. The parent chip's
display name gains `III` so the set reads as a series. Ids are never changed. This is implemented as a
pure, unit-tested rewriter rather than untestable Unity code (`Localization.db` is private with no
public setter), and it means no Harmony patching is needed anywhere in this mod.

## Configuration

Written to `%LOCALAPPDATA%\..\LocalLow\Magnum Scriptum Ltd\Quasimorph_ModConfigs\QM_CompanyTechTiers\config.json`
on first run.

| Key | Default | Meaning |
|-----|---------|---------|
| `RewardLevels` | `[3, 6, 10]` | Faction tech level at which each tier is offered |
| `TierBoundaries` | `[3, 6]` | Item tech level at which tier 1 ends and tier 2 ends |
| `TierPrices` | `[400, 525, 650]` | `Price` for each tier (tier 3 = existing value) |
| `RemapUpgradeCosts` | `true` | Whether to rewrite `ModifyItemsGrades` |
| `InheritParentDropWeight` | `false` | See below |

**Drop weights.** Note `Weight` means two different things: item mass in the `#datadisks` section,
and drop probability in a `*_rewardChips` table. This paragraph is about the latter.
By default tier 1 and tier 2 take the drop weight of the generic chip in the same
bracket (`low_chip` / `medium_chip` weight in that same table), not the parent company chip's weight.
Company chip entries carry weight 115 while a level-3 pool totals about 40, so inheriting it would
make company tech roughly 74% of all chip rewards at level 3. Setting `InheritParentDropWeight` to
`true` restores that behaviour for anyone who wants it.

## Module structure

The rewrite is pure string → string and testable without launching the game. The spec originally
planned six files; the implementation split the single `ConfigRewriter.cs` into four focused
rewriters (roughly 80 lines each, one per config resource) and dropped the planned
`LocalizationPatch.cs` entirely, since naming turned out to be a pure rewriter rather than Unity-side
patching (see Names, above).

| File | Responsibility | Depends on |
|------|----------------|------------|
| `Configs/ConfigDocument.cs` | Parse and serialize the `#section` / header / rows / `#end` TSV format | nothing |
| `Tiering/ChipTierPlan.cs` | Config text in, tier assignment out. Pure, no Unity types | `ConfigDocument` |
| `Rewriting/ItemsRewriter.cs` | Emit tier rows in `config_items`, narrow the parent's `UnlockIds` | `ConfigDocument`, `ChipTierPlan` |
| `Rewriting/FactionDropsRewriter.cs` | Insert tier-1/tier-2 reward entries into `config_faction_drops` | `ConfigDocument`, `ChipTierPlan` |
| `Rewriting/CraftingRewriter.cs` | Remap `ModifyItemsGrades` company-chip costs in `config_crafting` | `ConfigDocument`, `ChipTierPlan` |
| `Rewriting/LocalizationRewriter.cs` | Duplicate localization keys and append tier suffixes | `ChipTierPlan` |
| `ModSettings.cs` | User-tunable settings, JSON load/save, validation | nothing (no Unity types) |
| `ResourceHook.cs` | `ResourcesLoad` dispatch, `TextAsset` construction, descriptor cloning | the four rewriters, `ChipTierPlan`, Unity |
| `Plugin.cs` | Hook registration, config load, logging | `ModSettings`, `ResourceHook`, `MGSC` |

Only `ResourceHook.cs` and `Plugin.cs` touch Unity or `MGSC`. `ConfigDocument`, `ChipTierPlan`,
`ModSettings` and the four rewriters are plain .NET and are exercised directly against the captured
config dump in `tests/fixtures/`.

## Testing

The captured config dump (`config_items.txt`, `config_faction_drops.txt`, `config_crafting.txt`,
pulled from the running game) is the test fixture. These are game assets, so they live in
`tests/fixtures/` but are **gitignored, not committed** — the repo carries a small dump mod and a
note on regenerating them instead.

**Unit, against the fixture, no game required:**

1. `ConfigDocument` round-trips every dumped config byte-for-byte.
2. Discovery finds exactly the 12 known company chips and no generic chip.
3. Partition sizes match the table above.
4. Every id in a parent chip's original `UnlockIds` appears in exactly one tier — no loss, no
   duplication.
5. Drop rewriting produces tier-1 and tier-2 entries for every table that had a level-10 company
   chip, and touches no other table. UnchainedBelt gains exactly 14 entries.
6. Cost remapping changes only `ModifyItemsGrades` cells that named a company chip, and leaves TL7+
   recipes alone.
7. Rewritten output re-parses cleanly and column counts are preserved.

**In-game verification:**

8. Game reaches the main menu with no config parse errors in `Player.log`.
9. `listmod` shows the mod loaded.
10. New chips resolve a name and an icon rather than a missing-key placeholder.
11. A faction at tech level 3 offers the tier-1 chip in its reward pool.

## Error handling

Any failure inside the `ResourcesLoad` hook must return `null` rather than throw, so the game falls
back to the stock resource and boots normally with the mod inert. Every fallback logs a warning
naming the resource and the reason. A config parse that finds zero company chips is treated as a
failure, not as "nothing to do" — that signals the format changed under us.

## Out of scope

- Custom artwork for the new tiers.
- Barter and shop availability (`config_barter`) for the new chips.
- Mod Configuration Menu integration; config is a JSON file for now.
- Migration of existing saves. Unlocks already granted stay granted.

## Known risks

- Saves created with the mod contain the new item ids. Removing the mod later leaves unknown ids in
  the save.
- Conflicts with any other mod that rewrites `config_items`, `config_faction_drops` or
  `config_crafting` wholesale. Mods that patch parsed data instead are fine.
- A game update that changes the config table format would disable the mod via the fallback path
  rather than corrupt anything.
