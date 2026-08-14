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
