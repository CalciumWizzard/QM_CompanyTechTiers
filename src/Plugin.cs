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

            ResourceHook.Initialise(
                Settings,
                Path.Combine(ConfigDirectories.ModPersistenceFolder, "configdump"),
                Logger.Log,
                Logger.LogWarning);

            Logger.Log("Armed. Config at " + ConfigDirectories.ConfigPath);
        }

        [Hook(ModHookType.ResourcesLoad)]
        public static UnityEngine.Object ResourcesLoad(string path)
        {
            return ResourceHook.Load(path);
        }
    }
}
