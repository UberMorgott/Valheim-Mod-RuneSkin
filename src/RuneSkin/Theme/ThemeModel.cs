using System;

namespace RuneSkin.Theme
{
    // JSON shape of a theme file (UnityEngine.JsonUtility: public fields, arrays of [Serializable] classes).
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
        public string font;
        public string textColor;
        public string stateNormal;
        public string stateHover;
        public string statePressed;
        public string stateSelected;
        public string stateDisabled;
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
