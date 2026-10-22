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
    // RuneSkin's player HUD (family "Hud"): one strip along the bottom of the safe area (or of a centred 16:9 box on
    // ultrawide), Track H / mockup hud-2-final: food wing | hotbar + ability zone over the HP / shield bar and the
    // stamina | eitr halves | potion wing, status rows centred above it. Lives under Hud.m_rootObject, so Hud.SetVisible
    // / the user hide toggle (Hud.cs:436) apply; one nested Canvas isolates its rebuilds. The vanilla bars, the vanilla
    // hotbar, the EAQS quick bar and Epic Loot's AbilityBar keep running untouched (input, state) but are not drawn: the
    // strip renders their data. Values are sampled in LateUpdate, after Hud.Update ran this frame (Hud.cs:467-543);
    // providers add the reservation / conversion / energy-shield data (Skin.RegisterResourceProvider). Geometry is
    // recomputed only when its inputs change (StripKey); per-frame work = values and cell contents, change-only writes.
    public sealed class HudCluster : MonoBehaviour
    {
        public static HudCluster Instance { get; private set; }

        private Hud _hud;
        private Canvas _canvas;
        private HudArt _art;
        private RectTransform _root;
        private BarView _health, _stamina, _adrenaline, _eitr;
        private RectTransform _plaque;
        private TMPro.TMP_Text _plaqueText;
        private string _plaqueShown;
        private StatusStrip _strip, _buffs;
        private HudStrip _hudStrip;
        private readonly List<StatusEffect> _hudEffects = new List<StatusEffect>(16);
        private bool _fed;
        private readonly List<KeyValuePair<int, float>> _pending = new List<KeyValuePair<int, float>>(4);
        private readonly HudMath.Stripe[] _stripes = new HudMath.Stripe[BarView.MaxStripes];
        private readonly int[] _stripeKinds = new int[BarView.MaxStripes];
        private HotkeyBar _hotbar;
        private HotkeyBar _quickBar;
        private Transform _abilityBar;
        private float _foreignSearchAt;

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
            gameObject.AddComponent<GraphicRaycaster>(); // "+N" cell, strip cells (drop / click)
            BuildViews();
            Suppress();
            _hotbar = _hud.GetComponentInChildren<HotkeyBar>(true);
            GuardianAbility.Register();
            EpicLootBridge.RegisterAbilities();
            Plugin.Log.LogInfo("[RuneSkin] HUD strip built");
        }

        private int _themeRevision = -1;

        // Theme reload (runeskin_reload): every HUD graphic is ours, so the views are rebuilt from the new tokens.
        private void RebuildViews()
        {
            foreach (var c in Children.Of(_root)) Destroy(c.gameObject);
            _art = new HudArt();
            BuildViews();
            foreach (var r in _suppressedRoots) _art.Suppressed(r, "vanilla HUD part (RuneSkin draws its own; vanilla code keeps running)");
        }

        private void BuildViews()
        {
            _themeRevision = ThemeTokens.Revision;
            _childrenOn = true;
            _plaqueShown = null;
            _stripKey = int.MinValue;
            // Draw order: strip panels + captions -> status rows -> bars -> strip cells.
            _hudStrip = new HudStrip(_art, _root);
            _strip = new StatusStrip(_art, _root, true, "StatusStrip");
            _buffs = new StatusStrip(_art, _root, false, "BuffStrip");
            // Bars as in mockup hud-3: pill well, fills and rounded right end cut from it (strip3_*), mockup trough grey.
            var frame = "sprite.hud.strip3.bar.frame";
            var cap = "sprite.hud.strip3.bar.cap";
            var capW = "size.hud.strip.bar.cap";
            var trough = "color.hud.strip.trough";
            var hpScale = FrameScale(frame, ThemeTokens.GetFloat("size.hud.strip.bar.hp", 24f));
            var slimScale = FrameScale(frame, ThemeTokens.GetFloat("size.hud.strip.bar.slim", 24f));
            _eitr = new BarView(_art, _root, "Eitr", false, frame, "color.hud.strip.fill.eitr", slimScale,
                capToken: cap, fillToken: "sprite.hud.strip3.fill.ei", troughColorToken: trough, capWidthToken: capW, troughToken: "sprite.hud.bar.fill");
            _adrenaline = new BarView(_art, _root, "Adrenaline", true, "sprite.hud.rail.frame.adrenaline", "color.hud.adrenaline");
            _stamina = new BarView(_art, _root, "Stamina", false, frame, "color.hud.strip.fill.stamina", slimScale,
                capToken: cap, fillToken: "sprite.hud.strip3.fill.st", troughColorToken: trough, capWidthToken: capW, troughToken: "sprite.hud.bar.fill");
            _health = new BarView(_art, _root, "Health", false, frame, "color.hud.strip.fill.health", hpScale, mainValue: true,
                capToken: cap, fillToken: "sprite.hud.strip3.fill.hp", troughColorToken: trough, capWidthToken: capW, troughToken: "sprite.hud.bar.fill", shieldToken: "sprite.hud.strip3.shield");
            _health.Plain = _stamina.Plain = _eitr.Plain = true;
            _health.ShieldLine = true;
            _plaque = HudArt.Rect("ConversionPlaque", _root);
            var seal = _art.Image("Seal", _plaque, "sprite.hud.plaque.seal", "color.frame", Image.Type.Sliced);
            seal.rectTransform.anchorMin = Vector2.zero;
            seal.rectTransform.anchorMax = Vector2.one;
            seal.rectTransform.offsetMin = seal.rectTransform.offsetMax = Vector2.zero;
            _plaqueText = _art.Text("Label", _plaque, "font.body", "color.text.accent", ThemeTokens.GetFloat("size.hud.font.number", 16f), TMPro.TextAlignmentOptions.Center);
            _plaqueText.rectTransform.anchorMin = Vector2.zero;
            _plaqueText.rectTransform.anchorMax = Vector2.one;
            _plaqueText.rectTransform.offsetMin = new Vector2(20f, 0f);
            _plaqueText.rectTransform.offsetMax = Vector2.zero;
            _plaque.gameObject.SetActive(false);
            _hudStrip.BuildFront();
        }

        // Bar scale that gives the frame sprite the wanted outer height (reference px).
        private static float FrameScale(string token, float height)
        {
            BarView.FrameGeometry(token, out _, out _, out var b, out var t, out var h);
            var natural = h + b + t;
            return natural > 0f ? height / natural : 1f;
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
            var roots = new[] { _hud.m_healthPanel, _hud.m_staminaBar2Root, _hud.m_adrenalineBarRoot, _hud.m_eitrBarRoot, _hud.m_statusEffectListRoot, _hud.m_gpRoot };
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

        // Vanilla hotbar, EAQS quick bar (hudroot/QuickSlotsHotkeyBar) and Epic Loot's AbilityBar: alpha 0, no raycasts,
        // their code keeps running (gamepad selection, ability input). Written per frame (LargeMap re-shows hotbars when
        // the map closes), only when off.
        private void HideForeign()
        {
            if ((!_quickBar || !_abilityBar) && Time.unscaledTime >= _foreignSearchAt)
            {
                _foreignSearchAt = Time.unscaledTime + 2f;
                var root = _hud.m_rootObject.transform;
                if (!_quickBar) { var q = root.Find("QuickSlotsHotkeyBar"); if (q) _quickBar = q.GetComponent<HotkeyBar>(); }
                if (!_abilityBar)
                    for (var i = 0; i < root.childCount; i++)
                    {
                        var c = root.GetChild(i);
                        if (c.name.StartsWith("AbilityBar", System.StringComparison.Ordinal)) { _abilityBar = c; break; }
                    }
            }
            Hide(_hotbar ? _hotbar.transform : null);
            Hide(_quickBar ? _quickBar.transform : null);
            Hide(_abilityBar);
        }

        private static void Hide(Transform t)
        {
            if (!t) return;
            if (!t.TryGetComponent<CanvasGroup>(out var g)) g = t.gameObject.AddComponent<CanvasGroup>();
            if (g.alpha != 0f) g.alpha = 0f;
            if (g.blocksRaycasts) g.blocksRaycasts = false;
            if (g.interactable) g.interactable = false;
        }

        // ---- frame ---------------------------------------------------------------------------------------------

        private void LateUpdate()
        {
            var player = Player.m_localPlayer;
            // Hud.SetVisible hides by moving m_rootObject off screen (Hud.cs:436-448): never place against that.
            var visible = player && !player.IsDead() && _hud.IsVisible() && _hud.m_healthPanel && _hud.m_healthPanel.localScale.x > 0f;
            if (_themeRevision != ThemeTokens.Revision) RebuildViews();
            if (_canvas) _art.SyncPpu(_canvas.referencePixelsPerUnit);
            SetChildrenActive(visible);
            HideForeign();
            AboveInventory();
            if (!visible) return;
            var dt = Time.deltaTime;
            StatusKeys();
            _hudStrip.Counts(player);
            Sample(player, dt);
            Arrange(player);
            _hudStrip.Fill(player, _hotbar, _quickBar);
            _strip.Tick(dt);
            _buffs.Tick(dt);
            // No periodic Record: a full report (paths, descriptions, GetComponentsInChildren) every 300 frames was a GC
            // spike every ~5 s (perf audit P0). Diagnostics record on demand (Plugin.DiagDump / runeskin_diag).
        }

        private bool _childrenOn = true;

        // While the inventory is open the strip sorts above the GUI canvas (its hotbar is the inventory's row 0 and a
        // drop target): otherwise InventoryGui's full-screen root/dropButton (drop to the ground) takes the press over
        // the strip (run 20261022-194551: raycast top = root/dropButton).
        private void AboveInventory()
        {
            if (!_canvas) return;
            var want = InventoryGui.IsVisible();
            if (_canvas.overrideSorting == want) return;
            _canvas.overrideSorting = want;
            if (want && _canvas.transform.parent)
            {
                var root = _canvas.transform.parent.GetComponentInParent<Canvas>();
                var top = root ? root.rootCanvas : null;
                var order = top ? top.sortingOrder : 0;
                if (InventoryGui.instance && InventoryGui.instance.GetComponentInParent<Canvas>() is Canvas inv)
                    order = Mathf.Max(order, inv.rootCanvas.sortingOrder);
                _canvas.sortingOrder = order + 1;
                if (top) _canvas.sortingLayerID = top.sortingLayerID;
            }
        }

        private void SetChildrenActive(bool on)
        {
            if (_childrenOn == on) return;
            _childrenOn = on;
            foreach (var c in Children.Of(_root)) c.gameObject.SetActive(on);
            if (on) _stripKey = int.MinValue; // views re-enabled: re-apply their visibility
        }

        private void StatusKeys()
        {
            if (Chat.instance && Chat.instance.HasFocus() || global::Console.IsVisible() || TextInput.IsVisible()) return;
            var key = Plugin.StatusListKey?.Value ?? default;
            var pad = Plugin.StatusListButton?.Value;
            var pressed = key.MainKey != KeyCode.None && key.IsDown();
            if (!pressed && !string.IsNullOrEmpty(pad) && ZInput.IsGamepadActive() && ZInput.GetButton("JoyAltKeys") && ZInput.GetButtonDown(pad))
                pressed = true;
            // One key cycles the full lists: debuffs -> buffs -> closed (only lists with entries open).
            if (pressed)
            {
                if (_strip.PopupOpen) { _strip.ClosePopup(); if (_buffs.Count > 0) _buffs.TogglePopup(); }
                else if (_buffs.PopupOpen) _buffs.ClosePopup();
                else if (_strip.Count > 0) _strip.TogglePopup();
                else if (_buffs.Count > 0) _buffs.TogglePopup();
            }
            else if ((_strip.PopupOpen || _buffs.PopupOpen) && (ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyBack")))
            {
                _strip.ClosePopup();
                _buffs.ClosePopup();
            }
        }

        // Remaining absorb of every vanilla shield effect (SE_Shield: m_totalAbsorbDamage - m_damage, SE_Shield.cs:23-57).
        public static float VanillaShield(Player p)
        {
            var sum = 0f;
            foreach (var se in p.GetSEMan().GetStatusEffects())
                if (se is SE_Shield s) sum += Mathf.Max(0f, s.m_totalAbsorbDamage - s.m_damage);
            return sum;
        }

        // Bar widths come from the last strip layout (stable until the layout key changes).
        private float _hpOuter = 600f, _halfOuter = 298f;

        private void Sample(Player p, float dt)
        {
            // Energy shield (mod pool) as the main bar while it carries the player.
            var esOn = Providers.TryResource(ResourcePools.EnergyShield, p, true, out var es) && es.Max > 0f;
            var esMain = esOn && es.MainBar;

            var hp = p.GetHealth();
            var hpC = p.GetMaxHealth();
            var hpB = Extension(ResourcePools.Health, p, hpC, out _);
            CollectDots(p);
            FrameOverlayOf(p, out var overlaySprite, out var overlayColor);
            _health.SetOuterWidth(_hpOuter);
            _health.Tick(dt);
            if (esMain)
            {
                _health.Render(es.Current, es.Max, es.Available, _stripes, 0, _stripeKinds, "color.hud.es", overlaySprite, overlayColor, es.Inert, dt, es.Available < es.Max - 0.5f);
                _health.Aux(0);
            }
            else
            {
                var count = HudMath.Stripes(_pending, hp, hpB, _health.WindowLength, ThemeTokens.GetFloat("size.hud.stripe.min", 4f), BarView.MaxStripes, _stripes);
                for (var i = 0; i < count; i++) _stripeKinds[i] = _stripes[i].Key < 0 ? (int)DotKind.Other : _stripes[i].Key;
                // Shield = MorgottTweaks energy shield (provider pool) + vanilla staff shields (SE_Shield): the upper band
                // of the HP window, label "HP + shield" (user calls 2026-10-22).
                _health.SetShield((esOn ? Mathf.Max(0f, es.Current) : 0f) + VanillaShield(p));
                _health.Render(hp, hpB, hpC, _stripes, count, _stripeKinds, null, overlaySprite, overlayColor, false, dt, false);
            }

            // Stamina (+ adrenaline rail between the rows, Hud.cs:1147-1160: vanilla hides it at 0).
            var st = p.GetStamina();
            var stC = p.GetMaxStamina();
            var stB = Extension(ResourcePools.Stamina, p, stC, out var stRes);
            _stamina.SetOuterWidth(_halfOuter);
            _stamina.Tick(dt);
            _stamina.Render(st, stB, stC, _stripes, 0, _stripeKinds, null, null, null, false, dt, stRes);
            var adMax = p.GetMaxAdrenaline();
            var ad = p.GetAdrenaline();
            _adrenalineOn = adMax > 0f && ad > 0f;
            _adrenaline.SetVisible(_adrenalineOn);
            if (_adrenalineOn)
            {
                _adrenaline.SetOuterWidth(_hpOuter);
                _adrenaline.Tick(dt);
                _adrenaline.Render(ad, adMax, adMax, _stripes, 0, _stripeKinds, null, null, null, false, dt, false);
            }

            // Eitr: vanilla hides it at max 0; a chosen conversion collapses it to the sealed plaque (same box).
            var ei = p.GetEitr();
            var eiC = p.GetMaxEitr();
            Providers.TryResource(ResourcePools.Eitr, p, false, out var eiState);
            var converted = eiState.Valid && !string.IsNullOrEmpty(eiState.ConversionTarget);
            var eiB = Extension(ResourcePools.Eitr, p, eiC, out var eiRes);
            _eitrOn = !converted && eiB > 0f;
            _eitr.SetVisible(_eitrOn);
            if (_eitrOn)
            {
                _eitr.SetOuterWidth(_halfOuter);
                _eitr.Tick(dt);
                _eitr.Render(ei, eiB, eiC, _stripes, 0, _stripeKinds, null, null, null, false, dt, eiRes);
                _eitr.Paused(eiState.Valid && eiState.RegenBlocked);
            }
            Plaque(converted ? eiState.ConversionTarget : null);
        }

        private bool _adrenalineOn, _eitrOn;

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

        private void CollectDots(Player p)
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

        private int _stripKey = int.MinValue;
        private float _pxPerUnit = 1f;
        private float _reservedTop;

        // Everything that changes the strip geometry: counts, bar rows, screen / safe area / GUI scale, config, theme.
        private int StripKey()
        {
            unchecked
            {
                var h = _hudStrip.LayoutKey();
                h = h * 31 + (_adrenalineOn ? 1 : 0) + (_eitrOn ? 2 : 0) + (_plaque.gameObject.activeSelf ? 4 : 0);
                h = h * 31 + Screen.width;
                h = h * 31 + Screen.height;
                h = h * 31 + Screen.safeArea.GetHashCode();
                h = h * 31 + ResponsiveContext.GuiFactor.GetHashCode();
                // Canvas scale too: after a resolution change the CanvasScaler updates a frame later, and PlaceAtScreen
                // converts screen px through it (run 20261022-194038: strip laid out at the old 2560 scale, 1391 px wide).
                h = h * 31 + (_root.parent ? _root.parent.lossyScale.x : 0f).GetHashCode();
                h = h * 31 + (_canvas && _canvas.rootCanvas ? _canvas.rootCanvas.scaleFactor : 0f).GetHashCode();
                h = h * 31 + (Plugin.HudScale?.Value ?? 1f).GetHashCode();
                h = h * 31 + (Plugin.StripWidth?.Value ?? 0.99f).GetHashCode();
                h = h * 31 + (Plugin.StripHeight?.Value ?? 130f).GetHashCode();
                h = h * 31 + (Plugin.UltrawideBox?.Value ?? true ? 1 : 0);
                h = h * 31 + ThemeTokens.Revision;
                return h;
            }
        }

        private void Arrange(Player p)
        {
            var exclude = GuardianAbility.ActiveHash;
            _strip.ExcludeHash = _buffs.ExcludeHash = exclude;
            if (_fed) { _strip.Feed(_hudEffects); _buffs.Feed(_hudEffects); }
            var key = StripKey();
            if (key != _stripKey)
            {
                _stripKey = key;
                Relayout();
            }
            Status();
        }

        // Geometry pass (inputs changed): strip layout, bars, scale and screen placement.
        private void Relayout()
        {
            var barGap = ThemeTokens.GetFloat("size.hud.strip.bar.gap", 4f);
            var plaqueOn = _plaque.gameObject.activeSelf;
            var adrH = _adrenalineOn ? _adrenaline.Height + barGap : 0f;
            var barsH = _stamina.Height + barGap + adrH + _health.Height;

            // Screen box and scale: reference px x HUD scale (font floors win), times the configured strip height
            // over the natural one; the width is a share of the box in screen px, converted to strip units.
            var safe = ResponsiveContext.SafeArea;
            HudMath.HudBox(safe.x, safe.width, safe.height, Plugin.UltrawideBox?.Value ?? true, ThemeTokens.GetFloat("ratio.hud.ultrawide", 2.2f), out var boxX, out var boxW);
            var s = HudMath.ClusterScale(ResponsiveContext.GuiFactor, Plugin.HudScale?.Value ?? 1f, 0f, 0f, 0f, 0f, 0f, 0f,
                ThemeTokens.GetFloat("size.hud.font.number", 16f), ThemeTokens.GetFloat("floor.text.number.px", 14f),
                ThemeTokens.GetFloat("size.hud.font.timer", 13f), ThemeTokens.GetFloat("floor.text.timer.px", 12f));
            // Mockup hud-3 proportions scaled uniformly: height = Hud.Height (12 % of 1080 by default), width = the
            // design width (size.hud.strip.width, the mockup's panel) at that scale, capped by Hud.Width of the box.
            var probe = _hudStrip.Input(ThemeTokens.GetFloat("size.hud.strip.width", 1900f), barsH);
            var natural = StripMath.Compute(probe).Height;
            var k = StripMath.HeightScale((Plugin.StripHeight?.Value ?? 130f) * (Plugin.HudScale?.Value ?? 1f), natural);
            _pxPerUnit = s * k;
            var widthPx = Mathf.Min(ThemeTokens.GetFloat("size.hud.strip.width", 1900f) * _pxPerUnit, StripMath.WidthPx(boxW, Plugin.StripWidth?.Value ?? 0.99f));
            var input = _hudStrip.Input(widthPx / _pxPerUnit, barsH);
            var l = StripMath.Compute(input);
            _hudStrip.Apply(l, input);

            // Bars, bottom-up inside the bar block: stamina | eitr row, adrenaline rail, HP row.
            var pxPerUnitY = _root.lossyScale.y;
            var y = l.BarsY;
            var halfW = (l.BarsW - barGap) / 2f;
            _hpOuter = l.BarsW;
            _halfOuter = halfW;
            _stamina.SetOuterWidth(halfW);
            _eitr.SetOuterWidth(halfW);
            _health.SetOuterWidth(l.BarsW);
            _stamina.Root.anchoredPosition = new Vector2(l.BarsX, HudMath.Snap(y, pxPerUnitY));
            var eitrX = l.BarsX + halfW + barGap;
            _eitr.Root.anchoredPosition = new Vector2(eitrX, HudMath.Snap(y, pxPerUnitY));
            if (plaqueOn)
            {
                _plaque.anchoredPosition = new Vector2(eitrX, y);
                _plaque.sizeDelta = new Vector2(halfW, _stamina.Height);
            }
            var slimY = y;
            y += _stamina.Height + barGap;
            if (_adrenalineOn)
            {
                _adrenaline.SetOuterWidth(l.BarsW);
                _adrenaline.Root.anchoredPosition = new Vector2(l.BarsX, HudMath.Snap(y, pxPerUnitY));
                y += _adrenaline.Height + barGap;
            }
            _health.Root.anchoredPosition = new Vector2(l.BarsX, HudMath.Snap(y, pxPerUnitY));
            _hudStrip.BarKnots(new Rect(l.BarsX, l.BarsY, l.BarsW, y + _health.Height - l.BarsY));

            // Screen placement: centred in the box, the bottom margin above the safe area.
            var margin = ThemeTokens.GetFloat("size.hud.strip.margin", 8f) * s;
            var bottom = safe.y + margin;
            var left = boxX + (boxW - widthPx) / 2f;
            PlaceAtScreen(_root, new Vector2(left, bottom), _pxPerUnit);
            _stripWidth = input.Width;
            _statusBase = l.Height + ThemeTokens.GetFloat("size.hud.strip.status.gap", 8f) + ThemeTokens.GetFloat("size.hud.status.pad", 6f);
            // The inventory docks on the strip top (InventoryMath keeps its standard gap above the HUD top).
            // The column panels' stacks reach size.panel.glass.inset beyond their box: counted, so the visible gap is
            // InventoryMath's standard gap (size.inv.gap) from the strip top.
            _reservedTop = bottom + l.Height * _pxPerUnit + ThemeTokens.GetFloat("size.panel.glass.inset", 0f) * s;
            Plugin.Log.LogInfo($"[RuneSkin] HUD strip layout: width {widthPx:0}px scale {_pxPerUnit:0.###} (k {k:0.###}) height {l.Height:0} units = {l.Height * _pxPerUnit:0} px " +
                               $"({100f * l.Height * _pxPerUnit / Mathf.Max(1f, Screen.height):0.0} % of {Screen.height}), food {_hudStrip.FoodCount} potions {_hudStrip.PotionCount} abilities {_hudStrip.AbilityCount}{(l.Fits ? "" : " (tight)")}");
        }

        private float _stripWidth, _statusBase;

        // Status groups above the strip (user call 2026-10-22): buffs on one side, debuffs on the other (Hud.BuffSide),
        // Hud.StatusPerRow cells a row, rows growing upward (Hud.StatusRows, then "+N"); hidden while the inventory is open
        // (it docks on the strip top).
        private void Status()
        {
            var cell = StatusStrip.CellSize;
            var gap = ThemeTokens.GetFloat("size.hud.status.gap", 4f);
            var pad = ThemeTokens.GetFloat("size.hud.status.pad", 6f);
            var show = !InventoryGui.IsVisible();
            if (_buffs.Root.gameObject.activeSelf != show) _buffs.Root.gameObject.SetActive(show);
            if (_strip.Root.gameObject.activeSelf != show) _strip.Root.gameObject.SetActive(show);
            if (!show) return;
            var perRow = Mathf.Clamp(Plugin.StatusPerRow?.Value ?? 10, 1, 30);
            var rows = Mathf.Clamp(Plugin.StatusRows?.Value ?? 2, 1, 6);
            var rowW = StripMath.RowWidth(perRow, cell, gap);
            var buffsLeft = (Plugin.BuffSide?.Value ?? "Left") != "Right";
            var leftGroup = buffsLeft ? _buffs : _strip;
            var rightGroup = buffsLeft ? _strip : _buffs;
            leftGroup.Upward = rightGroup.Upward = true;
            leftGroup.AlignRight = false;
            rightGroup.AlignRight = true;
            var budget = rows * (cell + gap);
            leftGroup.Arrange(rowW, budget);
            rightGroup.Arrange(rowW, budget);
            var inset = ThemeTokens.GetFloat("size.hud.strip.knot", 40f) * StripMath.KnotReserve + pad;
            var lp = new Vector2(inset, _statusBase);
            if (leftGroup.Root.anchoredPosition != lp) leftGroup.Root.anchoredPosition = lp;
            var rp = new Vector2(_stripWidth - inset - rightGroup.Root.sizeDelta.x, _statusBase);
            if (rightGroup.Root.anchoredPosition != rp) rightGroup.Root.anchoredPosition = rp;
        }
        // ---- inventory contract (InventorySkin) ------------------------------------------------------------------

        // The strip stays at the bottom while the inventory is open (decisions 2026-10-22: the strip with its hotbar
        // remains visible, items move between the grid and the strip hotbar). The band request of the older layout
        // only marks the inventory as open; the inventory's EAQS quick cells sit on the strip's potion cells.
        private bool _bandWanted;

        public void SetBand(float leftPx, float rightPx, int quickSlots) => _bandWanted = rightPx > leftPx;

        public void ClearBand() => _bandWanted = false;

        public bool BandWanted => _bandWanted;

        public bool BandActive => _bandWanted && InventoryGui.IsVisible() && _childrenOn && isActiveAndEnabled;

        // Potion cell i as a box of the strip root (bottom-left world point, size in root units).
        public bool BandQuickCell(int i, out Transform space, out Vector3 bottomLeft, out Vector2 size)
        {
            space = _root;
            bottomLeft = default;
            size = default;
            if (!BandActive || !_hudStrip.PotionBox(i, out var bl, out var side)) return false;
            bottomLeft = _root.TransformPoint(new Vector3(bl.x, bl.y, 0f));
            size = new Vector2(side, side);
            return true;
        }

        // Bottom-left world point of the potion zone (the inventory puts its quick cells there when it has no box).
        public Vector3 QuickAnchor => _root.TransformPoint(new Vector3(_hudStrip.L.PotionX, _hudStrip.L.PotionY, 0f));

        public bool HasQuickAnchor => _stripKey != int.MinValue;

        // Screen y (pixels, bottom-up) of the strip top incl. a reserved status row; 0 while the HUD is hidden.
        public float ReservedScreenTop => _childrenOn && isActiveAndEnabled ? _reservedTop : 0f;

        // Strip hotbar cell i (harness, drop tests).
        public RectTransform HotbarCell(int i) => _hudStrip?.HotbarRect(i);

        // Summon frames ride in the buff side area (SummonFrames.Place): parented to the strip root on first call.
        public bool PlaceSummons(RectTransform rt, float h)
        {
            if (!_root || _stripKey == int.MinValue) return false;
            if (rt.parent != _root) rt.SetParent(_root, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0f, 1f);
            // In the buff side area (mockup hud-3: side groups above the strip), over the buff rows.
            var buffsLeft = (Plugin.BuffSide?.Value ?? "Left") != "Right";
            var inset = ThemeTokens.GetFloat("size.hud.strip.knot", 40f) * StripMath.KnotReserve + ThemeTokens.GetFloat("size.hud.status.pad", 6f);
            var x = buffsLeft ? inset : _stripWidth - inset - rt.sizeDelta.x;
            var want = new Vector2(x, _statusBase + _buffs.Root.sizeDelta.y + ThemeTokens.GetFloat("size.hud.gap", 5f) * 2f + h);
            if ((rt.anchoredPosition - want).sqrMagnitude > 0.25f) rt.anchoredPosition = want;
            if (rt.localScale != Vector3.one) rt.localScale = Vector3.one;
            return true;
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
