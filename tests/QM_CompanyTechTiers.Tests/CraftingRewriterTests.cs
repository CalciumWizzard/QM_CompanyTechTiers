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
