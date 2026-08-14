using System;
using System.IO;
using System.Linq;
using Xunit;

namespace QM_CompanyTechTiers.Tests
{
    public static class Fixtures
    {
        private static readonly string Root = FindRoot();

        private static string FindRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "tests", "fixtures");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return null;
        }

        private static readonly string[] Required =
        {
            "config_items.txt", "config_faction_drops.txt", "config_crafting.txt", "localization.txt",
        };

        public static bool Available =>
            Root != null && Required.All(f => File.Exists(Path.Combine(Root, f)));

        private static string Read(string name) => File.ReadAllText(Path.Combine(Root, name));

        public static string Items => Read("config_items.txt");
        public static string FactionDrops => Read("config_faction_drops.txt");
        public static string Crafting => Read("config_crafting.txt");
        public static string LocalizationTable => Read("localization.txt");
    }

    /// <summary>A Fact that skips itself when the game config fixtures are not present.</summary>
    public sealed class FixtureFactAttribute : FactAttribute
    {
        public FixtureFactAttribute()
        {
            if (!Fixtures.Available)
                Skip = "Config fixtures missing. See tests/fixtures/README.md.";
        }
    }
}
