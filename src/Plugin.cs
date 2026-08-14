using System.IO;
using MGSC;

namespace QM_CompanyTechTiers
{
    public static class Plugin
    {
        public static ConfigDirectories ConfigDirectories = new ConfigDirectories();
        public static Logger Logger = new Logger();
        public static ModSettings Settings { get; private set; }

        /// <summary>
        /// Runs before Data.Load(), which is where ConfigLoader reads every config resource.
        /// The ResourcesLoad hook must be armed by then.
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

        [Hook(ModHookType.ResourcesLoad)]
        public static UnityEngine.Object ResourcesLoad(string path)
        {
            return ResourceHook.Load(path);
        }
    }
}
