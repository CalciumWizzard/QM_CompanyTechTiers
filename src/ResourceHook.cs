using System;
using System.Collections.Generic;
using System.IO;
using MGSC;
using QM_CompanyTechTiers.Configs;
using QM_CompanyTechTiers.Rewriting;
using QM_CompanyTechTiers.Sprites;
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
        private static Dictionary<string, string> _rewrites;
        private static bool _failed;
        private static bool _dumped;
        private static bool _spritesDumped;

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
            _rewrites = null;
            _failed = false;
            _dumped = false;
            _spritesDumped = false;
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
                        RequirePlan();
                        return ServeCached(ItemsPath);

                    case DropsPath:
                        RequirePlan();
                        return ServeCached(DropsPath);

                    case CraftingPath:
                        if (!_settings.RemapUpgradeCosts) return null;
                        RequirePlan();
                        return ServeCached(CraftingPath);

                    case LocalizationPath:
                        RequirePlan();
                        return ServeCached(LocalizationPath);

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

        /// <summary>
        /// Builds the tiering plan and, the first time it runs, precomputes every config rewrite
        /// in one pass so the four resources are served transactionally: either all of them are
        /// ready to hand out, or an exception here trips <see cref="_failed"/> before any one of
        /// them has been returned to the game.
        /// </summary>
        private static ChipTierPlan RequirePlan()
        {
            if (_plan != null) return _plan;

            var stock = LoadStock<TextAsset>(ItemsPath);
            if (stock == null) throw new InvalidOperationException("config_items not found.");

            _plan = ChipTierPlan.Build(ConfigDocument.Parse(stock.text),
                                       _settings.TierBoundaries[0], _settings.TierBoundaries[1]);
            _log("Discovered " + _plan.Chips.Count + " company chips.");

            PrecomputeRewrites(_plan);

            return _plan;
        }

        private static void PrecomputeRewrites(ChipTierPlan plan)
        {
            var rewrites = new Dictionary<string, string>();

            AddRewrite(rewrites, ItemsPath, text => ItemsRewriter.Rewrite(
                text, plan, _settings.TierPrices, _settings.TierTechLevels));

            AddRewrite(rewrites, DropsPath, text => FactionDropsRewriter.Rewrite(
                text, plan, _settings.RewardLevels, _settings.TierPrices,
                _settings.WeightDonorIds, _settings.InheritParentDropWeight));

            if (_settings.RemapUpgradeCosts)
                AddRewrite(rewrites, CraftingPath, text => CraftingRewriter.Rewrite(text, plan));

            AddRewrite(rewrites, LocalizationPath, text => LocalizationRewriter.Rewrite(
                text, plan, _settings.TierSuffixes));

            // Assigned only once every rewrite above has succeeded, so a throw partway through
            // leaves _rewrites null and nothing gets served from a half-built cache.
            _rewrites = rewrites;
        }

        /// <summary>
        /// Loads the stock text for <paramref name="path"/> via <see cref="Resources.Load"/> (never
        /// through CustomResources, which would re-enter this hook) and stores the transformed text
        /// under the same key. A missing stock resource is not fatal: it is warned about and simply
        /// left out of the cache, same as the old per-path behaviour.
        /// </summary>
        private static void AddRewrite(Dictionary<string, string> rewrites, string path, Func<string, string> transform)
        {
            var stock = LoadStock<TextAsset>(path);
            if (stock == null) { _warn("Stock resource '" + path + "' missing."); return; }

            rewrites[path] = transform(stock.text);
            _log("Rewrote " + path + ".");
        }

        private static TextAsset ServeCached(string path)
        {
            string text;
            if (_rewrites == null || !_rewrites.TryGetValue(path, out text)) return null;

            var asset = new TextAsset(text);
            asset.name = path.Substring(path.LastIndexOf('/') + 1);
            return asset;
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

                    if (_settings.DumpChipSpritesOnLoad)
                        DumpSprite(chip.IdFor(tier), parent as ItemContentDescriptor);

                    collection.AddDescriptor(chip.IdFor(tier), parent);
                    added++;
                }
            }

            _log("Registered " + added + " tier descriptors.");
            return collection;
        }

        /// <summary>
        /// Writes the parent chip's inventory icon under a tier id, giving the author a pre-named
        /// copy to edit. Best-effort: a failure here must never disturb descriptor registration.
        /// </summary>
        private static void DumpSprite(string tierId, ItemContentDescriptor parent)
        {
            if (parent == null) return;

            try
            {
                string folder = Path.Combine(_dumpFolder, "..", "sprite_dump");
                Directory.CreateDirectory(folder);

                byte[] png = SpriteDumper.ToPng(parent.Icon);
                if (png == null) { _warn("No icon to dump for " + tierId + "."); return; }

                File.WriteAllBytes(Path.Combine(folder, SpriteFileResolver.FileNameFor(tierId)), png);

                if (!_spritesDumped)
                {
                    _spritesDumped = true;
                    _log("Chip sprite dump writing to " + Path.GetFullPath(folder));
                }
            }
            catch (Exception ex)
            {
                _warn("Sprite dump failed for " + tierId + ": " + ex.Message);
            }
        }
    }
}
