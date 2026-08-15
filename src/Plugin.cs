using System;
using System.IO;
using HarmonyLib;
using MGSC;

namespace QM_CompanyTechTiers
{
    public static class Plugin
    {
        public const string HarmonyId = "QM_CompanyTechTiers";

        public static ConfigDirectories ConfigDirectories = new ConfigDirectories();
        public static Logger Logger = new Logger();
        public static ModSettings Settings { get; private set; }

        /// <summary>
        /// Runs before Data.Load(), which is where ConfigLoader reads every config resource.
        /// The CustomResources.Load patch must be armed by then.
        /// </summary>
        [Hook(ModHookType.BeforeBootstrap)]
        public static void BeforeBootstrap(IModContext context)
        {
            Directory.CreateDirectory(ConfigDirectories.ModPersistenceFolder);
            Settings = ModSettings.LoadOrCreate(ConfigDirectories.ConfigPath);

            foreach (string problem in Settings.Validate())
                Logger.LogWarning("Config problem: " + problem);

            if (!string.IsNullOrEmpty(Settings.LoadProblem))
                Logger.LogWarning(Settings.LoadProblem);

            string spritesFolder = Path.Combine(ResolveModContentPath(context), "sprites");

            ResourceHook.Initialise(
                Settings,
                Path.Combine(ConfigDirectories.ModPersistenceFolder, "configdump"),
                spritesFolder,
                Logger.Log,
                Logger.LogWarning);

            try
            {
                var harmony = new Harmony(HarmonyId);

                // Deliberately not PatchAll: that aborts on the first patch class that fails, so one
                // broken target would take every other patch down with it.
                foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
                {
                    try
                    {
                        harmony.CreateClassProcessor(type).Patch();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("Patch class " + type.Name + " failed to apply.");
                        Logger.LogException(ex);
                    }
                }

                foreach (var method in harmony.GetPatchedMethods())
                    Logger.Log("Patched " + method.DeclaringType.Name + "." + method.Name + ".");
            }
            catch (Exception ex)
            {
                // A patch target a game update renamed must degrade to "mod does nothing", not to
                // "game will not start".
                Logger.LogError("Harmony patching failed; chip tiers are disabled for this session.");
                Logger.LogException(ex);
            }

            Logger.Log("Armed. Config at " + ConfigDirectories.ConfigPath + "; sprites from " + spritesFolder);
        }

        /// <summary>
        /// IModContext.ModContentPath is the mod's folder, but this mod has only ever read it from
        /// AfterConfigsLoaded. If it is not yet populated during BeforeBootstrap, fall back to the
        /// directory this assembly was loaded from, which is the same folder.
        /// </summary>
        private static string ResolveModContentPath(IModContext context)
        {
            string path = context != null ? context.ModContentPath : null;
            if (!string.IsNullOrEmpty(path)) return path;

            Logger.LogWarning("ModContentPath was empty during BeforeBootstrap; using the assembly folder.");
            return Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? string.Empty;
        }

        // No [Hook(ModHookType.ResourcesLoad)] here on purpose. That hook is first-answer-wins, so
        // using it made this mod silently disable every other config-rewriting mod that loaded after
        // it - and left it liable to be disabled itself by one that loaded first. See
        // Patches/CustomResourcesLoadPatch.
    }
}
