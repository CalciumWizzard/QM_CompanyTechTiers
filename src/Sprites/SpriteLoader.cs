using System;
using System.IO;
using System.Reflection;
using MGSC;
using UnityEngine;

namespace QM_CompanyTechTiers.Sprites
{
    /// <summary>
    /// Builds a DatadiskDescriptor whose inventory icon comes from a PNG on disk.
    ///
    /// ItemContentDescriptor's three sprite fields are private [SerializeField] with getters only,
    /// so they are assigned by reflection. The project carries BepInEx.AssemblyPublicizer, which
    /// would allow direct assignment at compile time, but that relies on the runtime not enforcing
    /// field access; reflection is guaranteed and this runs at most 24 times at startup.
    /// </summary>
    public static class SpriteLoader
    {
        private static readonly FieldInfo IconField = Field("_icon");
        private static readonly FieldInfo SmallIconField = Field("_smallIcon");
        private static readonly FieldInfo ShadowField = Field("_shadow");

        private static FieldInfo Field(string name)
        {
            return typeof(ItemContentDescriptor).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        }

        public static bool FieldsResolved
        {
            get { return IconField != null && SmallIconField != null && ShadowField != null; }
        }

        public static DatadiskDescriptor TryBuildDescriptor(string pngPath, ItemContentDescriptor parent, Action<string> warn)
        {
            if (parent == null) return null;

            if (!FieldsResolved)
            {
                warn("ItemContentDescriptor sprite fields not found by reflection (_icon/_smallIcon/_shadow). " +
                     "The game's field names have probably changed; custom chip art is disabled.");
                return null;
            }

            Sprite parentSprite = parent.Icon;
            if (parentSprite == null || parentSprite.rect.width < 1f || parentSprite.rect.height < 1f)
            {
                warn("Parent chip has no usable icon; keeping the shared descriptor.");
                return null;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(pngPath);
            }
            catch (Exception ex)
            {
                warn("Could not read '" + pngPath + "': " + ex.Message);
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes))
            {
                warn("'" + pngPath + "' is not a decodable image; keeping the shared descriptor.");
                UnityEngine.Object.Destroy(texture);
                return null;
            }

            // Copied from the parent, not defaulted. This is a pixel-art game: a texture left on
            // Bilinear renders visibly blurry next to every other icon.
            texture.filterMode = parentSprite.texture != null ? parentSprite.texture.filterMode : FilterMode.Point;
            texture.Apply();

            // Sprite.pivot is in pixels; Sprite.Create wants a 0-1 fraction of the rect.
            var pivot = new Vector2(parentSprite.pivot.x / parentSprite.rect.width,
                                    parentSprite.pivot.y / parentSprite.rect.height);

            // pixelsPerUnit copied from the parent: getting it wrong renders a correct image at the
            // wrong size, which is a silent and confusing failure.
            Sprite sprite = Sprite.Create(texture,
                                          new Rect(0f, 0f, texture.width, texture.height),
                                          pivot,
                                          parentSprite.pixelsPerUnit);

            var descriptor = ScriptableObject.CreateInstance<DatadiskDescriptor>();
            IconField.SetValue(descriptor, sprite);
            SmallIconField.SetValue(descriptor, parent.SmallIcon);
            ShadowField.SetValue(descriptor, parent.ShadowOnFloor);
            return descriptor;
        }
    }
}
