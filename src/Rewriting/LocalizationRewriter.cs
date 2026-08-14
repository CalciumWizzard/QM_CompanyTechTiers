using System;
using System.Collections.Generic;
using System.Linq;
using QM_CompanyTechTiers.Tiering;

namespace QM_CompanyTechTiers.Rewriting
{
    /// <summary>
    /// Names the new chip tiers by cloning the parent chip's rows in the localization table and
    /// appending a language-neutral roman numeral to the display name.
    ///
    /// The table is a flat TSV: column 0 is the key, columns 1..11 are the languages in
    /// MGSC.Localization.Lang order. MGSC.Localization.LoadDB keeps the FIRST occurrence of a key,
    /// so new keys are appended and the parent's own name row is edited in place.
    /// </summary>
    public static class LocalizationRewriter
    {
        private const int FirstLanguageColumn = 1;

        private static readonly string[] Facets = { "name", "shortdesc", "desc" };

        public static string Rewrite(string localizationText, ChipTierPlan plan, string[] tierSuffixes)
        {
            if (localizationText == null) throw new ArgumentNullException(nameof(localizationText));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (tierSuffixes == null || tierSuffixes.Length < 3)
                throw new ArgumentException("Need 3 suffixes.", nameof(tierSuffixes));

            var lines = localizationText.Split('\n').ToList();

            // key -> line index, first occurrence only (matching LoadDB).
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < lines.Count; i++)
            {
                string body = lines[i].TrimEnd('\r');
                if (body.Length == 0) continue;
                string key = body.Split('\t')[0];
                if (!index.ContainsKey(key)) index[key] = i;
            }

            var appended = new List<string>();

            foreach (var chip in plan.Chips)
            {
                // High tier: the parent row itself gains its numeral, in place.
                string parentNameKey = "item." + chip.ParentId + ".name";
                int parentNameLine;
                if (index.TryGetValue(parentNameKey, out parentNameLine))
                    lines[parentNameLine] = SuffixLanguages(lines[parentNameLine], tierSuffixes[(int)Tier.High]);

                foreach (var tier in new[] { Tier.Low, Tier.Mid })
                {
                    string tierId = chip.IdFor(tier);

                    foreach (string facet in Facets)
                    {
                        string sourceKey = "item." + chip.ParentId + "." + facet;
                        string targetKey = "item." + tierId + "." + facet;

                        int sourceLine;
                        if (!index.TryGetValue(sourceKey, out sourceLine)) continue;  // parent lacks this facet
                        if (index.ContainsKey(targetKey)) continue;                    // already present

                        string cloned = ReplaceKey(lines[sourceLine], targetKey);

                        if (facet == "name")
                        {
                            // The parent name row was suffixed above with " III"; strip it before
                            // applying this tier's numeral so the result is not "Chip III I".
                            cloned = StripSuffixLanguages(cloned, tierSuffixes[(int)Tier.High]);
                            cloned = SuffixLanguages(cloned, tierSuffixes[(int)tier]);
                        }

                        appended.Add(cloned);
                    }
                }
            }

            if (appended.Count == 0) return localizationText;

            // Every row ends with CR except the final one; keep that shape.
            for (int i = 0; i < lines.Count; i++)
                if (!lines[i].EndsWith("\r", StringComparison.Ordinal))
                    lines[i] = lines[i] + "\r";

            for (int i = 0; i < appended.Count; i++)
                if (!appended[i].EndsWith("\r", StringComparison.Ordinal))
                    appended[i] = appended[i] + "\r";

            lines.AddRange(appended);
            lines[lines.Count - 1] = lines[lines.Count - 1].TrimEnd('\r');

            return string.Join("\n", lines);
        }

        private static string ReplaceKey(string line, string newKey)
        {
            bool cr = line.EndsWith("\r", StringComparison.Ordinal);
            var cells = line.TrimEnd('\r').Split('\t');
            cells[0] = newKey;
            return string.Join("\t", cells) + (cr ? "\r" : string.Empty);
        }

        private static string SuffixLanguages(string line, string suffix)
        {
            bool cr = line.EndsWith("\r", StringComparison.Ordinal);
            var cells = line.TrimEnd('\r').Split('\t');

            for (int i = FirstLanguageColumn; i < cells.Length; i++)
            {
                if (cells[i].Length == 0) continue;
                if (cells[i].EndsWith(suffix, StringComparison.Ordinal)) continue;  // idempotent
                cells[i] += suffix;
            }

            return string.Join("\t", cells) + (cr ? "\r" : string.Empty);
        }

        private static string StripSuffixLanguages(string line, string suffix)
        {
            bool cr = line.EndsWith("\r", StringComparison.Ordinal);
            var cells = line.TrimEnd('\r').Split('\t');

            for (int i = FirstLanguageColumn; i < cells.Length; i++)
                if (cells[i].EndsWith(suffix, StringComparison.Ordinal))
                    cells[i] = cells[i].Substring(0, cells[i].Length - suffix.Length);

            return string.Join("\t", cells) + (cr ? "\r" : string.Empty);
        }
    }
}
