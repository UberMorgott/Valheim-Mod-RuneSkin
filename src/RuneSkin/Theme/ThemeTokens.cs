using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;

namespace RuneSkin.Theme
{
    // A role resolved against loaded assets. Null / unset members mean "leave vanilla's value".
    public sealed class ResolvedRole
    {
        public string Name;
        public string SpriteToken;
        public Sprite Sprite;
        public Color? Color;
        public Image.Type? ImageType;
        public float PpuMultiplier;
        public bool PreserveAspect;
        public bool KeepColor;
        public string FontToken;
        public TMP_FontAsset Font;
        public Color? TextColor;
        public float FitInset;
        public Color? StateNormal, StateHover, StatePressed, StateSelected, StateDisabled;
        public bool? FillCenter;
        public Sprite SpriteHover, SpritePressed, SpriteSelected, SpriteDisabled;

        // An image role that names a sprite is drawn only when the sprite exists (missing art = vanilla look, not a
        // recoloured vanilla sprite).
        public bool ImageUsable => SpriteToken == null || Sprite;
        public bool HasSpriteStates => SpriteHover || SpritePressed || SpriteSelected || SpriteDisabled;
        public bool HasStates => StateNormal.HasValue || StateHover.HasValue || StatePressed.HasValue || StateSelected.HasValue || StateDisabled.HasValue;
    }

    public sealed class SpriteRole
    {
        public string Match;
        public bool Prefix;
        public string Role;
        public string Context;
    }

    // ThemeTokens: the single source of colours, sprites, fonts and sizes. Code holds no colours or pixel values.
    // Load order: embedded default -> TextAsset "runeskin-theme" in the bundle (art-side values) -> dev override file.
    // Each layer is validated as a whole; an invalid layer is reported and the last good theme stays active.
    public static class ThemeTokens
    {
        public const string BundleThemeAsset = "runeskin-theme";
        private const string EmbeddedName = "RuneSkin.default-theme.json";

        private static readonly Dictionary<string, Color> Colors = new Dictionary<string, Color>();
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, TMP_FontAsset> Fonts = new Dictionary<string, TMP_FontAsset>();
        private static readonly Dictionary<string, float> Floats = new Dictionary<string, float>();
        private static readonly Dictionary<string, ResolvedRole> Roles = new Dictionary<string, ResolvedRole>();
        private static readonly List<SpriteRole> SpriteRoleList = new List<SpriteRole>();
        private static readonly Dictionary<string, string[]> Families = new Dictionary<string, string[]>();
        // Sprite tokens drawn with their fallback (art missing): token -> reason. Read by diagnostics.
        public static readonly Dictionary<string, string> FallbackTokens = new Dictionary<string, string>();

        public static bool IsFallback(string token) => token != null && FallbackTokens.ContainsKey(token);

        public static Color GetColor(string token, Color fallback) => token != null && Colors.TryGetValue(token, out var c) ? c : fallback;

        public static Sprite GetSprite(string token) => token != null && Sprites.TryGetValue(token, out var s) && s ? s : null;

        public static TMP_FontAsset GetFont(string token)
        {
            if (token == null) return null;
            if (Fonts.TryGetValue(token, out var f) && f) return f;
            if (!LoadedFonts.TryGetValue(token, out var name)) return null;
            // Not loaded yet (or absent): searched at most every 2 s (FindObjectsOfTypeAll is not free).
            if (Time.realtimeSinceStartup < _nextLoadedLookup) return null;
            _nextLoadedLookup = Time.realtimeSinceStartup + 2f;
            // Shared font another plugin / the game loads (source "loaded"): looked up by name once it exists.
            f = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(a => a && a.name == name);
            if (!f) return null;
            Fonts[token] = f;
            Plugin.Log?.LogInfo($"[RuneSkin] font '{token}' bound to loaded TMP asset '{name}'");
            return f;
        }

        // token -> TMP asset name for fonts with source "loaded" (never ours: no fallback links, no shader swap).
        private static readonly Dictionary<string, string> LoadedFonts = new Dictionary<string, string>();
        private static float _nextLoadedLookup;

        public static bool IsLoadedFont(TMP_FontAsset f) => f && LoadedFonts.ContainsValue(f.name);

        private static AssetBundle _bundle;
        private static readonly Dictionary<string, Sprite> BundleSprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, TMP_FontAsset> BundleFonts = new Dictionary<string, TMP_FontAsset>();
        private static bool _fallbacksLinked;
        private static readonly List<SpriteAtlas> Atlases = new List<SpriteAtlas>();

        // Late binding of our SpriteAtlas: Unity asks for an atlas a sprite references when it was not loaded with it.
        private static void OnAtlasRequested(string tag, Action<SpriteAtlas> done)
        {
            var atlas = Atlases.FirstOrDefault(a => a && (a.tag == tag || a.name == tag));
            if (!atlas && _bundle) atlas = _bundle.LoadAsset<SpriteAtlas>(tag);
            if (atlas) done(atlas);
        }

        public static string ThemeName { get; private set; }
        public static string Source { get; private set; }
        public static int Revision { get; private set; }
        public static bool BundleLoaded => _bundle;
        public static string BundlePath { get; private set; }
        public static IReadOnlyList<SpriteRole> SpriteRoles => SpriteRoleList;

        public static string OverridePath => Path.Combine(BepInEx.Paths.ConfigPath, "RuneSkin", "theme.json");

        // Loads the bundle (once) and the theme layers. Returns false only when not even the embedded default is valid.
        public static bool Load()
        {
            LoadBundle();
            ThemeFile active = null;
            string source = null;

            var embedded = ReadEmbedded();
            if (TryParse(embedded, "embedded default", out var file)) { active = file; source = "embedded"; }

            var bundleText = _bundle ? _bundle.LoadAsset<TextAsset>(BundleThemeAsset) : null;
            if (bundleText && TryParse(bundleText.text, $"bundle {BundleThemeAsset}", out file)) { active = file; source = "bundle"; }

            if (File.Exists(OverridePath))
            {
                string text = null;
                try { text = File.ReadAllText(OverridePath); }
                catch (Exception e) { Diagnostics.SkinDiagnostics.Theme($"override {OverridePath} unreadable: {e.Message}"); }
                if (text != null && TryParse(text, OverridePath, out file)) { active = file; source = OverridePath; }
            }

            if (active == null)
            {
                if (Revision == 0)
                {
                    Plugin.Log.LogError("[RuneSkin] no valid theme (embedded default invalid); RuneSkin stays inactive");
                    return false;
                }
                Plugin.Log.LogWarning("[RuneSkin] theme reload: no valid layer, keeping the last good theme");
                return true;
            }
            Apply(active);
            Source = source;
            Revision++;
            Plugin.Log.LogInfo($"[RuneSkin] theme '{ThemeName}' rev {Revision} from {source}; bundle {(BundleLoaded ? "loaded" : "MISSING")} ({BundlePath})");
            return true;
        }

        public static bool TryGetColor(string token, out Color color) => Colors.TryGetValue(token, out color);
        public static bool TryGetFloat(string token, out float value) => Floats.TryGetValue(token, out value);

        public static bool TryGetSprite(string token, out Sprite sprite)
        {
            sprite = null;
            return Sprites.TryGetValue(token, out sprite) && sprite;
        }

        public static float GetFloat(string token, float fallback) => Floats.TryGetValue(token, out var v) ? v : fallback;

        public static ResolvedRole Role(string name)
        {
            if (name == null || !Roles.TryGetValue(name, out var r)) return null;
            if (!r.Font && r.FontToken != null) r.Font = GetFont(r.FontToken); // loaded fonts bind late
            return r;
        }

        public static bool HasRole(string name) => name != null && Roles.ContainsKey(name);

        // Asset preflight: every token the family requires resolved to a loaded asset (or a colour/float).
        public static bool FamilyReady(string family, List<string> missing)
        {
            if (!Families.TryGetValue(family, out var requires))
            {
                missing?.Add($"family '{family}' not declared in theme");
                return false;
            }
            foreach (var token in requires)
            {
                var asset = (Sprites.TryGetValue(token, out var s) && s) || (Fonts.TryGetValue(token, out var f) && f);
                var ok = asset || Colors.ContainsKey(token) || Floats.ContainsKey(token);
                if (!ok) missing?.Add(token);
            }
            return missing == null || missing.Count == 0;
        }

        // Our own font assets get the game's fonts as tail fallbacks (glyphs our fonts lack: CJK, symbols). Built once,
        // in a fixed order, only on our assets (never on shared game assets).
        public static void EnsureFontFallbacks()
        {
            if (_fallbacksLinked || Fonts.Count == 0) return;
            var game = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            var tail = new[] { "Valheim-AveriaSansLibre", "Valheim-AveriaSerifLibre", "Valheim-Norsebold" }
                .Select(n => game.FirstOrDefault(f => f && f.name == n)).Where(f => f).ToList();
            if (tail.Count == 0) return; // game fonts not loaded yet; retried on the next restyle
            _fallbacksLinked = true;
            // Our TMP materials use the game's own SDF shader: a shader compiled into the bundle may not match the
            // game's graphics setup (art pipeline note), the game's instance always does.
            var gameShader = tail[0].material ? tail[0].material.shader : null;
            foreach (var font in Fonts.Values.Where(f => f && !LoadedFonts.ContainsValue(f.name)).Distinct())
            {
                if (gameShader && font.material && font.material.shader != gameShader)
                    font.material.shader = gameShader;
                if (font.fallbackFontAssetTable == null) font.fallbackFontAssetTable = new List<TMP_FontAsset>();
                foreach (var t in tail)
                    if (t != font && !font.fallbackFontAssetTable.Contains(t))
                        font.fallbackFontAssetTable.Add(t);
            }
        }

        private static void LoadBundle()
        {
            if (_bundle || BundlePath != null) return;
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            BundlePath = Path.Combine(dir, "runeskin");
            if (!File.Exists(BundlePath))
            {
                Diagnostics.SkinDiagnostics.Theme($"asset bundle not found: {BundlePath} (all sprite/font tokens degrade to vanilla)");
                return;
            }
            try
            {
                _bundle = AssetBundle.LoadFromFile(BundlePath);
            }
            catch (Exception e)
            {
                Diagnostics.SkinDiagnostics.Theme($"asset bundle failed to load: {e.Message}");
            }
            if (!_bundle)
            {
                Diagnostics.SkinDiagnostics.Theme($"asset bundle unreadable (Unity version?): {BundlePath}");
                return;
            }
            SpriteAtlasManager.atlasRequested += OnAtlasRequested; // before any sprite load can ask for it
            // Sprites may be standalone assets, texture sub-assets or packed in a SpriteAtlas: index all by name.
            foreach (var s in _bundle.LoadAllAssets<Sprite>())
                if (s && !BundleSprites.ContainsKey(s.name)) BundleSprites.Add(s.name, s);
            foreach (var atlas in _bundle.LoadAllAssets<SpriteAtlas>())
            {
                Atlases.Add(atlas);
                var packed = new Sprite[atlas.spriteCount];
                atlas.GetSprites(packed);
                foreach (var s in packed)
                {
                    if (!s) continue;
                    var name = s.name.EndsWith("(Clone)", StringComparison.Ordinal) ? s.name.Substring(0, s.name.Length - 7) : s.name;
                    if (!BundleSprites.ContainsKey(name)) BundleSprites.Add(name, s);
                }
            }

            foreach (var f in _bundle.LoadAllAssets<TMP_FontAsset>())
                if (f && !BundleFonts.ContainsKey(f.name)) BundleFonts.Add(f.name, f);
            Plugin.Log.LogInfo($"[RuneSkin] bundle: {BundleSprites.Count} sprites, {BundleFonts.Count} TMP fonts");
        }

        private static string ReadEmbedded()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedName))
            {
                if (stream == null) return null;
                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
        }

        private static bool TryParse(string json, string origin, out ThemeFile file)
        {
            file = null;
            if (string.IsNullOrEmpty(json))
            {
                Diagnostics.SkinDiagnostics.Theme($"{origin}: empty");
                return false;
            }
            try
            {
                file = ThemeJson.Parse(json);
            }
            catch (Exception e)
            {
                Diagnostics.SkinDiagnostics.Theme($"{origin}: JSON error {e.Message}");
                return false;
            }
            var errors = ThemeSchema.Validate(file);
            foreach (var e in errors)
                Diagnostics.SkinDiagnostics.Theme($"{origin}: {e}");
            if (errors.Count == 0) return true;
            file = null;
            return false;
        }

        private static void Apply(ThemeFile f)
        {
            ThemeName = f.name;
            Colors.Clear(); Sprites.Clear(); Fonts.Clear(); Floats.Clear(); Roles.Clear(); SpriteRoleList.Clear(); Families.Clear();
            foreach (var c in f.colors ?? new ColorEntry[0])
                if (HexColor.TryParse(c.value, out var color)) Colors[c.token] = color;
            FallbackTokens.Clear();
            foreach (var s in f.sprites ?? new AssetEntry[0])
            {
                BundleSprites.TryGetValue(s.asset, out var sprite);
                if (!sprite && BundleLoaded && string.IsNullOrEmpty(s.fallback))
                    Diagnostics.SkinDiagnostics.Theme($"sprite '{s.token}': asset '{s.asset}' not in bundle");
                Sprites[s.token] = sprite;
            }
            // Fallbacks resolve after every asset (one level: a fallback token's own asset, never a chain).
            foreach (var s in f.sprites ?? new AssetEntry[0])
            {
                if (Sprites[s.token] || string.IsNullOrEmpty(s.fallback)) continue;
                if (Sprites.TryGetValue(s.fallback, out var fb) && fb)
                {
                    Sprites[s.token] = fb;
                    FallbackTokens[s.token] = $"asset '{s.asset}' not in bundle, using '{s.fallback}'";
                }
                else Diagnostics.SkinDiagnostics.Theme($"sprite '{s.token}': asset '{s.asset}' and fallback '{s.fallback}' missing");
            }
            foreach (var p in FallbackTokens)
                Diagnostics.SkinDiagnostics.Theme($"sprite '{p.Key}': {p.Value}");
            LoadedFonts.Clear();
            foreach (var s in f.fonts ?? new AssetEntry[0])
            {
                if (s.source == "loaded")
                {
                    LoadedFonts[s.token] = s.asset;
                    Fonts[s.token] = null;
                    continue;
                }
                BundleFonts.TryGetValue(s.asset, out var font);
                if (!font && BundleLoaded) Diagnostics.SkinDiagnostics.Theme($"font '{s.token}': TMP asset '{s.asset}' not in bundle");
                Fonts[s.token] = font;
            }
            _fallbacksLinked = false;
            foreach (var v in f.floats ?? new FloatEntry[0])
                Floats[v.token] = v.value;
            Sprite S(string t) => !string.IsNullOrEmpty(t) && Sprites.TryGetValue(t, out var s) ? s : null;
            Color? C(string t) => !string.IsNullOrEmpty(t) && Colors.TryGetValue(t, out var c) ? c : (Color?)null;
            foreach (var r in f.roles ?? new RoleRule[0])
            {
                Roles[r.role] = new ResolvedRole
                {
                    Name = r.role,
                    SpriteToken = string.IsNullOrEmpty(r.sprite) ? null : r.sprite,
                    Sprite = string.IsNullOrEmpty(r.sprite) ? null : Sprites[r.sprite],
                    Color = r.keepColor ? null : C(r.color),
                    ImageType = string.IsNullOrEmpty(r.imageType) ? (Image.Type?)null : (Image.Type)Enum.Parse(typeof(Image.Type), r.imageType),
                    PpuMultiplier = r.ppuMultiplier,
                    PreserveAspect = r.preserveAspect,
                    KeepColor = r.keepColor,
                    FontToken = string.IsNullOrEmpty(r.font) ? null : r.font,
                    Font = string.IsNullOrEmpty(r.font) ? null : Fonts[r.font],
                    TextColor = C(r.textColor),
                    FitInset = r.fitInset,
                    StateNormal = C(r.stateNormal),
                    StateHover = C(r.stateHover),
                    StatePressed = C(r.statePressed),
                    StateSelected = C(r.stateSelected),
                    StateDisabled = C(r.stateDisabled),
                    FillCenter = string.IsNullOrEmpty(r.fillCenter) ? (bool?)null : r.fillCenter == "true",
                    SpriteHover = S(r.spriteHover),
                    SpritePressed = S(r.spritePressed),
                    SpriteSelected = S(r.spriteSelected),
                    SpriteDisabled = S(r.spriteDisabled),
                };
            }
            foreach (var s in f.spriteRoles ?? new SpriteRoleEntry[0])
            {
                var prefix = s.match.EndsWith("*", StringComparison.Ordinal);
                SpriteRoleList.Add(new SpriteRole
                {
                    Match = prefix ? s.match.Substring(0, s.match.Length - 1) : s.match,
                    Prefix = prefix,
                    Role = s.role,
                    Context = string.IsNullOrEmpty(s.context) ? null : s.context
                });
            }
            foreach (var fam in f.families ?? new FamilyEntry[0])
                Families[fam.family] = fam.requires ?? new string[0];
        }
    }
}
