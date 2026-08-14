# Chip Tier Sprites — Design

**Date:** 2026-08-14
**Target:** Quasimorph 1.0.2.573s (Unity 2022.3.62f2, Steam app 2059170)
**Builds on:** `2026-08-14-company-tech-tiers-design.md`

## Problem

The mod invents 24 new datadisk ids (`<x>_chip_low`, `<x>_chip_mid` for twelve companies). They
currently have no art of their own: `ResourceHook.CloneDescriptors` registers the **parent chip's
descriptor object** against each new id, so all three tiers of a company render with one identical
icon. That was the right call while there was no art pipeline — a shared icon beats a wrong one —
but it leaves the three tiers visually indistinguishable in inventory and cargo.

This design adds two things: a way to get the existing icons out as editable PNGs, and a way for the
mod to use hand-edited replacements when they exist.

## Goal

Distinct inventory icons for the low and mid tiers of each company chip, authored by hand, shipped
with the mod.

## Decisions

Settled during design:

1. **Art ships inside the mod.** PNGs live in `media/sprites/` in the repo, are copied into the mod
   folder at build time, and are loaded from `ModContentPath`. Workshop subscribers get the art.
2. **Inventory icon only.** `_smallIcon` (map) and `_shadow` (floor) continue to be inherited from
   the parent chip. The inventory icon is what a player sees constantly; a dropped chip still looks
   correct without extra authoring.
3. **Tier 3 is untouched.** Only the 24 invented ids get new descriptors. The vanilla company chip
   keeps its base-game descriptor object, so no base-game item's appearance is modified and no
   conflict is created with other mods that touch it.

## Relevant game API

Verified against a decompile of the 1.0.1 build (`ItemContentDescriptor.cs`, `DatadiskDescriptor.cs`,
`DescriptorsCollection.cs`):

```csharp
public class ItemContentDescriptor : ScriptableObject
{
    [SerializeField] private Sprite _icon;       // public Sprite Icon
    [SerializeField] private Sprite _smallIcon;  // public Sprite SmallIcon
    [SerializeField] private Sprite _shadow;     // public Sprite ShadowOnFloor
}

public class DatadiskDescriptor : ItemContentDescriptor { }   // adds nothing

// DescriptorsCollection
public bool TryGetDescriptor(string id, out UnityEngine.Object descriptor);
public void AddDescriptor(string id, UnityEngine.Object descriptor);
```

The three sprite fields are private with getters only. New descriptor instances therefore have their
fields assigned by **reflection** (`BindingFlags.NonPublic | BindingFlags.Instance`). The project
carries `BepInEx.AssemblyPublicizer`, which would permit direct assignment at compile time, but that
relies on the runtime not enforcing field access; reflection is guaranteed and this runs 24 times at
startup, not on a hot path.

## Part 1 — Extraction

A new setting `DumpChipSpritesOnLoad` (bool, default `false`), mirroring the existing
`DumpConfigsOnLoad`. When enabled, during `CloneDescriptors` the mod writes each company chip's
inventory icon **twice** — once per tier id — into `<ModPersistenceFolder>/sprite_dump/`:

```
anc_chip_low.png   anc_chip_mid.png
rwa_chip_low.png   rwa_chip_mid.png
...                                     24 files
```

Pre-named copies, ready to edit and drop into `media/sprites/`.

**Naming convention, used by both halves:** the file for a tier id is exactly `<tierId>.png` — e.g.
`anc_chip_low.png`. The dump writes that name; the loader looks for that name. There is no separate
mapping table to keep in sync.

**The authoring loop:** enable `DumpChipSpritesOnLoad` → launch once → edit the PNGs in
`sprite_dump/` → copy the ones you changed into `media/sprites/` in the repo → build → relaunch.
Only the files you copy across are overridden; the rest keep the shared parent icon.

**Why a runtime dump rather than AssetRipper or UnityPy:** the mod already holds the resolved
`Sprite` object via `TryGetDescriptor(chip.ParentId, ...)`. Reading it directly needs no external
tooling, no GUID chasing through `resources.assets`, and captures the correct sub-rect if the sprite
is atlas-packed.

**Reading a non-readable texture.** Sprite textures usually have `isReadable = false`, so
`GetPixels` throws. The dump blits through a temporary `RenderTexture` instead:

1. `RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32)`
2. `Graphics.Blit(sprite.texture, rt)`
3. set `RenderTexture.active = rt`, `ReadPixels` the sprite's sub-rect into a new `Texture2D`
4. `EncodeToPNG`, restore `RenderTexture.active`, `ReleaseTemporary`

The sub-rect is `sprite.textureRect` when `sprite.packed` is true, otherwise `sprite.rect`.

**Orientation is a verification step, not an assumption.** `Graphics.Blit` can flip vertically
depending on graphics API. The implementer must open a dumped PNG and confirm it is upright,
flipping the `ReadPixels` source rect if not. This is checked by eye against the in-game icon, once.

## Part 2 — Loading

In `CloneDescriptors`, for each tier id:

- If `<spritesFolder>/<id>.png` exists, decode and build a replacement descriptor.
- Otherwise — or if decoding fails — register the parent descriptor, exactly as today.

Building the replacement:

```
bytes  -> new Texture2D(2, 2, TextureFormat.RGBA32, false), tex.LoadImage(bytes)
          tex.filterMode = parentSprite.texture.filterMode
sprite -> Sprite.Create(tex,
                        new Rect(0, 0, tex.width, tex.height),
                        parentSprite.pivot / parentSprite.rect.size,   // normalised
                        parentSprite.pixelsPerUnit)
desc   -> ScriptableObject.CreateInstance<DatadiskDescriptor>()
          _icon      = sprite
          _smallIcon = parentDescriptor.SmallIcon
          _shadow    = parentDescriptor.ShadowOnFloor
```

Three details carry real failure modes:

- **`pixelsPerUnit` copied from the parent.** Getting it wrong renders a perfectly correct image at
  the wrong size — a silent, confusing failure.
- **Pivot normalised.** `Sprite.pivot` is in pixels; `Sprite.Create` expects a 0–1 fraction of the
  rect. Passing the raw value misplaces the icon.
- **`filterMode` copied from the parent.** This is a pixel-art game; a texture defaulting to
  `Bilinear` renders visibly blurry next to every other icon.

`Texture2D.LoadImage` returns `false` on malformed input. That path logs a warning and falls back to
the parent descriptor. The `ResourcesLoad` hook's existing no-throw contract still holds: nothing
here may propagate an exception, because that would break game startup rather than just the mod.

## Part 3 — Plumbing

`ResourceHook.Initialise` gains a `string spritesFolder` parameter. `Plugin.BeforeBootstrap` passes
`Path.Combine(context.ModContentPath, "sprites")`.

**`ModContentPath` availability must be verified, not assumed.** It is declared on `IModContext`, but
this project has only ever read it from `AfterConfigsLoaded`. If it turns out to be unpopulated
during `BeforeBootstrap`, the fallback is to resolve the folder relative to the executing assembly's
location instead. This is an explicit implementation check.

Two MSBuild targets in `src/QM_CompanyTechTiers.csproj` need updating, and the second is easy to
miss:

- `LocalPresetsDeploy` copies an explicit flat file list — it must also copy `sprites/**` into the
  deployed mod folder.
- `SteamWorkshopDeploy` and `PostBuildPackage` glob `$(TargetDir)/*.*`, which is **flat**. A
  subfolder is silently skipped, so art would work locally and vanish on Workshop publish.

A build step also has to copy `media/sprites/*.png` into `$(TargetDir)sprites/` in the first place,
alongside the existing `ModBaseItems` target that copies the manifest and thumbnail. That copy must
be a no-op when `media/sprites/` is empty or absent — which is its state today, and the state of any
fresh clone — rather than failing the build the way the missing `thumbnail.png` did before Task 10.

## Module structure

| File | Responsibility | Depends on |
|------|----------------|------------|
| `src/Sprites/SpriteFileResolver.cs` | tier id → expected path; does art exist for this id | nothing (pure) |
| `src/Sprites/SpriteDumper.cs` | Sprite → PNG bytes via RenderTexture blit | Unity |
| `src/Sprites/SpriteLoader.cs` | PNG bytes → Sprite → `DatadiskDescriptor` | Unity, reflection |
| `src/ResourceHook.cs` | calls the above from `CloneDescriptors` | all |
| `src/ModSettings.cs` | adds `DumpChipSpritesOnLoad` | nothing |

`SpriteFileResolver` is deliberately Unity-free so the test project can link it.

## Testing

**Unit, no game required:** `SpriteFileResolver` against a temp directory — id maps to the expected
filename; art present is detected; art absent returns no path; an empty or missing folder is handled
without throwing. `ModSettings` gains a default-value assertion for the new flag.

**In-game, by eye:** the honest position is that the Unity half cannot be unit-tested. It is verified
by enabling the dump, confirming 24 upright PNGs appear, replacing two of them with obviously
different images, relaunching, and looking at the chips in cargo. Specifically checked: the
replacement renders at the same size as the vanilla icon (pixels-per-unit), is crisp rather than
blurry (filter mode), and sits correctly in its inventory cell (pivot).

**Regression:** with no `sprites/` folder present, behaviour must be byte-for-byte what ships today —
parent descriptor registered for every tier id, `Registered 24 tier descriptors.` in the log.

## Out of scope

- Map icon and floor shadow overrides.
- Any change to the vanilla company chip's appearance.
- Generating art. The mod extracts and loads; the drawing is done by hand.
- A local-override folder outside the mod. Art lives with the mod so subscribers get it.

## Known risks

- If a future game patch changes `ItemContentDescriptor`'s private field names, the reflection breaks.
  It fails soft — the catch falls back to the parent descriptor — but the art silently stops applying.
  The field names are logged once at startup when a lookup fails, so the cause is discoverable.
- Sprites created at runtime are not managed by Unity's asset system. They persist for the process
  lifetime, which is what we want, but they are never unloaded; 24 small textures is negligible.
