using RuneSkin.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.PlayerHud
{
    // Rage ring around the crosshair (vanilla adrenaline: Player.GetAdrenaline / GetMaxAdrenaline, the trinket's
    // m_maxAdrenaline, decays by Player.m_adrenalineDegen; full = the trinket's m_fullAdrenalineSE) and the player's
    // stagger column beside it (Character.GetStaggerPercentage = m_staggerDamage / (max HP x m_staggerDamageFactor),
    // decays over 5 s; Character.IsStaggering), mockup rs-C1 / rs-C2 (user pick 2026-10-22, variant C). Ring fills
    // clockwise from 12 o'clock and pulses when full; the column fills bottom-up, staggered = bright pale-gold fill
    // draining with sparks. Both fade in while they change and out after HudCenter.IdleDelay. Built once, change-only
    // writes, nothing allocates per frame.
    public sealed class CenterGauges
    {
        private readonly RectTransform _root;
        private readonly CanvasGroup _ringGroup, _colGroup;
        private readonly Image _ringFrame, _ringFill, _tickStart, _tickEnd, _colFrame, _colFill, _colBurst;
        private readonly RectTransform _tickEndPivot;
        private GaugeState _rage, _stagger;
        private bool _staggerRun;
        private float _pulse;
        private float _shownRage = -1f, _shownStagger = -1f;
        private bool _shownBurst;

        public CenterGauges(HudArt art, Transform hudRoot)
        {
            _root = HudArt.Rect("RuneSkinCenterGauges", hudRoot);
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 0.5f);
            _root.anchoredPosition = Vector2.zero;

            var ring = HudArt.Rect("Rage", _root);
            _ringGroup = ring.gameObject.AddComponent<CanvasGroup>();
            _ringGroup.alpha = 0f;
            _ringGroup.blocksRaycasts = false;
            _ringFrame = art.Image("Track", ring, "sprite.hud.gauge.rage.frame", "color.hud.gauge.frame", Image.Type.Simple);
            _ringFill = art.Image("Fill", ring, "sprite.hud.gauge.rage.fill", "color.hud.gauge.rage", Image.Type.Simple);
            if (_ringFill.sprite)
            {
                _ringFill.type = Image.Type.Filled;
                _ringFill.fillMethod = Image.FillMethod.Radial360;
                _ringFill.fillOrigin = (int)Image.Origin360.Top;
                _ringFill.fillClockwise = true;
            }
            _tickStart = art.Image("TickStart", ring, "sprite.hud.gauge.rage.tick", "color.hud.gauge.rage", Image.Type.Simple);
            _tickEndPivot = HudArt.Rect("TickEndPivot", ring);
            _tickEnd = art.Image("TickEnd", _tickEndPivot, "sprite.hud.gauge.rage.tick", "color.hud.gauge.rage", Image.Type.Simple);

            var col = HudArt.Rect("Stagger", _root);
            _colGroup = col.gameObject.AddComponent<CanvasGroup>();
            _colGroup.alpha = 0f;
            _colGroup.blocksRaycasts = false;
            // The frame's dark well is the backdrop; fills draw over it, inset.
            _colFrame = art.Image("Frame", col, "sprite.hud.gauge.stagger.frame", "color.hud.gauge.frame", Image.Type.Simple);
            _colFill = art.Image("Fill", col, "sprite.hud.gauge.stagger.fill", "color.hud.gauge.stagger", Image.Type.Simple);
            _colBurst = art.Image("Burst", col, "sprite.hud.gauge.stagger.burst", "color.hud.gauge.stagger.active", Image.Type.Simple);
            foreach (var f in new[] { _colFill, _colBurst })
            {
                if (!f.sprite) continue;
                f.type = Image.Type.Filled;
                f.fillMethod = Image.FillMethod.Vertical;
                f.fillOrigin = (int)Image.OriginVertical.Bottom;
            }
            _colBurst.gameObject.SetActive(false);
            Layout(ring, col);
        }

        private void Layout(RectTransform ring, RectTransform col)
        {
            var d = ThemeTokens.GetFloat("size.hud.gauge.ring", 76f);
            Centre(ring, Vector2.zero, d, d);
            Stretch(_ringFrame.rectTransform);
            Stretch(_ringFill.rectTransform);
            var t = d * ThemeTokens.GetFloat("share.hud.gauge.tick", 0.16f);
            Centre(_tickStart.rectTransform, new Vector2(0f, d / 2f), t * 0.75f, t);
            Centre(_tickEndPivot, Vector2.zero, d, d);
            Centre(_tickEnd.rectTransform, new Vector2(0f, d / 2f), t * 0.75f, t);
            var cw = ThemeTokens.GetFloat("size.hud.gauge.stagger.w", 16f);
            var ch = ThemeTokens.GetFloat("size.hud.gauge.stagger.h", 88f);
            Centre(col, new Vector2(d / 2f + ThemeTokens.GetFloat("size.hud.gauge.stagger.gap", 22f) + cw / 2f, 0f), cw, ch);
            Stretch(_colFrame.rectTransform);
            var inset = ThemeTokens.GetFloat("share.hud.gauge.stagger.inset", 0.18f);
            Inset(_colFill.rectTransform, cw * inset, ch * 0.06f);
            Inset(_colBurst.rectTransform, cw * inset, ch * 0.06f); // inside the frame channel (the burst never covers the frame)
        }

        public void SetActive(bool on)
        {
            if (_root && _root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
        }

        public void Destroy()
        {
            if (_root) UnityEngine.Object.Destroy(_root.gameObject);
        }

        public void Tick(Player p, float dt)
        {
            var now = Time.time;
            var delay = Plugin.GaugeIdleDelay?.Value ?? 1.5f;
            var opacity = Plugin.GaugeOpacity?.Value ?? 0.85f;
            var fade = ThemeTokens.GetFloat("time.hud.gauge.fade", 0.25f);

            // Rage (adrenaline): only with a source equipped (max > 0).
            var max = p.GetMaxAdrenaline();
            var rage = max > 0f ? Mathf.Clamp01(p.GetAdrenaline() / max) : 0f;
            var full = rage >= 0.999f;
            _rage.Step(rage, full, now, dt, delay, fade);
            if (Mathf.Abs(rage - _shownRage) > 0.001f)
            {
                _shownRage = rage;
                if (_ringFill.sprite) _ringFill.fillAmount = rage;
                var on = rage > 0.01f && rage < 0.999f;
                if (_tickEnd.gameObject.activeSelf != on) _tickEnd.gameObject.SetActive(on);
                _tickEndPivot.localRotation = Quaternion.Euler(0f, 0f, -360f * rage);
            }
            // Pulse when full (alpha breathes on the fill: canvasRenderer alpha, no rebuild).
            _pulse = full ? _pulse + dt : 0f;
            var pa = full ? Mathf.Lerp(0.65f, 1f, 0.5f + 0.5f * Mathf.Sin(_pulse * ThemeTokens.GetFloat("rate.hud.gauge.pulse", 5f))) : 1f;
            if (!Mathf.Approximately(_ringFill.canvasRenderer.GetAlpha(), pa)) _ringFill.canvasRenderer.SetAlpha(pa);
            SetAlpha(_ringGroup, _rage.Alpha * opacity);

            // Stagger: builds with hits; a stagger (value at the threshold / the stagger animation) turns the column
            // bright until it drained to 0.
            var st = Mathf.Clamp01(p.GetStaggerPercentage());
            if (p.IsStaggering() || st >= 0.999f) _staggerRun = true;
            if (st <= 0.001f && !p.IsStaggering()) _staggerRun = false;
            _stagger.Step(st, _staggerRun, now, dt, delay, fade);
            if (Mathf.Abs(st - _shownStagger) > 0.002f || _staggerRun != _shownBurst)
            {
                _shownStagger = st;
                _shownBurst = _staggerRun;
                if (_colBurst.gameObject.activeSelf != _staggerRun) _colBurst.gameObject.SetActive(_staggerRun);
                if (_colFill.gameObject.activeSelf == _staggerRun) _colFill.gameObject.SetActive(!_staggerRun);
                var target = _staggerRun ? _colBurst : _colFill;
                if (target.sprite) target.fillAmount = st;
            }
            SetAlpha(_colGroup, _stagger.Alpha * opacity);
        }

        // Faded out = not drawn (RenderGate); the gauges' idle state is alpha 0, so this is most of the time.
        private static void SetAlpha(CanvasGroup g, float a)
        {
            if (RuneSkin.Restyle.RenderGateMath.Concealed(a)) a = 0f;
            if (Mathf.Abs(g.alpha - a) > 0.002f || (a == 0f && g.alpha != 0f)) g.alpha = a;
            RuneSkin.Restyle.RenderGate.Set(g, "fade", RuneSkin.Restyle.RenderGateMath.Concealed(g.alpha));
        }

        private static void Centre(RectTransform rt, Vector2 at, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = at;
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static void Inset(RectTransform rt, float x, float y)
        {
            Stretch(rt);
            rt.offsetMin = new Vector2(x, y);
            rt.offsetMax = new Vector2(-x, -y);
        }
    }
}
