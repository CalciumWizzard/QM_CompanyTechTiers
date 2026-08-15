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
    /// Applies this mod's changes to config resources before MGSC.ConfigLoader parses them.
    ///
    /// Driven by a Harmony postfix on CustomResources.Load rather than the game's ResourcesLoad
    /// hook - see Patches/CustomResourcesLoadPatch for why that hook cannot be shared. The practical
    /// consequence for this file: each path arrives separately carrying whatever earlier mods
    /// produced, and must be transformed as it arrives. It is NOT valid to precompute rewrites from
    /// the stock text and serve those, because that would discard other mods' work just as
    /// thoroughly as owning the hook did.
    ///
    /// That costs the old all-or-nothing guarantee. config_items is served before localization is
    /// even requested, so a late failure cannot un-rewrite an early success. Instead the first
    /// failure latches the mod inert and logs which paths were already rewritten, so a half-applied
    /// state is diagnosable rather than mysterious.
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
        private static SpriteFileResolver _sprites;
        private static Action<string> _log = _ => { };
        private static Action<string> _warn = _ => { };

        private static ChipTierPlan _plan;
        private static bool _failed;
        private static bool _dumped;
        private static bool _spritesDumped;

        /// <summary>
        /// Produced assets, keyed by path. Localization.LoadDB reloads on a language change and the
        /// table is ~15 MB, so re-running the rewrite on every call would be a visible stall.
        /// </summary>
        private static readonly Dictionary<string, TextAsset> Produced =
            new Dictionary<string, TextAsset>(StringComparer.Ordinal);

        /// <summary>Paths already handed back rewritten, so a later failure can name them.</summary>
        private static readonly List<string> Rewritten = new List<string>();

        /// <summary>The plan built during config rewriting. Null if rewriting never ran or failed.</summary>
        public static ChipTierPlan Plan { get { return _plan; } }

        public static void Initialise(ModSettings settings, string dumpFolder, string spritesFolder,
                                      Action<string> log, Action<string> warn)
        {
            _settings = settings;
            _dumpFolder = dumpFolder;
            _sprites = new SpriteFileResolver(spritesFolder);
            _log = log ?? (_ => { });
            _warn = warn ?? (_ => { });
            _plan = null;
            _failed = false;
            _dumped = false;
            _spritesDumped = false;
            Produced.Clear();
            Rewritten.Clear();
        }

        /// <summary>
        /// Takes whatever CustomResources.Load resolved - another mod's rewrite, or the stock asset -
        /// and returns it with this mod's changes applied. Returns <paramref name="current"/>
        /// untouched for anything this mod does not handle.
        /// </summary>
        public static UnityEngine.Object PostProcess(string path, UnityEngine.Object current)
        {
            if (_failed || _settings == null || current == null) return current;

            try
            {
                switch (path)
                {
                    case ItemsPath:
                        if (_settings.DumpConfigsOnLoad) DumpOnce();
                        return Apply(ItemsPath, current, text => ItemsRewriter.Rewrite(
                            text, RequirePlan(), _settings.TierPrices, _settings.TierTechLevels));

                    case DropsPath:
                        return Apply(DropsPath, current, text => FactionDropsRewriter.Rewrite(
                            text, RequirePlan(), _settings.RewardLevels, _settings.TierPrices,
                            _settings.WeightDonorIds, _settings.InheritParentDropWeight));

                    case CraftingPath:
                        if (!_settings.RemapUpgradeCosts) return current;
                        return Apply(CraftingPath, current,
                                     text => CraftingRewriter.Rewrite(text, RequirePlan()));

                    case LocalizationPath:
                        return Apply(LocalizationPath, current, text => LocalizationRewriter.Rewrite(
                            text, RequirePlan(), _settings.TierSuffixes));

                    case DatadiskDescriptorsPath:
                        CloneDescriptors(current as DescriptorsCollection);
                        return current;

                    default:
                        return current;
                }
            }
            catch (Exception ex)
            {
                _failed = true;
                ReportFailure(path, ex);
                return current;
            }
        }

        /// <summary>
        /// Explains a mid-sequence failure in the terms that matter: what was already changed.
        ///
        /// Paths arrive one at a time and the game consumes each immediately, so a rewrite that
        /// succeeded before this one cannot be taken back. Naming them turns "some chips have no
        /// names" into something a player can act on.
        /// </summary>
        private static void ReportFailure(string path, Exception ex)
        {
            string already = Rewritten.Count == 0
                ? "Nothing had been rewritten yet, so the game is running on stock configs."
                : "Already rewritten before this failure: " + string.Join(", ", Rewritten.ToArray()) +
                  ". Those changes cannot be undone mid-session - restart the game to return to " +
                  "stock configs.";

            _warn("Rewriting '" + path + "' failed; this mod is now inert for the rest of the " +
                  "session. " + already + " " + ex);
        }

        /// <summary>
        /// Rewrites the text this mod was handed - never the stock text. Serving a stock-derived
        /// rewrite here would silently discard whatever earlier mods produced, which is the very
        /// problem the postfix exists to avoid.
        /// </summary>
        private static UnityEngine.Object Apply(string path, UnityEngine.Object current,
                                                Func<string, string> transform)
        {
            var source = current as TextAsset;
            if (source == null)
            {
                // Returning null here would blank the resource for the game and every other mod.
                _warn("Resource '" + path + "' was not a TextAsset; leaving it alone.");
                return current;
            }

            TextAsset cached;
            if (Produced.TryGetValue(path, out cached) && cached != null) return cached;

            var rewritten = new TextAsset(transform(source.text));
            rewritten.name = source.name;

            Produced[path] = rewritten;
            Rewritten.Add(path);
            _log("Rewrote " + path + ".");
            return rewritten;
        }

        /// <summary>Loads without re-entering CustomResources, so this hook is not called recursively.</summary>
        private static T LoadStock<T>(string path) where T : UnityEngine.Object
        {
            return Resources.Load<T>(path);
        }

        /// <summary>
        /// Builds the tiering plan, once, from the STOCK config_items.
        ///
        /// Deliberately not from the text this mod was handed. The plan is the list of vanilla
        /// company chips to build tiers for; deriving it from another mod's rewritten table could
        /// pick up ids that mod invented and manufacture tiers for them.
        /// </summary>
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

        /// <summary>
        /// Gives each new tier id the parent chip's icon by reusing its descriptor object.
        ///
        /// Mutates the collection it is handed and returns nothing: DescriptorsCollection is a
        /// ScriptableObject, so additions are visible to the game and to every other mod without
        /// substituting a different instance.
        /// </summary>
        private static void CloneDescriptors(DescriptorsCollection collection)
        {
            if (collection == null) return;

            var plan = RequirePlan();
            int added = 0;
            int customised = 0;

            foreach (var chip in plan.Chips)
            {
                UnityEngine.Object parent;
                if (!collection.TryGetDescriptor(chip.ParentId, out parent) || parent == null) continue;

                var parentDescriptor = parent as ItemContentDescriptor;

                foreach (var tier in new[] { Tier.Low, Tier.Mid })
                {
                    string id = chip.IdFor(tier);

                    UnityEngine.Object existing;
                    if (collection.TryGetDescriptor(id, out existing)) continue;

                    if (_settings.DumpChipSpritesOnLoad) DumpSprite(id, parentDescriptor);

                    UnityEngine.Object descriptor = parent;
                    if (_sprites != null && _sprites.HasArtFor(id))
                    {
                        var custom = SpriteLoader.TryBuildDescriptor(_sprites.PathFor(id), parentDescriptor, _warn);
                        if (custom != null) { descriptor = custom; customised++; }
                    }

                    collection.AddDescriptor(id, descriptor);
                    added++;
                }
            }

            _log("Registered " + added + " tier descriptors (" + customised + " with custom art).");
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
