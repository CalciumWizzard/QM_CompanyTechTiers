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
