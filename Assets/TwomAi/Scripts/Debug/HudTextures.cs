using UnityEngine;

namespace TwomAi
{
    // Procedural placeholder textures for the IMGUI HUD (no art assets required).
    public static class HudTextures
    {
        private static Texture2D _disc;
        private static Texture2D _ring;

        public static Texture2D White => Texture2D.whiteTexture;
        public static Texture2D Disc => _disc != null ? _disc : (_disc = Build(0f));
        public static Texture2D Ring => _ring != null ? _ring : (_ring = Build(0.82f));

        // innerRatio 0 = filled disc; otherwise ring from innerRatio..1 of the radius.
        private static Texture2D Build(float innerRatio)
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            float center = (size - 1) * 0.5f;
            float radius = size * 0.5f - 1f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center)) / radius;
                    float outer = Mathf.Clamp01((1f - d) * radius);
                    float inner = innerRatio > 0f ? Mathf.Clamp01((d - innerRatio) * radius) : 1f;
                    byte alpha = (byte)(255f * Mathf.Min(outer, inner));
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
