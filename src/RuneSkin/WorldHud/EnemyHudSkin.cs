using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RuneSkin.Diagnostics;
using RuneSkin.PlayerHud;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.WorldHud
{
    // Family "EnemyHud" (art\README.md "M4 sprites": enemy bars = m4-hud-b, boss bar = A). EnemyHud instantiates its
    // templates per creature (ShowHud, EnemyHud.cs:126-157: m_baseHud / m_baseHudBoss / m_baseHudPlayer /
    // m_baseHudMount with Health/{darken, bkg, health_slow/bar, health_fast/bar, health_fast_friendly/bar}, Name,
    // level_2/3 stars), so the templates are restyled once in EnemyHud.Awake and every clone inherits it: trough
    // (bar_trough) + fill (bar_enemy_fill white mask; GuiBar keeps the vanilla colours, GuiBar.cs:32-38), name in the
    // body font with a dark outline, stars as badges, a RuneSkin_EnemyFrame (frame_enemy_normal / frame_boss) fitted
    // around Health, the boss name on a header plaque. Vaettir (morgott.valheim.bossawakening, UI\RarityFrames.cs) adds
    // its own VT_Frame child per HUD with the creature's rarity tier and hides the stars: RuneSkin keeps that frame
    // object and its tier and only redraws it with the matching RuneSkin tier art (frame_enemy_<tier>; boss bars
    // frame_boss) after Vaettir's UpdateHuds postfix; its own frame is off while a VT_Frame shows.
    public static class EnemyHudSkin
    {
        public const string ScreenId = "EnemyHud";
        public const string FrameName = "RuneSkin_EnemyFrame";
        private const string VaettirFrame = "VT_Frame";
        private const string VaettirGuid = "morgott.valheim.bossawakening";

        private static readonly HudArt Art = new HudArt(ScreenId);
        private static Transform _root;
        private static float _canvasFactor = 1f;

        private sealed class State
        {
            public RectTransform Health;
            public Image Own, Vt;
            public Sprite VtSet;
            public bool Boss;
        }

        private static readonly ConditionalWeakTable<EnemyHud.HudData, State> States = new ConditionalWeakTable<EnemyHud.HudData, State>();

        internal static void Attach(EnemyHud hud)
        {
            _root = hud.m_hudRoot ? hud.m_hudRoot.transform : hud.transform;
            var canvas = _root.GetComponentInParent<Canvas>(true);
            if (canvas)
            {
                // Valheim canvases run at referencePixelsPerUnit 50: borders authored at 100 (Restyler.CanvasPpuFactor).
                Art.SyncPpu(canvas.rootCanvas.referencePixelsPerUnit);
                _canvasFactor = canvas.rootCanvas.referencePixelsPerUnit / 100f;
            }
            Template(hud.m_baseHud, false, true);
            Template(hud.m_baseHudBoss, true, true);
            Template(hud.m_baseHudPlayer, false, false);
            Template(hud.m_baseHudMount, false, false);
            Plugin.Log.LogInfo("[RuneSkin] enemy HUD templates restyled");
        }

        private static void Template(GameObject template, bool boss, bool framed)
        {
            if (!template) return;
            var t = template.transform;
            // Bar + frame read at mockup size (Codex r1: too thin): the creature HUD scales as a whole, the name font
            // is divided back so the label does not grow with it.
            var scale = boss || !framed ? 1f : ThemeTokens.GetFloat("size.enemy.scale", 1.35f);
            t.localScale = new Vector3(scale, scale, 1f);
            var health = t.Find("Health") as RectTransform;
            if (health)
            {
                if (framed) Grow(t, health, ThemeTokens.GetFloat(boss ? "size.boss.bar.h" : "size.enemy.bar.h", 0f));
                var bkg = health.Find("bkg") as RectTransform;
                if (bkg && bkg.TryGetComponent<Image>(out var trough) && Art.Reskin(trough, "sprite.hud.bar.trough", "color.frame", "enemy bar trough"))
                    bkg.offsetMin = bkg.offsetMax = Vector2.zero; // trough = bar rect, the frame rim covers the edge
                for (var i = 0; i < health.childCount; i++)
                {
                    var child = health.GetChild(i);
                    if (!child.name.StartsWith("health_", StringComparison.Ordinal)) continue;
                    var bar = child.Find("bar");
                    if (!bar || !bar.TryGetComponent<Image>(out var fill)) continue;
                    // health_fast = deep HUD red (Codex r2: the orange read as a rarity colour), health_slow = the
                    // soft lag ghost of the player bars; friendly / mount bars keep vanilla colours (GuiBar caches them).
                    var color = child.name == "health_fast" ? "color.hud.health" : child.name == "health_slow" ? "color.hud.lag" : null;
                    Art.Reskin(fill, "sprite.bar.enemy.fill", color, "enemy bar fill");
                }
                if (framed)
                {
                    var frame = NewFrame(health);
                    var fit = SetFrame(frame, health, boss ? "sprite.frame.boss" : "sprite.frame.enemy.normal", boss);
                    if (!boss) ClearName(t, health, fit.Top);
                    Art.Note(frame, boss ? "frame.boss" : "frame.enemy.normal", frame.sprite, "RuneSkin enemy frame around Health (off while a Vaettir VT_Frame shows)");
                }
            }
            var name = t.Find("Name");
            if (name && name.TryGetComponent<TMP_Text>(out var text))
            {
                var ok = TextLook.Apply(text, boss ? "font.display" : "font.body", boss ? "boss" : "enemy");
                text.color = ThemeTokens.GetColor(boss ? "color.boss.name" : "color.enemy.name", text.color);
                text.fontSize = ThemeTokens.GetFloat(boss ? "size.boss.name.font" : "size.enemy.name.font", text.fontSize) / scale;
                Art.Note(text, boss ? "boss.name" : "enemy.name", ok, "enemy name (outlined)");
                if (boss) BossPlaque(name as RectTransform);
            }
            for (var i = 0; i < t.childCount; i++)
            {
                var row = t.GetChild(i);
                if (!row.name.StartsWith("level_", StringComparison.Ordinal)) continue;
                foreach (var img in row.GetComponentsInChildren<Image>(true))
                {
                    if (img.name == "star")
                    {
                        if (Art.Reskin(img, "sprite.badge.level.star", "color.frame", "level star badge"))
                        {
                            var s = ThemeTokens.GetFloat("size.enemy.star", 14f);
                            img.rectTransform.sizeDelta = new Vector2(s, s);
                            img.preserveAspect = true;
                        }
                    }
                    else if (img.name.StartsWith("star", StringComparison.Ordinal))
                    {
                        img.enabled = false; // inner gold star of the vanilla two-layer star: the badge is whole
                        Art.Suppressed(img.transform, "inner vanilla star layer (the badge is one sprite)");
                    }
                }
            }
        }

        // Taller Health bar (template, every clone inherits it). GuiBar only writes the horizontal size of its m_bar
        // (GuiBar.cs SetBar / SetWidth: SetSizeWithCurrentAnchors(Horizontal)), so the vertical geometry is free: the
        // Health rect grows to the token height around its pivot, every non-stretched descendant (bkg, darken,
        // health_* and their bar) scales its height and y position by the same factor, stretched ones follow the
        // parent (ClearName then lifts the name above the frame). Vaettir's VT_Frame
        // sizes itself from health.rect.height (RarityFrames.cs:245) and is refitted by SetFrame, so tiers keep working.
        private static void Grow(Transform hud, RectTransform health, float target)
        {
            var old = health.rect.height;
            var k = EnemyHudMath.BarGrowth(old, target);
            Plugin.Log.LogInfo($"[RuneSkin] enemy HUD '{hud.name}' Health height {old:0.#} -> {old * k:0.#} (x{k:0.##})");
            if (k <= 1.001f) return;
            var delta = old * (k - 1f);
            if (Mathf.Approximately(health.anchorMin.y, health.anchorMax.y)) health.sizeDelta += new Vector2(0f, delta);
            else
            {
                health.offsetMin -= new Vector2(0f, delta * health.pivot.y);
                health.offsetMax += new Vector2(0f, delta * (1f - health.pivot.y));
            }
            foreach (var rt in health.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt == health || !Mathf.Approximately(rt.anchorMin.y, rt.anchorMax.y)) continue;
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, rt.sizeDelta.y * k);
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, rt.anchoredPosition.y * k);
            }
        }

        private static void BossPlaque(RectTransform name)
        {
            if (!name || !name.parent) return;
            var plaque = HudArt.Rect("RuneSkin_BossPlaque", name.parent);
            plaque.SetSiblingIndex(name.GetSiblingIndex());
            plaque.anchorMin = name.anchorMin;
            plaque.anchorMax = name.anchorMax;
            plaque.pivot = new Vector2(0.5f, 0.5f);
            var r = name.rect;
            plaque.anchoredPosition = name.anchoredPosition + new Vector2((0.5f - name.pivot.x) * r.width, (0.5f - name.pivot.y) * r.height);
            plaque.sizeDelta = new Vector2(ThemeTokens.GetFloat("size.boss.plaque.w", 300f), ThemeTokens.GetFloat("size.boss.plaque.h", 42f));
            if (plaque.anchorMin != plaque.anchorMax) plaque.sizeDelta = Vector2.zero;
            var img = plaque.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            Art.Reskin(img, "sprite.plaque.header", "color.frame", "boss name plaque");
        }

        private static Image NewFrame(RectTransform health)
        {
            var go = new GameObject(FrameName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(health, false);
            var after = -1;
            for (var i = 0; i < health.childCount; i++)
                if (health.GetChild(i).name.StartsWith("health_", StringComparison.Ordinal)) after = i;
            rect.SetSiblingIndex(after + 1);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.fillCenter = false;
            return img;
        }

        // Frame window fitted to the Health rect (EnemyHudMath.Fit): borders spill outside the bar.
        private static FrameFit SetFrame(Image frame, RectTransform health, string token, bool boss)
        {
            var sprite = ThemeTokens.GetSprite(token);
            if (!sprite)
            {
                frame.enabled = false;
                return default;
            }
            var b = sprite.border; // x = L, y = B, z = R, w = T
            var fit = EnemyHudMath.Fit(health.rect.height, sprite.rect.height, b.x, b.z, b.w, b.y, sprite.pixelsPerUnit,
                boss ? 0f : ThemeTokens.GetFloat("scale.enemy.frame.border", 0f));
            var rt = frame.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(-fit.Left, -fit.Bottom);
            rt.offsetMax = new Vector2(fit.Right, fit.Top);
            frame.sprite = sprite;
            frame.type = Image.Type.Sliced;
            frame.fillCenter = false;
            frame.color = Color.white;
            frame.pixelsPerUnitMultiplier = fit.PpuMultiplier * _canvasFactor;
            frame.enabled = true;
            return fit;
        }

        // The enemy name (and star rows) sit above the frame top (the frame spills fit.Top over the bar): moved up
        // until their rect bottom clears it by size.enemy.name.gap.
        private static void ClearName(Transform hud, RectTransform health, float frameTop)
        {
            var top = health.rect.yMax + frameTop + ThemeTokens.GetFloat("size.enemy.name.gap", 2f);
            var topInHud = hud.InverseTransformPoint(health.TransformPoint(new Vector3(0f, top, 0f))).y;
            var name = hud.Find("Name") as RectTransform;
            if (!name) return;
            var bottom = hud.InverseTransformPoint(name.TransformPoint(new Vector3(0f, name.rect.yMin, 0f))).y;
            if (bottom >= topInHud) return;
            var up = topInHud - bottom;
            for (var i = 0; i < hud.childCount; i++)
            {
                var c = hud.GetChild(i) as RectTransform;
                if (c && (c == name || c.name.StartsWith("level_", StringComparison.Ordinal))) c.anchoredPosition += new Vector2(0f, up);
            }
        }

        // After Vaettir's UpdateHuds postfix: its VT_Frame keeps the tier, RuneSkin draws the tier art.
        internal static void Update(EnemyHud hud)
        {
            foreach (var pair in hud.m_huds)
            {
                var data = pair.Value;
                if (data == null || !data.m_gui) continue;
                if (!States.TryGetValue(data, out var s))
                {
                    s = new State { Health = data.m_gui.transform.Find("Health") as RectTransform, Boss = data.m_character && data.m_character.IsBoss() };
                    if (s.Health)
                    {
                        var own = s.Health.Find(FrameName);
                        if (own) s.Own = own.GetComponent<Image>();
                    }
                    States.Add(data, s);
                }
                if (!s.Health) continue;
                if (!s.Vt)
                {
                    var vt = s.Health.Find(VaettirFrame);
                    if (vt) s.Vt = vt.GetComponent<Image>();
                }
                var vtOn = s.Vt && s.Vt.enabled && s.Vt.sprite;
                if (vtOn && s.Vt.sprite != s.VtSet)
                {
                    SetFrame(s.Vt, s.Health, EnemyHudMath.TierToken(s.Vt.sprite.name, s.Boss), s.Boss);
                    s.VtSet = s.Vt.sprite;
                }
                if (s.Own && s.Own.enabled == vtOn) s.Own.enabled = !vtOn && s.Own.sprite;
            }
        }

        public static void Record()
        {
            if (_root) SkinDiagnostics.Record(Art.Report(_root));
        }
    }

    // Family "EnemyHud" hooks.
    [HarmonyPatch]
    public static class EnemyHudAdapter
    {
        [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Awake_Postfix(EnemyHud __instance)
        {
            try { EnemyHudSkin.Attach(__instance); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] enemy HUD restyle failed (vanilla bars stay): {e}"); }
        }

        [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.UpdateHuds))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("morgott.valheim.bossawakening", "morgott.valheim.tweaks", "MidnightsFX.StarLevelSystem")]
        private static void UpdateHuds_Postfix(EnemyHud __instance)
        {
            try { EnemyHudSkin.Update(__instance); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] enemy HUD frame pass failed: {e}"); }
        }
    }
}
