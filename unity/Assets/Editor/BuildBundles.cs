// RuneSkin asset bundle build (Unity 6000.0.75f1, com.unity.ugui 2.0.0 = the game's TMP).
// Unity.exe -batchmode -quit -nographics -projectPath <repo>/unity -executeMethod BuildBundles.Build -logFile -
// Steps: TMP shaders (from the editor's ugui package) -> sync art/sprites + art/fonts -> sprite import (9-slice
// borders, pivot, PPU from art/sprites/sprites.json) -> SpriteAtlas -> static TMP SDF font assets (bake report in
// art/fonts/bake-report.txt) -> legacy dynamic Fonts + readable textures (art/textures) -> AssetBundle "runeskin" (StandaloneWindows64, LZ4) -> unity/Build/runeskin
// -> contents listing loaded back from the built file (unity/Build/runeskin.contents.txt).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.U2D;

public static class BuildBundles
{
    const string BundleName = "runeskin";
    const string Root = "Assets/RuneSkin";
    const string SpriteDir = Root + "/Sprites";
    const string FontDir = Root + "/Fonts";
    const string AtlasPath = Root + "/RuneSkin.spriteatlas";
    const string OutDir = "Build";

    [Serializable] public class Border { public int left, bottom, right, top; }
    [Serializable] public class SpriteDef { public string name, file, imageType, token; public int[] size; public Border border; public float[] pivot; }
    [Serializable] public class Manifest { public int version, authoredScale, pixelsPerUnit; public SpriteDef[] sprites; }

    // display + body; sampling size/padding per role, atlas grows until every charset glyph fits
    static readonly (string name, string file, int pointSize, int padding)[] Fonts =
    {
        ("RuneSkin-Display Cormorant-Bold SDF", "Cormorant-Bold.otf", 72, 8),
        ("RuneSkin-Body PTSans-Regular SDF", "PT_Sans-Web-Regular.ttf", 60, 7),
    };

    // The same source files as legacy dynamic UnityEngine.Font assets (asset name = file name without extension): uGUI
    // Text and IMGUI draw with a Font, never a TMP asset, and the baked TMP faces drop their source font. Dynamic =
    // glyphs rasterised from the embedded font data at runtime (Cyrillic included), no OS font lookup.
    static readonly string[] LegacyFonts = { "PT_Sans-Web-Regular.ttf", "Cormorant-Bold.otf" };

    const string TextureDir = Root + "/Textures";

    [Serializable] public class TexDef { public string name, file, usage; public int[] size; public float[] hotspot; }
    [Serializable] public class TexManifest { public TexDef[] textures; }

    static string Art => Path.GetFullPath(Path.Combine(Application.dataPath, "../../art"));

    public static void Build()
    {
        try
        {
            EnsureTmpResources();
            var man = JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(Art, "sprites", "sprites.json")));
            var sprites = ImportSprites(man);
            BuildAtlas(sprites);
            var fonts = BakeFonts();
            fonts.AddRange(ImportLegacyFonts());
            fonts.AddRange(ImportTextures());
            foreach (var p in sprites.Select(AssetDatabase.GetAssetPath).Distinct().Append(AtlasPath).Concat(fonts))
                AssetImporter.GetAtPath(p).SetAssetBundleNameAndVariant(BundleName, "");
            AssetDatabase.SaveAssets();
            AssetDatabase.RemoveUnusedAssetBundleNames();
            Directory.CreateDirectory(OutDir);
            var res = BuildPipeline.BuildAssetBundles(OutDir, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
                BuildTarget.StandaloneWindows64);
            var file = Path.Combine(OutDir, BundleName);
            if (res == null || !File.Exists(file))
                throw new Exception("BuildAssetBundles failed");
            Debug.Log($"RuneSkin: bundle built {Path.GetFullPath(file)} ({new FileInfo(file).Length} bytes)");
            List(man);
        }
        catch (Exception e)
        {
            Debug.LogError("RuneSkin: build FAILED: " + e);
            EditorApplication.Exit(1);
        }
    }

    // TMP shaders + TMP Settings come from the editor's own ugui package (not committed, re-imported on demand).
    static void EnsureTmpResources()
    {
        if (Shader.Find("TextMeshPro/Distance Field") != null && Resources.Load<TMP_Settings>("TMP Settings") != null)
            return;
        var pkg = Path.Combine(EditorApplication.applicationContentsPath,
            "Resources/PackageManager/BuiltInPackages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage");
        // AssetDatabase.ImportPackage is deferred to a later editor tick in batchmode -> unpack the .unitypackage
        // (gzip tar of <guid>/{pathname,asset,asset.meta}) ourselves and import synchronously.
        var entries = new Dictionary<string, Dictionary<string, byte[]>>();
        using (var gz = new System.IO.Compression.GZipStream(File.OpenRead(pkg), System.IO.Compression.CompressionMode.Decompress))
        {
            var hdr = new byte[512];
            string longName = null;
            while (ReadFull(gz, hdr) && hdr[0] != 0)
            {
                var name = longName ?? Encoding.ASCII.GetString(hdr, 0, 100).TrimEnd('\0');
                longName = null;
                var size = Convert.ToInt64(Encoding.ASCII.GetString(hdr, 124, 12).Trim('\0', ' '), 8);
                var data = new byte[size];
                ReadFull(gz, data);
                var skip = new byte[(512 - size % 512) % 512];
                ReadFull(gz, skip);
                if (hdr[156] == (byte)'L') { longName = Encoding.ASCII.GetString(data).TrimEnd('\0'); continue; }
                if (hdr[156] != (byte)'0' && hdr[156] != 0) continue;
                var parts = name.TrimStart('.', '/').Split('/');
                if (parts.Length != 2) continue;
                if (!entries.TryGetValue(parts[0], out var e)) entries[parts[0]] = e = new Dictionary<string, byte[]>();
                e[parts[1]] = data;
            }
        }
        var project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        foreach (var e in entries.Values.Where(e => e.ContainsKey("pathname")))
        {
            var rel = Encoding.UTF8.GetString(e["pathname"]).Split('\n')[0].Trim();
            var dst = Path.Combine(project, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            if (e.TryGetValue("asset", out var asset)) File.WriteAllBytes(dst, asset);
            else Directory.CreateDirectory(dst);
            if (e.TryGetValue("asset.meta", out var meta)) File.WriteAllBytes(dst + ".meta", meta);
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        if (Shader.Find("TextMeshPro/Distance Field") == null)
            throw new Exception("TMP Essential Resources import failed: " + pkg);
        Debug.Log($"RuneSkin: TMP Essential Resources unpacked ({entries.Count} entries)");
    }

    static bool ReadFull(Stream s, byte[] buf)
    {
        int off = 0;
        while (off < buf.Length)
        {
            int n = s.Read(buf, off, buf.Length - off);
            if (n <= 0) return false;
            off += n;
        }
        return true;
    }

    static void Sync(string srcDir, IEnumerable<string> files, string dstDir)
    {
        var full = Path.GetFullPath(dstDir);
        Directory.CreateDirectory(full);
        foreach (var f in files)
        {
            var src = Path.Combine(srcDir, f);
            var dst = Path.Combine(full, f);
            if (!File.Exists(dst) || !File.ReadAllBytes(dst).SequenceEqual(File.ReadAllBytes(src)))
                File.Copy(src, dst, true);
        }
        foreach (var stale in Directory.GetFiles(full).Where(p => !p.EndsWith(".meta") && !files.Contains(Path.GetFileName(p))))
        {
            File.Delete(stale);
            File.Delete(stale + ".meta");
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    }

    static List<Sprite> ImportSprites(Manifest man)
    {
        Sync(Path.Combine(Art, "sprites"), man.sprites.Select(s => s.file).ToList(), SpriteDir);
        var list = new List<Sprite>();
        foreach (var s in man.sprites)
        {
            var path = $"{SpriteDir}/{s.file}";
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            var st = new TextureImporterSettings();
            ti.ReadTextureSettings(st);
            st.spriteMeshType = SpriteMeshType.FullRect; // 9-slice/tiling needs the full rect
            st.spriteAlignment = (int)SpriteAlignment.Custom;
            st.spritePivot = new Vector2(s.pivot[0], s.pivot[1]);
            st.spriteBorder = new Vector4(s.border.left, s.border.bottom, s.border.right, s.border.top);
            st.spritePixelsPerUnit = man.pixelsPerUnit;
            st.spriteGenerateFallbackPhysicsShape = false;
            ti.SetTextureSettings(st);
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.sRGBTexture = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.isReadable = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed; // the atlas decides the shipped format
            ti.SaveAndReimport();
            var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path) ?? throw new Exception("no sprite at " + path);
            if (sp.name != s.name)
                throw new Exception($"sprite name {sp.name} != {s.name}");
            list.Add(sp);
        }
        return list;
    }

    static void BuildAtlas(List<Sprite> sprites)
    {
        EditorSettings.spritePackerMode = SpritePackerMode.AlwaysOnAtlas;
        var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);
        if (atlas == null)
        {
            atlas = new SpriteAtlas();
            AssetDatabase.CreateAsset(atlas, AtlasPath);
        }
        atlas.Remove(atlas.GetPackables());
        atlas.SetPackingSettings(new SpriteAtlasPackingSettings
        {
            blockOffset = 1, padding = 4, enableRotation = false, enableTightPacking = false, enableAlphaDilation = true,
        });
        atlas.SetTextureSettings(new SpriteAtlasTextureSettings
        {
            readable = false, generateMipMaps = false, sRGB = true, filterMode = FilterMode.Bilinear, anisoLevel = 0,
        });
        var ps = atlas.GetPlatformSettings("Standalone");
        ps.overridden = true;
        ps.maxTextureSize = 2048;
        ps.format = TextureImporterFormat.BC7;
        ps.textureCompression = TextureImporterCompression.CompressedHQ;
        atlas.SetPlatformSettings(ps);
        atlas.SetIncludeInBuild(true);
        atlas.Add(sprites.Cast<UnityEngine.Object>().ToArray());
        EditorUtility.SetDirty(atlas);
        AssetDatabase.SaveAssets();
        SpriteAtlasUtility.PackAtlases(new[] { atlas }, BuildTarget.StandaloneWindows64);
        Debug.Log($"RuneSkin: atlas {AtlasPath} packed with {atlas.spriteCount} sprites");
    }

    static List<string> BakeFonts()
    {
        var charset = File.ReadAllText(Path.Combine(Art, "fonts", "charset-ru-en.txt"), Encoding.UTF8).Replace("\n", "");
        Sync(Path.Combine(Art, "fonts"), Fonts.Select(f => f.file).ToList(), FontDir + "/Source");
        var report = new StringBuilder($"RuneSkin TMP bake report ({DateTime.Now:yyyy-MM-dd HH:mm}), Unity {Application.unityVersion}, " +
            $"charset art/fonts/charset-ru-en.txt = {charset.Length} chars (RU+EN)\n");
        var paths = new List<string>();
        foreach (var (name, file, pt, pad) in Fonts)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>($"{FontDir}/Source/{file}") ?? throw new Exception("no font " + file);
            TMP_FontAsset fa = null;
            string missing = null;
            int w = 0, h = 0;
            foreach (var (aw, ah) in new[] { (1024, 1024), (2048, 1024), (2048, 2048), (4096, 2048), (4096, 4096) })
            {
                if (fa != null)
                    UnityEngine.Object.DestroyImmediate(fa);
                fa = TMP_FontAsset.CreateFontAsset(font, pt, pad, GlyphRenderMode.SDFAA, aw, ah, AtlasPopulationMode.Dynamic, false);
                fa.TryAddCharacters(charset, out missing, true);
                (w, h) = (aw, ah);
                if (string.IsNullOrEmpty(missing))
                    break;
            }
            if (!string.IsNullOrEmpty(missing))
                throw new Exception($"{name}: missing {missing.Length} chars: " + string.Join(" ", missing.Select(c => $"U+{(int)c:X4}")));
            var path = $"{FontDir}/{name}.asset";
            AssetDatabase.DeleteAsset(path);
            fa.name = name;
            fa.atlasPopulationMode = AtlasPopulationMode.Static; // editor setter also drops the source font reference
            fa.material.shader = Shader.Find("TextMeshPro/Distance Field");
            fa.material.name = name + " Material";
            var tex = fa.atlasTexture;
            tex.name = name + " Atlas";
            AssetDatabase.CreateAsset(fa, path);
            AssetDatabase.AddObjectToAsset(tex, fa);
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            var so = new SerializedObject(tex); // static atlas: GPU only, no CPU copy in the game
            so.FindProperty("m_IsReadable").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            int cyr = fa.characterTable.Count(c => c.unicode >= 0x400 && c.unicode <= 0x4FF);
            var line = $"{name}: {file} v{FontVersion(font)} {pt}pt pad {pad} SDFAA atlas {w}x{h} Alpha8 static; " +
                $"chars {fa.characterTable.Count} (Cyrillic {cyr}), glyphs {fa.glyphTable.Count}, missing {missing?.Length ?? 0}";
            Line(report, line);
            Debug.Log("RuneSkin: " + line);
            paths.Add(path);
        }
        File.WriteAllText(Path.Combine(Art, "fonts", "bake-report.txt"), report.ToString());
        return paths;
    }

    // Source fonts (synced by BakeFonts) re-imported as dynamic legacy fonts with their data embedded.
    static List<string> ImportLegacyFonts()
    {
        var paths = new List<string>();
        foreach (var file in LegacyFonts)
        {
            var path = $"{FontDir}/Source/{file}";
            var ti = AssetImporter.GetAtPath(path) as TrueTypeFontImporter ?? throw new Exception("no font importer at " + path);
            ti.fontTextureCase = FontTextureCase.Dynamic;
            ti.includeFontData = true;
            ti.fontRenderingMode = FontRenderingMode.Smooth;
            ti.fontReferences = new Font[0];
            ti.fontNames = new string[0]; // no OS font fallback: the embedded data or nothing
            ti.SaveAndReimport();
            var font = AssetDatabase.LoadAssetAtPath<Font>(path) ?? throw new Exception("no legacy font " + path);
            Debug.Log($"RuneSkin: legacy font {font.name} ({file}) dynamic {font.dynamic}");
            paths.Add(path);
        }
        return paths;
    }

    // art/textures (Go build output): readable, uncompressed, unmipped RGBA textures (cursor).
    static List<string> ImportTextures()
    {
        var manPath = Path.Combine(Art, "textures", "textures.json");
        if (!File.Exists(manPath)) return new List<string>();
        var man = JsonUtility.FromJson<TexManifest>(File.ReadAllText(manPath));
        Sync(Path.Combine(Art, "textures"), man.textures.Select(t => t.file).ToList(), TextureDir);
        var paths = new List<string>();
        foreach (var t in man.textures)
        {
            var path = $"{TextureDir}/{t.file}";
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = t.usage == "cursor" ? TextureImporterType.Cursor : TextureImporterType.Default;
            ti.isReadable = true;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.sRGBTexture = true;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            var ps = ti.GetPlatformTextureSettings("Standalone");
            ps.overridden = true;
            ps.format = TextureImporterFormat.RGBA32;
            ti.SetPlatformTextureSettings(ps);
            ti.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path) ?? throw new Exception("no texture at " + path);
            if (tex.name != t.name) throw new Exception($"texture name {tex.name} != {t.name}");
            paths.Add(path);
        }
        return paths;
    }

    static void Line(StringBuilder b, string s) => b.Append(s).Append('\n');

    static string FontVersion(Font font)
    {
        var p = Path.GetFullPath(AssetDatabase.GetAssetPath(font));
        if (FontEngine.LoadFontFace(p) != FontEngineError.Success)
            return "?";
        return $"{FontEngine.GetFaceInfo().familyName} {FontEngine.GetFaceInfo().styleName}";
    }

    // Loads the built file back and lists what the game will get; fails if a sprites.json entry or a font is absent.
    public static void List() => List(JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(Art, "sprites", "sprites.json"))));

    static void List(Manifest man)
    {
        AssetBundle.UnloadAllAssetBundles(true);
        var file = Path.Combine(OutDir, BundleName);
        var ab = AssetBundle.LoadFromFile(file) ?? throw new Exception("cannot load " + file);
        try
        {
            var sb = new StringBuilder($"{Path.GetFullPath(file)} {new FileInfo(file).Length} bytes, Unity {Application.unityVersion}, StandaloneWindows64\n");
            var all = ab.LoadAllAssets();
            var sprites = all.OfType<Sprite>().GroupBy(s => s.name).Select(g => g.First()).OrderBy(s => s.name).ToList();
            foreach (var s in sprites)
                Line(sb, $"Sprite {s.name}: rect {s.rect.width}x{s.rect.height} border(L,B,R,T)={s.border} ppu {s.pixelsPerUnit} pivot {s.pivot} packed {s.packed} tex {s.texture?.name}");
            foreach (var a in all.OfType<SpriteAtlas>())
                Line(sb, $"SpriteAtlas {a.name}: {a.spriteCount} sprites");
            foreach (var t in all.OfType<Texture2D>())
                Line(sb, $"Texture2D {t.name}: {t.width}x{t.height} {t.format} readable {t.isReadable}");
            var charset = File.ReadAllText(Path.Combine(Art, "fonts", "charset-ru-en.txt"), Encoding.UTF8).Replace("\n", "");
            var fonts = all.OfType<TMP_FontAsset>().ToList();
            foreach (var f in fonts)
            {
                f.HasCharacters(charset, out List<char> miss);
                Line(sb, $"TMP_FontAsset {f.name}: {f.atlasPopulationMode}, chars {f.characterTable.Count}, atlas {f.atlasWidth}x{f.atlasHeight}, " +
                    $"shader {f.material?.shader?.name}, RU+EN missing {miss?.Count ?? -1}");
            }
            var legacy = all.OfType<Font>().ToList();
            var legacyBad = new List<string>();
            foreach (var want in LegacyFonts.Select(Path.GetFileNameWithoutExtension))
            {
                var f = legacy.FirstOrDefault(x => x.name == want);
                var miss = f ? charset.Count(c => !char.IsWhiteSpace(c) && !f.HasCharacter(c)) : -1;
                Line(sb, $"Font {want}: {(f ? $"dynamic {f.dynamic}, RU+EN missing {miss}" : "ABSENT")}");
                if (!f || !f.dynamic || miss != 0) legacyBad.Add(want);
            }
            var texMan = Path.Combine(Art, "textures", "textures.json");
            var texDefs = File.Exists(texMan) ? JsonUtility.FromJson<TexManifest>(File.ReadAllText(texMan)).textures : new TexDef[0];
            var texAbsent = texDefs.Where(t => !all.OfType<Texture2D>().Any(x => x.name == t.name && x.isReadable && x.format == TextureFormat.RGBA32))
                .Select(t => t.name).ToList();
            var names = new HashSet<string>(sprites.Select(s => s.name));
            var absent = man.sprites.Where(s => !names.Contains(s.name)).Select(s => s.name).ToList();
            Line(sb, $"check: sprites.json {man.sprites.Length}, in bundle {sprites.Count}, absent [{string.Join(",", absent)}], fonts {fonts.Count}, " +
                $"legacy fonts {legacy.Count} bad [{string.Join(",", legacyBad)}], textures {texDefs.Length} absent/unreadable [{string.Join(",", texAbsent)}]");
            File.WriteAllText(file + ".contents.txt", sb.ToString());
            Debug.Log("RuneSkin contents:\n" + sb);
            if (absent.Count > 0 || fonts.Count != Fonts.Length || legacyBad.Count > 0 || texAbsent.Count > 0)
                throw new Exception("bundle incomplete");
            Debug.Log("RuneSkin: BUILD OK");
        }
        finally
        {
            ab.Unload(true);
        }
    }
}
