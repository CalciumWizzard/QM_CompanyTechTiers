using System;
using System.IO;
using MGSC;
using QM_CompanyTechTiers.Configs;
using QM_CompanyTechTiers.Rewriting;
using QM_CompanyTechTiers.Tiering;
using UnityEngine;

namespace QM_CompanyTechTiers
{
    /// <summary>
    /// Intercepts config resources before MGSC.ConfigLoader parses them and returns rewritten text.
    /// Anything unexpected returns null so the game falls back to the stock resource.
    /// </summary>
    public static class ResourceHook
    {
        private const string ItemsPath = "config_items";
        private const string DropsPath = "config_faction_drops";
        private const string CraftingPath = "config_crafting";
        private const string LocalizationPath = "localization";
        private const string DatadiskDescriptorsPath = "DescriptorsCollections/datadisks_descriptors";

        private static ModSettings _settings;
        private static string _dumpFolder;
        private static Action<string> _log = _ => { };
        private static Action<string> _warn = _ => { };

        private static ChipTierPlan _plan;
        private static bool _failed;
        private static bool _dumped;

        /// <summary>The plan built during config rewriting. Null if rewriting never ran or failed.</summary>
        public static ChipTierPlan Plan { get { return _plan; } }

        public static void Initialise(ModSettings settings, string dumpFolder,
                                      Action<string> log, Action<string> warn)
        {
            _settings = settings;
            _dumpFolder = dumpFolder;
            _log = log ?? (_ => { });
            _warn = warn ?? (_ => { });
            _plan = null;
            _failed = false;
            _dumped = false;
        }

        public static UnityEngine.Object Load(string path)
        {
            if (_failed || _settings == null) return null;

            try
            {
                switch (path)
                {
                    case ItemsPath:
                        if (_settings.DumpConfigsOnLoad) DumpOnce();
                        return Rewrite(ItemsPath, text => ItemsRewriter.Rewrite(
                            text, RequirePlan(), _settings.TierPrices, _settings.TierTechLevels));

                    case DropsPath:
                        return Rewrite(DropsPath, text => FactionDropsRewriter.Rewrite(
                            text, RequirePlan(), _settings.RewardLevels, _settings.TierPrices,
                            _settings.WeightDonorIds, _settings.InheritParentDropWeight));

                    case CraftingPath:
                        if (!_settings.RemapUpgradeCosts) return null;
                        return Rewrite(CraftingPath, text => CraftingRewriter.Rewrite(text, RequirePlan()));

                    case LocalizationPath:
                        return Rewrite(LocalizationPath, text => LocalizationRewriter.Rewrite(
                            text, RequirePlan(), _settings.TierSuffixes));

                    case DatadiskDescriptorsPath:
                        return CloneDescriptors();

                    default:
                        return null;
                }
            }
            catch (Exception ex)
            {
                _failed = true;
                _warn("Rewriting '" + path + "' failed; falling back to stock configs. Mod is now inert. " + ex);
                return null;
            }
        }

        /// <summary>Loads without re-entering CustomResources, so this hook is not called recursively.</summary>
        private static T LoadStock<T>(string path) where T : UnityEngine.Object
        {
            return Resources.Load<T>(path);
        }

        private static ChipTierPlan RequirePlan()
        {
            if (_plan != null) return _plan;

            var stock = LoadStock<TextAsset>(ItemsPath);
            if (stock == null) throw new InvalidOperationException("config_items not found.");

            _plan = ChipTierPlan.Build(ConfigDocument.Parse(stock.text),
                                       _settings.TierBoundaries[0], _settings.TierBoundaries[1]);
            _log("Discovered " + _plan.Chips.Count + " company chips.");
            return _plan;
        }

        private static TextAsset Rewrite(string path, Func<string, string> transform)
        {
            var stock = LoadStock<TextAsset>(path);
            if (stock == null) { _warn("Stock resource '" + path + "' missing."); return null; }

            var rewritten = new TextAsset(transform(stock.text));
            rewritten.name = path.Substring(path.LastIndexOf('/') + 1);
            _log("Rewrote " + path + ".");
            return rewritten;
        }

        private static void DumpOnce()
        {
            if (_dumped) return;
            _dumped = true;

            try
            {
                Directory.CreateDirectory(_dumpFolder);
                foreach (string name in new[] { ItemsPath, DropsPath, CraftingPath, LocalizationPath })
                {
                    var asset = LoadStock<TextAsset>(name);
                    if (asset != null)
                        File.WriteAllText(Path.Combine(_dumpFolder, name + ".txt"), asset.text);
                }
                _log("Config dump written to " + _dumpFolder);
            }
            catch (Exception ex)
            {
                _warn("Config dump failed: " + ex.Message);
            }
        }

        /// <summary>Gives each new tier id the parent chip's icon by reusing its descriptor object.</summary>
        private static UnityEngine.Object CloneDescriptors()
        {
            var collection = LoadStock<DescriptorsCollection>(DatadiskDescriptorsPath);
            if (collection == null) return null;

            var plan = RequirePlan();
            int added = 0;

            foreach (var chip in plan.Chips)
            {
                UnityEngine.Object parent;
                if (!collection.TryGetDescriptor(chip.ParentId, out parent) || parent == null) continue;

                foreach (var tier in new[] { Tier.Low, Tier.Mid })
                {
                    UnityEngine.Object existing;
                    if (collection.TryGetDescriptor(chip.IdFor(tier), out existing)) continue;
                    collection.AddDescriptor(chip.IdFor(tier), parent);
                    added++;
                }
            }

            _log("Registered " + added + " tier descriptors.");
            return collection;
        }
    }
}
