using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RuneSkin.Restyle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.Diagnostics
{
    // Coverage scan (M7 harness, Plugin.Coverage): every graphic visible on screen right now (active, enabled, drawn
    // alpha, on a screen-space canvas, inside the screen) is one of
    // - styled: a RuneSkin role applied (SkinMark Styled);
    // - explained: classified by a family pass as content / skipped (icons, bar fills, masks), or a RuneSkin object;
    // - unstyled: never classified by any family, or classified Unknown / Degraded.
    // "before" = what would be unstyled without the M7 families (fallback units ModUi.*, InventoryExtras.*, Radial).
    public static class Coverage
    {
        private static bool M7(string screen) =>
            screen != null && (screen.StartsWith("ModUi.", StringComparison.Ordinal) || screen.StartsWith("InventoryExtras", StringComparison.Ordinal) || screen.StartsWith("Radial", StringComparison.Ordinal));

        public static string[] Scan(string state)
        {
            var rows = new Dictionary<Graphic, KeyValuePair<string, ReportRow>>();
            foreach (var report in SkinDiagnostics.Reports)
                foreach (var row in report.Rows)
                    if (row.Graphic) rows[row.Graphic] = new KeyValuePair<string, ReportRow>(report.ScreenId, row);
            int visible = 0, styled = 0, explained = 0, own = 0, unstyled = 0, m7 = 0, foreignFont = 0, legacyTheme = 0, legacyForeign = 0;
            var perScreen = new SortedDictionary<string, int[]>(StringComparer.Ordinal); // styled, explained, unstyled
            var lines = new List<string>();
            var screen = new Rect(0, 0, Screen.width, Screen.height);
            var corners = new Vector3[4];
            foreach (var g in UnityEngine.Object.FindObjectsByType<Graphic>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!Visible(g, screen, corners)) continue;
                visible++;
                rows.TryGetValue(g, out var hit);
                var screenId = hit.Key;
                var mark = g.GetComponent<SkinMark>();
                string status, reason;
                if (mark && mark.Internal) { status = "own"; reason = "RuneSkin decor"; }
                else if (mark && mark.Outcome == Outcome.Styled && mark.Role != null) { status = "styled"; reason = mark.Role; }
                else if (hit.Value != null && hit.Value.Result.Outcome == Outcome.Styled) { status = "styled"; reason = hit.Value.Result.Reason; }
                else if (hit.Value != null && (hit.Value.Result.Outcome == Outcome.Content || hit.Value.Result.Outcome == Outcome.Skipped)) { status = "explained"; reason = hit.Value.Result.Reason; }
                else if (hit.Value != null) { status = "unstyled"; reason = $"{hit.Value.Result.Outcome}: {hit.Value.Result.Reason}"; }
                else if (g is TMP_SubMeshUI || g is TMP_SelectionCaret) { status = "explained"; reason = "TMP sub-mesh / caret of a text"; }
                else if (OwnObject(g.transform)) { status = "own"; reason = "RuneSkin object"; }
                // Families that draw in their own code (messages, crosshair, minimap...) set theme faces / art without a
                // role pass: a theme face or a bundle sprite is RuneSkin's look.
                else if (g is TMP_Text ft && ft.font && (ft.font.name.StartsWith("RuneSkin", StringComparison.Ordinal) || Theme.ThemeTokens.IsLoadedFont(ft.font))) { status = "styled"; reason = "theme face (family code)"; }
                else if (g is Image fi && Theme.ThemeTokens.IsThemeSprite(fi.sprite)) { status = "styled"; reason = "theme art (family code)"; }
                else if (FamilyDrawn(g.transform)) { status = "explained"; reason = "content of a family drawn in code (message icon, map)"; }
                else { status = "unstyled"; reason = "never classified (no family root, no fallback unit)"; }
                if (screenId == null && mark) screenId = RootId(g.transform);
                switch (status)
                {
                    case "styled": styled++; break;
                    case "explained": explained++; break;
                    case "own": own++; break;
                    default: unstyled++; break;
                }
                if (M7(screenId) && status != "unstyled") m7++;
                if (status == "styled" && g is TMP_Text t && t.font && !t.font.name.StartsWith("RuneSkin", StringComparison.Ordinal) && !Theme.ThemeTokens.IsLoadedFont(t.font)) foreignFont++;
                // Legacy uGUI texts that took a role: on a theme legacy face (bundle PT Sans / Cormorant, rune source) or not.
                if (status == "styled" && g is UnityEngine.UI.Text lt) { if (Theme.ThemeTokens.IsLegacyThemeFont(lt.font)) legacyTheme++; else legacyForeign++; }
                var key = screenId ?? "(none)";
                if (!perScreen.TryGetValue(key, out var c)) perScreen[key] = c = new int[3];
                c[status == "styled" ? 0 : status == "unstyled" ? 2 : 1]++;
                if (status == "unstyled")
                    lines.Add($"UNSTYLED {SkinDiagnostics.PathOf(g.transform, null)} | {g.GetType().Name} {SkinDiagnostics.Describe(g)} | screen={key} | {reason}");
            }
            var head = $"coverage state={state} visible={visible} styled={styled} explained={explained} own={own} unstyled={unstyled} m7={m7} before={unstyled + m7} units={ModScreens.ForeignUi.UnitCount} styledForeignFont={foreignFont} legacyThemeFace={legacyTheme} legacyForeignFace={legacyForeign}";
            var output = new List<string> { head };
            foreach (var p in perScreen)
                output.Add($"screen {p.Key} styled={p.Value[0]} explained={p.Value[1]} unstyled={p.Value[2]}");
            foreach (var u in ModScreens.ForeignUi.AllUnits)
                if (u.Key) output.Add($"unit {u.Value} {SkinDiagnostics.PathOf(u.Key, null)} active={u.Key.gameObject.activeInHierarchy}");
            output.AddRange(lines.Take(400));
            try
            {
                Directory.CreateDirectory(SkinDiagnostics.DumpDir);
                File.WriteAllLines(Path.Combine(SkinDiagnostics.DumpDir, $"coverage-{state}.txt"), output, Encoding.UTF8);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[RuneSkin] coverage dump: {e.Message}"); }
            Plugin.Log.LogInfo($"[RuneSkin] {head}");
            return output.ToArray();
        }

        private static bool Visible(Graphic g, Rect screen, Vector3[] corners)
        {
            if (!g || !g.isActiveAndEnabled) return false;
            var canvas = g.canvas;
            if (!canvas || !canvas.isActiveAndEnabled) return false;
            var rootCanvas = canvas.rootCanvas;
            if (rootCanvas.renderMode == RenderMode.WorldSpace) return false;
            if (g.color.a <= 0.01f || g.canvasRenderer.cull || g.canvasRenderer.GetInheritedAlpha() <= 0.01f) return false;
            if (g is TMP_Text t && string.IsNullOrWhiteSpace(t.text)) return false;
            if (g is Text lt && string.IsNullOrWhiteSpace(lt.text)) return false;
            if (g is Image img && !img.sprite && img.color.a <= 0.02f) return false;
            g.rectTransform.GetWorldCorners(corners);
            var cam = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
            var a = (Vector2)RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            var b = (Vector2)RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            var r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            return r.width >= 1f && r.height >= 1f && r.Overlaps(screen);
        }

        private static bool FamilyDrawn(Transform t)
        {
            bool Under(Component c, string family) => c && Ownership.OwnershipRegistry.IsOwned(family) && t.IsChildOf(c.transform);
            var msg = MessageHud.instance;
            if (msg && msg.m_messageIcon && Ownership.OwnershipRegistry.IsOwned(Ownership.OwnershipRegistry.MessageHud) && t.IsChildOf(msg.m_messageIcon.transform.parent)) return true;
            return Under(MessageHud.instance, Ownership.OwnershipRegistry.MessageHud) || Under(Minimap.instance, Ownership.OwnershipRegistry.Minimap) ||
                   Under(EnemyHud.instance, Ownership.OwnershipRegistry.EnemyHud) || Under(DamageText.instance, Ownership.OwnershipRegistry.DamageText);
        }

        private static bool OwnObject(Transform t)
        {
            for (var p = t; p; p = p.parent)
                if (p.name.StartsWith("RuneSkin", StringComparison.Ordinal)) return true;
            return false;
        }

        private static string RootId(Transform t)
        {
            for (var p = t; p; p = p.parent)
                if (p.TryGetComponent<RootMark>(out var m)) return m.ScreenId;
            var root = Api.ScreenRegistry.RootOf(t);
            return root ? Api.ScreenRegistry.ContextOf(root)?.ScreenId : null;
        }
    }
}
