// Offline preview of the RuneSkin/UI Bar shader (HUD strip bars) at in-game pixel sizes, for same-scale crops against
// mockup hud-4M3 before a game launch. Draws the fill pass (mode 0) into a render texture per state and writes PNG files.
// Unity.exe -batchmode -quit -projectPath unity -executeMethod RenderBar.Render -logFile <log>   env: RSBAR_OUT (dir)
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class RenderBar
{
    public static void Render()
    {
        AssetBundle bundle = null;
        try
        {
            var outDir = Environment.GetEnvironmentVariable("RSBAR_OUT") ?? throw new Exception("RSBAR_OUT not set");
            Directory.CreateDirectory(outDir);
            AssetBundle.UnloadAllAssetBundles(true);
            bundle = AssetBundle.LoadFromFile(Path.GetFullPath("Build/runeskin")) ?? throw new Exception("bundle missing (run BuildBundles first)");
            var shader = bundle.LoadAsset<Shader>("RuneSkinBar") ?? throw new Exception("shader missing in bundle");
            // window sizes in px: HP / resource row at 1080p (mockup hud-4M3: 940 x 30, 460 x 22)
            Bar(outDir, "hp-1080", shader, 940, 30, new Color(0.847f, 0.11f, 0.133f), 0.9f, 0.9f, 0f);
            Bar(outDir, "hp-lag-1080", shader, 940, 30, new Color(0.847f, 0.11f, 0.133f), 0.55f, 0.7f, 0f);
            Bar(outDir, "hp-shield-1080", shader, 940, 30, new Color(0.847f, 0.11f, 0.133f), 0.8f, 0.8f, 0.5f);
            Bar(outDir, "stamina-1080", shader, 460, 22, new Color(0.933f, 0.647f, 0.063f), 1f, 1f, 0f);
            Bar(outDir, "eitr-1080", shader, 460, 22, new Color(0.557f, 0.235f, 0.941f), 0.85f, 0.85f, 0f);
            Bar(outDir, "hp-720", shader, 627, 20, new Color(0.847f, 0.11f, 0.133f), 0.9f, 0.9f, 0f);
            Bar(outDir, "hp-1440", shader, 1253, 40, new Color(0.847f, 0.11f, 0.133f), 0.9f, 0.9f, 0f);
            Debug.Log("RenderBar: OK " + outDir);
        }
        catch (Exception e)
        {
            Debug.LogError("RenderBar: FAILED " + e);
            EditorApplication.Exit(1);
        }
        finally
        {
            if (bundle != null) bundle.Unload(true);
        }
    }

    static void Bar(string dir, string name, Shader shader, int w, int h, Color fill, float f, float lag, float shield)
    {
        var m = new Material(shader);
        m.SetFloat("_Mode", 0f);
        m.SetFloat("_Fill", f);
        m.SetFloat("_Lag", lag);
        m.SetFloat("_Shield", shield);
        m.SetFloat("_Aspect", (float)w / h);
        m.SetColor("_FillColor", fill);
        m.SetFloat("_Depth", 0.45f); // theme share.hud.bar.fx.depth
        m.SetFloat("_HiPos", 0.72f); // theme share.hud.bar.fx.hi.pos
        m.SetFloat("_HiWidth", 0.2f); // theme share.hud.bar.fx.hi.width
        m.SetFloat("_HiAmount", 0.3f); // theme share.hud.bar.fx.hi
        m.SetFloat("_Rim", 1f); // theme share.hud.bar.fx.rim
        m.SetColor("_MissingColor", new Color(0.353f, 0.353f, 0.353f)); // theme color.hud.bar.fx.missing
        var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(Texture2D.whiteTexture, rt, m);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
        rt.Release();
        UnityEngine.Object.DestroyImmediate(m);
    }
}
