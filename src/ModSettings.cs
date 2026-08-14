using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace QM_CompanyTechTiers
{
    /// <summary>
    /// User-tunable settings. Deliberately free of Unity and MGSC types so it can be unit tested.
    /// </summary>
    public sealed class ModSettings
    {
        /// <summary>Faction tech level at which each tier is offered as a reward.</summary>
        public int[] RewardLevels = { 3, 6, 10 };

        /// <summary>Item tech level at which tier 1 ends and tier 2 ends.</summary>
        public int[] TierBoundaries = { 3, 6 };

        /// <summary>Price for each tier. Index 2 is unused; the parent row keeps its own price.</summary>
        public int[] TierPrices = { 400, 525, 650 };

        /// <summary>TechLevel field on each tier's datadisk row. Index 2 is unused.</summary>
        public int[] TierTechLevels = { 1, 4, 10 };

        /// <summary>Appended to each tier's display name in every language.</summary>
        public string[] TierSuffixes = { " I", " II", " III" };

        /// <summary>Ids whose drop weight each lower tier borrows: [0] Low, [1] Mid.</summary>
        public string[] WeightDonorIds = { "low_chip", "medium_chip" };

        public bool RemapUpgradeCosts = true;

        /// <summary>Give lower tiers the parent chip's drop weight instead of the generic chip's.</summary>
        public bool InheritParentDropWeight = false;

        /// <summary>Write the game's raw config text next to this file, for regenerating test fixtures.</summary>
        public bool DumpConfigsOnLoad = false;

        /// <summary>Write each company chip's icon to sprite_dump/ as editable per-tier PNGs.</summary>
        public bool DumpChipSpritesOnLoad = false;

        /// <summary>
        /// Non-null when LoadOrCreate could not use the config file and substituted defaults.
        /// Describes what went wrong, for the caller to log. Never serialized.
        /// </summary>
        [JsonIgnore]
        public string LoadProblem { get; private set; }

        public static ModSettings LoadOrCreate(string path)
        {
            string readProblem = null;
            try
            {
                if (File.Exists(path))
                {
                    var loaded = JsonConvert.DeserializeObject<ModSettings>(File.ReadAllText(path));
                    if (loaded != null) return loaded;
                }
            }
            catch (Exception ex)
            {
                // Malformed config must not stop the mod loading; defaults are written below.
                readProblem = "Could not read or parse config at '" + path + "', using defaults: " + ex.Message;
                try
                {
                    File.Copy(path, path + ".bak", true);
                    readProblem += " Your file was preserved as config.json.bak.";
                }
                catch
                {
                    // Best effort only - a failed backup must not prevent the mod loading.
                }
            }

            var settings = new ModSettings();
            settings.LoadProblem = readProblem;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(settings, Formatting.Indented));
            }
            catch (Exception ex)
            {
                // A read-only location is survivable - carry on with in-memory defaults.
                settings.LoadProblem = "Could not write default config to '" + path + "': " + ex.Message;
            }
            return settings;
        }

        public List<string> Validate()
        {
            var problems = new List<string>();

            if (RewardLevels == null || RewardLevels.Length != 3)
                problems.Add("RewardLevels must have exactly 3 entries.");
            else if (RewardLevels[0] > RewardLevels[1] || RewardLevels[1] > RewardLevels[2])
                problems.Add("RewardLevels must be non-decreasing.");

            if (TierBoundaries == null || TierBoundaries.Length != 2)
                problems.Add("TierBoundaries must have exactly 2 entries.");
            else if (TierBoundaries[0] > TierBoundaries[1])
                problems.Add("TierBoundaries must be non-decreasing.");

            if (TierPrices == null || TierPrices.Length != 3)
                problems.Add("TierPrices must have exactly 3 entries.");

            if (TierTechLevels == null || TierTechLevels.Length != 3)
                problems.Add("TierTechLevels must have exactly 3 entries.");

            if (TierSuffixes == null || TierSuffixes.Length != 3)
                problems.Add("TierSuffixes must have exactly 3 entries.");

            if (WeightDonorIds == null || WeightDonorIds.Length != 2)
                problems.Add("WeightDonorIds must have exactly 2 entries.");

            return problems;
        }
    }
}
