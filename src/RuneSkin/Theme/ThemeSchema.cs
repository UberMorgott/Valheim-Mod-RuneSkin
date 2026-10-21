using System;
using System.Collections.Generic;
using UnityEngine.UI;

namespace RuneSkin.Theme
{
    // Theme JSON schema check (managed only, unit-tested). Any error rejects the whole file.
    public static class ThemeSchema
    {
        public static List<string> Validate(ThemeFile f)
        {
            var errors = new List<string>();
            if (f == null) { errors.Add("not a theme object"); return errors; }
            if (f.schema != 1) errors.Add($"schema {f.schema} unsupported (expected 1)");
            if (string.IsNullOrEmpty(f.name)) errors.Add("name missing");
            var tokens = new HashSet<string>();
            void Token(string t, string kind)
            {
                if (string.IsNullOrEmpty(t)) errors.Add($"{kind}: empty token");
                else if (!tokens.Add(t)) errors.Add($"{kind}: duplicate token '{t}'");
            }
            var colorTokens = new HashSet<string>();
            foreach (var c in f.colors ?? new ColorEntry[0])
            {
                Token(c.token, "color");
                if (!HexColor.TryParse(c.value, out _)) errors.Add($"color '{c.token}': bad value '{c.value}'");
                else colorTokens.Add(c.token);
            }
            var spriteTokens = new HashSet<string>();
            foreach (var s in f.sprites ?? new AssetEntry[0])
            {
                Token(s.token, "sprite");
                if (string.IsNullOrEmpty(s.asset)) errors.Add($"sprite '{s.token}': asset missing");
                spriteTokens.Add(s.token);
            }
            foreach (var s in f.sprites ?? new AssetEntry[0])
            {
                if (string.IsNullOrEmpty(s.fallback)) continue;
                if (!spriteTokens.Contains(s.fallback)) errors.Add($"sprite '{s.token}': fallback token '{s.fallback}' undefined");
                else if (s.fallback == s.token) errors.Add($"sprite '{s.token}': falls back to itself");
            }
            var fontTokens = new HashSet<string>();
            foreach (var s in f.fonts ?? new AssetEntry[0])
            {
                Token(s.token, "font");
                if (string.IsNullOrEmpty(s.asset)) errors.Add($"font '{s.token}': asset missing");
                fontTokens.Add(s.token);
            }
            foreach (var v in f.floats ?? new FloatEntry[0])
            {
                Token(v.token, "float");
                if (float.IsNaN(v.value) || float.IsInfinity(v.value)) errors.Add($"float '{v.token}': not finite");
            }
            var roles = new HashSet<string>();
            foreach (var r in f.roles ?? new RoleRule[0])
            {
                if (string.IsNullOrEmpty(r.role)) { errors.Add("role: empty name"); continue; }
                if (!roles.Add(r.role)) errors.Add($"role '{r.role}': duplicate");
                void Ref(string t, HashSet<string> set, string what)
                {
                    if (!string.IsNullOrEmpty(t) && !set.Contains(t)) errors.Add($"role '{r.role}': {what} token '{t}' undefined");
                }
                Ref(r.sprite, spriteTokens, "sprite");
                Ref(r.color, colorTokens, "color");
                Ref(r.font, fontTokens, "font");
                Ref(r.textColor, colorTokens, "textColor");
                Ref(r.stateNormal, colorTokens, "stateNormal");
                Ref(r.stateHover, colorTokens, "stateHover");
                Ref(r.statePressed, colorTokens, "statePressed");
                Ref(r.stateSelected, colorTokens, "stateSelected");
                Ref(r.stateDisabled, colorTokens, "stateDisabled");
                Ref(r.spriteHover, spriteTokens, "spriteHover");
                Ref(r.spritePressed, spriteTokens, "spritePressed");
                Ref(r.spriteSelected, spriteTokens, "spriteSelected");
                Ref(r.spriteDisabled, spriteTokens, "spriteDisabled");
                if (!string.IsNullOrEmpty(r.fillCenter) && r.fillCenter != "true" && r.fillCenter != "false")
                    errors.Add($"role '{r.role}': fillCenter '{r.fillCenter}' must be true or false");
                if (!string.IsNullOrEmpty(r.imageType) && !Enum.TryParse<Image.Type>(r.imageType, out _))
                    errors.Add($"role '{r.role}': imageType '{r.imageType}' invalid");
                if (r.ppuMultiplier < 0f) errors.Add($"role '{r.role}': ppuMultiplier < 0");
            }
            foreach (var required in new[] { "content", "skip" })
                if (!roles.Contains(required)) errors.Add($"role '{required}' missing (classifier needs it)");
            foreach (var s in f.spriteRoles ?? new SpriteRoleEntry[0])
            {
                if (string.IsNullOrEmpty(s.match)) errors.Add("spriteRoles: empty match");
                if (!roles.Contains(s.role ?? "")) errors.Add($"spriteRoles '{s.match}': role '{s.role}' undefined");
            }
            foreach (var fam in f.families ?? new FamilyEntry[0])
            {
                if (string.IsNullOrEmpty(fam.family)) errors.Add("families: empty name");
                foreach (var t in fam.requires ?? new string[0])
                    if (!tokens.Contains(t)) errors.Add($"family '{fam.family}': token '{t}' undefined");
            }
            return errors;
        }
    }
}
