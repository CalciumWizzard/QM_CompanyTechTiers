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
        /// The high tier keeps the original id so the existing recipes referencing it stay valid.
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
        }

        public CompanyChip ChipByParentId(string parentId) =>
            _byParentId.TryGetValue(parentId, out var chip) ? chip : null;

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
