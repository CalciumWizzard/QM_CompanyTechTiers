using UnityEngine;

namespace QM_CompanyTechTiers.Sprites
{
    /// <summary>
    /// Reads a Sprite's pixels into PNG bytes.
    ///
    /// Sprite textures normally have isReadable = false, so Texture2D.GetPixels throws on them.
    /// Blitting through a RenderTexture reads the GPU copy instead and works regardless of the
    /// texture's import settings.
    /// </summary>
    public static class SpriteDumper
    {
        public static byte[] ToPng(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return null;

            Texture2D source = sprite.texture;

            // A packed sprite's own rect is in atlas space; textureRect is the sub-rect to read.
            Rect area = sprite.packed ? sprite.textureRect : sprite.rect;
            if (area.width < 1f || area.height < 1f) return null;

            RenderTexture buffer = RenderTexture.GetTemporary(
                source.width, source.height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Texture2D copy = null;

            try
            {
                Graphics.Blit(source, buffer);
                RenderTexture.active = buffer;

                copy = new Texture2D((int)area.width, (int)area.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(area.x, area.y, area.width, area.height), 0, 0);
                copy.Apply();

                return copy.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(buffer);
                if (copy != null) Object.Destroy(copy);
            }
        }
    }
}
