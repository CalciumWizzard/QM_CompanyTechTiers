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
