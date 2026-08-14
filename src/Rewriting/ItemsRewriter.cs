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
