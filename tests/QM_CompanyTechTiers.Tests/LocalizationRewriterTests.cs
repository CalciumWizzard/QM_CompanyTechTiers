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

        /// <summary>
        /// `Is_idempotent` above only pins the no-op path: when every target key already exists,
        /// `Rewrite` returns the original string unchanged and never touches `lines` at all, so the
        /// "skip cells that already end with the suffix" guard inside `SuffixLanguages` is never
        /// exercised. The guard actually matters when a *later* run has new work to append (e.g. a
        /// 13th company chip shows up) and therefore fully re-serializes `lines`, including the
        /// already-suffixed parent rows from the first pass. This test forces that path.
        /// </summary>
        [FixtureFact]
        public void Second_run_with_a_larger_chip_set_does_not_double_suffix_already_tiered_parents()
        {
            var plan = Plan();
            string once = LocalizationRewriter.Rewrite(Fixtures.LocalizationTable, plan, Suffixes);

            // Grow the chip roster: clone an existing datadisk row as a brand-new company chip so
            // plan2.Chips is a strict superset of plan.Chips.
            var doc = ConfigDocument.Parse(Fixtures.Items);
            var datadisks = doc.Section("datadisks");
            int idCol = datadisks.ColumnIndex("Id");
            int categoriesCol = datadisks.ColumnIndex("Categories");
            int unlockCol = datadisks.ColumnIndex("UnlockIds");
            var template = datadisks.Rows.First(r => !r.IsBlank && r.Get(idCol).Length > 0);

            const string newChipId = "zzz_synthetic_extra_chip";
            var newRow = Enumerable.Repeat(string.Empty, datadisks.Columns.Count).ToArray();
            newRow[idCol] = newChipId;
            newRow[categoriesCol] = "";   // must not contain the generic "Chip" token
            newRow[unlockCol] = "";
            datadisks.InsertRowAfter(template, newRow);

            var plan2 = ChipTierPlan.Build(doc, 3, 6);
            Assert.Equal(plan.Chips.Count + 1, plan2.Chips.Count);
            var newChip = plan2.ChipByParentId(newChipId);

            // The new chip needs its own pre-existing name row for the second Rewrite call to have
            // anything to append (and thus take the full-serialization path, not the early return).
            string extraNameRow =
                "item." + newChipId + ".name\tSynthetic Chip" + new string('\t', 16);
            string onceWithNewChip = once + "\r\n" + extraNameRow;

            string twice = LocalizationRewriter.Rewrite(onceWithNewChip, plan2, Suffixes);

            // Proof the full-serialization branch actually ran, not the appended.Count == 0 shortcut.
            Assert.True(Has(twice, "item." + newChip.IdFor(Tier.Low) + ".name"));

            // Every parent already suffixed in the first pass must come through unchanged.
            foreach (var chip in plan.Chips)
            {
                string key = "item." + chip.ParentId + ".name";
                Assert.Equal(Row(onceWithNewChip, key), Row(twice, key));
            }
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

        [FixtureFact]
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
