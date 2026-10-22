using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RuneSkin.Classify;
using RuneSkin.Restyle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.Diagnostics
{
    public sealed class ReportRow
    {
        public string Path;
        public string Component;
        public string Detail;
        public Classification Result;
        public Graphic Graphic; // live reference (coverage scan)
    }

    // One classification pass over a root.
    public sealed class ScreenReport
    {
        public readonly string ScreenId;
        public readonly Transform Root;
        public readonly List<ReportRow> Rows = new List<ReportRow>();
        public readonly int[] Counts = new int[Enum.GetValues(typeof(Outcome)).Length];

        public ScreenReport(string screenId, Transform root)
        {
            ScreenId = screenId;
            Root = root;
        }

        public int Count(Outcome o) => Counts[(int)o];

        public void Add(Graphic g, Transform root, Classification c)
        {
            Counts[(int)c.Outcome]++;
            Rows.Add(new ReportRow { Graphic = g, Path = SkinDiagnostics.PathOf(g.transform, root), Component = g.GetType().Name, Detail = SkinDiagnostics.Describe(g), Result = c });
        }

        public string Summary =>
            $"{ScreenId}: graphics={Rows.Count} styled={Count(Outcome.Styled)} degraded={Count(Outcome.Degraded)} content={Count(Outcome.Content)} skipped={Count(Outcome.Skipped)} unknown={Count(Outcome.Unknown)}";
    }

    // Diagnostics: per-screen classification counts, unexplained (Unknown) rows, theme/asset problems, and on dump the
    // live checks (text below the readable floor, rects off the safe area). Dumps go to BepInEx\RuneSkin\diag.
    public static class SkinDiagnostics
    {
        private static readonly Dictionary<string, ScreenReport> Last = new Dictionary<string, ScreenReport>();
        private static readonly List<string> ThemeIssues = new List<string>();
        private static readonly HashSet<string> WarnedUnknown = new HashSet<string>();
        private static readonly Dictionary<string, string> LastSummary = new Dictionary<string, string>();

        public static string DumpDir => Path.Combine(BepInEx.Paths.BepInExRootPath, "RuneSkin", "diag");
        public static IReadOnlyList<string> ThemeProblems => ThemeIssues;

        public static void Theme(string message)
        {
            ThemeIssues.Add(message);
            Plugin.Log?.LogWarning($"[RuneSkin] theme: {message}");
        }

        public static void Note(string message) => Plugin.Log?.LogInfo($"[RuneSkin] {message}");

        public static void Record(ScreenReport report)
        {
            Last[report.ScreenId] = report;
            // One line per change of the counts (re-shows of an unchanged screen stay quiet).
            var summary = report.Summary;
            if (!LastSummary.TryGetValue(report.ScreenId, out var prev) || prev != summary)
            {
                LastSummary[report.ScreenId] = summary;
                Plugin.Log?.LogInfo($"[RuneSkin] diag {summary}");
            }
            foreach (var row in report.Rows)
                if (row.Result.Outcome == Outcome.Unknown && WarnedUnknown.Add(report.ScreenId + "|" + row.Path))
                    Plugin.Log?.LogWarning($"[RuneSkin] unknown {report.ScreenId}: {row.Path} [{row.Component} {row.Detail}] {row.Result.Reason}");
        }

        public static ScreenReport Get(string screenId) => Last.TryGetValue(screenId, out var r) ? r : null;

        public static IEnumerable<string> Screens => Last.Keys;

        public static IEnumerable<ScreenReport> Reports => Last.Values;

        // Writes one file per screen (or all); returns the paths. Also logs the summary with live checks.
        public static List<string> Dump(string screenId = null)
        {
            var written = new List<string>();
            Directory.CreateDirectory(DumpDir);
            foreach (var report in Last.Values.Where(r => screenId == null || r.ScreenId == screenId).ToList())
            {
                var sb = new StringBuilder();
                sb.AppendLine($"# {report.Summary}");
                sb.AppendLine($"# theme '{Theme_Name()}' rev {RuneSkin.Theme.ThemeTokens.Revision} bundle={(RuneSkin.Theme.ThemeTokens.BundleLoaded ? "loaded" : "missing")}");
                foreach (var issue in ThemeIssues) sb.AppendLine($"# theme-issue {issue}");
                var floor = RuneSkin.Theme.ThemeTokens.GetFloat("floor.text.body.px", 16f);
                int belowFloor = 0, offSafe = 0;
                var safe = Screen.safeArea;
                foreach (var row in report.Rows)
                    sb.AppendLine($"{row.Result.Outcome,-8} {row.Result.Role ?? "-",-14} {row.Path} | {row.Component} {row.Detail} | {row.Result.Reason}");
                if (report.Root)
                {
                    foreach (var text in report.Root.GetComponentsInChildren<TMP_Text>(false))
                    {
                        var px = PhysicalSize(text);
                        var min = text.TryGetComponent<RuneSkin.PlayerHud.TextFloor>(out var own) ? own.Px : floor;
                        if (px > 0f && px < min - 0.05f && !string.IsNullOrEmpty(text.text))
                        {
                            belowFloor++;
                            sb.AppendLine($"FLOOR    {PathOf(text.transform, report.Root)} {px:0.0}px < {min}px");
                        }
                    }
                    var corners = new Vector3[4];
                    foreach (var g in report.Root.GetComponentsInChildren<Graphic>(false))
                    {
                        if (!g.canvas || g.canvas.renderMode != RenderMode.ScreenSpaceOverlay || !g.enabled || g.color.a <= 0f) continue;
                        g.rectTransform.GetWorldCorners(corners);
                        if (corners[0].x < safe.xMin - 1 || corners[0].y < safe.yMin - 1 || corners[2].x > safe.xMax + 1 || corners[2].y > safe.yMax + 1)
                        {
                            // Full-screen scrims/blur may cover the whole screen; only report content that leaves it.
                            if (g.rectTransform.rect.width * g.canvas.scaleFactor >= Screen.width * 0.98f) continue;
                            offSafe++;
                            sb.AppendLine($"SAFEAREA {PathOf(g.transform, report.Root)} ({corners[0].x:0},{corners[0].y:0})-({corners[2].x:0},{corners[2].y:0}) safe {safe}");
                        }
                    }
                }
                sb.AppendLine($"# checks belowFloor={belowFloor} offSafeArea={offSafe}");
                var path = Path.Combine(DumpDir, Sanitize(report.ScreenId) + ".txt");
                File.WriteAllText(path, sb.ToString());
                written.Add(path);
                Plugin.Log?.LogInfo($"[RuneSkin] diag dump {report.Summary} belowFloor={belowFloor} offSafeArea={offSafe} -> {path}");
            }
            return written;
        }

        private static string Theme_Name() => RuneSkin.Theme.ThemeTokens.ThemeName;

        // Rendered glyph size in physical pixels: font size x canvas scale x accumulated local scale.
        public static float PhysicalSize(TMP_Text text)
        {
            var canvas = text.canvas;
            if (!canvas) return 0f;
            var size = text.fontSize; // the auto-sized result when auto sizing is on
            var root = canvas.rootCanvas.transform;
            var scale = text.transform.lossyScale.y / Mathf.Max(1e-6f, root.lossyScale.y);
            return size * scale * canvas.rootCanvas.scaleFactor;
        }

        public static string PathOf(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (var p = t; p; p = p.parent)
            {
                parts.Add(p.name);
                if (p == root) break;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        public static string Describe(Graphic g)
        {
            switch (g)
            {
                case Image i:
                    var mark = g.GetComponent<SkinMark>();
                    var sprite = mark && mark.OriginalSprite != null ? mark.OriginalSprite : i.sprite ? i.sprite.name : "-";
                    var geo = "";
                    if (i.sprite && (i.type == Image.Type.Sliced || i.type == Image.Type.Tiled))
                    {
                        // Live 9-slice data: border in sprite px, drawn border = border x refPPU / (PPU x multiplier).
                        var b = i.sprite.border;
                        var c = i.canvas;
                        var r = i.rectTransform.rect;
                        geo = $" type={i.type} border={b.x:0}/{b.y:0}/{b.z:0}/{b.w:0} ppu={i.sprite.pixelsPerUnit:0.#} mult={i.pixelsPerUnitMultiplier:0.###}" +
                              $" refPpu={(c ? c.referencePixelsPerUnit : 0f):0.#} rootRefPpu={(c ? c.rootCanvas.referencePixelsPerUnit : 0f):0.#}" +
                              $" scaleFactor={(c ? c.scaleFactor : 0f):0.###} rect={r.width:0.#}x{r.height:0.#}";
                    }
                    return $"spr={sprite} active={g.gameObject.activeInHierarchy}{geo}";
                case TMP_Text t:
                    return $"font={(t.font ? t.font.name : "-")} size={t.fontSize:0.#} active={g.gameObject.activeInHierarchy}";
                default:
                    return $"active={g.gameObject.activeInHierarchy}";
            }
        }

        private static string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
