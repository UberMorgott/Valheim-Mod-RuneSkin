using System;

namespace RuneSkin.Theme
{
    // JSON shape of a theme file (read by ThemeJson).
    // Default theme: Theme\default-theme.json (embedded); dev override: BepInEx\config\RuneSkin\theme.json.
    // Contract for the art bundle: docs\theme-contract.md.
    [Serializable]
    public sealed class ThemeFile
    {
        public int schema;
        public string name;
        public string bundle;
        public ColorEntry[] colors;
        public AssetEntry[] sprites;
        public AssetEntry[] fonts;
        public FloatEntry[] floats;
        public RoleRule[] roles;
        public SpriteRoleEntry[] spriteRoles;
        public FamilyEntry[] families;
    }

    [Serializable]
    public sealed class ColorEntry
    {
        public string token;
        public string value;
    }

    [Serializable]
    public sealed class AssetEntry
    {
        public string token;
        public string asset;
        // Sprites only: token used while this asset is not in the bundle (art not delivered yet). The use is listed
        // in diagnostics as a fallback, so it never hides missing art.
        public string fallback;
        // Fonts only: "loaded" = a TMP font asset already loaded by the game or another plugin (e.g. the rune face,
        // AugaSkin's "Norsebold SDF"), looked up by name at first use; empty = our bundle.
        public string source;
    }

    [Serializable]
    public sealed class FloatEntry
    {
        public string token;
        public float value;
    }

    // How one role is drawn. Every field is optional; an empty field leaves that property as vanilla had it.
    [Serializable]
    public sealed class RoleRule
    {
        public string role;
        public string sprite;
        public string color;
        public string imageType;
        public float ppuMultiplier;
        public bool preserveAspect;
        public bool keepColor;
        public string fillCenter; // "true" / "false" / empty = keep
        public string font;
        public string textColor;
        // TMP label inside a Selectable (button plates): > 0 = the label shrinks (auto-size, max = its own size, min =
        // float size.text.fit.min) to fit the button minus this inset per side (ref px; plate caps / knots).
        public float fitInset;
        public string stateNormal;
        public string stateHover;
        public string statePressed;
        public string stateSelected;
        public string stateDisabled;
        // Sprite tokens for Selectable SpriteSwap states (button art per state).
        public string spriteHover;
        public string spritePressed;
        public string spriteSelected;
        public string spriteDisabled;
    }

    // Classifier fallback (d): vanilla sprite name (exact, or prefix with a trailing '*') -> role, optionally only
    // inside one screen family.
    [Serializable]
    public sealed class SpriteRoleEntry
    {
        public string match;
        public string role;
        public string context;
    }

    // Tokens a family needs before RuneSkin may own it (asset preflight).
    [Serializable]
    public sealed class FamilyEntry
    {
        public string family;
        public string[] requires;
    }
}
