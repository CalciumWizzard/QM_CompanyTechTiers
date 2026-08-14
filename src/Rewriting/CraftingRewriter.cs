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
