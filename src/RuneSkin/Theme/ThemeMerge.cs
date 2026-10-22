using System;
using System.Collections.Generic;
using System.Linq;

namespace RuneSkin.Theme
{
    // Layered theme (perf audit B4): a layer (bundle theme, dev override BepInEx\config\RuneSkin\theme.json) lists only
    // what it changes. Entries replace the base entry with the same key, new keys are added: colors / sprites / fonts /
    // textures / floats by token, roles by role, families by family, spriteRoles by (match, context). spriteRoles are
    // first-match: a new overlay rule goes in front of the base rules (it is the more specific one), a replaced rule
    // keeps its place. Scalars (schema, name, bundle) come from the layer when it sets them. The merged file is
    // validated as a whole (ThemeSchema); an invalid result leaves the previous layers active. Managed only: unit-tested.
    public static class ThemeMerge
    {
        public static ThemeFile Merge(ThemeFile baseFile, ThemeFile layer)
        {
            if (baseFile == null) return layer;
            if (layer == null) return baseFile;
            return new ThemeFile
            {
                schema = layer.schema != 0 ? layer.schema : baseFile.schema,
                name = !string.IsNullOrEmpty(layer.name) ? layer.name : baseFile.name,
                bundle = !string.IsNullOrEmpty(layer.bundle) ? layer.bundle : baseFile.bundle,
                colors = ByKey(baseFile.colors, layer.colors, c => c.token),
                sprites = ByKey(baseFile.sprites, layer.sprites, s => s.token),
                fonts = ByKey(baseFile.fonts, layer.fonts, s => s.token),
                textures = ByKey(baseFile.textures, layer.textures, s => s.token),
                floats = ByKey(baseFile.floats, layer.floats, v => v.token),
                roles = ByKey(baseFile.roles, layer.roles, r => r.role),
                families = ByKey(baseFile.families, layer.families, f => f.family),
                spriteRoles = ByKey(baseFile.spriteRoles, layer.spriteRoles, s => (s.match ?? "") + "\u0001" + (s.context ?? ""), newFirst: true),
            };
        }

        // Base order kept, same-key entries replaced in place, new ones appended (or put in front).
        private static T[] ByKey<T>(T[] baseItems, T[] layerItems, Func<T, string> key, bool newFirst = false)
        {
            if (layerItems == null || layerItems.Length == 0) return baseItems;
            if (baseItems == null || baseItems.Length == 0) return layerItems;
            var replace = new Dictionary<string, T>();
            var added = new List<T>();
            foreach (var item in layerItems)
            {
                if (item == null) continue;
                replace[key(item) ?? ""] = item; // last one in the layer wins
            }
            var baseKeys = new HashSet<string>(baseItems.Where(i => i != null).Select(i => key(i) ?? ""));
            foreach (var item in layerItems)
                if (item != null && !baseKeys.Contains(key(item) ?? "") && ReferenceEquals(replace[key(item) ?? ""], item)) added.Add(item);
            var kept = baseItems.Select(i => i != null && replace.TryGetValue(key(i) ?? "", out var r) ? r : i);
            return (newFirst ? added.Concat(kept) : kept.Concat(added)).ToArray();
        }
    }
}
