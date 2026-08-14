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
        private static readonly string[] WeightDonorIds = { "low_chip", "medium_chip" };

        private static ChipTierPlan Plan() =>
            ChipTierPlan.Build(ConfigDocument.Parse(Fixtures.Items), 3, 6);

        private static string Rewritten(bool inherit = false) =>
            FactionDropsRewriter.Rewrite(Fixtures.FactionDrops, Plan(), Levels, Prices, WeightDonorIds, inherit);

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
        public void New_rows_sit_directly_beneath_the_parent_in_low_then_mid_order()
        {
            var before = ConfigDocument.Parse(Fixtures.FactionDrops);
            var after = ConfigDocument.Parse(Rewritten());
            var plan = Plan();

            foreach (var section in before.Sections.Where(s => s.Name.EndsWith("_rewardChips")))
            {
                var target = after.Section(section.Name);
                int ci = target.ColumnIndex("ContentIds");
                var rows = target.Rows;

                foreach (var chip in plan.Chips)
                {
                    if (CountChipEntries(section, chip.ParentId) == 0) continue;

                    int parentIndex = -1;
                    for (int i = 0; i < rows.Count; i++)
                    {
                        if (rows[i].Get(ci) == chip.ParentId) { parentIndex = i; break; }
                    }

                    Assert.True(parentIndex >= 0, $"Parent row {chip.ParentId} not found in {section.Name}.");
                    Assert.True(parentIndex + 2 < rows.Count, $"Not enough rows after parent {chip.ParentId} in {section.Name}.");
                    Assert.Equal(chip.IdFor(Tier.Low), rows[parentIndex + 1].Get(ci));
                    Assert.Equal(chip.IdFor(Tier.Mid), rows[parentIndex + 2].Get(ci));
                }
            }
        }

        [FixtureFact]
        public void Output_reparses_and_round_trips()
        {
            string text = Rewritten();
            Assert.Equal(text, ConfigDocument.Parse(text).Render());
        }
    }
}
