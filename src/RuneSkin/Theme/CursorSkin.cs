using UnityEngine;

namespace RuneSkin.Theme
{
    // Mouse cursor (family Cursor, batch P: was an AugaSkin feature): the bundle's readable cursor texture (theme
    // "texture.cursor", art/src/kit/textures.json, 32x32 hardware cursor) with its click point from the theme
    // (size.cursor.hot.x / .y, px from the top-left). Vanilla never sets a cursor, so it is set once and on theme reload.
    public static class CursorSkin
    {
        public const string Token = "texture.cursor";

        public static string Applied { get; private set; } = "none";

        public static void Apply()
        {
            var tex = ThemeTokens.GetTexture(Token);
            if (!tex)
            {
                Applied = "none (no texture.cursor in the bundle)";
                return;
            }
            var hot = new Vector2(ThemeTokens.GetFloat("size.cursor.hot.x", 0f), ThemeTokens.GetFloat("size.cursor.hot.y", 0f));
            Cursor.SetCursor(tex, hot, CursorMode.Auto);
            Applied = $"{tex.name} {tex.width}x{tex.height} {tex.format} hot {hot}";
            Plugin.Log.LogInfo($"[RuneSkin] cursor: {Applied}");
        }
    }
}
