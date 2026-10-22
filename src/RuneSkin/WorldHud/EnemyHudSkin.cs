using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using RuneSkin.Diagnostics;
using RuneSkin.PlayerHud;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RuneSkin.Ownership;
using RuneSkin.Compat;

namespace RuneSkin.WorldHud
{
    // Family "EnemyHud" (user picks 2026-10-22: enemy bars = hud-design enemy-b "emblem socket", spec
    // hud-design\spec.md sections 1-5). EnemyHud instantiates its templates per creature (ShowHud, EnemyHud.cs:126-157:
    // m_baseHud / m_baseHudBoss / m_baseHudPlayer / m_baseHudMount with Health/{darken, bkg, health_slow/bar,
    // health_fast/bar, health_fast_friendly/bar}, Name, level_2/3 stars), so the templates are restyled once in
    // EnemyHud.Awake and every clone inherits it: trough (bar_trough) + fill (bar_enemy_fill white mask; GuiBar keeps the
    // vanilla colours, GuiBar.cs:32-38), name in the body font with a dark outline, stars as badges, a
    // RuneSkin_EnemyFrame (frame_enemy_normal / frame_boss) fitted around Health with the tier emblem socket at the bar's left
    // end (part of the tier frame, cut with it from the mockup), the shield band and the stagger line inside the window, the boss name on a header plaque. Vaettir
    // (morgott.valheim.bossawakening, UI\RarityFrames.cs) adds its own VT_Frame child per HUD with the creature's rarity
    // tier and hides the stars: RuneSkin keeps that frame object and its tier and redraws it with the matching RuneSkin
    // tier art (frame_enemy_<tier>: socket + rail + cap cut from enemy-b; boss bars frame_boss) after Vaettir's UpdateHuds postfix.
    //
    // Visibility (Hud.EnemyBars = Smart): per frame a score per counted HUD (aimed + linger, engaged, alerted threat;
    // EnemyHudMath.Score), the top Hud.EnemyBarsMax survive (at most Hud.EnemyBarsThreat threats), the rest fade out via
    // a CanvasGroup on the template (vanilla's SetActive is never fought: vanilla keeps the picked HUDs active through its
    // own m_hoverTimer, written in the UpdateHuds prefix). Players, bosses, mounts, tamed and friendly creatures keep the
    // vanilla rules at alpha 1.
    public static class EnemyHudSkin
    {
        public const string ScreenId = "EnemyHud";
        public const string FrameName = "RuneSkin_EnemyFrame";
        public const string ShieldName = "RuneSkin_Shield";
        public const string StaggerName = "RuneSkin_Stagger";
        private const string VaettirFrame = ModCompat.VaettirFrame;
        private const float VtRetrySeconds = 1f; // HUDs without a VT_Frame (players, tamed, Vaettir off) look again at most once a second
        private const int MaxCandidates = 64;

        private static readonly HudArt Art = new HudArt(ScreenId);
        private static Transform _root;
        private static float _canvasFactor = 1f;
        private static float _baseScale = 1f;

        private static readonly int EsHash = "mt_es".GetStableHashCode();
        private static readonly int EsMaxHash = "mt_es_max".GetStableHashCode();
        private static readonly int StgHash = "mt_stg".GetStableHashCode();

        private sealed class State
        {
            public EnemyHud.HudData Data;
            public RectTransform Health;
            public Image Own, Vt, Shield, Stagger;
            public Sprite VtSet;
            public bool Boss;
            public float VtRetryAt;
            public CanvasGroup Group;
            public TMP_Text Name;
            public bool Managed, Picked, WasManaged;
            public EnemyHudMath.Reason Reason;
            public float AimedAt = -1f, Alpha = 1f, Distance, ScaleK = 1f, ShieldW = -1f, StaggerW = -1f;
            public bool NameShown = true;
            public EnemyHudMath.StaggerState Stg;
            public EnemyHudMath.StaggerPhase ShownPhase = (EnemyHudMath.StaggerPhase)(-1);
            public float ShownFlash = -1f;
        }

        private static readonly ConditionalWeakTable<EnemyHud.HudData, State> States = new ConditionalWeakTable<EnemyHud.HudData, State>();

        // Pick scratch (no per-frame allocation).
        private static readonly float[] Scores = new float[MaxCandidates];
        private static readonly EnemyHudMath.Reason[] Reasons = new EnemyHudMath.Reason[MaxCandidates];
        private static readonly int[] Ids = new int[MaxCandidates];
        private static readonly bool[] Picked = new bool[MaxCandidates];
        private static readonly State[] Candidates = new State[MaxCandidates];

        internal static void Attach(EnemyHud hud)
        {
            Art.Reset();
            EnemyEngagement.Clear();
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
            // Threat / engaged bars reach past vanilla's 10 m creation radius (EnemyHud.cs:54, TestShow :101-123).
            if (EnemyHudConfig.Smart) hud.m_maxShowDistance = Mathf.Max(hud.m_maxShowDistance, EnemyHudConfig.Range.Value);
            Plugin.Log.LogInfo($"[RuneSkin] enemy HUD templates restyled (bars {EnemyHudConfig.Mode.Value}, range {hud.m_maxShowDistance:0.#} m)");
        }

        private static void Template(GameObject template, bool boss, bool framed)
        {
            if (!template) return;
            var t = template.transform;
            var scale = boss || !framed ? 1f : ThemeTokens.GetFloat("size.enemy.scale", 1f);
            if (!boss && framed) _baseScale = scale;
            t.localScale = new Vector3(scale, scale, 1f);
            var health = t.Find("Health") as RectTransform;
            if (health)
            {
                if (framed) Grow(t, health, ThemeTokens.GetFloat(boss ? "size.boss.bar.h" : "size.enemy.bar.h", 0f));
                if (framed && !boss) Widen(health, ThemeTokens.GetFloat("size.enemy.bar.w", 0f));
                var bkg = health.Find("bkg") as RectTransform;
                if (bkg && bkg.TryGetComponent<Image>(out var trough) && (boss || !framed
                        ? Art.Reskin(trough, "sprite.hud.bar.trough", "color.frame", "enemy bar trough")
                        : MockupStrip(trough, "sprite.bar.enemy.trough.b", "enemy bar trough (cut from enemy-b)")))
                    bkg.offsetMin = bkg.offsetMax = Vector2.zero; // trough = bar rect, the frame rim covers the edge
                for (var i = 0; i < health.childCount; i++)
                {
                    var child = health.GetChild(i);
                    if (!child.name.StartsWith("health_", StringComparison.Ordinal)) continue;
                    var bar = child.Find("bar");
                    if (!bar || !bar.TryGetComponent<Image>(out var fill)) continue;
                    // Boss / player / mount templates: health_fast deep HUD red, health_slow the soft lag ghost; friendly
                    // bars keep vanilla colours (GuiBar caches them). Enemy bars (enemy-b): the fill and the lag segment are
                    // strips cut from the approved mockup, untinted, stretched (straight cut end, its own bevel).
                    if (!boss && framed && child.name == "health_fast") MockupStrip(fill, "sprite.bar.enemy.fill.b", "enemy bar fill (cut from enemy-b)");
                    else if (!boss && framed && child.name == "health_slow") MockupStrip(fill, "sprite.bar.enemy.lag.b", "enemy lag segment (cut from enemy-b)");
                    else Art.Reskin(fill, "sprite.bar.enemy.fill", child.name == "health_fast" ? "color.hud.health" : child.name == "health_slow" ? "color.hud.lag" : null, "enemy bar fill");
                }
                if (framed)
                {
                    var frame = NewFrame(health);
                    var fit = SetFrame(frame, health, boss ? "sprite.frame.boss" : "sprite.frame.enemy.normal", boss);
                    Art.Note(frame, boss ? "frame.boss" : "frame.enemy.normal", frame.sprite, "RuneSkin enemy frame around Health (off while a Vaettir VT_Frame shows)");
                    var lift = fit.Top;
                    if (!boss)
                    {
                        // Inside the window, over the fill, under the frame (NewInWindow inserts right after health_*).
                        var stagger = NewInWindow(health, StaggerName);
                        Art.Note(stagger, "enemy.stagger", true, "stagger line (bottom inner edge of the window: gold building, pale while staggered)");
                        stagger.enabled = false;
                        var shield = NewInWindow(health, ShieldName);
                        shield.color = ThemeTokens.GetColor("color.enemy.shield", new Color(0.31f, 0.88f, 0.9f, 0.6f));
                        Art.Note(shield, "enemy.shield", true, "enemy shield band (upper share of the window)");
                        shield.enabled = false;
                        if (!template.GetComponent<CanvasGroup>())
                        {
                            var group = template.AddComponent<CanvasGroup>();
                            group.interactable = false;
                            group.blocksRaycasts = false;
                        }
                        ClearName(t, health, lift);
                    }
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

        // Bar width to the token (spec 2: 96 ref px). GuiBar caches its m_bar width at the first SetValue
        // (GuiBar.cs:63-69), so this runs on the template before any clone exists: every non-stretched descendant scales
        // its width and x position with the Health rect.
        private static void Widen(RectTransform health, float target)
        {
            var old = health.rect.width;
            if (!(target > 0f) || old < 1f || Mathf.Abs(target - old) < 0.5f) return;
            var k = target / old;
            Plugin.Log.LogInfo($"[RuneSkin] enemy HUD Health width {old:0.#} -> {target:0.#}");
            if (Mathf.Approximately(health.anchorMin.x, health.anchorMax.x)) health.sizeDelta = new Vector2(target, health.sizeDelta.y);
            else
            {
                var delta = target - old;
                health.offsetMin -= new Vector2(delta * health.pivot.x, 0f);
                health.offsetMax += new Vector2(delta * (1f - health.pivot.x), 0f);
            }
            foreach (var rt in health.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt == health || !Mathf.Approximately(rt.anchorMin.x, rt.anchorMax.x)) continue;
                rt.sizeDelta = new Vector2(rt.sizeDelta.x * k, rt.sizeDelta.y);
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x * k, rt.anchoredPosition.y);
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

        private static int AfterBars(RectTransform health)
        {
            var after = -1;
            for (var i = 0; i < health.childCount; i++)
                if (health.GetChild(i).name.StartsWith("health_", StringComparison.Ordinal)) after = i;
            return after + 1;
        }

        private static Image NewFrame(RectTransform health)
        {
            var go = new GameObject(FrameName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(health, false);
            rect.SetSiblingIndex(AfterBars(health));
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.fillCenter = false;
            return img;
        }

        // A flat-colour strip inside the bar window (bottom-left pivot, sized per frame), right after the health bars.
        private static Image NewInWindow(RectTransform health, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(health, false);
            rect.SetSiblingIndex(AfterBars(health));
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.sprite = null;
            return img;
        }

        // A Simple strip cut from the enemy-b mockup drawn untinted (its own colours / bevel), stretched to the image rect.
        private static bool MockupStrip(Image img, string token, string reason)
        {
            if (!Art.Reskin(img, token, null, reason)) return false;
            img.type = Image.Type.Simple;
            img.color = Color.white;
            return true;
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

        // The enemy name (and star rows) sit above the frame top (socket included): moved up until their rect bottom clears it by
        // size.enemy.name.gap.
        private static void ClearName(Transform hud, RectTransform health, float frameTop)
        {
            var top = health.rect.yMax + frameTop + ThemeTokens.GetFloat("size.enemy.name.gap", 2f);
            var topInHud = hud.InverseTransformPoint(health.TransformPoint(new Vector3(0f, top, 0f))).y;
            var name = hud.Find("Name") as RectTransform;
            if (!name) return;
            var bottom = hud.InverseTransformPoint(name.TransformPoint(new Vector3(0f, name.rect.yMin, 0f))).y;
            var up = topInHud - bottom;
            if (Mathf.Abs(up) < 0.25f) return;
            for (var i = 0; i < hud.childCount; i++)
            {
                var c = hud.GetChild(i) as RectTransform;
                if (c && (c == name || c.name.StartsWith("level_", StringComparison.Ordinal))) c.anchoredPosition += new Vector2(0f, up);
            }
        }

        private static State Get(EnemyHud.HudData data)
        {
            if (States.TryGetValue(data, out var s)) return s;
            s = new State
            {
                Data = data,
                Health = data.m_gui.transform.Find("Health") as RectTransform,
                Boss = data.m_character && data.m_character.IsBoss(),
                Group = data.m_gui.GetComponent<CanvasGroup>(),
                Name = data.m_name,
            };
            if (s.Health)
            {
                s.Own = Child(s.Health, FrameName);
                s.Shield = Child(s.Health, ShieldName);
                s.Stagger = Child(s.Health, StaggerName);
            }
            if (s.Group) s.Alpha = s.Group.alpha;
            States.Add(data, s);
            return s;
        }

        private static Image Child(Transform parent, string name)
        {
            var t = parent.Find(name);
            return t ? t.GetComponent<Image>() : null;
        }

        // A counted HUD follows the Smart rule; players, bosses, mounts, tamed and friendly creatures stay vanilla.
        private static bool Counted(EnemyHud.HudData data, Player player)
        {
            var c = data.m_character;
            return !data.m_isMount && !c.IsPlayer() && !c.IsBoss() && !c.IsTamed() && BaseAI.IsEnemy(player, c);
        }

        // UpdateHuds prefix (Smart): scores, picks and hands vanilla its own switch (m_hoverTimer, EnemyHud.cs:186-190):
        // 0 = active this frame (picked or still fading out), 99999 (the HudData default) = inactive.
        internal static void BeforeUpdate(EnemyHud hud)
        {
            if (!EnemyHudConfig.Smart) return;
            var player = Player.m_localPlayer;
            if (!player) return;
            var now = Time.unscaledTime;
            var hover = player.GetHoverCreature();
            var pos = player.transform.position;
            var linger = EnemyHudConfig.Linger.Value;
            var engaged = EnemyHudConfig.Engaged.Value;
            var threat = EnemyHudConfig.ThreatRange.Value;
            var n = 0;
            foreach (var pair in hud.m_huds)
            {
                var data = pair.Value;
                if (data == null || !data.m_gui || !data.m_character) continue;
                var s = Get(data);
                s.Managed = s.Group && Counted(data, player);
                if (!s.Managed) continue;
                var c = data.m_character;
                if (c == hover) s.AimedAt = now;
                s.Distance = Vector3.Distance(pos, c.transform.position);
                var alerted = data.m_ai && data.m_ai.IsAlerted();
                var targets = data.m_ai is MonsterAI monster && monster.GetTargetCreature() == player;
                var score = EnemyHudMath.Score(s.AimedAt < 0f ? -1f : now - s.AimedAt, linger,
                    EnemyEngagement.Since(c, now, engaged), engaged, alerted, s.Distance, threat, targets, out var reason);
                s.Picked = false;
                s.Reason = reason;
                if (n >= MaxCandidates) continue;
                Scores[n] = score;
                Reasons[n] = reason;
                Ids[n] = n;
                Candidates[n] = s;
                n++;
            }
            EnemyHudMath.Pick(Scores, Reasons, Ids, n, EnemyHudConfig.Max.Value, EnemyHudConfig.Threats.Value, Picked);
            for (var i = 0; i < n; i++)
            {
                var s = Candidates[Ids[i]];
                s.Picked = Picked[i];
                s.Data.m_hoverTimer = s.Picked || s.Alpha > 0.001f ? 0f : 99999f;
                Candidates[Ids[i]] = null;
            }
        }

        // After Vaettir's UpdateHuds postfix: its VT_Frame keeps the tier, RuneSkin draws the tier art; then alpha,
        // distance scale, name, shield band and stagger line.
        internal static void Update(EnemyHud hud)
        {
            var now = Time.unscaledTime;
            var dt = Time.unscaledDeltaTime;
            var smart = EnemyHudConfig.Smart;
            var low = ThemeTokens.GetFloat("alpha.enemy.hud.low", 0.75f);
            var fadeIn = ThemeTokens.GetFloat("time.enemy.hud.fade.in", 0.12f);
            var fadeOut = ThemeTokens.GetFloat("time.enemy.hud.fade.out", 0.35f);
            foreach (var pair in hud.m_huds)
            {
                var data = pair.Value;
                if (data == null || !data.m_gui || !data.m_character) continue;
                var s = Get(data);
                if (!s.Health) continue;
                Tier(s, now);

                var managed = smart && s.Managed;
                var target = managed ? EnemyHudMath.TargetAlpha(s.Picked, s.Reason, low) : 1f;
                var alpha = managed ? EnemyHudMath.FadeStep(s.Alpha, target, dt, fadeIn, fadeOut) : 1f;
                // Only bars this rule manages (or managed until now, e.g. just tamed) are written: other groups belong to
                // their owners (BossAwakening fades the vanilla boss GUI of its world-bar boss through one, BossBarAttach).
                if (s.Group && (managed || s.WasManaged) &&
                    (Mathf.Abs(alpha - s.Group.alpha) > 0.004f || (alpha != s.Group.alpha && (alpha <= 0f || alpha >= 1f))))
                    s.Group.alpha = alpha;
                s.WasManaged = managed;
                s.Alpha = alpha;

                var showName = !managed || (s.Picked && s.Reason >= EnemyHudMath.Reason.Engaged);
                if (s.Name && showName != s.NameShown)
                {
                    s.Name.enabled = showName;
                    s.NameShown = showName;
                }

                if (managed && !s.Boss)
                {
                    var k = EnemyHudMath.DistanceScale(s.Distance, ThemeTokens.GetFloat("dist.enemy.hud.near", 6f),
                        ThemeTokens.GetFloat("dist.enemy.hud.far", 20f), ThemeTokens.GetFloat("scale.enemy.hud.far", 0.8f));
                    if (Mathf.Abs(k - s.ScaleK) > 0.001f)
                    {
                        s.ScaleK = k;
                        data.m_gui.transform.localScale = new Vector3(_baseScale * k, _baseScale * k, 1f);
                    }
                }

                if (s.Boss || alpha <= 0f || !data.m_gui.activeSelf) continue;
                ShieldBand(s, data.m_character);
                StaggerLine(s, data.m_character, dt);
            }
        }

        // Vaettir tier art: frame (with its socket) redrawn when its sprite changes. HUDs without a VT_Frame (players, tamed,
        // Vaettir off) look for it again at most once a second (audit P2: Find per frame).
        private static void Tier(State s, float now)
        {
            if (!s.Vt && now >= s.VtRetryAt)
            {
                s.VtRetryAt = now + VtRetrySeconds;
                s.Vt = Child(s.Health, VaettirFrame);
            }
            var vtOn = s.Vt && s.Vt.enabled && s.Vt.sprite;
            if (vtOn && s.Vt.sprite != s.VtSet)
            {
                SetFrame(s.Vt, s.Health, EnemyHudMath.TierToken(s.Vt.sprite.name, s.Boss), s.Boss);
                s.VtSet = s.Vt.sprite;
            }
            if (s.Own && s.Own.enabled == vtOn) s.Own.enabled = !vtOn && s.Own.sprite;
        }

        // Shield band (spec 4): ZDO mt_es / mt_es_max on the creature when published, else the owner's SE_Shield
        // remaining absorb (SE_Shield.cs:23-57); width = shield / max HP of the window, upper share.hud.shield.band.
        private static void ShieldBand(State s, Character c)
        {
            if (!s.Shield) return;
            var shield = 0f;
            var zdo = c.m_nview ? c.m_nview.GetZDO() : null;
            if (zdo != null && zdo.GetFloat(EsMaxHash) > 0f) shield = Mathf.Max(0f, zdo.GetFloat(EsHash));
            else if (c.m_nview && c.m_nview.IsOwner()) shield = VanillaShield(c);
            var r = s.Health.rect;
            HudMath.ShieldBand(shield, c.GetMaxHealth(), r.width, r.height, ThemeTokens.GetFloat("share.enemy.shield.band", 0.3f),
                out var x, out var y, out var w, out var h);
            if (Mathf.Abs(w - s.ShieldW) < 0.5f) return;
            s.ShieldW = w;
            s.Shield.enabled = w >= 0.5f;
            var rect = s.Shield.rectTransform;
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(w, h);
        }

        internal static float VanillaShield(Character c)
        {
            var sum = 0f;
            foreach (var se in c.GetSEMan().GetStatusEffects())
                if (se is SE_Shield shield) sum += Mathf.Max(0f, shield.m_totalAbsorbDamage - shield.m_damage);
            return sum;
        }

        // Stagger line (spec 5): build-up = GetStaggerPercentage on the owner (Character.cs:2603) else ZDO mt_stg, while
        // staggering the remaining animation fraction (1 - normalizedTime of the "stagger" tagged state, Character.cs:3735).
        private static void StaggerLine(State s, Character c, float dt)
        {
            if (!s.Stagger) return;
            var can = c.m_staggerDamageFactor > 0f;
            var build = -1f;
            if (can)
            {
                if (c.m_nview && c.m_nview.IsOwner()) build = c.GetStaggerPercentage();
                else
                {
                    var zdo = c.m_nview ? c.m_nview.GetZDO() : null;
                    if (zdo != null) build = zdo.GetFloat(StgHash, -1f);
                }
            }
            var staggering = can && c.IsStaggering();
            s.Stg = EnemyHudMath.StaggerStep(s.Stg, can, build, staggering, staggering ? StaggerRemaining(c) : 0f, dt);
            var st = s.Stg;
            var shown = st.Phase != EnemyHudMath.StaggerPhase.Hidden;
            if (st.Phase != s.ShownPhase || Mathf.Abs(st.Flash - s.ShownFlash) > 0.01f)
            {
                s.ShownPhase = st.Phase;
                s.ShownFlash = st.Flash;
                if (s.Stagger.enabled != shown) s.Stagger.enabled = shown;
                if (shown)
                {
                    var color = st.Phase == EnemyHudMath.StaggerPhase.Active
                        ? ThemeTokens.GetColor("color.enemy.stagger.active", new Color(1f, 0.945f, 0.79f, 1f))
                        : ThemeTokens.GetColor("color.enemy.stagger", new Color(0.91f, 0.71f, 0.29f, 0.85f));
                    if (st.Flash > 0f) color = Color.Lerp(color, Color.white, st.Flash / 0.2f);
                    s.Stagger.color = color;
                }
            }
            if (!shown) return;
            var r = s.Health.rect;
            var w = r.width * st.Fill;
            if (Mathf.Abs(w - s.StaggerW) < 0.25f) return;
            s.StaggerW = w;
            var rect = s.Stagger.rectTransform;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(w, Mathf.Max(1f, r.height * ThemeTokens.GetFloat("share.enemy.stagger", 0.28f)));
        }

        private static float StaggerRemaining(Character c)
        {
            var a = c.m_animator;
            if (!a || !a.isActiveAndEnabled) return 1f;
            var tag = Character.s_animatorTagStagger;
            if (a.IsInTransition(0))
            {
                var next = a.GetNextAnimatorStateInfo(0);
                if (next.tagHash == tag) return EnemyHudMath.StaggerRemaining(next.normalizedTime);
            }
            var cur = a.GetCurrentAnimatorStateInfo(0);
            return cur.tagHash == tag ? EnemyHudMath.StaggerRemaining(cur.normalizedTime) : 1f;
        }

        public static void Record()
        {
            if (_root) SkinDiagnostics.Record(Art.Report(_root));
        }

        // Test hook (AutotestDriver by reflection): one line per HUD.
        public static string DebugState()
        {
            var hud = EnemyHud.instance;
            if (!hud) return "no EnemyHud";
            var sb = new StringBuilder();
            sb.Append("mode=").Append(EnemyHudConfig.Mode.Value).Append(" range=").Append(hud.m_maxShowDistance.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
            foreach (var pair in hud.m_huds)
            {
                var data = pair.Value;
                if (data == null || !data.m_gui || !data.m_character || !States.TryGetValue(data, out var s)) continue;
                sb.Append('\n').Append(data.m_character.GetZDOID().ToString()).Append(' ').Append(data.m_character.name)
                    .Append(" managed=").Append(s.Managed ? 1 : 0).Append(" picked=").Append(s.Picked ? 1 : 0)
                    .Append(" reason=").Append(s.Reason).Append(" active=").Append(data.m_gui.activeSelf ? 1 : 0)
                    .Append(" alpha=").Append(s.Alpha.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(" dist=").Append(s.Distance.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(" scale=").Append(data.m_gui.transform.localScale.x.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(" name=").Append(s.NameShown ? 1 : 0)
                    .Append(" frame=").Append(s.Vt && s.Vt.enabled && s.Vt.sprite ? s.Vt.sprite.name : s.Own && s.Own.enabled && s.Own.sprite ? s.Own.sprite.name : "-")
                    .Append(" shieldW=").Append((s.Shield && s.Shield.enabled ? s.Shield.rectTransform.sizeDelta.x : 0f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(" barW=").Append(s.Health.rect.width.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(" stg=").Append(s.Stg.Phase).Append(':').Append(s.Stg.Fill.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(" stgW=").Append((s.Stagger && s.Stagger.enabled ? s.Stagger.rectTransform.sizeDelta.x : 0f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }

    // Local-player engagements (spec 1 "Engaged"): ZDOID -> unscaled time of the last hit either way. Entries older than
    // the window are dropped when read; cleared on every EnemyHud.Awake (world load).
    internal static class EnemyEngagement
    {
        private static readonly Dictionary<ZDOID, float> Last = new Dictionary<ZDOID, float>();

        internal static void Mark(Character c)
        {
            if (!c || c.IsPlayer()) return;
            var id = c.GetZDOID();
            if (id.IsNone()) return;
            Last[id] = Time.unscaledTime;
        }

        internal static float Since(Character c, float now, float window)
        {
            if (Last.Count == 0) return -1f;
            var id = c.GetZDOID();
            if (!Last.TryGetValue(id, out var t)) return -1f;
            var age = now - t;
            if (age <= window) return age;
            Last.Remove(id);
            return -1f;
        }

        internal static void Clear() => Last.Clear();

        internal static int Count => Last.Count;
    }

    public enum EnemyBarMode { Smart, Vanilla }

    // Config of the overhead enemy bars (numbers of hud-design spec 1; read live).
    public static class EnemyHudConfig
    {
        public static ConfigEntry<EnemyBarMode> Mode { get; private set; }
        public static ConfigEntry<int> Max { get; private set; }
        public static ConfigEntry<int> Threats { get; private set; }
        public static ConfigEntry<float> Linger { get; private set; }
        public static ConfigEntry<float> Engaged { get; private set; }
        public static ConfigEntry<float> ThreatRange { get; private set; }
        public static ConfigEntry<float> Range { get; private set; }

        public static bool Smart => Mode != null && Mode.Value == EnemyBarMode.Smart;

        internal static void Bind(ConfigFile config)
        {
            Mode = config.Bind("Hud", "EnemyBars", EnemyBarMode.Smart,
                "Smart = overhead enemy bars only for the aimed enemy (+ linger), enemies you fought recently and the nearest alerted threats, capped; Vanilla = every hovered enemy for 60 s (vanilla rule).");
            Max = config.Bind("Hud", "EnemyBarsMax", 4, new ConfigDescription("Smart: most overhead enemy bars at once (the aimed one always counts first).", new AcceptableValueRange<int>(1, 16)));
            Threats = config.Bind("Hud", "EnemyBarsThreats", 2, new ConfigDescription("Smart: most bars shown only because an enemy is alerted nearby.", new AcceptableValueRange<int>(0, 16)));
            Linger = config.Bind("Hud", "EnemyBarsLinger", 1.5f, new ConfigDescription("Smart: seconds the aimed enemy's bar stays after the crosshair leaves it.", new AcceptableValueRange<float>(0f, 10f)));
            Engaged = config.Bind("Hud", "EnemyBarsEngaged", 8f, new ConfigDescription("Smart: seconds an enemy's bar stays after you hit it or it hit you (refreshed by every hit).", new AcceptableValueRange<float>(0f, 60f)));
            ThreatRange = config.Bind("Hud", "EnemyBarsThreatRange", 15f, new ConfigDescription("Smart: metres within which an alerted enemy counts as a threat.", new AcceptableValueRange<float>(0f, 40f)));
            Range = config.Bind("Hud", "EnemyBarsRange", 20f, new ConfigDescription("Smart: creation radius of overhead bars in metres (vanilla 10; bars shrink to 0.8 at 20 m). Applied on world load.", new AcceptableValueRange<float>(10f, 40f)));
        }
    }

    // Family "EnemyHud" hooks.
    [HarmonyPatch]
    public static class EnemyHudAdapter
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly), batched by FastStartup).
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.EnemyHud);

        [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Awake_Postfix(EnemyHud __instance)
        {
            try { EnemyHudSkin.Attach(__instance); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] enemy HUD restyle failed (vanilla bars stay): {e}"); }
        }

        [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.UpdateHuds))]
        [HarmonyPrefix]
        private static void UpdateHuds_Prefix(EnemyHud __instance)
        {
            try { EnemyHudSkin.BeforeUpdate(__instance); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] enemy HUD visibility pass failed: {e}"); }
        }

        [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.UpdateHuds))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(ModCompat.BossAwakeningGuid, ModCompat.MtGuid, ModCompat.StarLevelSystemGuid)]
        private static void UpdateHuds_Postfix(EnemyHud __instance)
        {
            try { EnemyHudSkin.Update(__instance); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] enemy HUD frame pass failed: {e}"); }
        }

        // Engaged: the local player hit it (runs on the attacker's client, Character.cs:2232) ...
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        [HarmonyPostfix]
        private static void Damage_Postfix(Character __instance, HitData hit)
        {
            if (hit != null && Player.m_localPlayer && hit.m_attacker == Player.m_localPlayer.GetZDOID()) EnemyEngagement.Mark(__instance);
        }

        // ... or it hit the local player (ApplyDamage runs on the owner = the local player, Character.cs:2417).
        [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
        [HarmonyPostfix]
        private static void ApplyDamage_Postfix(Character __instance, HitData hit)
        {
            if (hit == null || __instance != Player.m_localPlayer) return;
            var attacker = hit.GetAttacker();
            if (attacker) EnemyEngagement.Mark(attacker);
        }
    }
}
