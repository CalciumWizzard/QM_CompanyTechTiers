using System.IO;
using QM_CompanyTechTiers;
using Xunit;

namespace QM_CompanyTechTiers.Tests
{
    public class ModSettingsTests
    {
        private static string TempPath() =>
            Path.Combine(Path.GetTempPath(), "qmctt_" + Path.GetRandomFileName(), "config.json");

        [Fact]
        public void Creates_the_file_with_documented_defaults_when_absent()
        {
            string path = TempPath();
            var settings = ModSettings.LoadOrCreate(path);

            Assert.True(File.Exists(path));
            Assert.Equal(new[] { 3, 6, 10 }, settings.RewardLevels);
            Assert.Equal(new[] { 3, 6 }, settings.TierBoundaries);
            Assert.Equal(new[] { 400, 525, 650 }, settings.TierPrices);
            Assert.Equal(new[] { 1, 4, 10 }, settings.TierTechLevels);
            Assert.Equal(new[] { " I", " II", " III" }, settings.TierSuffixes);
            Assert.Equal(new[] { "low_chip", "medium_chip" }, settings.WeightDonorIds);
            Assert.True(settings.RemapUpgradeCosts);
            Assert.False(settings.InheritParentDropWeight);
            Assert.False(settings.DumpConfigsOnLoad);
            Assert.False(settings.DumpChipSpritesOnLoad);
        }

        [Fact]
        public void Reads_values_back_from_disk()
        {
            string path = TempPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{ \"RewardLevels\": [2, 5, 10], \"RemapUpgradeCosts\": false }");

            var settings = ModSettings.LoadOrCreate(path);
            Assert.Equal(new[] { 2, 5, 10 }, settings.RewardLevels);
            Assert.False(settings.RemapUpgradeCosts);
            Assert.Equal(new[] { 3, 6 }, settings.TierBoundaries);   // untouched key keeps its default
        }

        [Fact]
        public void Falls_back_to_defaults_on_malformed_json_rather_than_throwing()
        {
            string path = TempPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{ this is not json");

            var settings = ModSettings.LoadOrCreate(path);
            Assert.Equal(new[] { 3, 6, 10 }, settings.RewardLevels);
        }

        [Fact]
        public void Reports_LoadProblem_on_malformed_json()
        {
            string path = TempPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{ this is not json");

            var settings = ModSettings.LoadOrCreate(path);
            Assert.NotNull(settings.LoadProblem);
            Assert.Contains(path, settings.LoadProblem);
        }

        [Fact]
        public void Preserves_the_malformed_file_as_a_bak_before_overwriting_with_defaults()
        {
            string path = TempPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            const string malformed = "{ this is not json";
            File.WriteAllText(path, malformed);

            ModSettings.LoadOrCreate(path);

            string bakPath = path + ".bak";
            Assert.True(File.Exists(bakPath));
            Assert.Equal(malformed, File.ReadAllText(bakPath));
        }

        [Fact]
        public void LoadProblem_is_null_after_a_normal_load_of_a_valid_file()
        {
            string path = TempPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{ \"RewardLevels\": [2, 5, 10] }");

            var settings = ModSettings.LoadOrCreate(path);
            Assert.Null(settings.LoadProblem);
        }

        [Fact]
        public void LoadProblem_is_null_on_fresh_creation_when_the_file_is_absent()
        {
            var settings = ModSettings.LoadOrCreate(TempPath());
            Assert.Null(settings.LoadProblem);
        }

        [Fact]
        public void The_file_written_on_first_run_does_not_contain_a_LoadProblem_key()
        {
            string path = TempPath();
            ModSettings.LoadOrCreate(path);

            string written = File.ReadAllText(path);
            Assert.DoesNotContain("LoadProblem", written);
        }

        [Fact]
        public void Validate_rejects_out_of_order_levels_and_bad_array_lengths()
        {
            var settings = ModSettings.LoadOrCreate(TempPath());
            Assert.Empty(settings.Validate());

            settings.RewardLevels = new[] { 6, 3, 10 };
            Assert.Contains(settings.Validate(), m => m.Contains("RewardLevels"));

            settings = ModSettings.LoadOrCreate(TempPath());
            settings.TierBoundaries = new[] { 3 };
            Assert.Contains(settings.Validate(), m => m.Contains("TierBoundaries"));

            settings = ModSettings.LoadOrCreate(TempPath());
            settings.TierBoundaries = new[] { 7, 6 };
            Assert.Contains(settings.Validate(), m => m.Contains("TierBoundaries"));

            settings = ModSettings.LoadOrCreate(TempPath());
            settings.TierSuffixes = new[] { " I" };
            Assert.Contains(settings.Validate(), m => m.Contains("TierSuffixes"));

            settings = ModSettings.LoadOrCreate(TempPath());
            settings.WeightDonorIds = null;
            Assert.Contains(settings.Validate(), m => m.Contains("WeightDonorIds"));

            settings = ModSettings.LoadOrCreate(TempPath());
            settings.WeightDonorIds = new[] { "low_chip" };
            Assert.Contains(settings.Validate(), m => m.Contains("WeightDonorIds"));
        }
    }
}
