using System.IO;
using QM_CompanyTechTiers.Sprites;
using Xunit;

namespace QM_CompanyTechTiers.Tests
{
    public class SpriteFileResolverTests
    {
        private static string FreshFolder()
        {
            string dir = Path.Combine(Path.GetTempPath(), "qmctt_sprites_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public void FileNameFor_appends_png_to_the_tier_id()
        {
            Assert.Equal("anc_chip_low.png", SpriteFileResolver.FileNameFor("anc_chip_low"));
            Assert.Equal("rwa_chip_mid.png", SpriteFileResolver.FileNameFor("rwa_chip_mid"));
        }

        [Fact]
        public void PathFor_combines_the_folder_and_the_file_name()
        {
            string folder = FreshFolder();
            var resolver = new SpriteFileResolver(folder);
            Assert.Equal(Path.Combine(folder, "anc_chip_low.png"), resolver.PathFor("anc_chip_low"));
        }

        [Fact]
        public void HasArtFor_is_true_only_when_the_file_exists()
        {
            string folder = FreshFolder();
            var resolver = new SpriteFileResolver(folder);

            Assert.False(resolver.HasArtFor("anc_chip_low"));

            File.WriteAllBytes(Path.Combine(folder, "anc_chip_low.png"), new byte[] { 1, 2, 3 });

            Assert.True(resolver.HasArtFor("anc_chip_low"));
            Assert.False(resolver.HasArtFor("anc_chip_mid"));
        }

        [Fact]
        public void A_missing_folder_is_handled_without_throwing()
        {
            var resolver = new SpriteFileResolver(Path.Combine(Path.GetTempPath(), "qmctt_no_such_" + Path.GetRandomFileName()));
            Assert.False(resolver.HasArtFor("anc_chip_low"));
        }

        [Fact]
        public void A_null_or_empty_folder_yields_no_path_and_no_art()
        {
            foreach (var resolver in new[] { new SpriteFileResolver(null), new SpriteFileResolver("") })
            {
                Assert.Null(resolver.PathFor("anc_chip_low"));
                Assert.False(resolver.HasArtFor("anc_chip_low"));
            }
        }

        [Fact]
        public void A_null_or_empty_id_yields_no_path_and_no_art()
        {
            var resolver = new SpriteFileResolver(FreshFolder());
            Assert.Null(resolver.PathFor(null));
            Assert.Null(resolver.PathFor(""));
            Assert.False(resolver.HasArtFor(null));
        }
    }
}
