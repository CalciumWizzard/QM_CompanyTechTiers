using System.IO;

namespace QM_CompanyTechTiers.Sprites
{
    /// <summary>
    /// Maps a chip tier id to the PNG that overrides its inventory icon.
    /// Deliberately free of Unity types so the test project can link it.
    /// </summary>
    public sealed class SpriteFileResolver
    {
        private readonly string _folder;

        public SpriteFileResolver(string folder)
        {
            _folder = folder;
        }

        /// <summary>The one filename convention, shared by the dumper and the loader.</summary>
        public static string FileNameFor(string tierId)
        {
            return tierId + ".png";
        }

        /// <summary>Full path to a tier's art, or null when the folder or id is missing.</summary>
        public string PathFor(string tierId)
        {
            if (string.IsNullOrEmpty(_folder) || string.IsNullOrEmpty(tierId)) return null;
            try
            {
                return Path.Combine(_folder, FileNameFor(tierId));
            }
            catch
            {
                // Invalid path characters in folder or id - treat as "no art".
                return null;
            }
        }

        public bool HasArtFor(string tierId)
        {
            try
            {
                string path = PathFor(tierId);
                if (path == null) return false;
                return File.Exists(path);
            }
            catch
            {
                // Any unexpected error means "no art" - never a reason to break startup.
                return false;
            }
        }
    }
}
