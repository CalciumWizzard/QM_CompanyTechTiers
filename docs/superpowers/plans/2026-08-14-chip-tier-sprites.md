# Chip Tier Sprites Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the 24 invented chip-tier ids their own inventory icons — dumped from the game as editable PNGs, hand-edited, then shipped with the mod.

**Architecture:** Two halves sharing one filename convention (`<tierId>.png`). A runtime dump reads the parent chip's `Sprite` through a `RenderTexture` blit and writes PNGs; a loader decodes a PNG back into a `Sprite` and builds a fresh `DatadiskDescriptor` whose private sprite fields are set by reflection. When no PNG exists the existing behaviour is unchanged — the parent's descriptor object is registered, exactly as today.

**Tech Stack:** C# on `net48`, Unity 2022.3 (`UnityEngine.CoreModule`, `UnityEngine.ImageConversionModule`), xunit 2.9.2 for the one Unity-free component, MSBuild for build/deploy plumbing.

## Global Constraints

- Target framework `net48`, `<LangVersion>latest</LangVersion>`. **No `record`, no `init` accessors** (they need an `IsExternalInit` polyfill on net48). Classic brace namespaces, matching the existing code.
- Game build targeted: **Quasimorph 1.0.2.573s**. Game path `C:\Program Files (x86)\Steam\steamapps\common\Quasimorph`.
- **Zero compiler warnings.** The build is currently clean and must stay clean.
- **Nothing may throw out of the `ResourcesLoad` hook.** `ResourceHook.Load` has one outer catch that latches `_failed` and returns `null`; an escaping exception breaks game startup, not just the mod. Every new code path lives under that catch and must also fail soft on its own.
- **The no-art path must stay byte-for-byte identical to today's behaviour**: parent descriptor registered for all 24 ids, `Registered 24 tier descriptors.` in `Player.log`. This is the regression guard — the mod currently works and must keep working when `sprites/` is absent, which is its state on any fresh clone.
- Filename convention, used by dumper and loader alike: the file for a tier id is exactly `<tierId>.png`, e.g. `anc_chip_low.png`. No separate mapping table.
- Vanilla company chips (`anc_chip` etc.) are **never** given a new descriptor. Only the 24 invented ids.
- `tests/fixtures/` is gitignored game-publisher data. `git status` must never list those files.
- Do not commit anything under `.superpowers/`.

**Reference material:** the spec at `docs/superpowers/specs/2026-08-14-chip-tier-sprites-design.md`. A decompile of `Assembly-CSharp.dll` can be regenerated with the commands in `tests/fixtures/README.md` — consult it rather than guessing at game APIs.

**Current suite size is 52 tests.** Earlier tasks in this repo added tests at review request, so per-task totals below are lower bounds. Judge success by "all green, none unexpectedly skipped", not by hitting an exact number.

---

### Task 1: `SpriteFileResolver` and the dump setting

The only Unity-free part of this feature, and therefore the only part that can be unit-tested. Both deliverables are groundwork the next two tasks consume.

**Files:**
- Create: `src/Sprites/SpriteFileResolver.cs`
- Modify: `src/ModSettings.cs` (add one field, one validation-free default)
- Create: `tests/QM_CompanyTechTiers.Tests/SpriteFileResolverTests.cs`
- Modify: `tests/QM_CompanyTechTiers.Tests/ModSettingsTests.cs` (extend the defaults assertion)
- Modify: `tests/QM_CompanyTechTiers.Tests/QM_CompanyTechTiers.Tests.csproj` (link the new source folder)

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `QM_CompanyTechTiers.Sprites.SpriteFileResolver` — constructor `SpriteFileResolver(string folder)`; `static string FileNameFor(string tierId)`; `string PathFor(string tierId)` (null when folder or id is null/empty); `bool HasArtFor(string tierId)`.
  - `ModSettings.DumpChipSpritesOnLoad` — `public bool`, default `false`.

- [ ] **Step 1: Link the new source folder into the test project**

In `tests/QM_CompanyTechTiers.Tests/QM_CompanyTechTiers.Tests.csproj`, add one line to the existing linked-sources `ItemGroup` so it reads:

```xml
  <ItemGroup>
    <Compile Include="../../src/Configs/**/*.cs" LinkBase="Linked/Configs" />
    <Compile Include="../../src/Tiering/**/*.cs" LinkBase="Linked/Tiering" />
    <Compile Include="../../src/Rewriting/**/*.cs" LinkBase="Linked/Rewriting" />
    <Compile Include="../../src/Sprites/SpriteFileResolver.cs" Link="Linked/SpriteFileResolver.cs" />
    <Compile Include="../../src/ModSettings.cs" Link="Linked/ModSettings.cs" />
  </ItemGroup>
```

Link `SpriteFileResolver.cs` **by name, not with a `src/Sprites/**` glob.** The other two files in that folder (Tasks 2 and 3) reference `UnityEngine` and `MGSC`, which the test project cannot load — a glob would drag them in and break the test build.

- [ ] **Step 2: Write the failing tests**

`tests/QM_CompanyTechTiers.Tests/SpriteFileResolverTests.cs`:

```csharp
using System.IO;
using QM_CompanyTechTiers.Sprites;
using Xunit;

namespace QM_CompanyTechTiers.Tests
{
    public class SpriteFileResolverTests
    {
        private static string FreshFolder()
        {
            string dir = Path.Combine(Path.GetTempPath(), "qmctt_sprites_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public void FileNameFor_appends_png_to_the_tier_id()
        {
            Assert.Equal("anc_chip_low.png", SpriteFileResolver.FileNameFor("anc_chip_low"));
            Assert.Equal("rwa_chip_mid.png", SpriteFileResolver.FileNameFor("rwa_chip_mid"));
        }

        [Fact]
        public void PathFor_combines_the_folder_and_the_file_name()
        {
            string folder = FreshFolder();
            var resolver = new SpriteFileResolver(folder);
            Assert.Equal(Path.Combine(folder, "anc_chip_low.png"), resolver.PathFor("anc_chip_low"));
        }

        [Fact]
        public void HasArtFor_is_true_only_when_the_file_exists()
        {
            string folder = FreshFolder();
            var resolver = new SpriteFileResolver(folder);

            Assert.False(resolver.HasArtFor("anc_chip_low"));

            File.WriteAllBytes(Path.Combine(folder, "anc_chip_low.png"), new byte[] { 1, 2, 3 });

            Assert.True(resolver.HasArtFor("anc_chip_low"));
            Assert.False(resolver.HasArtFor("anc_chip_mid"));
        }

        [Fact]
        public void A_missing_folder_is_handled_without_throwing()
        {
            var resolver = new SpriteFileResolver(Path.Combine(Path.GetTempPath(), "qmctt_no_such_" + Path.GetRandomFileName()));
            Assert.False(resolver.HasArtFor("anc_chip_low"));
        }

        [Fact]
        public void A_null_or_empty_folder_yields_no_path_and_no_art()
        {
            foreach (var resolver in new[] { new SpriteFileResolver(null), new SpriteFileResolver("") })
            {
                Assert.Null(resolver.PathFor("anc_chip_low"));
                Assert.False(resolver.HasArtFor("anc_chip_low"));
            }
        }

        [Fact]
        public void A_null_or_empty_id_yields_no_path_and_no_art()
        {
            var resolver = new SpriteFileResolver(FreshFolder());
            Assert.Null(resolver.PathFor(null));
            Assert.Null(resolver.PathFor(""));
            Assert.False(resolver.HasArtFor(null));
        }
    }
}
```

Then extend the existing defaults test in `tests/QM_CompanyTechTiers.Tests/ModSettingsTests.cs` — find `Creates_the_file_with_documented_defaults_when_absent` and add one assertion alongside the others:

```csharp
            Assert.False(settings.DumpChipSpritesOnLoad);
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `cd "%USERPROFILE%/git/QM_CompanyTechTiers/tests/QM_CompanyTechTiers.Tests" && dotnet test --nologo`
Expected: compile failure — `The type or namespace name 'Sprites' does not exist in the namespace 'QM_CompanyTechTiers'`.

- [ ] **Step 4: Implement `SpriteFileResolver`**

`src/Sprites/SpriteFileResolver.cs`:

```csharp
using System.IO;

namespace QM_CompanyTechTiers.Sprites
{
    /// <summary>
    /// Maps a chip tier id to the PNG that overrides its inventory icon.
    /// Deliberately free of Unity types so the test project can link it.
    /// </summary>
    public sealed class SpriteFileResolver
    {
        private readonly string _folder;

        public SpriteFileResolver(string folder)
        {
            _folder = folder;
        }

        /// <summary>The one filename convention, shared by the dumper and the loader.</summary>
        public static string FileNameFor(string tierId)
        {
            return tierId + ".png";
        }

        /// <summary>Full path to a tier's art, or null when the folder or id is missing.</summary>
        public string PathFor(string tierId)
        {
            if (string.IsNullOrEmpty(_folder) || string.IsNullOrEmpty(tierId)) return null;
            return Path.Combine(_folder, FileNameFor(tierId));
        }

        public bool HasArtFor(string tierId)
        {
            string path = PathFor(tierId);
            if (path == null) return false;

            try
            {
                return File.Exists(path);
            }
            catch
            {
                // An unreadable path is simply "no art" - never a reason to break startup.
                return false;
            }
        }
    }
}
```

- [ ] **Step 5: Add the setting**

In `src/ModSettings.cs`, directly after the existing `DumpConfigsOnLoad` field, add:

```csharp
        /// <summary>Write each company chip's icon to sprite_dump/ as editable per-tier PNGs.</summary>
        public bool DumpChipSpritesOnLoad = false;
```

Do **not** add it to `Validate()` — a bool has no invalid value, and every existing `Validate()` rule is an array-length or ordering check.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd "%USERPROFILE%/git/QM_CompanyTechTiers/tests/QM_CompanyTechTiers.Tests" && dotnet test --nologo`
Expected: PASS, at least 58 tests, 0 skipped, no warnings.

- [ ] **Step 7: Commit**

```bash
cd "%USERPROFILE%/git/QM_CompanyTechTiers"
git add src/Sprites/SpriteFileResolver.cs src/ModSettings.cs tests/
git commit -m "feat: sprite file resolver and chip sprite dump setting"
```

---

### Task 2: Dump the parent icons as editable PNGs

This task's deliverable is the 24 PNGs themselves. No unit tests are possible — `Sprite`, `Texture2D` and `RenderTexture` cannot load outside the game — so it is verified by running the game and inspecting the output files.

**Files:**
- Create: `src/Sprites/SpriteDumper.cs`
- Modify: `src/QM_CompanyTechTiers.csproj` (add the `UnityEngine.ImageConversionModule` reference)
- Modify: `src/ResourceHook.cs` (call the dumper from `CloneDescriptors`)

**Interfaces:**
- Consumes: `ModSettings.DumpChipSpritesOnLoad`, `SpriteFileResolver.FileNameFor(string)` (Task 1).
- Produces: `QM_CompanyTechTiers.Sprites.SpriteDumper.ToPng(UnityEngine.Sprite sprite)` returning `byte[]`, or `null` when the sprite or its texture is null.

- [ ] **Step 1: Add the assembly reference**

`EncodeToPNG` and `LoadImage` are extension methods in `UnityEngine.ImageConversionModule`, which is **not** currently referenced. Confirmed present at
`C:\Program Files (x86)\Steam\steamapps\common\Quasimorph\Quasimorph_Data\Managed\UnityEngine.ImageConversionModule.dll`.

In `src/QM_CompanyTechTiers.csproj`, alongside the existing `UnityEngine.InputLegacyModule` reference, add:

```xml
		<Reference Include="UnityEngine.ImageConversionModule, Version=0.0.0.0, Culture=neutral, processorArchitecture=MSIL">
			<SpecificVersion>False</SpecificVersion>
			<HintPath>$(ManagedPath)UnityEngine.ImageConversionModule.dll</HintPath>
			<Private>False</Private>
		</Reference>
```

`<Private>False</Private>` matters: it stops the DLL being copied into the output and shipped, which would duplicate a game assembly.

- [ ] **Step 2: Implement the dumper**

`src/Sprites/SpriteDumper.cs`:

```csharp
using UnityEngine;

namespace QM_CompanyTechTiers.Sprites
{
    /// <summary>
    /// Reads a Sprite's pixels into PNG bytes.
    ///
    /// Sprite textures normally have isReadable = false, so Texture2D.GetPixels throws on them.
    /// Blitting through a RenderTexture reads the GPU copy instead and works regardless of the
    /// texture's import settings.
    /// </summary>
    public static class SpriteDumper
    {
        public static byte[] ToPng(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return null;

            Texture2D source = sprite.texture;

            // A packed sprite's own rect is in atlas space; textureRect is the sub-rect to read.
            Rect area = sprite.packed ? sprite.textureRect : sprite.rect;
            if (area.width < 1f || area.height < 1f) return null;

            RenderTexture buffer = RenderTexture.GetTemporary(
                source.width, source.height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Texture2D copy = null;

            try
            {
                Graphics.Blit(source, buffer);
                RenderTexture.active = buffer;

                copy = new Texture2D((int)area.width, (int)area.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(area.x, area.y, area.width, area.height), 0, 0);
                copy.Apply();

                return copy.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(buffer);
                if (copy != null) Object.Destroy(copy);
            }
        }
    }
}
```

- [ ] **Step 3: Call it from `CloneDescriptors`**

In `src/ResourceHook.cs`, add `using QM_CompanyTechTiers.Sprites;` to the usings, and a field beside the existing `_dumped`:

```csharp
        private static bool _spritesDumped;
```

Reset it in `Initialise` alongside the others:

```csharp
            _spritesDumped = false;
```

Then in `CloneDescriptors`, inside the `foreach (var chip in plan.Chips)` loop and inside the existing `foreach (var tier in ...)` loop, immediately after the `if (collection.TryGetDescriptor(...)) continue;` line, add:

```csharp
                    if (_settings.DumpChipSpritesOnLoad)
                        DumpSprite(chip.IdFor(tier), parent as ItemContentDescriptor);
```

And add this method to the class:

```csharp
        /// <summary>
        /// Writes the parent chip's inventory icon under a tier id, giving the author a pre-named
        /// copy to edit. Best-effort: a failure here must never disturb descriptor registration.
        /// </summary>
        private static void DumpSprite(string tierId, ItemContentDescriptor parent)
        {
            if (parent == null) return;

            try
            {
                string folder = Path.Combine(_dumpFolder, "..", "sprite_dump");
                Directory.CreateDirectory(folder);

                byte[] png = SpriteDumper.ToPng(parent.Icon);
                if (png == null) { _warn("No icon to dump for " + tierId + "."); return; }

                File.WriteAllBytes(Path.Combine(folder, SpriteFileResolver.FileNameFor(tierId)), png);

                if (!_spritesDumped)
                {
                    _spritesDumped = true;
                    _log("Chip sprite dump writing to " + Path.GetFullPath(folder));
                }
            }
            catch (Exception ex)
            {
                _warn("Sprite dump failed for " + tierId + ": " + ex.Message);
            }
        }
```

`_dumpFolder` is the config-dump folder (`<ModPersistenceFolder>/configdump`), so `..` puts `sprite_dump` beside it under the mod's persistence folder — the same writable location, without changing `Initialise`'s signature.

- [ ] **Step 4: Build and run the suite**

Run:
```bash
cd "%USERPROFILE%/git/QM_CompanyTechTiers/src" && dotnet build -c Release -v m -tl:off
cd "%USERPROFILE%/git/QM_CompanyTechTiers/tests/QM_CompanyTechTiers.Tests" && dotnet test --nologo
```
Expected: build succeeds with 0 warnings; the suite is unchanged and still green (this task adds no tests — say so plainly in your report rather than implying coverage).

- [ ] **Step 5: Produce the PNGs**

Set `"DumpChipSpritesOnLoad": true` in
`%LOCALAPPDATA%\..\LocalLow\Magnum Scriptum Ltd\Quasimorph_ModConfigs\QM_CompanyTechTiers\config.json`

Launch: `Start-Process "steam://rungameid/2059170"` (PowerShell).

The log is at `%USERPROFILE%\AppData\LocalLow\Magnum Scriptum Ltd\Quasimorph\Player.log` and is truncated on each launch. **Beware a stale-log race:** the previous run's content is on disk when you start polling, so confirm freshness (the `Game version:` banner reappearing, or the file's write time advancing) before trusting a match. Also confirm only one `Quasimorph` process is running — a leftover instance interleaves its writes and has produced misleading results in this project before. Wait for `GameModeStateMachine transition -> MainMenu`.

Close it:
```powershell
$p = Get-Process Quasimorph -ErrorAction SilentlyContinue
if ($p) { $p.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 4;
          $q = Get-Process Quasimorph -ErrorAction SilentlyContinue
          if ($q) { Stop-Process -Id $q.Id -Force } }
```

Then set `DumpChipSpritesOnLoad` back to `false`.

- [ ] **Step 6: Verify the dump — including orientation**

Count the files:
```bash
ls "%USERPROFILE%/AppData/LocalLow/Magnum Scriptum Ltd/Quasimorph_ModConfigs/QM_CompanyTechTiers/sprite_dump/" | wc -l
```
Expected: **24**, named `<prefix>_chip_low.png` and `<prefix>_chip_mid.png` for the twelve prefixes `anc rwa sbn ddr sun cor plb gra chu fra dil tia`.

**Then open at least two of them and look at them.** `Graphics.Blit` can flip vertically depending on the graphics API, and this is the check the spec calls out explicitly. If the images are upside-down, flip the `ReadPixels` source rect's Y origin in `SpriteDumper.ToPng`:

```csharp
                copy.ReadPixels(new Rect(area.x, source.height - area.y - area.height, area.width, area.height), 0, 0);
```

re-run, and confirm. Report which variant was correct — that fact matters to anyone reading this code later.

Also sanity-check the dimensions: chips are `InventoryWidthSize` 1, so expect roughly 50×50.

- [ ] **Step 7: Commit**

```bash
cd "%USERPROFILE%/git/QM_CompanyTechTiers"
git add src/Sprites/SpriteDumper.cs src/ResourceHook.cs src/QM_CompanyTechTiers.csproj
git commit -m "feat: dump company chip icons as per-tier PNGs"
```

The dumped PNGs live in the config folder, outside the repo — nothing to commit from `sprite_dump/`.

---

### Task 3: Load hand-edited art, and ship it with the mod

**Files:**
- Create: `src/Sprites/SpriteLoader.cs`
- Modify: `src/ResourceHook.cs` (`Initialise` gains a parameter; `CloneDescriptors` prefers custom art)
- Modify: `src/Plugin.cs` (resolve and pass the sprites folder)
- Modify: `src/QM_CompanyTechTiers.csproj` (copy `media/sprites` into the build output, the deploy folder, the Workshop folder and the release zip)
- Create: `media/sprites/.gitkeep`

**Interfaces:**
- Consumes: `SpriteFileResolver` (Task 1); `SpriteDumper` is untouched.
- Produces:
  - `QM_CompanyTechTiers.Sprites.SpriteLoader.TryBuildDescriptor(string pngPath, MGSC.ItemContentDescriptor parent, System.Action<string> warn)` returning `MGSC.DatadiskDescriptor` or `null`.
  - `ResourceHook.Initialise(ModSettings settings, string dumpFolder, string spritesFolder, Action<string> log, Action<string> warn)` — note the **third** parameter is new.

- [ ] **Step 1: Implement the loader**

`src/Sprites/SpriteLoader.cs`:

```csharp
using System;
using System.IO;
using System.Reflection;
using MGSC;
using UnityEngine;

namespace QM_CompanyTechTiers.Sprites
{
    /// <summary>
    /// Builds a DatadiskDescriptor whose inventory icon comes from a PNG on disk.
    ///
    /// ItemContentDescriptor's three sprite fields are private [SerializeField] with getters only,
    /// so they are assigned by reflection. The project carries BepInEx.AssemblyPublicizer, which
    /// would allow direct assignment at compile time, but that relies on the runtime not enforcing
    /// field access; reflection is guaranteed and this runs at most 24 times at startup.
    /// </summary>
    public static class SpriteLoader
    {
        private static readonly FieldInfo IconField = Field("_icon");
        private static readonly FieldInfo SmallIconField = Field("_smallIcon");
        private static readonly FieldInfo ShadowField = Field("_shadow");

        private static FieldInfo Field(string name)
        {
            return typeof(ItemContentDescriptor).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        }

        public static bool FieldsResolved
        {
            get { return IconField != null && SmallIconField != null && ShadowField != null; }
        }

        public static DatadiskDescriptor TryBuildDescriptor(string pngPath, ItemContentDescriptor parent, Action<string> warn)
        {
            if (parent == null) return null;

            if (!FieldsResolved)
            {
                warn("ItemContentDescriptor sprite fields not found by reflection (_icon/_smallIcon/_shadow). " +
                     "The game's field names have probably changed; custom chip art is disabled.");
                return null;
            }

            Sprite parentSprite = parent.Icon;
            if (parentSprite == null || parentSprite.rect.width < 1f || parentSprite.rect.height < 1f)
            {
                warn("Parent chip has no usable icon; keeping the shared descriptor.");
                return null;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(pngPath);
            }
            catch (Exception ex)
            {
                warn("Could not read '" + pngPath + "': " + ex.Message);
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes))
            {
                warn("'" + pngPath + "' is not a decodable image; keeping the shared descriptor.");
                Object.Destroy(texture);
                return null;
            }

            // Copied from the parent, not defaulted. This is a pixel-art game: a texture left on
            // Bilinear renders visibly blurry next to every other icon.
            texture.filterMode = parentSprite.texture != null ? parentSprite.texture.filterMode : FilterMode.Point;
            texture.Apply();

            // Sprite.pivot is in pixels; Sprite.Create wants a 0-1 fraction of the rect.
            var pivot = new Vector2(parentSprite.pivot.x / parentSprite.rect.width,
                                    parentSprite.pivot.y / parentSprite.rect.height);

            // pixelsPerUnit copied from the parent: getting it wrong renders a correct image at the
            // wrong size, which is a silent and confusing failure.
            Sprite sprite = Sprite.Create(texture,
                                          new Rect(0f, 0f, texture.width, texture.height),
                                          pivot,
                                          parentSprite.pixelsPerUnit);

            var descriptor = ScriptableObject.CreateInstance<DatadiskDescriptor>();
            IconField.SetValue(descriptor, sprite);
            SmallIconField.SetValue(descriptor, parent.SmallIcon);
            ShadowField.SetValue(descriptor, parent.ShadowOnFloor);
            return descriptor;
        }
    }
}
```

- [ ] **Step 2: Thread the sprites folder through `ResourceHook`**

In `src/ResourceHook.cs`, add a field beside `_dumpFolder`:

```csharp
        private static SpriteFileResolver _sprites;
```

Change `Initialise` to take the folder as its third parameter and build the resolver:

```csharp
        public static void Initialise(ModSettings settings, string dumpFolder, string spritesFolder,
                                      Action<string> log, Action<string> warn)
        {
            _settings = settings;
            _dumpFolder = dumpFolder;
            _sprites = new SpriteFileResolver(spritesFolder);
            _log = log ?? (_ => { });
            _warn = warn ?? (_ => { });
            _plan = null;
            _rewrites = null;
            _failed = false;
            _dumped = false;
            _spritesDumped = false;
        }
```

Then replace the body of the tier loop in `CloneDescriptors` so custom art is preferred and the log reports both counts:

```csharp
            foreach (var chip in plan.Chips)
            {
                UnityEngine.Object parent;
                if (!collection.TryGetDescriptor(chip.ParentId, out parent) || parent == null) continue;

                var parentDescriptor = parent as ItemContentDescriptor;

                foreach (var tier in new[] { Tier.Low, Tier.Mid })
                {
                    string id = chip.IdFor(tier);

                    UnityEngine.Object existing;
                    if (collection.TryGetDescriptor(id, out existing)) continue;

                    if (_settings.DumpChipSpritesOnLoad) DumpSprite(id, parentDescriptor);

                    UnityEngine.Object descriptor = parent;
                    if (_sprites != null && _sprites.HasArtFor(id))
                    {
                        var custom = SpriteLoader.TryBuildDescriptor(_sprites.PathFor(id), parentDescriptor, _warn);
                        if (custom != null) { descriptor = custom; customised++; }
                    }

                    collection.AddDescriptor(id, descriptor);
                    added++;
                }
            }

            _log("Registered " + added + " tier descriptors (" + customised + " with custom art).");
```

Declare `int customised = 0;` beside the existing `int added = 0;`.

- [ ] **Step 3: Resolve the folder in `Plugin`**

In `src/Plugin.cs`, replace the `ResourceHook.Initialise(...)` call with:

```csharp
            string spritesFolder = Path.Combine(ResolveModContentPath(context), "sprites");

            ResourceHook.Initialise(
                Settings,
                Path.Combine(ConfigDirectories.ModPersistenceFolder, "configdump"),
                spritesFolder,
                Logger.Log,
                Logger.LogWarning);

            Logger.Log("Armed. Config at " + ConfigDirectories.ConfigPath + "; sprites from " + spritesFolder);
```

and add this method to `Plugin`:

```csharp
        /// <summary>
        /// IModContext.ModContentPath is the mod's folder, but this mod has only ever read it from
        /// AfterConfigsLoaded. If it is not yet populated during BeforeBootstrap, fall back to the
        /// directory this assembly was loaded from, which is the same folder.
        /// </summary>
        private static string ResolveModContentPath(IModContext context)
        {
            string path = context != null ? context.ModContentPath : null;
            if (!string.IsNullOrEmpty(path)) return path;

            Logger.LogWarning("ModContentPath was empty during BeforeBootstrap; using the assembly folder.");
            return Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? string.Empty;
        }
```

The log line prints the resolved folder either way, so Step 6 can confirm which path was taken.

- [ ] **Step 4: Ship the art through the build**

Create `media/sprites/.gitkeep` (an empty file) so the folder exists in a fresh clone.

In `src/QM_CompanyTechTiers.csproj`, extend `ModBaseItems` to copy the art into the build output:

```xml
	<Target Name="ModBaseItems" AfterTargets="PostBuildEvent">
		<Copy SourceFiles="../modmanifest.json" DestinationFolder="$(TargetDir)" />
		<Copy SourceFiles="../media/thumbnail.png" DestinationFolder="$(TargetDir)" ContinueOnError="true" />
		<ItemGroup>
			<SpriteArt Include="../media/sprites/*.png" />
		</ItemGroup>
		<Copy SourceFiles="@(SpriteArt)" DestinationFolder="$(TargetDir)sprites/" Condition="'@(SpriteArt)'!=''" />
	</Target>
```

The `Condition` makes this a no-op while `media/sprites/` holds only `.gitkeep`, which is its state today and on any fresh clone — it must not fail the build the way the missing `thumbnail.png` did before.

Then add the subfolder to the three places that currently copy a **flat** file list. This is the step that is easy to miss: a flat glob silently skips a subfolder, so the art would work locally and vanish on Workshop publish.

In `LocalPresetsDeploy`, after the existing `Copy`:

```xml
		<ItemGroup>
			<LocalSpriteSource Include="$(TargetDir)sprites/*.png" />
		</ItemGroup>
		<Copy SourceFiles="@(LocalSpriteSource)" DestinationFolder="$(LocalPresetsPath)sprites/" Condition="'@(LocalSpriteSource)'!=''" />
```

In `SteamWorkshopDeploy`, after its existing `Copy`:

```xml
		<ItemGroup>
			<WorkshopSpriteSource Include="$(TargetDir)sprites/*.png" />
		</ItemGroup>
		<Copy SourceFiles="@(WorkshopSpriteSource)" DestinationFolder="$(WorkshopPath)sprites/" Condition="'@(WorkshopSpriteSource)'!=''" />
```

In `PostBuildPackage`, after its existing `Copy`:

```xml
		<ItemGroup>
			<PackageSpriteSource Include="$(WorkshopPath)sprites/*.png" />
		</ItemGroup>
		<Copy SourceFiles="@(PackageSpriteSource)" DestinationFolder="$(PackageFolder)$(TargetName)/sprites/" Condition="'@(PackageSpriteSource)'!=''" />
```

- [ ] **Step 5: Build and run the suite**

Run:
```bash
cd "%USERPROFILE%/git/QM_CompanyTechTiers/src" && dotnet build -c Release -v m -tl:off
cd "%USERPROFILE%/git/QM_CompanyTechTiers/tests/QM_CompanyTechTiers.Tests" && dotnet test --nologo
```
Expected: build succeeds with 0 warnings; suite still green.

- [ ] **Step 6: Verify in game — with art, and without**

**First, the regression case.** With `media/sprites/` still empty, launch the game (see Task 2 Step 5 for the launch, freshness and close procedure). The log must show:

```
[QM_CompanyTechTiers] Registered 24 tier descriptors (0 with custom art).
```

and no `ModContentPath was empty` warning unless the fallback genuinely fired — either way, note which happened, because that resolves an open question in the spec.

**Then the art case.** Take two of the PNGs dumped in Task 2 — `anc_chip_low.png` and `anc_chip_mid.png` — and make them *obviously* different from each other and from the original (fill them with flat colour; this is a mechanism test, not a design exercise). Put them in `media/sprites/`, rebuild, relaunch. The log must show:

```
[QM_CompanyTechTiers] Registered 24 tier descriptors (2 with custom art).
```

Then, in game, spawn all three AnCom tiers into ship cargo with the developer console (`~`):

```
item anc_chip_low
item anc_chip_mid
item anc_chip
```

and check three things by eye, each of which corresponds to a specific property the loader copies:

1. The two custom icons render at the **same size** as the vanilla chip — wrong `pixelsPerUnit` shows here.
2. They are **crisp, not blurry** — wrong `filterMode` shows here.
3. They sit **correctly within the inventory cell**, not offset — wrong pivot shows here.

Report what you saw for each. If something looks wrong, say so plainly and name which of the three it was rather than guessing at a fix.

- [ ] **Step 7: Commit**

```bash
cd "%USERPROFILE%/git/QM_CompanyTechTiers"
git add src/Sprites/SpriteLoader.cs src/ResourceHook.cs src/Plugin.cs src/QM_CompanyTechTiers.csproj media/sprites/.gitkeep
git commit -m "feat: load hand-edited chip tier art and ship it with the mod"
```

Do not commit the flat-colour test PNGs — they are scaffolding, not art.

---

### Task 4: Documentation and final verification

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-08-14-chip-tier-sprites-design.md` (reconcile anything the implementation settled)

**Interfaces:**
- Consumes: everything above.
- Produces: nothing code-facing.

- [ ] **Step 1: Document the authoring loop in the README**

Add a section describing, in this order: enable `DumpChipSpritesOnLoad`, launch once, edit the PNGs in
`%LOCALAPPDATA%\..\LocalLow\Magnum Scriptum Ltd\Quasimorph_ModConfigs\QM_CompanyTechTiers\sprite_dump\`,
copy the ones you changed into `media/sprites/` in the repo, rebuild, relaunch. State that only the
files you copy across are overridden and everything else keeps the shared parent icon, and that the
vanilla level-10 chip is never re-skinned.

Add `DumpChipSpritesOnLoad` to the README's config-key table with its `false` default, keeping the
table's existing format.

Check the config path you write matches the real one — it has **no** `Quasimorph\` segment before
`Quasimorph_ModConfigs`. That exact mistake was a finding in the previous plan's final review.

- [ ] **Step 2: Reconcile the spec**

Update `docs/superpowers/specs/2026-08-14-chip-tier-sprites-design.md` where implementation settled
something the spec left open:

- Under "Part 1 — Extraction", replace the orientation paragraph's "must be verified" wording with what was actually observed in Task 2 Step 6 — flipped or not flipped.
- Under "Part 3 — Plumbing", replace the `ModContentPath` "must be verified" wording with what Task 3 Step 6 observed — populated during `BeforeBootstrap`, or fallback used.

Leave the rest of the spec alone; it describes what was built.

- [ ] **Step 3: Full verification sweep**

Run:
```bash
cd "%USERPROFILE%/git/QM_CompanyTechTiers/tests/QM_CompanyTechTiers.Tests" && dotnet test --nologo
cd "%USERPROFILE%/git/QM_CompanyTechTiers/src" && dotnet build -c Release -v m -tl:off
cd "%USERPROFILE%/git/QM_CompanyTechTiers" && git status --short
```
Expected: all tests green and none skipped; build succeeds with 0 warnings; `git status` lists nothing under `tests/fixtures/` or `.superpowers/`.

- [ ] **Step 4: Commit**

```bash
cd "%USERPROFILE%/git/QM_CompanyTechTiers"
git add README.md docs/
git commit -m "docs: chip tier sprite authoring loop"
```

---

## Self-review notes

Checked against the spec:

- **Spec coverage.** Decisions 1–3 → Tasks 3, 3, and the Global Constraint forbidding new descriptors for vanilla ids. Part 1 (extraction) → Task 2. Part 2 (loading) → Task 3 Steps 1–2. Part 3 (plumbing) → Task 3 Steps 3–4. Module structure → the five files across Tasks 1–3. Testing → Task 1 (unit), Task 3 Step 6 (in-game, including the regression case). Out-of-scope items appear in no task, as intended.
- **The spec's module table lists `src/Sprites/SpriteLoader.cs` as depending on "Unity, reflection"** and that is what Task 3 builds. No file in the spec is unimplemented.
- **One deviation from the spec, deliberate:** the spec says the dump goes to `<ModPersistenceFolder>/sprite_dump/`, and Task 2 reaches it as `Path.Combine(_dumpFolder, "..", "sprite_dump")` rather than by adding a parameter to `Initialise`. Same destination, one less signature change. Task 3 then adds a parameter anyway for the *sprites* folder, so if the implementer prefers symmetry they may pass the dump folder explicitly instead — either is acceptable, and the report should say which was done.
- **Type consistency.** `SpriteFileResolver.FileNameFor` is static and used as such in Task 2 Step 3. `SpriteLoader.TryBuildDescriptor(string, ItemContentDescriptor, Action<string>)` is declared in Task 3's Interfaces block and called with exactly those arguments in Step 2. `ResourceHook.Initialise`'s new five-argument form is defined in Task 3 Step 2 and called with five arguments in Step 3.
- **Known gap, stated rather than hidden:** Tasks 2 and 3 add no unit tests, because `Sprite`, `Texture2D` and `RenderTexture` cannot be constructed outside the game. Their verification is running the game and looking at the result. The user has said they will report visual problems themselves, which is the honest completion of this loop — an agent cannot see the inventory screen.
