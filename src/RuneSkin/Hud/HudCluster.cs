using System.Collections.Generic;
using RuneSkin.Api;
using RuneSkin.API;
using RuneSkin.Diagnostics;
using RuneSkin.Responsive;
using RuneSkin.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.PlayerHud
{
    // RuneSkin's player HUD (family "Hud", hud-resource-states.md): bottom-left cluster in the safe area (or the
    // corners of a centred 16:9 box on ultrawide), top-down: food row (+ EAQS quick slots, guardian power) -> ES rail
    // -> health -> stamina -> adrenaline rail -> eitr / conversion plaque -> status strip. Lives under Hud.m_rootObject,
    // so Hud.SetVisible / the user hide toggle (Hud.cs:436) apply; one nested Canvas isolates its rebuilds.
    // The vanilla bars keep running untouched (no transpilers) inside a CanvasGroup wrapper at alpha 0.
    // Values are sampled in LateUpdate, after Hud.Update ran this frame (Hud.cs:467-543); providers add the
    // reservation / conversion / energy-shield data (Skin.RegisterResourceProvider).
    public sealed class HudCluster : MonoBehaviour
    {
        public static HudCluster Instance { get; private set; }

        private Hud _hud;
        private Canvas _canvas;
        private HudArt _art;
        private RectTransform _root;
        private BarView _es, _health, _stamina, _adrenaline, _eitr;
        private RectTransform _plaque;
        private TMPro.TMP_Text _plaqueText;
        private string _plaqueShown;
        private FoodRow _food;
        private StatusStrip _strip;
        private readonly List<StatusEffect> _hudEffects = new List<StatusEffect>(16);
        private bool _fed;
        private readonly List<KeyValuePair<int, float>> _pending = new List<KeyValuePair<int, float>>(4);
        private readonly HudMath.Stripe[] _stripes = new HudMath.Stripe[BarView.MaxStripes];
        private readonly int[] _stripeKinds = new int[BarView.MaxStripes];
        private readonly Vector3[] _corners = new Vector3[4];
        private readonly List<RectTransform> _rects = new List<RectTransform>(32);
        private Transform _quickBar;
        private float _quickSearchAt;
        private CanvasGroup _hotbarGroup;
        private Transform _hotbar;
        private Player _player;
        private bool _wasBroken;
        private float _breakPulse;

        public static void Attach(Hud hud)
        {
            if (Instance) return;
            var go = new GameObject("RuneSkinHud", typeof(RectTransform));
            go.transform.SetParent(hud.m_rootObject.transform, false);
            var c = go.AddComponent<HudCluster>();
            c._hud = hud;
            c.Build();
        }

        private void Build()
        {
            Instance = this;
            _art = new HudArt();
            _root = (RectTransform)transform;
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = Vector2.zero;
            _canvas = gameObject.AddComponent<Canvas>(); // nested: own rebuild batch, inherits sorting
            _canvas.overrideSorting = false;
            gameObject.AddComponent<GraphicRaycaster>(); // "+N" cell
            BuildViews();
            Suppress();
            _hotbar = _hud.GetComponentInChildren<HotkeyBar>(true)?.transform;
            if (_hotbar && !_hotbar.TryGetComponent(out _hotbarGroup))
                _hotbarGroup = _hotbar.gameObject.AddComponent<CanvasGroup>();
            Plugin.Log.LogInfo("[RuneSkin] HUD cluster built");
        }

        private int _themeRevision = -1;

        // Theme reload (runeskin_reload): every HUD graphic is ours, so the views are rebuilt from the new tokens
        // (sprites, colours, sprite-derived geometry); the vanilla suppression stays.
        private void RebuildViews()
        {
            foreach (Transform c in _root) Destroy(c.gameObject);
            _art = new HudArt();
            BuildViews();
            foreach (var r in _suppressedRoots) _art.Suppressed(r, "vanilla HUD part (RuneSkin draws its own; vanilla code keeps running)");
        }

        private void BuildViews()
        {
            _themeRevision = ThemeTokens.Revision;
            _childrenOn = true;
            _plaqueShown = null;
            _strip = new StatusStrip(_art, _root);
            _eitr = new BarView(_art, _root, "Eitr", false, "sprite.hud.bar.frame.slim", "color.hud.eitr");
            _adrenaline = new BarView(_art, _root, "Adrenaline", true, "sprite.hud.rail.frame.adrenaline", "color.hud.adrenaline");
            _stamina = new BarView(_art, _root, "Stamina", false, "sprite.hud.bar.frame.slim", "color.hud.stamina");
            _health = new BarView(_art, _root, "Health", false, "sprite.hud.bar.frame", "color.hud.health", 1f, mainValue: true);
            _es = new BarView(_art, _root, "EnergyShield", true, "sprite.hud.rail.frame.es", "color.hud.es");
            _food = new FoodRow(_art, _root);
            _plaque = HudArt.Rect("ConversionPlaque", _root);
            var seal = _art.Image("Seal", _plaque, "sprite.hud.plaque.seal", "color.frame", Image.Type.Sliced);
            seal.rectTransform.anchorMin = Vector2.zero;
            seal.rectTransform.anchorMax = Vector2.one;
            seal.rectTransform.offsetMin = seal.rectTransform.offsetMax = Vector2.zero;
            _plaqueText = _art.Text("Label", _plaque, "font.display", "color.text.accent", ThemeTokens.GetFloat("size.hud.font.number", 16f), TMPro.TextAlignmentOptions.Center);
            _plaqueText.rectTransform.anchorMin = Vector2.zero;
            _plaqueText.rectTransform.anchorMax = Vector2.one;
            _plaqueText.rectTransform.offsetMin = new Vector2(20f, 0f);
            _plaqueText.rectTransform.offsetMax = Vector2.zero;
            _plaque.gameObject.SetActive(false);
        }

        private readonly List<Transform> _suppressedRoots = new List<Transform>();

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // Vanilla Hud.UpdateStatusEffects input (prefix in HudAdapter; the vanilla body is not run while we own it).
        public void Feed(List<StatusEffect> effects)
        {
            _hudEffects.Clear();
            _hudEffects.AddRange(effects);
            _fed = true;
        }

        // ---- vanilla suppression ---------------------------------------------------------------------------------

        // Each vanilla root moves into a stretched wrapper with the parent's rect and pivot (same coordinate space, so
        // vanilla's per-frame anchoredPosition writes, Hud.cs:1112-1116/:1181-1185, land as before) under a
        // CanvasGroup at alpha 0. Animators keep animating their own groups inside it.
        private void Suppress()
        {
            var roots = new[] { _hud.m_healthPanel, _hud.m_staminaBar2Root, _hud.m_adrenalineBarRoot, _hud.m_eitrBarRoot, _hud.m_statusEffectListRoot };
            var wrappers = new Dictionary<Transform, Transform>();
            foreach (var r in roots)
            {
                if (!r) continue;
                var parent = r.parent;
                if (!parent) continue;
                if (parent.GetComponent<LayoutGroup>())
                {
                    // A layout group would re-flow without the child; hide in place instead.
                    if (!r.TryGetComponent<CanvasGroup>(out var g)) g = r.gameObject.AddComponent<CanvasGroup>();
                    g.alpha = 0f;
                    g.blocksRaycasts = false;
                    _art.Suppressed(r, "vanilla HUD part hidden in place (parent has a LayoutGroup); RuneSkin draws its own");
                    continue;
                }
                if (!wrappers.TryGetValue(parent, out var w))
                {
                    var pr = (RectTransform)parent;
                    var wr = HudArt.Rect("RuneSkinVanillaHidden", parent);
                    wr.anchorMin = Vector2.zero;
                    wr.anchorMax = Vector2.one;
                    wr.offsetMin = wr.offsetMax = Vector2.zero;
                    wr.pivot = pr.pivot;
                    wr.localPosition = Vector3.zero;
                    wr.SetSiblingIndex(r.GetSiblingIndex());
                    var group = wr.gameObject.AddComponent<CanvasGroup>();
                    group.alpha = 0f;
                    group.blocksRaycasts = false;
                    group.interactable = false;
                    wrappers[parent] = w = wr;
                }
                r.SetParent(w, false);
                foreach (var cg in r.GetComponentsInChildren<CanvasGroup>(true))
                    if (cg.ignoreParentGroups)
                        Plugin.Log.LogWarning($"[RuneSkin] HUD: {SkinDiagnostics.PathOf(cg.transform, _hud.transform)} ignores parent groups (stays visible)");
                _art.Suppressed(r, "vanilla HUD part (RuneSkin draws its own; vanilla code keeps running)");
                _suppressedRoots.Add(r);
            }
        }

        // ---- frame ---------------------------------------------------------------------------------------------

        private void LateUpdate()
        {
            var player = Player.m_localPlayer;
            // Hud.SetVisible hides by moving m_rootObject off screen (Hud.cs:436-448): never place against that.
            var visible = player && !player.IsDead() && _hud.IsVisible() && _hud.m_healthPanel && _hud.m_healthPanel.localScale.x > 0f;
            if (_themeRevision != ThemeTokens.Revision) RebuildViews();
            if (_canvas) _art.SyncPpu(_canvas.referencePixelsPerUnit);
            if (_player != player)
            {
                _player = player;
            }
            SetChildrenActive(visible);
            HotbarVisibility();
            if (!visible) return;
            var dt = Time.deltaTime;
            StatusKeys();
            Sample(player, dt);
            ArrangeAndPlace(player);
            _strip.Tick(dt);
            if (Time.frameCount % 300 == 0) Record();
        }

        private bool _childrenOn = true;

        private void SetChildrenActive(bool on)
        {
            if (_childrenOn == on) return;
            _childrenOn = on;
            foreach (Transform c in _root) c.gameObject.SetActive(on);
        }

        private void HotbarVisibility()
        {
            if (!_hotbarGroup) return;
            var a = InventoryGui.IsVisible() ? 0f : 1f;
            if (!Mathf.Approximately(_hotbarGroup.alpha, a)) _hotbarGroup.alpha = a;
        }

        private void StatusKeys()
        {
            if (Chat.instance && Chat.instance.HasFocus() || global::Console.IsVisible() || TextInput.IsVisible()) return;
            var key = Plugin.StatusListKey?.Value ?? default;
            var pad = Plugin.StatusListButton?.Value;
            var pressed = key.MainKey != KeyCode.None && key.IsDown();
            if (!pressed && !string.IsNullOrEmpty(pad) && ZInput.IsGamepadActive() && ZInput.GetButton("JoyAltKeys") && ZInput.GetButtonDown(pad))
                pressed = true;
            if (pressed) _strip.TogglePopup();
            else if (_strip.PopupOpen && (ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyBack"))) _strip.ClosePopup();
        }

        private void Sample(Player p, float dt)
        {
            // One outer width for every main bar (mockup hud-status-b; the max is the number, not the length).
            var outerW = ThemeTokens.GetFloat("size.hud.bar.outer", 438f);

            // Energy shield (mod pool): rail above health, or the main bar while it carries the player.
            var esOn = Providers.TryResource(ResourcePools.EnergyShield, p, true, out var es) && es.Max > 0f;
            var esMain = esOn && es.MainBar;

            // Health.
            var hp = p.GetHealth();
            var hpC = p.GetMaxHealth();
            var hpB = Extension(ResourcePools.Health, p, hpC, out var hpRes);
            CollectDots(p, hp);
            // Health fill stays red: effects show only as pending-DoT stripes, frame overlays and strip icons.
            FrameOverlayOf(p, out var overlaySprite, out var overlayColor);
            if (esMain)
            {
                _health.SetOuterWidth(outerW);
                _health.Tick(dt);
                _health.Render(es.Current, es.Max, es.Available, _stripes, 0, _stripeKinds, "color.hud.es", overlaySprite, overlayColor, es.Inert, dt, es.Available < es.Max - 0.5f);
                _health.Aux(0);
            }
            else
            {
                _health.SetOuterWidth(outerW);
                _health.Tick(dt);
                var count = HudMath.Stripes(_pending, hp, hpB, _health.WindowLength, ThemeTokens.GetFloat("size.hud.stripe.min", 4f), BarView.MaxStripes, _stripes);
                for (var i = 0; i < count; i++) _stripeKinds[i] = _stripes[i].Key < 0 ? (int)DotKind.Other : _stripes[i].Key;
                _health.Render(hp, hpB, hpC, _stripes, count, _stripeKinds, null, overlaySprite, overlayColor, false, dt, true); // "54/100" (mockup)
            }

            // ES rail: same outer width as the health frame (rail window = health window + inset difference).
            var railOn = esOn && !esMain;
            _es.SetVisible(railOn);
            if (railOn)
            {
                _es.SetOuterWidth(outerW);
                _es.Tick(dt);
                var broken = es.Phase == ResourcePhase.Broken;
                if (broken && !_wasBroken) _breakPulse = ThemeTokens.GetFloat("time.hud.break.pulse", 0.35f);
                _wasBroken = broken;
                if (_breakPulse > 0f) _breakPulse -= dt;
                var dim = es.Phase == ResourcePhase.RechargeDelay || broken;
                _es.Render(es.Current, es.Max, es.Available, _stripes, 0, _stripeKinds,
                    _breakPulse > 0f ? "color.text.accent" : dim ? "color.hud.es.dim" : null, null, null, es.Inert, dt, false);
                _es.Aux(es.Phase == ResourcePhase.RechargeDelay ? Mathf.CeilToInt(es.PhaseSeconds) : 0);
            }

            // Stamina (+ adrenaline rail riding on it, Hud.cs:1147-1160: vanilla hides it at 0).
            var st = p.GetStamina();
            var stC = p.GetMaxStamina();
            var stB = Extension(ResourcePools.Stamina, p, stC, out var stRes);
            _stamina.SetOuterWidth(outerW);
            _stamina.Tick(dt);
            _stamina.Render(st, stB, stC, _stripes, 0, _stripeKinds, null, null, null, false, dt, stRes);
            var adMax = p.GetMaxAdrenaline();
            var ad = p.GetAdrenaline();
            var adOn = adMax > 0f && ad > 0f;
            _adrenaline.SetVisible(adOn);
            if (adOn)
            {
                _adrenaline.SetOuterWidth(outerW);
                _adrenaline.Tick(dt);
                _adrenaline.Render(ad, adMax, adMax, _stripes, 0, _stripeKinds, null, null, null, false, dt, false);
            }

            // Eitr: vanilla hides it at max 0 (no plaque); a chosen conversion collapses it to the sealed plaque.
            var ei = p.GetEitr();
            var eiC = p.GetMaxEitr();
            Providers.TryResource(ResourcePools.Eitr, p, false, out var eiState);
            var converted = eiState.Valid && !string.IsNullOrEmpty(eiState.ConversionTarget);
            var eiB = Extension(ResourcePools.Eitr, p, eiC, out var eiRes);
            var eitrOn = !converted && eiB > 0f;
            _eitr.SetVisible(eitrOn);
            if (eitrOn)
            {
                _eitr.SetOuterWidth(outerW);
                _eitr.Tick(dt);
                _eitr.Render(ei, eiB, eiC, _stripes, 0, _stripeKinds, null, null, null, false, dt, eiRes);
                _eitr.Paused(eiState.Valid && eiState.RegenBlocked);
            }
            Plaque(converted ? eiState.ConversionTarget : null);
        }

        // B for a vanilla pool: the provider's pre-reservation max when its C matches the game's max, else C.
        private static float Extension(string pool, Player p, float gameMax, out bool reserved)
        {
            reserved = false;
            if (!Providers.TryResource(pool, p, false, out var s)) return gameMax;
            if (Mathf.Abs(s.Available - gameMax) > 0.5f + 0.01f * gameMax) return gameMax; // stale / inconsistent
            reserved = s.Max > gameMax + 0.5f;
            return Mathf.Max(gameMax, s.Max);
        }

        private void Plaque(string target)
        {
            var on = target != null;
            if (_plaque.gameObject.activeSelf != on) _plaque.gameObject.SetActive(on);
            if (!on || target == _plaqueShown) return;
            _plaqueShown = target;
            var key = target == ResourcePools.Health ? "$runeskin_eitr_to_hp" : target == "shield" || target == ResourcePools.EnergyShield ? "$runeskin_eitr_to_shield" : null;
            var text = key != null && Localization.instance != null ? Localization.instance.Localize(key) : null;
            if (string.IsNullOrEmpty(text) || text.StartsWith("[", System.StringComparison.Ordinal))
                text = target == ResourcePools.Health ? "EITR → HP" : "EITR → SHIELD";
            _plaqueText.text = text;
        }

        // Pending damage per kind in fixed stripe order Poison, Fire, Spirit, Other (all active effects, not only
        // the HUD list: iconless effects still hurt, SEMan.cs:328-335).
        private readonly float[] _kinds = new float[5];

        private void CollectDots(Player p, float hp)
        {
            for (var i = 0; i < _kinds.Length; i++) _kinds[i] = 0f;
            foreach (var se in p.GetSEMan().GetStatusEffects())
            {
                if (!se) continue;
                var v = StatusSource.Pending(se, out var kind, out var spirit);
                if (v > 0f) _kinds[(int)kind] += v;
                if (spirit > 0f) _kinds[(int)DotKind.Spirit] += spirit;
            }
            _pending.Clear();
            for (var k = (int)DotKind.Poison; k <= (int)DotKind.Other; k++)
                if (_kinds[k] > 0.05f) _pending.Add(new KeyValuePair<int, float>(k, _kinds[k]));
        }

        // One primary frame overlay from the active effects.
        private static void FrameOverlayOf(Player p, out string overlaySprite, out string overlayColor)
        {
            var overlay = FrameOverlay.None;
            var overlayPrio = int.MinValue;
            foreach (var se in p.GetSEMan().GetStatusEffects())
            {
                if (!se) continue;
                var c = StatusSource.Classify(se, out _);
                if (c.Overlay != FrameOverlay.None && c.OverlayPriority > overlayPrio) { overlay = c.Overlay; overlayPrio = c.OverlayPriority; }
            }
            overlaySprite = overlay == FrameOverlay.Frost ? "sprite.hud.overlay.frost" : overlay == FrameOverlay.Wet ? "sprite.hud.overlay.wet" : null;
            overlayColor = overlay == FrameOverlay.Frost ? "color.overlay.frost" : "color.overlay.wet";
        }

        // ---- layout ----------------------------------------------------------------------------------------------


        private void HudBox(out float x, out float w)
        {
            var safe = ResponsiveContext.SafeArea;
            HudMath.HudBox(safe.x, safe.width, safe.height, Plugin.UltrawideBox?.Value ?? true,
                ThemeTokens.GetFloat("ratio.hud.ultrawide", 2.2f), out x, out w);
        }

        private void ArrangeAndPlace(Player p)
        {
            var gap = ThemeTokens.GetFloat("size.hud.gap", 5f);
            var barW = Mathf.Max(_health.Width, Mathf.Max(_stamina.Width, _eitr.Root.gameObject.activeSelf ? _eitr.Width : 0f));
            if (_es.Root.gameObject.activeSelf) barW = Mathf.Max(barW, _es.Width);
            var y = 0f;
            var stripBudget = ThemeTokens.GetFloat("size.hud.status.budget", 84f);
            if (_fed) _strip.Feed(_hudEffects);
            _strip.Arrange(barW, stripBudget);
            y += stripBudget;
            _strip.Root.anchoredPosition = new Vector2(0f, y);
            y += gap + ThemeTokens.GetFloat("size.hud.status.pad", 6f); // the strip panel reaches pad above its cells
            if (_eitr.Root.gameObject.activeSelf) { _eitr.Root.anchoredPosition = new Vector2(0f, y); y += _eitr.Height + gap; }
            if (_plaque.gameObject.activeSelf)
            {
                var h = _health.Height;
                _plaque.sizeDelta = new Vector2(Mathf.Max(120f, barW * ThemeTokens.GetFloat("share.hud.plaque", 0.34f)), h);
                _plaque.anchoredPosition = new Vector2(0f, y);
                y += h + gap;
            }
            if (_adrenaline.Root.gameObject.activeSelf || p.GetMaxAdrenaline() > 0f)
            {
                _adrenaline.Root.anchoredPosition = new Vector2(0f, y);
                y += _adrenaline.Height + gap;
            }
            _stamina.Root.anchoredPosition = new Vector2(0f, y);
            y += _stamina.Height + gap;
            _health.Root.anchoredPosition = new Vector2(0f, y);
            y += _health.Height + gap;
            if (_es.Root.gameObject.activeSelf) { _es.Root.anchoredPosition = new Vector2(0f, y); y += _es.Height + gap; }
            _food.Update(p, barW);
            y += _food.TimerDrop; // the timers hang below the sockets: keep them off the rail / bar below
            _food.Root.anchoredPosition = new Vector2(0f, y);
            var rowTop = y + _food.Height;
            // Width to fit: the bars, or the food row plus the foreign blocks placed after it (EAQS quick slots,
            // guardian power), measured in reference px from their visible bounds (last frame's cluster scale).
            var foreignW = 0f;
            var perRef = _root.lossyScale.x;
            if (perRef > 0f)
            {
                var quick = QuickBar();
                if (quick && Bounds(quick, out var qMin, out var qMax)) foreignW += (qMax.x - qMin.x) / perRef + gap * 2f;
                if (_hud.m_gpRoot && _hud.m_gpRoot.gameObject.activeInHierarchy && Bounds(_hud.m_gpRoot, out var gMin, out var gMax))
                    foreignW += (gMax.x - gMin.x) / perRef + gap * 2f;
            }
            var clusterW = Mathf.Max(barW, _food.Width + gap * 2f + foreignW);
            var clusterH = rowTop;

            // Scale and place the cluster at the safe/box bottom-left corner.
            HudBox(out var boxX, out var boxW);
            var safe = ResponsiveContext.SafeArea;
            var s = HudMath.ClusterScale(ResponsiveContext.GuiFactor, Plugin.HudScale?.Value ?? 1f, clusterW, clusterH,
                boxW, safe.height, ThemeTokens.GetFloat("share.hud.cluster.w", 0.34f), ThemeTokens.GetFloat("share.hud.cluster.h", 0.3f),
                ThemeTokens.GetFloat("size.hud.font.number", 16f), ThemeTokens.GetFloat("floor.text.number.px", 14f),
                ThemeTokens.GetFloat("size.hud.font.timer", 13f), ThemeTokens.GetFloat("floor.text.timer.px", 12f));
            var margin = ThemeTokens.GetFloat("size.hud.margin", 24f) * s;
            PlaceAtScreen(_root, new Vector2(boxX + margin, safe.y + margin), s);

            // Foreign blocks: EAQS quick slots right of the food row, guardian power after them, hotbar top-left.
            var food = _root.TransformPoint(new Vector3(_food.Width + gap * 2f, y, 0f));
            var after = PlaceBlock(QuickBar(), food, false);
            if (_hud.m_gpRoot && _hud.m_gpRoot.gameObject.activeInHierarchy)
                PlaceBlock(_hud.m_gpRoot, after, false);
            if (_hotbar)
            {
                var top = ThemeTokens.GetFloat("size.hud.hotbar.top", 24f) * s;
                var target = ScreenToWorld(_root.parent as RectTransform, new Vector2(boxX + margin, safe.yMax - top));
                PlaceBlock(_hotbar, target, true);
            }
        }

        private Transform QuickBar()
        {
            if (_quickBar) return _quickBar;
            if (Time.unscaledTime < _quickSearchAt) return null;
            _quickSearchAt = Time.unscaledTime + 2f;
            // EquipmentAndQuickSlots adds its bar under hudroot (autotest 20-hud: hudroot/QuickSlotsHotkeyBar).
            _quickBar = _hud.m_rootObject.transform.Find("QuickSlotsHotkeyBar");
            return _quickBar;
        }

        // Moves a foreign block so the bounds of its visible rects start at `anchor` (bottom-left, or top-left when
        // fromTop). Returns the world point right after the block. Writes only when off by more than half a pixel.
        private Vector3 PlaceBlock(Transform block, Vector3 anchor, bool fromTop)
        {
            if (!block) return anchor;
            if (!Bounds(block, out var min, out var max)) return anchor;
            var current = fromTop ? new Vector3(min.x, max.y, 0f) : new Vector3(min.x, min.y, 0f);
            var delta = anchor - current;
            delta.z = 0f;
            if (delta.sqrMagnitude > 0.25f) block.position += delta;
            var gap = ThemeTokens.GetFloat("size.hud.gap", 5f) * _root.lossyScale.x * 2f;
            return new Vector3(anchor.x + (max.x - min.x) + gap, anchor.y, anchor.z);
        }

        private bool Bounds(Transform block, out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue, float.MaxValue);
            max = new Vector3(float.MinValue, float.MinValue);
            _rects.Clear();
            block.GetComponentsInChildren(false, _rects);
            var any = false;
            foreach (var r in _rects)
            {
                if (r == block && _rects.Count > 1) continue;
                if (!r.TryGetComponent<Graphic>(out var g) || !g.enabled) continue;
                r.GetWorldCorners(_corners);
                min = Vector3.Min(min, _corners[0]);
                max = Vector3.Max(max, _corners[2]);
                any = true;
            }
            return any;
        }

        private static Vector3 ScreenToWorld(RectTransform plane, Vector2 screen)
        {
            var canvas = plane ? plane.GetComponentInParent<Canvas>() : null;
            var cam = canvas && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            RectTransformUtility.ScreenPointToWorldPointInRectangle(plane, screen, cam, out var world);
            return world;
        }

        // Bottom-left of `rt` at a screen point; `pxPerRef` physical pixels per reference pixel.
        private static void PlaceAtScreen(RectTransform rt, Vector2 screen, float pxPerRef)
        {
            var parent = rt.parent as RectTransform;
            if (!parent) return;
            var a = ScreenToWorld(parent, screen);
            var b = ScreenToWorld(parent, screen + new Vector2(100f, 0f));
            var worldPerPx = (b - a).magnitude / 100f;
            var parentScale = parent.lossyScale.x;
            if (parentScale <= 0f || worldPerPx <= 0f) return;
            var local = pxPerRef * worldPerPx / parentScale;
            var scale = new Vector3(local, local, 1f);
            if ((rt.localScale - scale).sqrMagnitude > 1e-8f) rt.localScale = scale;
            if ((rt.position - a).sqrMagnitude > 0.01f) rt.position = a;
        }

        // ---- diagnostics ---------------------------------------------------------------------------------------

        public void Record()
        {
            var report = _art.Report(_root);
            SkinDiagnostics.Record(report);
        }
    }
}
