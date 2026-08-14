# Company Tech Tiers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split each Quasimorph company datadisk into three tiers offered as faction rewards at tech level 3, 6 and 10, mirroring the generic `low_chip` / `medium_chip` / `high_chip` breakdown.

**Architecture:** A single mod that rewrites the game's config text at load time through a `ModHookType.ResourcesLoad` hook, before `Data.Load()` parses it. All rewriting is pure `string -> string` and unit-tested against config dumps captured from the running game. Only three files touch Unity or `MGSC`.

**Tech Stack:** C# on `net48`, Harmony 2.3.3 (present but unused for game-logic patching), xunit 2.9.2 on net48, MSBuild auto-deploy to the game's local mod folder.

## Global Constraints

- Target framework is `net48`. The SDK defaults `LangVersion` to 7.3 on net48; the csproj files set `<LangVersion>latest</LangVersion>`. Do not use `record` or `init` accessors — they need an `IsExternalInit` polyfill on net48. Classic brace namespaces, matching template style.
- Game build targeted: **Quasimorph 1.0.1.566s.7e4da55**. Game path `C:\Program Files (x86)\Steam\steamapps\common\Quasimorph`.
- Mod unique name is `babyak_QM_CompanyTechTiers`. Never change it — it is the key `modprefs.json` uses.
- **Nothing may be hardcoded** that can be derived from config text: no item ids, faction names, chip ids, tech levels or table names. Tests must assert against values derived from the fixture, not literals copied into the source.
- Ship only `QM_CompanyTechTiers.dll`, `modmanifest.json`, `thumbnail.png`. Never bundle `Assembly-CSharp.dll`, `UnityEngine*.dll` or `0Harmony.dll` — the game supplies them.
- Config table format: CRLF line endings, tab-separated, rows padded with trailing empty cells to a fixed column count, **no trailing newline**. Round-trip fidelity is mandatory.
- Register at most **one method per `ModHookType`**. `UserModSystem.GrabMethods` double-registers the second and subsequent methods sharing a hook type, invoking them twice.
- Any failure inside the `ResourcesLoad` hook returns `null` (game falls back to the stock resource) and logs a warning. It must never throw.
- Tier ids are `<parent>_chip_low` and `<parent>_chip_mid`. The parent id (e.g. `anc_chip`) is never renamed.
- Unresolved unlock ids (present in `UnlockIds` but absent from the tech-level index) stay with the parent tier-3 chip.
- `tests/fixtures/` is gitignored. Game config files are Magnum Scriptum's assets and must never be committed.

**Reference material available in the repo:** the design spec at `docs/superpowers/specs/2026-08-14-company-tech-tiers-design.md`. A full decompile of `Assembly-CSharp.dll` is at the scratchpad path recorded in `tests/fixtures/README.md` (Task 1) — consult it rather than guessing at game APIs.

---

### Task 1: Test harness, fixtures, and `ConfigDocument` round-trip

The parser must reproduce input byte-for-byte before any mutation logic is trusted. That is the whole point of this task.

**Files:**
- Create: `src/Configs/ConfigDocument.cs`
- Create: `tests/QM_CompanyTechTiers.Tests/QM_CompanyTechTiers.Tests.csproj`
- Create: `tests/QM_CompanyTechTiers.Tests/Fixtures.cs`
- Create: `tests/QM_CompanyTechTiers.Tests/ConfigDocumentTests.cs`
- Create: `tests/fixtures/README.md`
- Modify: `src/QM_CompanyTechTiers.csproj` (add `<LangVersion>latest</LangVersion>`)

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `QM_CompanyTechTiers.Configs.ConfigDocument` with `static ConfigDocument Parse(string text)`, `string Render()`, `IReadOnlyList<ConfigSection> Sections { get; }`, `ConfigSection Section(string name)` (returns `null` when absent).
  - `QM_CompanyTechTiers.Configs.ConfigSection` with `string Name { get; }`, `IReadOnlyList<string> Columns { get; }`, `int ColumnIndex(string column)` (`-1` when absent), `IReadOnlyList<ConfigRow> Rows { get; }`.
  - `QM_CompanyTechTiers.Configs.ConfigRow` with `string Get(int index)` (empty string when out of range), `bool IsBlank { get; }`.
  - Test helper `Fixtures` with `static bool Available`, `static string Items`, `static string FactionDrops`, `static string Crafting`, and `FixtureFactAttribute`.

- [ ] **Step 1: Copy the captured fixtures into place and write the regeneration note**

The dump already exists from the exploration session. Copy it:

```bash
SRC="C:/Users/babya/AppData/Local/Temp/claude/C--Users-babya-git/94b2dbc8-a6e0-4652-ae38-25309e2d200a/scratchpad/configdump"
mkdir -p "C:/Users/babya/git/QM_CompanyTechTiers/tests/fixtures"
cp "$SRC/config_items.txt" "$SRC/config_faction_drops.txt" "$SRC/config_crafting.txt" \
   "$SRC/localization.txt" "C:/Users/babya/git/QM_CompanyTechTiers/tests/fixtures/"
```

`localization.txt` is ~12 MB. That is fine — the directory is gitignored.

If that scratchpad path no longer exists, regenerate by setting `"DumpConfigsOnLoad": true` in the mod's `config.json` (implemented in Task 7) and launching the game once.

Create `tests/fixtures/README.md`:

```markdown
# Config fixtures

These are Quasimorph's own config files, captured from the running game. They are
**gitignored on purpose** — they are Magnum Scriptum's assets, not ours.

Needed by the test suite:

- `config_items.txt`
- `config_faction_drops.txt`
- `config_crafting.txt`
- `localization.txt` (~12 MB)

## Regenerating

Set `"DumpConfigsOnLoad": true` in
`%LOCALAPPDATA%\..\LocalLow\Magnum Scriptum Ltd\Quasimorph_ModConfigs\QM_CompanyTechTiers\config.json`,
launch the game to the main menu, quit, then copy the files out of the dump folder
named in `Player.log`.

Captured from game build 1.0.1.566s.7e4da55.

## Reading game internals

A full decompile of `Assembly-CSharp.dll` is the fastest way to answer "what does the
game actually do here". Produce one with:

    dotnet tool install -g ilspycmd --version 9.0.0.7889
    DOTNET_ROLL_FORWARD=Major ilspycmd -p -o <outdir> \
      "C:/Program Files (x86)/Steam/steamapps/common/Quasimorph/Quasimorph_Data/Managed/Assembly-CSharp.dll"

Types worth reading first: `UserModSystem`, `ConfigLoader`, `CustomResources`,
`FactionDropCollection`, `DatadiskRecord`, `Localization`, `DescriptorsCollection`.
```

Without the fixtures the suite skips rather than fails — see Step 3.

- [ ] **Step 2: Add `<LangVersion>latest</LangVersion>` to the mod csproj**

In `src/QM_CompanyTechTiers.csproj`, inside the first `<PropertyGroup>` that already holds `<TargetFramework>net48</TargetFramework>`, add the line so it reads:

```xml
	<PropertyGroup>
		<TargetFramework>net48</TargetFramework>
		<OutputType>Library</OutputType>
		<GenerateAssemblyInfo>false</GenerateAssemblyInfo>
		<LangVersion>latest</LangVersion>
	</PropertyGroup>
```

- [ ] **Step 3: Create the test project and fixture helper**

`tests/QM_CompanyTechTiers.Tests/QM_CompanyTechTiers.Tests.csproj` — note it **links** the mod's pure sources rather than referencing the mod assembly. Referencing the assembly would drag in `Assembly-CSharp` and `UnityEngine`, which cannot load outside the game.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <IsPackable>false</IsPackable>
    <LangVersion>latest</LangVersion>
    <RootNamespace>QM_CompanyTechTiers.Tests</RootNamespace>
  </PropertyGroup>

  <!-- Link the Unity-free sources. Do NOT ProjectReference the mod. -->
  <ItemGroup>
    <Compile Include="../../src/Configs/**/*.cs" LinkBase="Linked/Configs" />
    <Compile Include="../../src/Tiering/**/*.cs" LinkBase="Linked/Tiering" />
    <Compile Include="../../src/Rewriting/**/*.cs" LinkBase="Linked/Rewriting" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
</Project>
```

`tests/QM_CompanyTechTiers.Tests/Fixtures.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace QM_CompanyTechTiers.Tests
{
    public static class Fixtures
    {
        private static readonly string Root = FindRoot();

        private static string FindRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "tests", "fixtures");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return null;
        }

        private static readonly string[] Required =
        {
            "config_items.txt", "config_faction_drops.txt", "config_crafting.txt", "localization.txt",
        };

        public static bool Available =>
            Root != null && Required.All(f => File.Exists(Path.Combine(Root, f)));

        private static string Read(string name) => File.ReadAllText(Path.Combine(Root, name));

        public static string Items => Read("config_items.txt");
        public static string FactionDrops => Read("config_faction_drops.txt");
        public static string Crafting => Read("config_crafting.txt");
        public static string LocalizationTable => Read("localization.txt");
    }

    /// <summary>A Fact that skips itself when the game config fixtures are not present.</summary>
    public sealed class FixtureFactAttribute : FactAttribute
    {
        public FixtureFactAttribute()
        {
            if (!Fixtures.Available)
                Skip = "Config fixtures missing. See tests/fixtures/README.md.";
        }
    }
}
```

- [ ] **Step 4: Write the failing round-trip tests**

`tests/QM_CompanyTechTiers.Tests/ConfigDocumentTests.cs`:

```csharp
using System.Linq;
using QM_CompanyTechTiers.Configs;
using Xunit;

namespace QM_CompanyTechTiers.Tests
{
    public class ConfigDocumentTests
    {
        [Fact]
        public void Parses_sections_headers_and_rows()
        {
            string text = "#alpha\t\t\r\nId\tValue\t\r\na\t1\t\r\n#end\t\t\r\n#beta\t\r\nId\t\r\nb\t\r\n#end\t";
            var doc = ConfigDocument.Parse(text);

            Assert.Equal(new[] { "alpha", "beta" }, doc.Sections.Select(s => s.Name).ToArray());
            Assert.Equal(new[] { "Id", "Value", "" }, doc.Section("alpha").Columns.ToArray());
            Assert.Equal(1, doc.Section("alpha").Rows.Count);
            Assert.Equal("a", doc.Section("alpha").Rows[0].Get(0));
            Assert.Equal("1", doc.Section("alpha").Rows[0].Get(1));
            Assert.Equal(1, doc.Section("alpha").ColumnIndex("Value"));
            Assert.Equal(-1, doc.Section("alpha").ColumnIndex("Nope"));
            Assert.Null(doc.Section("missing"));
        }

        [Fact]
        public void Get_past_end_of_row_returns_empty_string()
        {
            var doc = ConfigDocument.Parse("#s\r\nId\tValue\r\nonly\r\n#end");
            Assert.Equal("", doc.Section("s").Rows[0].Get(1));
            Assert.Equal("", doc.Section("s").Rows[0].Get(99));
        }

        [Fact]
        public void Blank_lines_between_sections_survive_and_are_not_rows()
        {
            var doc = ConfigDocument.Parse("#a\r\nId\r\nx\r\n#end\r\n\r\n#b\r\nId\r\ny\r\n#end");
            Assert.Equal(1, doc.Section("a").Rows.Count);
            Assert.Equal(1, doc.Section("b").Rows.Count);
        }

        [FixtureFact]
        public void Round_trips_every_real_config_byte_for_byte()
        {
            foreach (string text in new[] { Fixtures.Items, Fixtures.FactionDrops, Fixtures.Crafting })
                Assert.Equal(text, ConfigDocument.Parse(text).Render());
        }

        [FixtureFact]
        public void Finds_the_known_sections_in_the_real_configs()
        {
            Assert.NotNull(ConfigDocument.Parse(Fixtures.Items).Section("datadisks"));
            Assert.NotNull(ConfigDocument.Parse(Fixtures.Crafting).Section("itemreceipts"));

            var drops = ConfigDocument.Parse(Fixtures.FactionDrops);
            Assert.Contains(drops.Sections, s => s.Name == "factiondrop_AnCom_rewardChips");
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they fail**

Run: `cd "C:/Users/babya/git/QM_CompanyTechTiers/tests/QM_CompanyTechTiers.Tests" && dotnet test --nologo`
Expected: compile failure — `The type or namespace name 'Configs' does not exist in the namespace 'QM_CompanyTechTiers'`.

- [ ] **Step 6: Implement `ConfigDocument`**

`src/Configs/ConfigDocument.cs`. The design keeps the original lines and marks which are rows, so an unmodified document renders identically. `Split('\n')` leaves a trailing `'\r'` on every line; that `'\r'` is preserved as part of the line and re-emitted.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace QM_CompanyTechTiers.Configs
{
    /// <summary>
    /// One row of a config table. Cells are the tab-split contents with the trailing
    /// carriage return stripped; <see cref="HadCarriageReturn"/> remembers whether to put it back.
    /// </summary>
    public sealed class ConfigRow
    {
        internal string[] Cells;
        internal readonly bool HadCarriageReturn;
        internal bool Dirty;

        internal ConfigRow(string[] cells, bool hadCarriageReturn)
        {
            Cells = cells;
            HadCarriageReturn = hadCarriageReturn;
        }

        public string Get(int index) =>
            index >= 0 && index < Cells.Length ? Cells[index] : string.Empty;

        public bool IsBlank => Cells.All(c => c.Length == 0);

        internal string Render() =>
            string.Join("\t", Cells) + (HadCarriageReturn ? "\r" : string.Empty);
    }

    /// <summary>A <c>#name ... #end</c> block: a header row of column names followed by data rows.</summary>
    public sealed class ConfigSection
    {
        private readonly List<ConfigRow> _rows = new List<ConfigRow>();
        private readonly List<string> _columns;

        public string Name { get; }
        public IReadOnlyList<string> Columns => _columns;
        public IReadOnlyList<ConfigRow> Rows => _rows;

        /// <summary>Index of the document line holding the column header. Rows follow it.</summary>
        internal int HeaderLineIndex;
        internal readonly List<int> RowLineIndexes = new List<int>();

        internal ConfigSection(string name, List<string> columns, int headerLineIndex)
        {
            Name = name;
            _columns = columns;
            HeaderLineIndex = headerLineIndex;
        }

        public int ColumnIndex(string column)
        {
            for (int i = 0; i < _columns.Count; i++)
                if (string.Equals(_columns[i], column, StringComparison.Ordinal))
                    return i;
            return -1;
        }

        internal void AddRow(ConfigRow row, int lineIndex)
        {
            _rows.Add(row);
            RowLineIndexes.Add(lineIndex);
        }
    }

    /// <summary>
    /// Quasimorph's tab-separated config format. Parsing mirrors <c>MGSC.ConfigLoader.LoadSpecificFile</c>:
    /// blank lines and <c>//</c> comments are ignored, a line containing <c>#end</c> closes a section,
    /// any other line starting with <c>#</c> opens one, and the first line after that is the header.
    /// </summary>
    public sealed class ConfigDocument
    {
        private readonly List<string> _lines;
        private readonly List<ConfigSection> _sections;

        private ConfigDocument(List<string> lines, List<ConfigSection> sections)
        {
            _lines = lines;
            _sections = sections;
        }

        public IReadOnlyList<ConfigSection> Sections => _sections;

        public ConfigSection Section(string name)
        {
            foreach (var s in _sections)
                if (string.Equals(s.Name, name, StringComparison.Ordinal))
                    return s;
            return null;
        }

        public static ConfigDocument Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            var lines = text.Split('\n').ToList();
            var sections = new List<ConfigSection>();

            ConfigSection current = null;
            bool expectHeader = false;

            for (int i = 0; i < lines.Count; i++)
            {
                string raw = lines[i];
                string body = raw.TrimEnd('\r');

                if (body.Length == 0 || body.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    continue;

                if (body.Contains("#end"))
                {
                    current = null;
                    expectHeader = false;
                    continue;
                }

                if (body[0] == '#')
                {
                    string name = body.Trim('\t', '\r', '\n', '#');
                    current = new ConfigSection(name, body.Split('\t').ToList(), i);
                    sections.Add(current);
                    expectHeader = true;
                    continue;
                }

                if (current == null) continue;

                if (expectHeader)
                {
                    expectHeader = false;
                    var columns = body.Split('\t').ToList();
                    var replacement = new ConfigSection(current.Name, columns, i);
                    sections[sections.Count - 1] = replacement;
                    current = replacement;
                    continue;
                }

                current.AddRow(new ConfigRow(body.Split('\t'), raw.EndsWith("\r", StringComparison.Ordinal)), i);
            }

            return new ConfigDocument(lines, sections);
        }

        public string Render()
        {
            foreach (var section in _sections)
                for (int r = 0; r < section.Rows.Count; r++)
                    if (((ConfigRow)section.Rows[r]).Dirty)
                        _lines[section.RowLineIndexes[r]] = ((ConfigRow)section.Rows[r]).Render();

            return string.Join("\n", _lines);
        }
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `cd "C:/Users/babya/git/QM_CompanyTechTiers/tests/QM_CompanyTechTiers.Tests" && dotnet test --nologo`
Expected: PASS, 5 tests, 0 skipped (fixtures were copied in Step 1).

If `Round_trips_every_real_config_byte_for_byte` fails, the bug is in line-ending or trailing-cell handling — compare `text.Length` with `Render().Length` and bisect on the first differing index. Do not "fix" it by normalising line endings; the game's parser sees the raw bytes.

- [ ] **Step 8: Commit**

```bash
cd "C:/Users/babya/git/QM_CompanyTechTiers"
git add src/Configs/ConfigDocument.cs src/QM_CompanyTechTiers.csproj tests/ .gitignore
git commit -m "feat: config table parser with byte-exact round-trip"
```

---

### Task 2: Row mutation and insertion

`ConfigDocument` can currently only read. The rewriters need to change cells and add rows.

**Files:**
- Modify: `src/Configs/ConfigDocument.cs`
- Modify: `tests/QM_CompanyTechTiers.Tests/ConfigDocumentTests.cs`

**Interfaces:**
- Consumes: `ConfigDocument`, `ConfigSection`, `ConfigRow` from Task 1.
- Produces:
  - `ConfigRow.Set(int index, string value)` — widens the row with empty cells if needed.
  - `ConfigSection.InsertRowAfter(ConfigRow existing, IEnumerable<string> cells)` — inserts immediately below `existing`, padded to the section's column count, returns the new `ConfigRow`.

- [ ] **Step 1: Write the failing tests**

Append to `ConfigDocumentTests.cs` inside the class:

```csharp
        [Fact]
        public void Set_changes_a_cell_and_render_reflects_it()
        {
            var doc = ConfigDocument.Parse("#s\r\nId\tValue\r\na\t1\r\n#end");
            doc.Section("s").Rows[0].Set(1, "42");
            Assert.Equal("#s\r\nId\tValue\r\na\t42\r\n#end", doc.Render());
        }

        [Fact]
        public void Set_past_end_widens_the_row()
        {
            var doc = ConfigDocument.Parse("#s\r\nId\tA\tB\r\nx\r\n#end");
            doc.Section("s").Rows[0].Set(2, "z");
            Assert.Equal("#s\r\nId\tA\tB\r\nx\t\tz\r\n#end", doc.Render());
        }

        [Fact]
        public void InsertRowAfter_places_the_row_directly_below_and_pads_to_column_count()
        {
            var doc = ConfigDocument.Parse("#s\r\nId\tA\tB\r\nfirst\t1\t2\r\nsecond\t3\t4\r\n#end");
            var section = doc.Section("s");
            section.InsertRowAfter(section.Rows[0], new[] { "new", "9" });

            Assert.Equal("#s\r\nId\tA\tB\r\nfirst\t1\t2\r\nnew\t9\t\r\nsecond\t3\t4\r\n#end", doc.Render());
            Assert.Equal(3, section.Rows.Count);
            Assert.Equal("new", section.Rows[1].Get(0));
        }

        [Fact]
        public void InsertRowAfter_keeps_later_sections_intact()
        {
            var doc = ConfigDocument.Parse("#a\r\nId\r\nx\r\n#end\r\n#b\r\nId\r\ny\r\n#end");
            var a = doc.Section("a");
            a.InsertRowAfter(a.Rows[0], new[] { "inserted" });

            Assert.Equal("#a\r\nId\r\nx\r\ninserted\r\n#end\r\n#b\r\nId\r\ny\r\n#end", doc.Render());
            Assert.Equal(1, doc.Section("b").Rows.Count);
            Assert.Equal("y", doc.Section("b").Rows[0].Get(0));
        }

        [FixtureFact]
        public void Inserting_into_a_real_config_changes_only_the_expected_line_count()
        {
            var doc = ConfigDocument.Parse(Fixtures.FactionDrops);
            var section = doc.Section("factiondrop_AnCom_rewardChips");
            int before = Fixtures.FactionDrops.Split('\n').Length;

            section.InsertRowAfter(section.Rows[0], new[] { "3", "test_chip", "10", "300" });

            Assert.Equal(before + 1, doc.Render().Split('\n').Length);
            Assert.Equal(section.Rows.Count, ConfigDocument.Parse(doc.Render())
                .Section("factiondrop_AnCom_rewardChips").Rows.Count);
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd "C:/Users/babya/git/QM_CompanyTechTiers/tests/QM_CompanyTechTiers.Tests" && dotnet test --nologo`
Expected: compile failure — `'ConfigRow' does not contain a definition for 'Set'`.

- [ ] **Step 3: Implement `Set`**

In `ConfigRow`, add after `Get`:

```csharp
        public void Set(int index, string value)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            if (index >= Cells.Length)
            {
                var widened = new string[index + 1];
                Array.Copy(Cells, widened, Cells.Length);
                for (int i = Cells.Length; i < widened.Length; i++) widened[i] = string.Empty;
                Cells = widened;
            }
            Cells[index] = value;
            Dirty = true;
        }
```

- [ ] **Step 4: Implement insertion**

Insertion shifts every later line index, so the document owns it. In `ConfigSection`, add a back-reference and the public entry point:

```csharp
        internal ConfigDocument Owner;

        public ConfigRow InsertRowAfter(ConfigRow existing, IEnumerable<string> cells)
        {
            int position = _rows.IndexOf(existing);
            if (position < 0) throw new ArgumentException("Row does not belong to this section.", nameof(existing));

            var padded = cells.ToList();
            while (padded.Count < _columns.Count) padded.Add(string.Empty);

            var row = new ConfigRow(padded.ToArray(), hadCarriageReturn: true);
            int lineIndex = RowLineIndexes[position] + 1;

            Owner.InsertLine(lineIndex, row.Render());
            _rows.Insert(position + 1, row);
            RowLineIndexes.Insert(position + 1, lineIndex);
            return row;
        }
```

In `ConfigDocument`, add the line-shifting operation:

```csharp
        internal void InsertLine(int lineIndex, string content)
        {
            _lines.Insert(lineIndex, content);
            foreach (var section in _sections)
            {
                if (section.HeaderLineIndex >= lineIndex) section.HeaderLineIndex++;
                for (int i = 0; i < section.RowLineIndexes.Count; i++)
                    if (section.RowLineIndexes[i] >= lineIndex)
                        section.RowLineIndexes[i]++;
            }
        }
```

Wire `Owner` in `Parse` — set it at both places a section is constructed, i.e. immediately after `sections.Add(current);`:

```csharp
                    current.Owner = null; // assigned below once the document exists
```

That does not work, because the document is built last. Instead, assign owners just before returning. Replace the final line of `Parse`:

```csharp
            var document = new ConfigDocument(lines, sections);
            foreach (var section in sections) section.Owner = document;
            return document;
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd "C:/Users/babya/git/QM_CompanyTechTiers/tests/QM_CompanyTechTiers.Tests" && dotnet test --nologo`
Expected: PASS, 10 tests.

- [ ] **Step 6: Commit**

```bash
cd "C:/Users/babya/git/QM_CompanyTechTiers"
git add src/Configs/ConfigDocument.cs tests/
git commit -m "feat: cell mutation and row insertion for config tables"
```

---

### Task 3: `ChipTierPlan` — discovery, tech index, partition

The heart of the mod. Everything downstream reads this plan.

**Files:**
- Create: `src/Tiering/ChipTierPlan.cs`
- Create: `tests/QM_CompanyTechTiers.Tests/ChipTierPlanTests.cs`

**Interfaces:**
- Consumes: `ConfigDocument` from Task 1.
- Produces:
  - `QM_CompanyTechTiers.Tiering.Tier` — enum `{ Low = 0, Mid = 1, High = 2 }`.
  - `QM_CompanyTechTiers.Tiering.CompanyChip` with `string ParentId`, `IReadOnlyList<string> LowUnlocks`, `IReadOnlyList<string> MidUnlocks`, `IReadOnlyList<string> HighUnlocks`, `string IdFor(Tier tier)` (returns `ParentId + "_chip_low"`-style ids by stripping the trailing `_chip`; `Tier.High` returns `ParentId` unchanged).
  - `QM_CompanyTechTiers.Tiering.ChipTierPlan` with `static ChipTierPlan Build(ConfigDocument items, int lowMax, int midMax)`, `IReadOnlyList<CompanyChip> Chips`, `IReadOnlyDictionary<string,int> ItemTechLevels`, `Tier TierOfItem(string itemId)` (unknown items return `Tier.High`), `CompanyChip ChipByParentId(string parentId)` (null when absent), `bool ContainsChipId(string anyTierId)`.

- [ ] **Step 1: Write the failing tests**

`tests/QM_CompanyTechTiers.Tests/ChipTierPlanTests.cs`:

```csharp
using System.Linq;
using QM_CompanyTechTiers.Configs;
using QM_CompanyTechTiers.Tiering;
using Xunit;

namespace QM_CompanyTechTiers.Tests
{
    public class ChipTierPlanTests
    {
        private static ChipTierPlan RealPlan() =>
            ChipTierPlan.Build(ConfigDocument.Parse(Fixtures.Items), lowMax: 3, midMax: 6);

        [FixtureFact]
        public void Discovers_only_company_chips_and_never_a_generic_one()
        {
            var plan = RealPlan();
            var ids = plan.Chips.Select(c => c.ParentId).ToList();

            Assert.Equal(12, ids.Count);
            foreach (string generic in new[] { "low_chip", "medium_chip", "high_chip",
                                               "mercenary_chip", "class_chip", "augment_chip" })
                Assert.DoesNotContain(generic, ids);

            // Derived from the fixture, not asserted as a hardcoded roster:
            // every discovered chip must be a datadisk whose Categories lack the token "Chip".
            var datadisks = ConfigDocument.Parse(Fixtures.Items).Section("datadisks");
            int idCol = datadisks.ColumnIndex("Id");
            int catCol = datadisks.ColumnIndex("Categories");
            foreach (var row in datadisks.Rows.Where(r => !r.IsBlank && r.Get(idCol).Length > 0))
            {
                bool isGeneric = row.Get(catCol).Split(' ').Contains("Chip");
                Assert.Equal(!isGeneric, ids.Contains(row.Get(idCol)));
            }
        }

        [FixtureFact]
        public void Partition_loses_nothing_and_duplicates_nothing()
        {
            var plan = RealPlan();
            var datadisks = ConfigDocument.Parse(Fixtures.Items).Section("datadisks");
            int idCol = datadisks.ColumnIndex("Id");
            int unlockCol = datadisks.ColumnIndex("UnlockIds");

            foreach (var chip in plan.Chips)
            {
                var original = datadisks.Rows.First(r => r.Get(idCol) == chip.ParentId)
                    .Get(unlockCol).Split(' ').Where(s => s.Length > 0).ToList();

                var recombined = chip.LowUnlocks.Concat(chip.MidUnlocks).Concat(chip.HighUnlocks).ToList();

                Assert.Equal(original.OrderBy(x => x), recombined.OrderBy(x => x));
                Assert.Equal(recombined.Count, recombined.Distinct().Count());
            }
        }

        [FixtureFact]
        public void Every_unlock_lands_in_the_tier_its_tech_level_implies()
        {
            var plan = RealPlan();
            foreach (var chip in plan.Chips)
            {
                foreach (string id in chip.LowUnlocks)
                    Assert.InRange(plan.ItemTechLevels[id], 1, 3);
                foreach (string id in chip.MidUnlocks)
                    Assert.InRange(plan.ItemTechLevels[id], 4, 6);
                foreach (string id in chip.HighUnlocks)
                    Assert.True(!plan.ItemTechLevels.ContainsKey(id) || plan.ItemTechLevels[id] >= 7);
            }
        }

        [FixtureFact]
        public void Unresolved_unlock_ids_fall_to_the_high_tier()
        {
            var plan = RealPlan();
            var unresolved = plan.Chips.SelectMany(c => c.HighUnlocks)
                                       .Where(id => !plan.ItemTechLevels.ContainsKey(id))
                                       .ToList();
            Assert.All(unresolved, id => Assert.Equal(Tier.High, plan.TierOfItem(id)));
        }

        [FixtureFact]
        public void Tech_index_covers_the_whole_items_config()
        {
            Assert.True(RealPlan().ItemTechLevels.Count > 1000);
        }

        [FixtureFact]
        public void Tier_ids_are_derived_from_the_parent_id()
        {
            var chip = RealPlan().Chips.First();
            string stem = chip.ParentId.Substring(0, chip.ParentId.Length - "_chip".Length);

            Assert.Equal(stem + "_chip_low", chip.IdFor(Tier.Low));
            Assert.Equal(stem + "_chip_mid", chip.IdFor(Tier.Mid));
            Assert.Equal(chip.ParentId, chip.IdFor(Tier.High));
        }

        [FixtureFact]
        public void Boundaries_are_configurable()
        {
            var wide = ChipTierPlan.Build(ConfigDocument.Parse(Fixtures.Items), lowMax: 6, midMax: 6);
            foreach (var chip in wide.Chips)
                Assert.Empty(chip.MidUnlocks);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd "C:/Users/babya/git/QM_CompanyTechTiers/tests/QM_CompanyTechTiers.Tests" && dotnet test --nologo`
Expected: compile failure — `The type or namespace name 'Tiering' does not exist`.

- [ ] **Step 3: Implement `ChipTierPlan`**

`src/Tiering/ChipTierPlan.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using QM_CompanyTechTiers.Configs;

namespace QM_CompanyTechTiers.Tiering
{
    public enum Tier { Low = 0, Mid = 1, High = 2 }

    /// <summary>A company datadisk and its unlock list split by the tech level of each unlocked item.</summary>
    public sealed class CompanyChip
    {
        private const string ChipSuffix = "_chip";

        public string ParentId { get; }
        public IReadOnlyList<string> LowUnlocks { get; }
        public IReadOnlyList<string> MidUnlocks { get; }
        public IReadOnlyList<string> HighUnlocks { get; }

        internal CompanyChip(string parentId, List<string> low, List<string> mid, List<string> high)
        {
            ParentId = parentId;
            LowUnlocks = low;
            MidUnlocks = mid;
            HighUnlocks = high;
        }

        /// <summary>
        /// Tier ids are built from the parent stem so they read as siblings: anc_chip -> anc_chip_low.
        /// The high tier keeps the original id so the 236 existing recipes referencing it stay valid.
        /// </summary>
        public string IdFor(Tier tier)
        {
            if (tier == Tier.High) return ParentId;

            string stem = ParentId.EndsWith(ChipSuffix, StringComparison.Ordinal)
                ? ParentId.Substring(0, ParentId.Length - ChipSuffix.Length)
                : ParentId;

            return stem + ChipSuffix + (tier == Tier.Low ? "_low" : "_mid");
        }

        public IReadOnlyList<string> UnlocksFor(Tier tier) =>
            tier == Tier.Low ? LowUnlocks : tier == Tier.Mid ? MidUnlocks : HighUnlocks;
    }

    public sealed class ChipTierPlan
    {
        /// <summary>Token that marks a datadisk as one of the generic research chips.</summary>
        private const string GenericChipCategory = "Chip";

        private readonly Dictionary<string, CompanyChip> _byParentId;
        private readonly HashSet<string> _allTierIds;
        private readonly int _lowMax;
        private readonly int _midMax;

        public IReadOnlyList<CompanyChip> Chips { get; }
        public IReadOnlyDictionary<string, int> ItemTechLevels { get; }

        private ChipTierPlan(List<CompanyChip> chips, Dictionary<string, int> techLevels, int lowMax, int midMax)
        {
            Chips = chips;
            ItemTechLevels = techLevels;
            _lowMax = lowMax;
            _midMax = midMax;
            _byParentId = chips.ToDictionary(c => c.ParentId, StringComparer.Ordinal);
            _allTierIds = new HashSet<string>(
                chips.SelectMany(c => new[] { c.IdFor(Tier.Low), c.IdFor(Tier.Mid), c.IdFor(Tier.High) }),
                StringComparer.Ordinal);
        }

        public CompanyChip ChipByParentId(string parentId) =>
            _byParentId.TryGetValue(parentId, out var chip) ? chip : null;

        public bool ContainsChipId(string anyTierId) => _allTierIds.Contains(anyTierId);

        public Tier TierOfItem(string itemId)
        {
            if (!ItemTechLevels.TryGetValue(itemId, out int techLevel)) return Tier.High;
            if (techLevel <= _lowMax) return Tier.Low;
            if (techLevel <= _midMax) return Tier.Mid;
            return Tier.High;
        }

        public static ChipTierPlan Build(ConfigDocument items, int lowMax, int midMax)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            var techLevels = IndexTechLevels(items);

            var datadisks = items.Section("datadisks");
            if (datadisks == null)
                throw new InvalidOperationException("config_items has no #datadisks section.");

            int idCol = datadisks.ColumnIndex("Id");
            int categoriesCol = datadisks.ColumnIndex("Categories");
            int unlockCol = datadisks.ColumnIndex("UnlockIds");
            if (idCol < 0 || categoriesCol < 0 || unlockCol < 0)
                throw new InvalidOperationException("#datadisks is missing Id, Categories or UnlockIds.");

            var chips = new List<CompanyChip>();
            foreach (var row in datadisks.Rows)
            {
                string id = row.Get(idCol);
                if (id.Length == 0) continue;

                var categories = Tokens(row.Get(categoriesCol));
                if (categories.Contains(GenericChipCategory)) continue;   // generic research chip

                var low = new List<string>();
                var mid = new List<string>();
                var high = new List<string>();

                foreach (string unlockId in Tokens(row.Get(unlockCol)))
                {
                    if (!techLevels.TryGetValue(unlockId, out int level)) { high.Add(unlockId); continue; }
                    if (level <= lowMax) low.Add(unlockId);
                    else if (level <= midMax) mid.Add(unlockId);
                    else high.Add(unlockId);
                }

                chips.Add(new CompanyChip(id, low, mid, high));
            }

            if (chips.Count == 0)
                throw new InvalidOperationException(
                    "No company chips found in #datadisks. The config format has probably changed.");

            return new ChipTierPlan(chips, techLevels, lowMax, midMax);
        }

        /// <summary>Every section of config_items that has both Id and TechLevel columns contributes.</summary>
        private static Dictionary<string, int> IndexTechLevels(ConfigDocument items)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var section in items.Sections)
            {
                int idCol = section.ColumnIndex("Id");
                int techCol = section.ColumnIndex("TechLevel");
                if (idCol < 0 || techCol < 0) continue;

                foreach (var row in section.Rows)
                {
                    string id = row.Get(idCol);
                    if (id.Length == 0) continue;
                    if (int.TryParse(row.Get(techCol), out int level)) map[id] = level;
                }
            }

            return map;
        }

        private static List<string> Tokens(string cell) =>
            cell.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd "C:/Users/babya/git/QM_CompanyTechTiers/tests/QM_CompanyTechTiers.Tests" && dotnet test --nologo`
Expected: PASS, 17 tests.

If `Discovers_only_company_chips` reports 13 or 11, do not adjust the expected count — inspect which row differs and fix the predicate. The count 12 is asserted alongside a row-by-row check that re-derives the answer from the fixture, so a genuine game change will fail both together and that is the signal to update the spec.

- [ ] **Step 5: Commit**

```bash
cd "C:/Users/babya/git/QM_CompanyTechTiers"
git add src/Tiering/ChipTierPlan.cs tests/
git commit -m "feat: company chip discovery and tech-level partition"
```

---

### Task 4: Items rewriter — emit tier rows, shrink parent unlocks

**Files:**
- Create: `src/Rewriting/ItemsRewriter.cs`
- Create: `tests/QM_CompanyTechTiers.Tests/ItemsRewriterTests.cs`

**Interfaces:**
- Consumes: `ConfigDocument` (Task 1-2), `ChipTierPlan`, `CompanyChip`, `Tier` (Task 3).
- Produces: `QM_CompanyTechTiers.Rewriting.ItemsRewriter` with `static string Rewrite(string itemsText, ChipTierPlan plan, int[] tierPrices, int[] tierTechLevels)`. `tierPrices` and `tierTechLevels` are length-3, indexed by `(int)Tier`; index 2 is ignored because the parent row keeps its own values.

- [ ] **Step 1: Write the failing tests**

`tests/QM_CompanyTechTiers.Tests/ItemsRewriterTests.cs`:

```csharp
using System.Linq;
using QM_CompanyTechTiers.Configs;
using QM_CompanyTechTiers.Rewriting;
using QM_CompanyTechTiers.Tiering;
using Xunit;

namespace QM_CompanyTechTiers.Tests
{
    public class ItemsRewriterTests
    {
        private static readonly int[] Prices = { 400, 525, 650 };
        private static readonly int[] TechLevels = { 1, 4, 10 };

        private static string Rewritten()
        {
            var plan = ChipTierPlan.Build(ConfigDocument.Parse(Fixtures.Items), 3, 6);
            return ItemsRewriter.Rewrite(Fixtures.Items, plan, Prices, TechLevels);
        }

        [FixtureFact]
        public void Adds_two_rows_per_company_chip()
        {
            var before = ConfigDocument.Parse(Fixtures.Items).Section("datadisks");
            var after = ConfigDocument.Parse(Rewritten()).Section("datadisks");
            var plan = ChipTierPlan.Build(ConfigDocument.Parse(Fixtures.Items), 3, 6);

            Assert.Equal(before.Rows.Count + plan.Chips.Count * 2, after.Rows.Count);
        }

        [FixtureFact]
        public void New_rows_carry_the_parent_categories_and_the_configured_price_and_tech_level()
        {
            var source = ConfigDocument.Parse(Fixtures.Items);
            var plan = ChipTierPlan.Build(source, 3, 6);
            var after = ConfigDocument.Parse(Rewritten()).Section("datadisks");

            int idCol = after.ColumnIndex("Id");
            int catCol = after.ColumnIndex("Categories");
            int tlCol = after.ColumnIndex("TechLevel");
            int priceCol = after.ColumnIndex("Price");
            int unlockTypeCol = after.ColumnIndex("UnlockType");
            int classCol = after.ColumnIndex("ItemClass");

            foreach (var chip in plan.Chips)
            {
                var parent = after.Rows.First(r => r.Get(idCol) == chip.ParentId);

                foreach (var tier in new[] { Tier.Low, Tier.Mid })
                {
                    var row = after.Rows.First(r => r.Get(idCol) == chip.IdFor(tier));
                    Assert.Equal(parent.Get(catCol), row.Get(catCol));
                    Assert.Equal(parent.Get(unlockTypeCol), row.Get(unlockTypeCol));
                    Assert.Equal(parent.Get(classCol), row.Get(classCol));
                    Assert.Equal(Prices[(int)tier].ToString(), row.Get(priceCol));
                    Assert.Equal(TechLevels[(int)tier].ToString(), row.Get(tlCol));
                }
            }
        }

        [FixtureFact]
        public void Unlock_lists_are_partitioned_across_the_three_rows()
        {
            var source = ConfigDocument.Parse(Fixtures.Items);
            var plan = ChipTierPlan.Build(source, 3, 6);
            var after = ConfigDocument.Parse(Rewritten()).Section("datadisks");
            int idCol = after.ColumnIndex("Id");
            int unlockCol = after.ColumnIndex("UnlockIds");

            foreach (var chip in plan.Chips)
            {
                string Unlocks(string id) => after.Rows.First(r => r.Get(idCol) == id).Get(unlockCol);

                Assert.Equal(string.Join(" ", chip.LowUnlocks), Unlocks(chip.IdFor(Tier.Low)));
                Assert.Equal(string.Join(" ", chip.MidUnlocks), Unlocks(chip.IdFor(Tier.Mid)));
                Assert.Equal(string.Join(" ", chip.HighUnlocks), Unlocks(chip.ParentId));
            }
        }

        [FixtureFact]
        public void Leaves_generic_chips_and_other_sections_untouched()
        {
            var before = ConfigDocument.Parse(Fixtures.Items);
            var after = ConfigDocument.Parse(Rewritten());

            var b = before.Section("datadisks");
            var a = after.Section("datadisks");
            int idCol = b.ColumnIndex("Id");
            int unlockCol = b.ColumnIndex("UnlockIds");

            foreach (string generic in new[] { "low_chip", "medium_chip", "high_chip",
                                               "mercenary_chip", "class_chip", "augment_chip" })
            {
                Assert.Equal(b.Rows.First(r => r.Get(idCol) == generic).Get(unlockCol),
                             a.Rows.First(r => r.Get(idCol) == generic).Get(unlockCol));
            }

            Assert.Equal(before.Sections.Count, after.Sections.Count);
            Assert.Equal(before.Section("expgainers").Rows.Count, after.Section("expgainers").Rows.Count);
        }

        [FixtureFact]
        public void Output_reparses_and_round_trips()
        {
            string text = Rewritten();
            Assert.Equal(text, ConfigDocument.Parse(text).Render());
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd "C:/Users/babya/git/QM_CompanyTechTiers/tests/QM_CompanyTechTiers.Tests" && dotnet test --nologo`
Expected: compile failure — `The type or namespace name 'Rewriting' does not exist`.

- [ ] **Step 3: Implement `ItemsRewriter`**

`src/Rewriting/ItemsRewriter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using QM_CompanyTechTiers.Configs;
using QM_CompanyTechTiers.Tiering;

namespace QM_CompanyTechTiers.Rewriting
{
    /// <summary>
    /// Adds a low and mid tier datadisk row for every company chip and narrows the
    /// parent row's UnlockIds to the high tier.
    /// </summary>
    public static class ItemsRewriter
    {
        public static string Rewrite(string itemsText, ChipTierPlan plan, int[] tierPrices, int[] tierTechLevels)
        {
            if (itemsText == null) throw new ArgumentNullException(nameof(itemsText));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (tierPrices == null || tierPrices.Length < 3) throw new ArgumentException("Need 3 prices.", nameof(tierPrices));
            if (tierTechLevels == null || tierTechLevels.Length < 3) throw new ArgumentException("Need 3 tech levels.", nameof(tierTechLevels));

            var document = ConfigDocument.Parse(itemsText);
            var datadisks = document.Section("datadisks");
            if (datadisks == null) throw new InvalidOperationException("config_items has no #datadisks section.");

            int idCol = datadisks.ColumnIndex("Id");
            int techCol = datadisks.ColumnIndex("TechLevel");
            int priceCol = datadisks.ColumnIndex("Price");
            int unlockCol = datadisks.ColumnIndex("UnlockIds");

            foreach (var chip in plan.Chips)
            {
                var parent = datadisks.Rows.FirstOrDefault(r => r.Get(idCol) == chip.ParentId);
                if (parent == null) continue;

                // Insert mid first, then low, so low ends up directly beneath the parent.
                foreach (var tier in new[] { Tier.Mid, Tier.Low })
                {
                    var cells = CloneCells(parent, datadisks.Columns.Count);
                    cells[idCol] = chip.IdFor(tier);
                    if (techCol >= 0) cells[techCol] = tierTechLevels[(int)tier].ToString();
                    if (priceCol >= 0) cells[priceCol] = tierPrices[(int)tier].ToString();
                    cells[unlockCol] = string.Join(" ", chip.UnlocksFor(tier));

                    datadisks.InsertRowAfter(parent, cells);
                }

                parent.Set(unlockCol, string.Join(" ", chip.HighUnlocks));
            }

            return document.Render();
        }

        private static List<string> CloneCells(ConfigRow source, int columnCount)
        {
            var cells = new List<string>(columnCount);
            for (int i = 0; i < columnCount; i++) cells.Add(source.Get(i));
            return cells;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd "C:/Users/babya/git/QM_CompanyTechTiers/tests/QM_CompanyTechTiers.Tests" && dotnet test --nologo`
Expected: PASS, 22 tests.

- [ ] **Step 5: Commit**

```bash
cd "C:/Users/babya/git/QM_CompanyTechTiers"
git add src/Rewriting/ItemsRewriter.cs tests/
git commit -m "feat: emit tiered company chip rows in config_items"
```

---

### Task 5: Faction drops rewriter

**Files:**
- Create: `src/Rewriting/FactionDropsRewriter.cs`
- Create: `tests/QM_CompanyTechTiers.Tests/FactionDropsRewriterTests.cs`

**Interfaces:**
- Consumes: `ConfigDocument`, `ChipTierPlan`, `CompanyChip`, `Tier`.
- Produces: `QM_CompanyTechTiers.Rewriting.FactionDropsRewriter` with
  `static string Rewrite(string dropsText, ChipTierPlan plan, int[] rewardLevels, int[] tierPrices, bool inheritParentDropWeight)`.
  `rewardLevels` is length-3 (default `{3, 6, 10}`); index 2 is only used to locate existing parent entries.

Drop weight rule: when `inheritParentDropWeight` is false, tier 1 takes the `Weight` of the `low_chip`
entry in that same table and tier 2 the `medium_chip` entry's; if that generic entry is missing, fall
back to the parent chip's weight. `Points` is always the tier's price.

- [ ] **Step 1: Write the failing tests**

`tests/QM_CompanyTechTiers.Tests/FactionDropsRewriterTests.cs`:

```csharp
using System.Linq;
using QM_CompanyTechTiers.Configs;
using QM_CompanyTechTiers.Rewriting;
using QM_CompanyTechTiers.Tiering;
using Xunit;

namespace QM_CompanyTechTiers.Tests
{
    public class FactionDropsRewriterTests
    {
        private static readonly int[] Levels = { 3, 6, 10 };
        private static readonly int[] Prices = { 400, 525, 650 };

        private static ChipTierPlan Plan() =>
            ChipTierPlan.Build(ConfigDocument.Parse(Fixtures.Items), 3, 6);

        private static string Rewritten(bool inherit = false) =>
            FactionDropsRewriter.Rewrite(Fixtures.FactionDrops, Plan(), Levels, Prices, inherit);

        private static int CountChipEntries(ConfigSection section, string chipId)
        {
            int ci = section.ColumnIndex("ContentIds");
            return section.Rows.Count(r => r.Get(ci) == chipId);
        }

        [FixtureFact]
        public void Every_parent_entry_gains_a_low_and_mid_sibling_at_the_configured_levels()
        {
            var before = ConfigDocument.Parse(Fixtures.FactionDrops);
            var after = ConfigDocument.Parse(Rewritten());
            var plan = Plan();

            foreach (var section in before.Sections.Where(s => s.Name.EndsWith("_rewardChips")))
            {
                var target = after.Section(section.Name);
                int tl = target.ColumnIndex("TechLevel");
                int ci = target.ColumnIndex("ContentIds");

                foreach (var chip in plan.Chips)
                {
                    if (CountChipEntries(section, chip.ParentId) == 0)
                    {
                        Assert.Equal(0, CountChipEntries(target, chip.IdFor(Tier.Low)));
                        continue;
                    }

                    var low = target.Rows.Single(r => r.Get(ci) == chip.IdFor(Tier.Low));
                    var mid = target.Rows.Single(r => r.Get(ci) == chip.IdFor(Tier.Mid));
                    Assert.Equal("3", low.Get(tl));
                    Assert.Equal("6", mid.Get(tl));
                }
            }
        }

        [FixtureFact]
        public void UnchainedBelt_gains_exactly_fourteen_entries()
        {
            var before = ConfigDocument.Parse(Fixtures.FactionDrops).Section("factiondrop_UnchainedBelt_rewardChips");
            var after = ConfigDocument.Parse(Rewritten()).Section("factiondrop_UnchainedBelt_rewardChips");
            Assert.Equal(before.Rows.Count + 14, after.Rows.Count);
        }

        [FixtureFact]
        public void Tables_without_a_company_chip_are_untouched()
        {
            var before = ConfigDocument.Parse(Fixtures.FactionDrops);
            var after = ConfigDocument.Parse(Rewritten());

            foreach (var section in before.Sections.Where(s => !s.Name.EndsWith("_rewardChips")))
                Assert.Equal(section.Rows.Count, after.Section(section.Name).Rows.Count);
        }

        [FixtureFact]
        public void Weight_defaults_to_the_generic_chip_of_the_same_bracket_and_points_to_the_tier_price()
        {
            var before = ConfigDocument.Parse(Fixtures.FactionDrops).Section("factiondrop_AnCom_rewardChips");
            var after = ConfigDocument.Parse(Rewritten()).Section("factiondrop_AnCom_rewardChips");

            int ci = after.ColumnIndex("ContentIds");
            int w = after.ColumnIndex("Weight");
            int p = after.ColumnIndex("Points");

            string LowChipWeight = before.Rows.First(r => r.Get(ci) == "low_chip").Get(w);
            string MediumChipWeight = before.Rows.First(r => r.Get(ci) == "medium_chip").Get(w);

            var lowTier = after.Rows.Single(r => r.Get(ci) == "anc_chip_low");
            var midTier = after.Rows.Single(r => r.Get(ci) == "anc_chip_mid");

            Assert.Equal(LowChipWeight, lowTier.Get(w));
            Assert.Equal(MediumChipWeight, midTier.Get(w));
            Assert.Equal("400", lowTier.Get(p));
            Assert.Equal("525", midTier.Get(p));
        }

        [FixtureFact]
        public void Inherit_flag_copies_the_parent_weight_instead()
        {
            var after = ConfigDocument.Parse(Rewritten(inherit: true)).Section("factiondrop_AnCom_rewardChips");
            int ci = after.ColumnIndex("ContentIds");
            int w = after.ColumnIndex("Weight");

            string parentWeight = after.Rows.First(r => r.Get(ci) == "anc_chip").Get(w);
            Assert.Equal(parentWeight, after.Rows.Single(r => r.Get(ci) == "anc_chip_low").Get(w));
        }

        [FixtureFact]
        public void Output_reparses_and_round_trips()
        {
            string text = Rewritten();
            Assert.Equal(text, ConfigDocument.Parse(text).Render());
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd "C:/Users/babya/git/QM_CompanyTechTiers/tests/QM_CompanyTechTiers.Tests" && dotnet test --nologo`
Expected: compile failure — `FactionDropsRewriter` not found.

- [ ] **Step 3: Implement `FactionDropsRewriter`**

`src/Rewriting/FactionDropsRewriter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using QM_CompanyTechTiers.Configs;
using QM_CompanyTechTiers.Tiering;

namespace QM_CompanyTechTiers.Rewriting
{
    /// <summary>
    /// For every faction reward-chip table that offers a company chip, adds the two lower
    /// tiers at their configured faction tech levels.
    /// </summary>
    public static class FactionDropsRewriter
    {
        private const string ChipTableSuffix = "_rewardChips";

        /// <summary>Generic chip whose drop weight each tier borrows when not inheriting the parent's.</summary>
        private static readonly Dictionary<Tier, string> WeightDonor = new Dictionary<Tier, string>
        {
            { Tier.Low, "low_chip" },
            { Tier.Mid, "medium_chip" },
        };

        public static string Rewrite(string dropsText, ChipTierPlan plan, int[] rewardLevels,
                                     int[] tierPrices, bool inheritParentDropWeight)
        {
            if (dropsText == null) throw new ArgumentNullException(nameof(dropsText));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (rewardLevels == null || rewardLevels.Length < 3) throw new ArgumentException("Need 3 levels.", nameof(rewardLevels));
            if (tierPrices == null || tierPrices.Length < 3) throw new ArgumentException("Need 3 prices.", nameof(tierPrices));

            var document = ConfigDocument.Parse(dropsText);

            foreach (var section in document.Sections)
            {
                if (!section.Name.EndsWith(ChipTableSuffix, StringComparison.Ordinal)) continue;

                int techCol = section.ColumnIndex("TechLevel");
                int contentCol = section.ColumnIndex("ContentIds");
                int weightCol = section.ColumnIndex("Weight");
                int pointsCol = section.ColumnIndex("Points");
                if (techCol < 0 || contentCol < 0) continue;

                // Snapshot: InsertRowAfter mutates the row list while we iterate.
                var parentRows = section.Rows
                    .Where(r => plan.ChipByParentId(r.Get(contentCol)) != null)
                    .ToList();

                foreach (var parentRow in parentRows)
                {
                    var chip = plan.ChipByParentId(parentRow.Get(contentCol));

                    foreach (var tier in new[] { Tier.Mid, Tier.Low })
                    {
                        var cells = new List<string>();
                        for (int i = 0; i < section.Columns.Count; i++) cells.Add(parentRow.Get(i));

                        cells[techCol] = rewardLevels[(int)tier].ToString();
                        cells[contentCol] = chip.IdFor(tier);
                        if (weightCol >= 0)
                            cells[weightCol] = ResolveWeight(section, parentRow, tier, contentCol, weightCol,
                                                             inheritParentDropWeight);
                        if (pointsCol >= 0) cells[pointsCol] = tierPrices[(int)tier].ToString();

                        section.InsertRowAfter(parentRow, cells);
                    }
                }
            }

            return document.Render();
        }

        private static string ResolveWeight(ConfigSection section, ConfigRow parentRow, Tier tier,
                                            int contentCol, int weightCol, bool inheritParent)
        {
            if (inheritParent) return parentRow.Get(weightCol);

            string donorId;
            if (!WeightDonor.TryGetValue(tier, out donorId)) return parentRow.Get(weightCol);

            var donor = section.Rows.FirstOrDefault(r => r.Get(contentCol) == donorId);
            return donor != null ? donor.Get(weightCol) : parentRow.Get(weightCol);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd "C:/Users/babya/git/QM_CompanyTechTiers/tests/QM_CompanyTechTiers.Tests" && dotnet test --nologo`
Expected: PASS, 28 tests.

- [ ] **Step 5: Commit**

```bash
cd "C:/Users/babya/git/QM_CompanyTechTiers"
git add src/Rewriting/FactionDropsRewriter.cs tests/
git commit -m "feat: offer tiered company chips at faction levels 3 and 6"
```

---

### Task 6: Crafting upgrade-cost rewriter

**Files:**
- Create: `src/Rewriting/CraftingRewriter.cs`
- Create: `tests/QM_CompanyTechTiers.Tests/CraftingRewriterTests.cs`

**Interfaces:**
- Consumes: `ConfigDocument`, `ChipTierPlan`, `CompanyChip`, `Tier`.
- Produces: `QM_CompanyTechTiers.Rewriting.CraftingRewriter` with `static string Rewrite(string craftingText, ChipTierPlan plan)`.

`ModifyItemsGrades` cells are space-separated `id count` pairs, e.g.
`plastic 1 spring 2 military_parts_container 4 anc_chip 8 low_chip 16`. Only the id token is
replaced, and only when the recipe's `OutputItem` sits in the low or mid tier. Sections lacking a
`ModifyItemsGrades` column (`#workbenchreceipts`) are skipped.

- [ ] **Step 1: Write the failing tests**

`tests/QM_CompanyTechTiers.Tests/CraftingRewriterTests.cs`:

```csharp
using System.Linq;
using QM_CompanyTechTiers.Configs;
using QM_CompanyTechTiers.Rewriting;
using QM_CompanyTechTiers.Tiering;
using Xunit;

namespace QM_CompanyTechTiers.Tests
{
    public class CraftingRewriterTests
    {
        private static ChipTierPlan Plan() =>
            ChipTierPlan.Build(ConfigDocument.Parse(Fixtures.Items), 3, 6);

        private static string Rewritten() => CraftingRewriter.Rewrite(Fixtures.Crafting, Plan());

        [Fact]
        public void Replaces_only_the_id_token_and_keeps_counts()
        {
            var plan = ChipTierPlan.Build(ConfigDocument.Parse(Fixtures.Items), 3, 6);
            string text =
                "#itemreceipts\r\nId\tOutputItem\tModifyItemsGrades\r\n" +
                "\tanc_pistol_1\tplastic 1 anc_chip 8 low_chip 16\r\n#end";

            string result = CraftingRewriter.Rewrite(text, plan);
            var row = ConfigDocument.Parse(result).Section("itemreceipts").Rows[0];

            Assert.Equal("plastic 1 anc_chip_low 8 low_chip 16", row.Get(2));
        }

        [FixtureFact]
        public void High_tier_outputs_keep_the_parent_chip()
        {
            var plan = Plan();
            var after = ConfigDocument.Parse(Rewritten()).Section("itemreceipts");
            int outCol = after.ColumnIndex("OutputItem");
            int gradesCol = after.ColumnIndex("ModifyItemsGrades");

            foreach (var row in after.Rows)
            {
                string output = row.Get(outCol);
                if (output.Length == 0 || plan.TierOfItem(output) != Tier.High) continue;

                foreach (var chip in plan.Chips)
                {
                    Assert.DoesNotContain(chip.IdFor(Tier.Low), row.Get(gradesCol).Split(' '));
                    Assert.DoesNotContain(chip.IdFor(Tier.Mid), row.Get(gradesCol).Split(' '));
                }
            }
        }

        [FixtureFact]
        public void Low_and_mid_outputs_reference_their_own_tier()
        {
            var plan = Plan();
            var before = ConfigDocument.Parse(Fixtures.Crafting).Section("itemreceipts");
            var after = ConfigDocument.Parse(Rewritten()).Section("itemreceipts");
            int outCol = after.ColumnIndex("OutputItem");
            int gradesCol = after.ColumnIndex("ModifyItemsGrades");
            int changed = 0;

            for (int i = 0; i < before.Rows.Count; i++)
            {
                string output = before.Rows[i].Get(outCol);
                var tier = plan.TierOfItem(output);
                if (output.Length == 0 || tier == Tier.High) continue;

                var beforeTokens = before.Rows[i].Get(gradesCol).Split(' ');
                var afterTokens = after.Rows[i].Get(gradesCol).Split(' ');

                for (int t = 0; t < beforeTokens.Length; t++)
                {
                    var chip = plan.ChipByParentId(beforeTokens[t]);
                    if (chip == null) { Assert.Equal(beforeTokens[t], afterTokens[t]); continue; }
                    Assert.Equal(chip.IdFor(tier), afterTokens[t]);
                    changed++;
                }
            }

            Assert.True(changed > 0, "expected at least one remapped cost");
        }

        [FixtureFact]
        public void Workbench_receipts_are_untouched()
        {
            var before = ConfigDocument.Parse(Fixtures.Crafting).Section("workbenchreceipts");
            var after = ConfigDocument.Parse(Rewritten()).Section("workbenchreceipts");

            for (int i = 0; i < before.Rows.Count; i++)
                for (int c = 0; c < before.Columns.Count; c++)
                    Assert.Equal(before.Rows[i].Get(c), after.Rows[i].Get(c));
        }

        [FixtureFact]
        public void Output_reparses_and_round_trips()
        {
            string text = Rewritten();
            Assert.Equal(text, ConfigDocument.Parse(text).Render());
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd "C:/Users/babya/git/QM_CompanyTechTiers/tests/QM_CompanyTechTiers.Tests" && dotnet test --nologo`
Expected: compile failure — `CraftingRewriter` not found.

- [ ] **Step 3: Implement `CraftingRewriter`**

`src/Rewriting/CraftingRewriter.cs`:

```csharp
using System;
using QM_CompanyTechTiers.Configs;
using QM_CompanyTechTiers.Tiering;

namespace QM_CompanyTechTiers.Rewriting
{
    /// <summary>
    /// Rewrites upgrade costs so an item unlocked by a lower tier is also upgraded with that tier.
    /// Only the chip id token changes; counts and every other material are left alone.
    /// </summary>
    public static class CraftingRewriter
    {
        private const string GradesColumn = "ModifyItemsGrades";
        private const string OutputColumn = "OutputItem";

        public static string Rewrite(string craftingText, ChipTierPlan plan)
        {
            if (craftingText == null) throw new ArgumentNullException(nameof(craftingText));
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            var document = ConfigDocument.Parse(craftingText);

            foreach (var section in document.Sections)
            {
                int gradesCol = section.ColumnIndex(GradesColumn);
                int outputCol = section.ColumnIndex(OutputColumn);
                if (gradesCol < 0 || outputCol < 0) continue;

                foreach (var row in section.Rows)
                {
                    string output = row.Get(outputCol);
                    if (output.Length == 0) continue;

                    Tier tier = plan.TierOfItem(output);
                    if (tier == Tier.High) continue;   // parent chip is already correct

                    string grades = row.Get(gradesCol);
                    if (grades.Length == 0) continue;

                    var tokens = grades.Split(' ');
                    bool changed = false;

                    for (int i = 0; i < tokens.Length; i++)
                    {
                        var chip = plan.ChipByParentId(tokens[i]);
                        if (chip == null) continue;
                        tokens[i] = chip.IdFor(tier);
                        changed = true;
                    }

                    if (changed) row.Set(gradesCol, string.Join(" ", tokens));
                }
            }

            return document.Render();
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd "C:/Users/babya/git/QM_CompanyTechTiers/tests/QM_CompanyTechTiers.Tests" && dotnet test --nologo`
Expected: PASS, 33 tests.

- [ ] **Step 5: Commit**

```bash
cd "C:/Users/babya/git/QM_CompanyTechTiers"
git add src/Rewriting/CraftingRewriter.cs tests/
git commit -m "feat: remap upgrade costs to the matching chip tier"
```

---

### Task 7: Localization rewriter

The localization table is a flat 18-column TSV with **no `#section` structure**, so `ConfigDocument`
does not apply. Key is column 0; columns 1–11 are the eleven languages in `Localization.Lang` order
(`EnglishUS, Russian, German, French, Spanish, Polish, Turkish, BrazilianPortugal, Korean, Japanese,
ChineseSimp`); columns 12–17 are unused by `LoadDB`. CRLF, no trailing newline, ~11,566 lines.

`MGSC.Localization.LoadDB` ignores duplicate keys — first occurrence wins. New keys must therefore be
**appended**, and the parent chip's existing name line must be **edited in place**, never duplicated.

**Files:**
- Create: `src/Rewriting/LocalizationRewriter.cs`
- Create: `tests/QM_CompanyTechTiers.Tests/LocalizationRewriterTests.cs`

**Interfaces:**
- Consumes: `ChipTierPlan`, `CompanyChip`, `Tier` (Task 3).
- Produces: `QM_CompanyTechTiers.Rewriting.LocalizationRewriter` with
  `static string Rewrite(string localizationText, ChipTierPlan plan, string[] tierSuffixes)`.
  `tierSuffixes` is length-3 indexed by `(int)Tier`, default `{" I", " II", " III"}`.

Behaviour: for each company chip, for tiers Low and Mid, clone the parent's `item.<parent>.name`,
`.shortdesc` and `.desc` lines under the tier id (skipping facets the parent lacks), appending the
tier suffix to every language cell of `.name` only. For the High tier the parent's own `.name` line
is edited in place to carry its suffix. Suffixing is idempotent — re-running never double-appends.

- [ ] **Step 1: Write the failing tests**

`tests/QM_CompanyTechTiers.Tests/LocalizationRewriterTests.cs`:

```csharp
using System.Linq;
using QM_CompanyTechTiers.Configs;
using QM_CompanyTechTiers.Rewriting;
using QM_CompanyTechTiers.Tiering;
using Xunit;

namespace QM_CompanyTechTiers.Tests
{
    public class LocalizationRewriterTests
    {
        private static readonly string[] Suffixes = { " I", " II", " III" };

        private static ChipTierPlan Plan() =>
            ChipTierPlan.Build(ConfigDocument.Parse(Fixtures.Items), 3, 6);

        /// <summary>key -> full 18-column row, for whichever rows we care about.</summary>
        private static string[] Row(string text, string key) =>
            text.Split('\n').Select(l => l.TrimEnd('\r').Split('\t'))
                .First(c => c[0] == key);

        private static bool Has(string text, string key) =>
            text.Split('\n').Any(l => l.TrimEnd('\r').Split('\t')[0] == key);

        [FixtureFact]
        public void Adds_name_rows_for_every_new_tier_id()
        {
            var plan = Plan();
            string result = LocalizationRewriter.Rewrite(Fixtures.LocalizationTable, plan, Suffixes);

            foreach (var chip in plan.Chips)
                foreach (var tier in new[] { Tier.Low, Tier.Mid })
                    Assert.True(Has(result, "item." + chip.IdFor(tier) + ".name"),
                                "missing name for " + chip.IdFor(tier));
        }

        [FixtureFact]
        public void Tier_names_are_the_parent_name_plus_a_numeral_in_every_language()
        {
            var plan = Plan();
            string result = LocalizationRewriter.Rewrite(Fixtures.LocalizationTable, plan, Suffixes);
            var chip = plan.Chips.First();

            var parentBefore = Row(Fixtures.LocalizationTable, "item." + chip.ParentId + ".name");
            var low = Row(result, "item." + chip.IdFor(Tier.Low) + ".name");
            var parentAfter = Row(result, "item." + chip.ParentId + ".name");

            for (int lang = 1; lang <= 11; lang++)
            {
                Assert.Equal(parentBefore[lang] + " I", low[lang]);
                Assert.Equal(parentBefore[lang] + " III", parentAfter[lang]);
            }
        }

        [FixtureFact]
        public void Shortdesc_is_copied_verbatim_without_a_numeral()
        {
            var plan = Plan();
            string result = LocalizationRewriter.Rewrite(Fixtures.LocalizationTable, plan, Suffixes);
            var chip = plan.Chips.First();

            var parent = Row(Fixtures.LocalizationTable, "item." + chip.ParentId + ".shortdesc");
            var low = Row(result, "item." + chip.IdFor(Tier.Low) + ".shortdesc");

            for (int lang = 1; lang <= 11; lang++) Assert.Equal(parent[lang], low[lang]);
        }

        [FixtureFact]
        public void Never_duplicates_a_key_because_LoadDB_keeps_only_the_first()
        {
            var plan = Plan();
            string result = LocalizationRewriter.Rewrite(Fixtures.LocalizationTable, plan, Suffixes);

            var keys = result.Split('\n')
                             .Where(l => l.Length > 0)
                             .Select(l => l.TrimEnd('\r').Split('\t')[0])
                             .ToList();

            Assert.Equal(keys.Count, keys.Distinct().Count());
        }

        [FixtureFact]
        public void Is_idempotent()
        {
            var plan = Plan();
            string once = LocalizationRewriter.Rewrite(Fixtures.LocalizationTable, plan, Suffixes);
            string twice = LocalizationRewriter.Rewrite(once, plan, Suffixes);
            Assert.Equal(once, twice);
        }

        [FixtureFact]
        public void Leaves_unrelated_rows_and_column_count_alone()
        {
            var plan = Plan();
            string result = LocalizationRewriter.Rewrite(Fixtures.LocalizationTable, plan, Suffixes);

            Assert.Equal(Row(Fixtures.LocalizationTable, "item.low_chip.name"),
                         Row(result, "item.low_chip.name"));

            foreach (string line in result.Split('\n').Where(l => l.TrimEnd('\r').Length > 0))
                Assert.Equal(18, line.TrimEnd('\r').Split('\t').Length);
        }

        [Fact]
        public void Handles_a_parent_with_no_desc_facet()
        {
            var plan = ChipTierPlan.Build(ConfigDocument.Parse(Fixtures.Items), 3, 6);
            var chip = plan.Chips.First();

            string tiny =
                "item." + chip.ParentId + ".name\tAcme Chip" + new string('\t', 16) + "\r\n" +
                "item." + chip.ParentId + ".shortdesc\tA chip" + new string('\t', 16);

            string result = LocalizationRewriter.Rewrite(tiny, plan, Suffixes);

            Assert.True(Has(result, "item." + chip.IdFor(Tier.Low) + ".name"));
            Assert.False(Has(result, "item." + chip.IdFor(Tier.Low) + ".desc"));
            Assert.Equal("Acme Chip I", Row(result, "item." + chip.IdFor(Tier.Low) + ".name")[1]);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd "C:/Users/babya/git/QM_CompanyTechTiers/tests/QM_CompanyTechTiers.Tests" && dotnet test --nologo`
Expected: compile failure — `LocalizationRewriter` not found.

- [ ] **Step 3: Implement `LocalizationRewriter`**

`src/Rewriting/LocalizationRewriter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using QM_CompanyTechTiers.Tiering;

namespace QM_CompanyTechTiers.Rewriting
{
    /// <summary>
    /// Names the new chip tiers by cloning the parent chip's rows in the localization table and
    /// appending a language-neutral roman numeral to the display name.
    ///
    /// The table is a flat TSV: column 0 is the key, columns 1..11 are the languages in
    /// MGSC.Localization.Lang order. MGSC.Localization.LoadDB keeps the FIRST occurrence of a key,
    /// so new keys are appended and the parent's own name row is edited in place.
    /// </summary>
    public static class LocalizationRewriter
    {
        private const int FirstLanguageColumn = 1;
        private const int LastLanguageColumn = 11;

        private static readonly string[] Facets = { "name", "shortdesc", "desc" };

        public static string Rewrite(string localizationText, ChipTierPlan plan, string[] tierSuffixes)
        {
            if (localizationText == null) throw new ArgumentNullException(nameof(localizationText));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (tierSuffixes == null || tierSuffixes.Length < 3)
                throw new ArgumentException("Need 3 suffixes.", nameof(tierSuffixes));

            var lines = localizationText.Split('\n').ToList();

            // key -> line index, first occurrence only (matching LoadDB).
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < lines.Count; i++)
            {
                string body = lines[i].TrimEnd('\r');
                if (body.Length == 0) continue;
                string key = body.Split('\t')[0];
                if (!index.ContainsKey(key)) index[key] = i;
            }

            var appended = new List<string>();

            foreach (var chip in plan.Chips)
            {
                // High tier: the parent row itself gains its numeral, in place.
                string parentNameKey = "item." + chip.ParentId + ".name";
                int parentNameLine;
                if (index.TryGetValue(parentNameKey, out parentNameLine))
                    lines[parentNameLine] = SuffixLanguages(lines[parentNameLine], tierSuffixes[(int)Tier.High]);

                foreach (var tier in new[] { Tier.Low, Tier.Mid })
                {
                    string tierId = chip.IdFor(tier);

                    foreach (string facet in Facets)
                    {
                        string sourceKey = "item." + chip.ParentId + "." + facet;
                        string targetKey = "item." + tierId + "." + facet;

                        int sourceLine;
                        if (!index.TryGetValue(sourceKey, out sourceLine)) continue;  // parent lacks this facet
                        if (index.ContainsKey(targetKey)) continue;                    // already present

                        string cloned = ReplaceKey(lines[sourceLine], targetKey);

                        if (facet == "name")
                        {
                            // The parent name row was suffixed above with " III"; strip it before
                            // applying this tier's numeral so the result is not "Chip III I".
                            cloned = StripSuffixLanguages(cloned, tierSuffixes[(int)Tier.High]);
                            cloned = SuffixLanguages(cloned, tierSuffixes[(int)tier]);
                        }

                        appended.Add(cloned);
                    }
                }
            }

            if (appended.Count == 0) return localizationText;

            // Every row ends with CR except the final one; keep that shape.
            for (int i = 0; i < lines.Count; i++)
                if (!lines[i].EndsWith("\r", StringComparison.Ordinal))
                    lines[i] = lines[i] + "\r";

            for (int i = 0; i < appended.Count; i++)
                if (!appended[i].EndsWith("\r", StringComparison.Ordinal))
                    appended[i] = appended[i] + "\r";

            lines.AddRange(appended);
            lines[lines.Count - 1] = lines[lines.Count - 1].TrimEnd('\r');

            return string.Join("\n", lines);
        }

        private static string ReplaceKey(string line, string newKey)
        {
            bool cr = line.EndsWith("\r", StringComparison.Ordinal);
            var cells = line.TrimEnd('\r').Split('\t');
            cells[0] = newKey;
            return string.Join("\t", cells) + (cr ? "\r" : string.Empty);
        }

        private static string SuffixLanguages(string line, string suffix)
        {
            bool cr = line.EndsWith("\r", StringComparison.Ordinal);
            var cells = line.TrimEnd('\r').Split('\t');

            for (int i = FirstLanguageColumn; i <= LastLanguageColumn && i < cells.Length; i++)
            {
                if (cells[i].Length == 0) continue;
                if (cells[i].EndsWith(suffix, StringComparison.Ordinal)) continue;  // idempotent
                cells[i] += suffix;
            }

            return string.Join("\t", cells) + (cr ? "\r" : string.Empty);
        }

        private static string StripSuffixLanguages(string line, string suffix)
        {
            bool cr = line.EndsWith("\r", StringComparison.Ordinal);
            var cells = line.TrimEnd('\r').Split('\t');

            for (int i = FirstLanguageColumn; i <= LastLanguageColumn && i < cells.Length; i++)
                if (cells[i].EndsWith(suffix, StringComparison.Ordinal))
                    cells[i] = cells[i].Substring(0, cells[i].Length - suffix.Length);

            return string.Join("\t", cells) + (cr ? "\r" : string.Empty);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd "C:/Users/babya/git/QM_CompanyTechTiers/tests/QM_CompanyTechTiers.Tests" && dotnet test --nologo`
Expected: PASS, 40 tests.

Watch `Is_idempotent` in particular: the parent name row is suffixed on every pass, so `SuffixLanguages`
must skip cells that already end with the suffix. If it fails you will see `Ancom Chip III III`.

- [ ] **Step 5: Commit**

```bash
cd "C:/Users/babya/git/QM_CompanyTechTiers"
git add src/Rewriting/LocalizationRewriter.cs tests/
git commit -m "feat: name chip tiers in all 11 languages via the localization table"
```

---

### Task 8: Mod settings

**Files:**
- Create: `src/ModSettings.cs`
- Delete: `src/ModConfig.cs` (template placeholder)
- Create: `tests/QM_CompanyTechTiers.Tests/ModSettingsTests.cs`
- Modify: `tests/QM_CompanyTechTiers.Tests/QM_CompanyTechTiers.Tests.csproj` (link `src/ModSettings.cs`)

**Interfaces:**
- Consumes: nothing.
- Produces: `QM_CompanyTechTiers.ModSettings` with public fields `int[] RewardLevels`, `int[] TierBoundaries`, `int[] TierPrices`, `int[] TierTechLevels`, `string[] TierSuffixes`, `bool RemapUpgradeCosts`, `bool InheritParentDropWeight`, `bool DumpConfigsOnLoad`; plus `static ModSettings LoadOrCreate(string path)` and `List<string> Validate()` (empty list when valid).

`ModSettings` must not reference Unity so tests can link it. Use `Newtonsoft.Json`, already a
`PackageReference` in the template with `ExcludeAssets="runtime"`; the game ships
`Newtonsoft.Json.dll` in `Managed/`. Add the same package reference to the test project.

- [ ] **Step 1: Write the failing tests**

`tests/QM_CompanyTechTiers.Tests/ModSettingsTests.cs`:

```csharp
using System.IO;
using QM_CompanyTechTiers;
using Xunit;

namespace QM_CompanyTechTiers.Tests
{
    public class ModSettingsTests
    {
        private static string TempPath() =>
            Path.Combine(Path.GetTempPath(), "qmctt_" + Path.GetRandomFileName(), "config.json");

        [Fact]
        public void Creates_the_file_with_documented_defaults_when_absent()
        {
            string path = TempPath();
            var settings = ModSettings.LoadOrCreate(path);

            Assert.True(File.Exists(path));
            Assert.Equal(new[] { 3, 6, 10 }, settings.RewardLevels);
            Assert.Equal(new[] { 3, 6 }, settings.TierBoundaries);
            Assert.Equal(new[] { 400, 525, 650 }, settings.TierPrices);
            Assert.Equal(new[] { 1, 4, 10 }, settings.TierTechLevels);
            Assert.Equal(new[] { " I", " II", " III" }, settings.TierSuffixes);
            Assert.True(settings.RemapUpgradeCosts);
            Assert.False(settings.InheritParentDropWeight);
            Assert.False(settings.DumpConfigsOnLoad);
        }

        [Fact]
        public void Reads_values_back_from_disk()
        {
            string path = TempPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{ \"RewardLevels\": [2, 5, 10], \"RemapUpgradeCosts\": false }");

            var settings = ModSettings.LoadOrCreate(path);
            Assert.Equal(new[] { 2, 5, 10 }, settings.RewardLevels);
            Assert.False(settings.RemapUpgradeCosts);
            Assert.Equal(new[] { 3, 6 }, settings.TierBoundaries);   // untouched key keeps its default
        }

        [Fact]
        public void Falls_back_to_defaults_on_malformed_json_rather_than_throwing()
        {
            string path = TempPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{ this is not json");

            var settings = ModSettings.LoadOrCreate(path);
            Assert.Equal(new[] { 3, 6, 10 }, settings.RewardLevels);
        }

        [Fact]
        public void Validate_rejects_out_of_order_levels_and_bad_array_lengths()
        {
            var settings = ModSettings.LoadOrCreate(TempPath());
            Assert.Empty(settings.Validate());

            settings.RewardLevels = new[] { 6, 3, 10 };
            Assert.Contains(settings.Validate(), m => m.Contains("RewardLevels"));

            settings = ModSettings.LoadOrCreate(TempPath());
            settings.TierBoundaries = new[] { 3 };
            Assert.Contains(settings.Validate(), m => m.Contains("TierBoundaries"));

            settings = ModSettings.LoadOrCreate(TempPath());
            settings.TierBoundaries = new[] { 7, 6 };
            Assert.Contains(settings.Validate(), m => m.Contains("TierBoundaries"));

            settings = ModSettings.LoadOrCreate(TempPath());
            settings.TierSuffixes = new[] { " I" };
            Assert.Contains(settings.Validate(), m => m.Contains("TierSuffixes"));
        }
    }
}
```

- [ ] **Step 2: Add the Newtonsoft reference and link the new file**

In `tests/QM_CompanyTechTiers.Tests/QM_CompanyTechTiers.Tests.csproj`:

```xml
  <ItemGroup>
    <Compile Include="../../src/Configs/**/*.cs" LinkBase="Linked/Configs" />
    <Compile Include="../../src/Tiering/**/*.cs" LinkBase="Linked/Tiering" />
    <Compile Include="../../src/Rewriting/**/*.cs" LinkBase="Linked/Rewriting" />
    <Compile Include="../../src/ModSettings.cs" Link="Linked/ModSettings.cs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `cd "C:/Users/babya/git/QM_CompanyTechTiers/tests/QM_CompanyTechTiers.Tests" && dotnet test --nologo`
Expected: compile failure — `ModSettings` not found.

- [ ] **Step 4: Delete the template placeholder and implement `ModSettings`**

```bash
rm "C:/Users/babya/git/QM_CompanyTechTiers/src/ModConfig.cs"
```

`src/ModSettings.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace QM_CompanyTechTiers
{
    /// <summary>
    /// User-tunable settings. Deliberately free of Unity and MGSC types so it can be unit tested.
    /// </summary>
    public sealed class ModSettings
    {
        /// <summary>Faction tech level at which each tier is offered as a reward.</summary>
        public int[] RewardLevels = { 3, 6, 10 };

        /// <summary>Item tech level at which tier 1 ends and tier 2 ends.</summary>
        public int[] TierBoundaries = { 3, 6 };

        /// <summary>Price for each tier. Index 2 is unused; the parent row keeps its own price.</summary>
        public int[] TierPrices = { 400, 525, 650 };

        /// <summary>TechLevel field on each tier's datadisk row. Index 2 is unused.</summary>
        public int[] TierTechLevels = { 1, 4, 10 };

        /// <summary>Appended to each tier's display name in every language.</summary>
        public string[] TierSuffixes = { " I", " II", " III" };

        public bool RemapUpgradeCosts = true;

        /// <summary>Give lower tiers the parent chip's drop weight instead of the generic chip's.</summary>
        public bool InheritParentDropWeight = false;

        /// <summary>Write the game's raw config text next to this file, for regenerating test fixtures.</summary>
        public bool DumpConfigsOnLoad = false;

        public static ModSettings LoadOrCreate(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var loaded = JsonConvert.DeserializeObject<ModSettings>(File.ReadAllText(path));
                    if (loaded != null) return loaded;
                }
            }
            catch (Exception)
            {
                // Malformed config must not stop the mod loading; defaults are written below.
            }

            var settings = new ModSettings();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(settings, Formatting.Indented));
            }
            catch (Exception)
            {
                // A read-only location is survivable - carry on with in-memory defaults.
            }
            return settings;
        }

        public List<string> Validate()
        {
            var problems = new List<string>();

            if (RewardLevels == null || RewardLevels.Length != 3)
                problems.Add("RewardLevels must have exactly 3 entries.");
            else if (RewardLevels[0] > RewardLevels[1] || RewardLevels[1] > RewardLevels[2])
                problems.Add("RewardLevels must be non-decreasing.");

            if (TierBoundaries == null || TierBoundaries.Length != 2)
                problems.Add("TierBoundaries must have exactly 2 entries.");
            else if (TierBoundaries[0] > TierBoundaries[1])
                problems.Add("TierBoundaries must be non-decreasing.");

            if (TierPrices == null || TierPrices.Length != 3)
                problems.Add("TierPrices must have exactly 3 entries.");

            if (TierTechLevels == null || TierTechLevels.Length != 3)
                problems.Add("TierTechLevels must have exactly 3 entries.");

            if (TierSuffixes == null || TierSuffixes.Length != 3)
                problems.Add("TierSuffixes must have exactly 3 entries.");

            return problems;
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd "C:/Users/babya/git/QM_CompanyTechTiers/tests/QM_CompanyTechTiers.Tests" && dotnet test --nologo`
Expected: PASS, 44 tests.

- [ ] **Step 6: Commit**

```bash
cd "C:/Users/babya/git/QM_CompanyTechTiers"
git add -A src/ tests/
git commit -m "feat: mod settings with validation and safe defaults"
```

---

### Task 9: Resource hook — wire the rewriters into the game

First task that touches Unity. Not unit-testable; verified in-game in Task 10.

**Files:**
- Create: `src/ResourceHook.cs`

**Interfaces:**
- Consumes: `ModSettings` (Task 8), `ChipTierPlan` (Task 3), all four rewriters (Tasks 4–7).
- Produces: `QM_CompanyTechTiers.ResourceHook` with `static void Initialise(ModSettings settings, string dumpFolder, Action<string> log, Action<string> warn)`, `static UnityEngine.Object Load(string path)`, and `static ChipTierPlan Plan { get; }`.

`Plugin` owns the `[Hook]` attributes; `ResourceHook.Load` is a plain method, so only one method
carries each hook type. No new assembly references are needed — `TextAsset` and `Resources` both live
in `UnityEngine.CoreModule`, already referenced by the template.

Verified resource ordering on build 1.0.1.566s: `config_globals` is roughly the third resource
requested, `config_items` shortly after, and `localization` is request **#529** — all comfortably
after `InitMods()` arms the hook.

- [ ] **Step 1: Implement the hook**

`src/ResourceHook.cs`:

```csharp
using System;
using System.IO;
using MGSC;
using QM_CompanyTechTiers.Configs;
using QM_CompanyTechTiers.Rewriting;
using QM_CompanyTechTiers.Tiering;
using UnityEngine;

namespace QM_CompanyTechTiers
{
    /// <summary>
    /// Intercepts config resources before MGSC.ConfigLoader parses them and returns rewritten text.
    /// Anything unexpected returns null so the game falls back to the stock resource.
    /// </summary>
    public static class ResourceHook
    {
        private const string ItemsPath = "config_items";
        private const string DropsPath = "config_faction_drops";
        private const string CraftingPath = "config_crafting";
        private const string LocalizationPath = "localization";
        private const string DatadiskDescriptorsPath = "DescriptorsCollections/datadisks_descriptors";

        private static ModSettings _settings;
        private static string _dumpFolder;
        private static Action<string> _log = _ => { };
        private static Action<string> _warn = _ => { };

        private static ChipTierPlan _plan;
        private static bool _failed;
        private static bool _dumped;

        /// <summary>The plan built during config rewriting. Null if rewriting never ran or failed.</summary>
        public static ChipTierPlan Plan { get { return _plan; } }

        public static void Initialise(ModSettings settings, string dumpFolder,
                                      Action<string> log, Action<string> warn)
        {
            _settings = settings;
            _dumpFolder = dumpFolder;
            _log = log ?? (_ => { });
            _warn = warn ?? (_ => { });
            _plan = null;
            _failed = false;
            _dumped = false;
        }

        public static UnityEngine.Object Load(string path)
        {
            if (_failed || _settings == null) return null;

            try
            {
                switch (path)
                {
                    case ItemsPath:
                        if (_settings.DumpConfigsOnLoad) DumpOnce();
                        return Rewrite(ItemsPath, text => ItemsRewriter.Rewrite(
                            text, RequirePlan(), _settings.TierPrices, _settings.TierTechLevels));

                    case DropsPath:
                        return Rewrite(DropsPath, text => FactionDropsRewriter.Rewrite(
                            text, RequirePlan(), _settings.RewardLevels, _settings.TierPrices,
                            _settings.InheritParentDropWeight));

                    case CraftingPath:
                        if (!_settings.RemapUpgradeCosts) return null;
                        return Rewrite(CraftingPath, text => CraftingRewriter.Rewrite(text, RequirePlan()));

                    case LocalizationPath:
                        return Rewrite(LocalizationPath, text => LocalizationRewriter.Rewrite(
                            text, RequirePlan(), _settings.TierSuffixes));

                    case DatadiskDescriptorsPath:
                        return CloneDescriptors();

                    default:
                        return null;
                }
            }
            catch (Exception ex)
            {
                _failed = true;
                _warn("Rewriting '" + path + "' failed; falling back to stock configs. Mod is now inert. " + ex);
                return null;
            }
        }

        /// <summary>Loads without re-entering CustomResources, so this hook is not called recursively.</summary>
        private static T LoadStock<T>(string path) where T : UnityEngine.Object
        {
            return Resources.Load<T>(path);
        }

        private static ChipTierPlan RequirePlan()
        {
            if (_plan != null) return _plan;

            var stock = LoadStock<TextAsset>(ItemsPath);
            if (stock == null) throw new InvalidOperationException("config_items not found.");

            _plan = ChipTierPlan.Build(ConfigDocument.Parse(stock.text),
                                       _settings.TierBoundaries[0], _settings.TierBoundaries[1]);
            _log("Discovered " + _plan.Chips.Count + " company chips.");
            return _plan;
        }

        private static TextAsset Rewrite(string path, Func<string, string> transform)
        {
            var stock = LoadStock<TextAsset>(path);
            if (stock == null) { _warn("Stock resource '" + path + "' missing."); return null; }

            var rewritten = new TextAsset(transform(stock.text));
            rewritten.name = path.Substring(path.LastIndexOf('/') + 1);
            _log("Rewrote " + path + ".");
            return rewritten;
        }

        private static void DumpOnce()
        {
            if (_dumped) return;
            _dumped = true;

            try
            {
                Directory.CreateDirectory(_dumpFolder);
                foreach (string name in new[] { ItemsPath, DropsPath, CraftingPath, LocalizationPath })
                {
                    var asset = LoadStock<TextAsset>(name);
                    if (asset != null)
                        File.WriteAllText(Path.Combine(_dumpFolder, name + ".txt"), asset.text);
                }
                _log("Config dump written to " + _dumpFolder);
            }
            catch (Exception ex)
            {
                _warn("Config dump failed: " + ex.Message);
            }
        }

        /// <summary>Gives each new tier id the parent chip's icon by reusing its descriptor object.</summary>
        private static UnityEngine.Object CloneDescriptors()
        {
            var collection = LoadStock<DescriptorsCollection>(DatadiskDescriptorsPath);
            if (collection == null) return null;

            var plan = RequirePlan();
            int added = 0;

            foreach (var chip in plan.Chips)
            {
                UnityEngine.Object parent;
                if (!collection.TryGetDescriptor(chip.ParentId, out parent) || parent == null) continue;

                foreach (var tier in new[] { Tier.Low, Tier.Mid })
                {
                    UnityEngine.Object existing;
                    if (collection.TryGetDescriptor(chip.IdFor(tier), out existing)) continue;
                    collection.AddDescriptor(chip.IdFor(tier), parent);
                    added++;
                }
            }

            _log("Registered " + added + " tier descriptors.");
            return collection;
        }
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `cd "C:/Users/babya/git/QM_CompanyTechTiers/src" && dotnet build -c Release -v m -tl:off`
Expected: `Build succeeded`, 0 errors.

`DescriptorsCollection.TryGetDescriptor` is `bool TryGetDescriptor(string id, out UnityEngine.Object descriptor)`
and `AddDescriptor` is `void AddDescriptor(string id, UnityEngine.Object descriptor)` on build 1.0.1.
If either fails to resolve, confirm against the decompiled `DescriptorsCollection.cs` rather than guessing.

- [ ] **Step 3: Commit**

```bash
cd "C:/Users/babya/git/QM_CompanyTechTiers"
git add src/ResourceHook.cs
git commit -m "feat: ResourcesLoad hook wiring all rewriters and tier descriptors"
```

---

### Task 10: Plugin wiring, manifest, and in-game verification

**Files:**
- Modify: `src/Plugin.cs`
- Delete: `src/ExamplePatch.cs`
- Modify: `modmanifest.json`
- Create: `media/thumbnail.png`
- Modify: `src/QM_CompanyTechTiers.csproj` (local deploy target)
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-08-14-company-tech-tiers-design.md` (reconcile module table)

**Interfaces:**
- Consumes: everything above.
- Produces: the shipping mod.

- [ ] **Step 1: Rewrite `Plugin.cs`**

Exactly one method per hook type — see Global Constraints. Only two hooks are needed: one to arm the
rewriters, one to serve resources.

```csharp
using System.IO;
using MGSC;

namespace QM_CompanyTechTiers
{
    public static class Plugin
    {
        public static ConfigDirectories ConfigDirectories = new ConfigDirectories();
        public static Logger Logger = new Logger();
        public static ModSettings Settings { get; private set; }

        /// <summary>
        /// Runs before Data.Load(), which is where ConfigLoader reads every config resource.
        /// The ResourcesLoad hook must be armed by then.
        /// </summary>
        [Hook(ModHookType.BeforeBootstrap)]
        public static void BeforeBootstrap(IModContext context)
        {
            Directory.CreateDirectory(ConfigDirectories.ModPersistenceFolder);
            Settings = ModSettings.LoadOrCreate(ConfigDirectories.ConfigPath);

            foreach (string problem in Settings.Validate())
                Logger.LogWarning("Config problem: " + problem);

            ResourceHook.Initialise(
                Settings,
                Path.Combine(ConfigDirectories.ModPersistenceFolder, "configdump"),
                Logger.Log,
                Logger.LogWarning);

            Logger.Log("Armed. Config at " + ConfigDirectories.ConfigPath);
        }

        [Hook(ModHookType.ResourcesLoad)]
        public static UnityEngine.Object ResourcesLoad(string path)
        {
            return ResourceHook.Load(path);
        }
    }
}
```

- [ ] **Step 2: Delete the template's example patch**

```bash
rm "C:/Users/babya/git/QM_CompanyTechTiers/src/ExamplePatch.cs"
```

- [ ] **Step 3: Fill in the manifest**

`modmanifest.json`:

```json
{
  "UniqueModName": "babyak_QM_CompanyTechTiers",
  "Assemblies": [
    "QM_CompanyTechTiers.dll"
  ],
  "Dependencies": [],
  "SteamTags": [
    "1.0",
    "Gameplay Tweaks"
  ]
}
```

- [ ] **Step 4: Generate a thumbnail**

```powershell
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 256,256
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([System.Drawing.Color]::FromArgb(18,20,24))
$colors = @([System.Drawing.Color]::FromArgb(90,140,190),
            [System.Drawing.Color]::FromArgb(150,170,90),
            [System.Drawing.Color]::FromArgb(214,84,52))
for ($i = 0; $i -lt 3; $i++) {
  $b = New-Object System.Drawing.SolidBrush $colors[$i]
  $g.FillRectangle($b, 40, (40 + $i*64), (60 + $i*70), 48)
}
$g.Dispose()
$bmp.Save('C:\Users\babya\git\QM_CompanyTechTiers\media\thumbnail.png',
          [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
```

- [ ] **Step 5: Add the local deploy target**

In `src/QM_CompanyTechTiers.csproj`, immediately after the `NoSteamIdMessage` target, add:

```xml
	<!--
		Quasimorph 1.0.1 never calls UserModSystem.LoadModifications, so there is no plain "Mods"
		folder. LoadCustomPresets over LocalUserPresets is the only local path, and it reads
		modmanifest.json and loads Assemblies exactly like a Workshop mod. A folder here with no
		GameData/BinaryPresetsMap.json needs no RoomPresetNamespaceSlot.
		Skip with: dotnet build -p:LocalDeploy=false
	-->
	<PropertyGroup>
		<LocalDeploy Condition="'$(LocalDeploy)'==''">true</LocalDeploy>
		<!-- $(TargetName) is undefined during evaluation; $(AssemblyName) is not. -->
		<LocalPresetsPath>$(LOCALAPPDATA)\..\LocalLow\Magnum Scriptum Ltd\Quasimorph\LocalUserPresets\$(AssemblyName)\</LocalPresetsPath>
	</PropertyGroup>

	<Target Name="LocalPresetsDeploy" Condition="'$(LocalDeploy)'=='true'" AfterTargets="ModBaseItems">
		<ItemGroup>
			<LocalDeploySource Include="$(TargetDir)*.dll;$(TargetDir)modmanifest.json;$(TargetDir)thumbnail.png" />
		</ItemGroup>
		<MakeDir Directories="$(LocalPresetsPath)" />
		<Copy SourceFiles="@(LocalDeploySource)" DestinationFolder="$(LocalPresetsPath)" />
		<Message Importance="high" Text="Deployed locally to $(LocalPresetsPath)" />
	</Target>
```

- [ ] **Step 6: Build and run the full suite**

Run:
```bash
cd "C:/Users/babya/git/QM_CompanyTechTiers/tests/QM_CompanyTechTiers.Tests" && dotnet test --nologo
cd "C:/Users/babya/git/QM_CompanyTechTiers/src" && dotnet build -c Release -v m -tl:off
```
Expected: 44 tests pass; build succeeds and prints `Deployed locally to ...LocalUserPresets\QM_CompanyTechTiers\`.

- [ ] **Step 7: Verify in game**

Launch with PowerShell `Start-Process "steam://rungameid/2059170"`. Wait for the main menu, then quit
with `(Get-Process Quasimorph).CloseMainWindow()`.

Check `%USERPROFILE%\AppData\LocalLow\Magnum Scriptum Ltd\Quasimorph\Player.log` for:

1. `Mod babyak_QM_CompanyTechTiers loaded.` inside the `Mods list:` block.
2. `[QM_CompanyTechTiers] Discovered 12 company chips.`
3. `[QM_CompanyTechTiers] Rewrote config_items.`, `... config_faction_drops.`, `... config_crafting.`, `... localization.`
4. `[QM_CompanyTechTiers] Registered 24 tier descriptors.`
5. **No** `Rewriting '...' failed`, `NullReferenceException`, or `LocalizationManager error`.
6. **No** `MGSC` config parse errors — search the log for `Exception`, `Error` and `parse`.

If the game hangs on the loading screen, the rewriter emitted text `ConfigLoader` cannot parse. Set
`"DumpConfigsOnLoad": true`, diff the dumped stock config against the rewritten output, and add a
failing unit test reproducing the difference **before** fixing it.

- [ ] **Step 8: Verify the gameplay change**

Load a save and open the factions screen. Confirm that a company at tech level 3 or above lists its
tier-1 chip among the rewards, showing an icon and a name ending in ` I` — not a raw id and not a
missing-key placeholder. Confirm the level-10 chip now reads `... III`.

- [ ] **Step 9: Write the README and reconcile the spec**

Replace `README.md` with: what the mod does, the tier table, every config key from Task 8 with its
default, the install path, and the two known risks from the spec (save compatibility, conflicts with
other config-rewriting mods). Remove the template's Ko-Fi link.

Then update the spec's **Module structure** table to match what was actually built: `ConfigDocument.cs`
(not `ConfigTable.cs`), four rewriters under `src/Rewriting/` (not one `ConfigRewriter.cs`),
`ModSettings.cs` (not the template's `ModConfig.cs`), and no `LocalizationPatch.cs` — naming became a
pure rewriter instead of a Unity-side patch. Also correct the spec's claim that localization uses
`Localization.DuplicateKey`; it does not, because `DuplicateKey` cannot alter a value and the table is
interceptable through the same `ResourcesLoad` hook.

- [ ] **Step 10: Commit**

```bash
cd "C:/Users/babya/git/QM_CompanyTechTiers"
git add -A
git commit -m "feat: wire up plugin, manifest, local deploy and docs"
```

---

## Self-review notes

Checked against the spec:

- **Module structure drifted, deliberately.** The spec listed six files; the plan builds nine.
  `ConfigTable.cs` became `ConfigDocument.cs` (it holds a document of many tables). `ConfigRewriter.cs`
  split into four focused rewriters, one per config file, roughly 80 lines each rather than one
  ~330-line file. `ModSettings.cs` replaces the template's `ModConfig.cs`. Task 10 Step 9 reconciles
  the spec.
- **Localization approach changed, and it is a genuine improvement.** The spec planned
  `Localization.DuplicateKey` plus suffixes. Verification during planning showed `Localization.db` is
  private with no public setter, so suffixes were impossible that way — but the table is loaded through
  `CustomResources.Load("localization")` as resource #529, comfortably after the hook is armed. Naming
  is therefore a pure, unit-tested rewriter (Task 7) rather than untestable Unity code, and **no Harmony
  patching is needed anywhere in this mod**.
- **Spec test items 1–7** map to Tasks 1–7; **items 8–11** map to Task 10 Steps 7–8. Spec item 10
  ("new chips resolve a name and an icon") is additionally covered at unit level by Task 7.
- Spec's `Points` rule, drop-weight default, unresolved-id rule, tier `TechLevel` values, tier `Price`
  values and discovery predicate all appear with identical values.
- **Type consistency:** `ChipTierPlan.Build(ConfigDocument, int, int)`, `CompanyChip.IdFor(Tier)`,
  `CompanyChip.UnlocksFor(Tier)`, `ChipTierPlan.ChipByParentId(string)` and `ChipTierPlan.TierOfItem(string)`
  are used with those exact signatures in Tasks 4, 5, 6, 7 and 9. `ResourceHook.Initialise` takes four
  arguments in both its definition (Task 9) and its only call site (Task 10).
- **Test counts** are cumulative and stated per task: 5, 10, 17, 22, 28, 33, 40, 44, then 44 at the end.
- `ChipTierPlan.ContainsChipId` is declared in Task 3's interface block but never consumed by a later
  task. It is cheap and plausibly useful for debugging; if the implementer prefers, drop it rather than
  ship an unused public method.
