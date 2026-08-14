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
