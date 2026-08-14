using HarmonyLib;
using MGSC;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace QM_CompanyTechTiers
{
    public static class Plugin
    {

        public static ConfigDirectories ConfigDirectories = new ConfigDirectories();

        public static ModSettings Settings { get; private set; }

        public static Logger Logger = new Logger();

        [Hook(ModHookType.AfterConfigsLoaded)]
        public static void AfterConfig(IModContext context)
        {

            Directory.CreateDirectory(ConfigDirectories.ModPersistenceFolder);

            Settings = ModSettings.LoadOrCreate(ConfigDirectories.ConfigPath);

            new Harmony("babyak_" + ConfigDirectories.ModAssemblyName).PatchAll();
        }
     
    }
}
