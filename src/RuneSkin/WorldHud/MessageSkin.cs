using System;
using System.Collections.Generic;
using HarmonyLib;
using RuneSkin.Diagnostics;
using RuneSkin.PlayerHud;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RuneSkin.Ownership;

namespace RuneSkin.WorldHud
{
    // Family "MessageHud" (art\README.md "M4 sprites": notifications A, centre message B, biome banner B). Live
    // vanilla objects only:
    // - centre text (m_messageCenterText, MessageHud.cs:56; ShowMessage Center fades it with CrossFadeAlpha, :156):
    //   display serif + a soft band_message behind it sized to the text, its alpha following the text's;
    // - top-left line (m_messageText + m_messageIcon, :52-54; UpdateMessage :201 swaps text / icon and fades them):
    //   a frame_small plate behind icon + text, alpha following the text;
    // - biome-discovered banner (m_biomeFoundPrefab instance m_biomeMsgInstance, child "Title", Animator "done" tag,
    //   UpdateBiomeFound :260): vanilla decor images off, band_message + banner_rule left / mirrored right around the
    //   serif caps title, following the title's position, scale and alpha;
    // - unlock messages (m_unlockMessages, UpdateUnlockMsg :319: UnlockMessage/icon_bkg/UnlockIcon, UnlockTitle,
    //   UnlockDescription): plate + icon recess + fonts, restyled once per instance; title laid out inside the plate's
    //   content rect below the top rail (LayoutUnlock), slots re-spaced per frame (AlignUnlocks).
    public sealed class MessageSkin : MonoBehaviour
    {
        public const string ScreenId = "MessageHud";
        public static MessageSkin Instance { get; private set; }

        private readonly HudArt _art = new HudArt(ScreenId);
        private MessageHud _hud;
        private Image _centerBand, _plate;
        private RectTransform _banner;
        private CanvasGroup _bannerGroup;
        private TMP_Text _bannerTitle;
        private RectTransform _ruleL, _ruleR, _band;
        private readonly RectTransform[] _rules = new RectTransform[2];
        private GameObject _bannerFor;
        private string _centerText, _topText;
        private Vector2 _topBase;
        // Notification pushed below the party frame (SummonFrames, top-left corner since 2026-10-26): current shift and
        // the plate's top edge with no shift, both in the plate parent's local units.
        private float _topShift, _plateTopRaw = float.NaN;
        private readonly HashSet<int> _unlocks = new HashSet<int>();

        public static void Attach(MessageHud hud)
        {
            if (!hud || hud.GetComponent<MessageSkin>()) return;
            hud.gameObject.AddComponent<MessageSkin>().Init(hud);
        }

        private void Init(MessageHud hud)
        {
            Instance = this;
            _hud = hud;
            var canvas = hud.GetComponentInParent<Canvas>(true);
            if (canvas) _art.SyncPpu(canvas.rootCanvas.referencePixelsPerUnit);
            var center = hud.m_messageCenterText;
            if (center)
            {
                // Rune face like the main menu (user 2026-10-22: the thin dim serif "День 9" was nearly invisible): the
                // Norsebold face with a copy of its own material plus a soft dark underlay (contrast
                // on sky / snow / water and at night), no faux bold, bright warm colour, display size.
                _art.Note(center, "message.center", TextLook.ApplyShadowed(center, "font.display", "message.rune"), "centre message (rune face, own material + soft underlay)");
                center.color = ThemeTokens.GetColor("color.message.center", new Color(1f, 0.93f, 0.8f, 1f));
                center.enableAutoSizing = false;
                center.fontSize = ThemeTokens.GetFloat("size.message.center.font", 44f);
                // A dark backing band (color.message.band) under the rune text: readable over snow / sky (run
                // 20261022-232945: contrast 3.9 on white with a light band).
                _centerBand = Behind("RuneSkin_CenterBand", center.rectTransform, "sprite.band.message", "color.message.band");
                // Solid, sprite-less: the band sprite is mostly transparent under the glyphs (run 20261023-012017:
                // contrast 3.7 on white with the tinted sprite). Offline estimate with #0B1419 at 0.9 over white:
                // lum 0.024 vs the warm face 0.86 -> ratio ~12.
                // Since 2026-10-24: a soft-edged plate (opaque core under the glyphs + pad, alpha ramp outside it),
                // sized from the rendered glyphs every time they change (Center).
                if (_centerBand)
                {
                    var refPpu = canvas ? canvas.rootCanvas.referencePixelsPerUnit : 100f;
                    _centerBand.sprite = SoftPlate(refPpu);
                    _centerBand.type = Image.Type.Sliced;
                    _centerBand.fillCenter = true;
                    _centerBand.pixelsPerUnitMultiplier = SoftRamp / Mathf.Max(1f, ThemeTokens.GetFloat("size.message.center.fade", 16f));
                    _centerBand.raycastTarget = false;
                }
            }
            var top = hud.m_messageText;
            if (top)
            {
                // Body sans at a readable size (user 2026-10-22: the serif line at 16 was unreadable at 1080p), full body
                // contrast; the HUD canvas scales with the screen, so the floor is px at 1080p at every resolution.
                _art.Note(top, "message.topleft", TextLook.Apply(top, "font.body", "notify"), "top-left notification (body sans)");
                top.color = ThemeTokens.GetColor("color.text.body", top.color);
                top.enableAutoSizing = false;
                top.fontSize = HudReadable(ThemeTokens.GetFloat("size.message.notify.font", top.fontSize));
                _plate = Behind("RuneSkin_NotifyPlate", top.rectTransform, "sprite.hud.frame.small");
                // Room for the icon left of the text (the vanilla icon sits under the line).
                // ...and below the old hotbar summon frames (Codex r1); the top-left party frame since 2026-10-26 pushes
                // the row further down per frame when it reaches it (ClearPartyFrame).
                // The icon slot is added per message in TopLeft, only when the message has an icon.
                top.rectTransform.anchoredPosition += new Vector2(0f, -ThemeTokens.GetFloat("size.message.notify.drop", 64f));
                _topBase = top.rectTransform.anchoredPosition;
                if (_plate) _plate.rectTransform.SetAsFirstSibling();
            }
            Plugin.Log.LogInfo("[RuneSkin] message HUD attached");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private Image Behind(string name, RectTransform text, string token, string colorToken = "color.frame")
        {
            if (!text.parent) return null;
            var img = _art.Image(name, text.parent, token, colorToken, Image.Type.Sliced);
            img.rectTransform.SetSiblingIndex(text.GetSiblingIndex());
            img.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            return img;
        }

        private void LateUpdate()
        {
            if (!_hud) return;
            try
            {
                Center();
                ClearPartyFrame();
                TopLeft();
                Banner();
                Unlocks();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[RuneSkin] message HUD pass failed (disabled): {e}");
                enabled = false;
            }
        }

        private static float Alpha(TMP_Text t) =>
            t && t.gameObject.activeInHierarchy && !string.IsNullOrEmpty(t.text) ? t.canvasRenderer.GetAlpha() * t.color.a : 0f;

        private void Center()
        {
            var t = _hud.m_messageCenterText;
            if (!t || !_centerBand) return;
            var a = Alpha(t);
            _centerBand.canvasRenderer.SetAlpha(a);
            var textChanged = t.text != _centerText;
            if (textChanged)
            {
                _centerText = t.text;
                if (string.IsNullOrEmpty(_centerText)) return;
            }
            else if (a <= 0f) return;
            // Re-fit from the laid-out glyphs only when the text or its layout inputs (rect, font size, canvas scale)
            // changed, so a message held on screen costs a few compares per frame, no glyph walk / mesh rebuild. A
            // font / size / rect change after the text arrived still re-fits. Before (user screenshot 2026-10-24) the
            // band was sized once from GetPreferredValues and centred on the text rect, so the rune caps sat half
            // outside it.
            var tr = t.rectTransform;
            var canvas = t.canvas;
            var key = new Vector4(tr.rect.width, tr.rect.height, t.fontSize, canvas ? canvas.scaleFactor : 1f);
            var keyPos = tr.localPosition;
            var keyScale = tr.lossyScale.x;
            if (!textChanged && key == _fitKey && keyPos == _fitPos && Mathf.Approximately(keyScale, _fitScale)) return;
            _fitKey = key;
            _fitPos = keyPos;
            _fitScale = keyScale;
            t.ForceMeshUpdate();
            FitCenterPlate(t);
        }

        private Vector4 _fitKey;
        private Vector3 _fitPos;
        private float _fitScale;

        private float[] _quads = new float[256];

        private void FitCenterPlate(TMP_Text t)
        {
            var info = t.textInfo;
            if (info == null || info.characterCount == 0) return;
            var n = 0;
            if (_quads.Length < 4 * info.characterCount) _quads = new float[4 * info.characterCount];
            for (var i = 0; i < info.characterCount && i < info.characterInfo.Length; i++)
            {
                var c = info.characterInfo[i];
                if (!c.isVisible) continue;
                // Glyph quad + the line box: rune caps overshoot the font's ascender, descenders the baseline.
                _quads[4 * n] = c.bottomLeft.x;
                _quads[4 * n + 1] = Mathf.Min(c.bottomLeft.y, c.descender);
                _quads[4 * n + 2] = c.topRight.x;
                _quads[4 * n + 3] = Mathf.Max(c.topRight.y, c.ascender);
                n++;
            }
            if (!PlayHudMath.GlyphBox(_quads, n, out var x0, out var y0, out var x1, out var y1)) return;
            var tr = t.rectTransform;
            var parent = tr.parent;
            var min = (Vector2)parent.InverseTransformPoint(tr.TransformPoint(new Vector3(x0, y0, 0f)));
            var max = (Vector2)parent.InverseTransformPoint(tr.TransformPoint(new Vector3(x1, y1, 0f)));
            var canvas = t.canvas ? t.canvas.rootCanvas : null;
            var scale = canvas ? canvas.scaleFactor * (parent.lossyScale.x / Mathf.Max(1e-6f, canvas.transform.lossyScale.x)) : 1f;
            var minPx = ThemeTokens.GetFloat("size.message.center.minpx", 10f);
            var pad = PlayHudMath.PadUnits(ThemeTokens.GetFloat("size.message.center.pad", 54f), minPx, scale);
            var vpad = PlayHudMath.PadUnits(ThemeTokens.GetFloat("size.message.center.vpad", 12f), minPx, scale);
            var fade = SliceBorder(_centerBand).x;
            var (cx, w) = PlayHudMath.CenterPlate(min.x, max.x, pad, fade);
            var (cy, h) = PlayHudMath.CenterPlate(min.y, max.y, vpad, fade);
            var rt = _centerBand.rectTransform;
            var pos = new Vector3(cx, cy, 0f);
            var size = new Vector2(w, h);
            if ((rt.localPosition - pos).sqrMagnitude < 0.01f && (rt.sizeDelta - size).sqrMagnitude < 0.01f && rt.anchorMin == rt.anchorMax) return;
            rt.anchorMin = rt.anchorMax = ((RectTransform)parent).pivot;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.localPosition = pos;
        }

        // Soft plate sprite: white, alpha ramp over SoftRamp texels on every side (smoothstep per axis, so the corners
        // round off), 9-sliced at the ramp; tinted by color.message.band. A mechanical alpha mask, not art.
        private const int SoftSize = 64;
        private const float SoftRamp = 24f;
        private static Sprite _softPlate;

        private static Sprite SoftPlate(float ppu)
        {
            if (_softPlate) return _softPlate;
            var tex = new Texture2D(SoftSize, SoftSize, TextureFormat.RGBA32, false) { name = "RuneSkin_SoftPlate", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[SoftSize * SoftSize];
            for (var y = 0; y < SoftSize; y++)
                for (var x = 0; x < SoftSize; x++)
                {
                    var dx = Mathf.Min(x + 0.5f, SoftSize - x - 0.5f);
                    var dy = Mathf.Min(y + 0.5f, SoftSize - y - 0.5f);
                    var al = PlayHudMath.EdgeFade(dx, SoftRamp) * PlayHudMath.EdgeFade(dy, SoftRamp);
                    px[y * SoftSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(al * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            _softPlate = Sprite.Create(tex, new Rect(0, 0, SoftSize, SoftSize), new Vector2(0.5f, 0.5f), Mathf.Max(1f, ppu), 0, SpriteMeshType.FullRect,
                new Vector4(SoftRamp, SoftRamp, SoftRamp, SoftRamp));
            _softPlate.name = "RuneSkin_SoftPlate";
            return _softPlate;
        }

        private void TopLeft()
        {
            var t = _hud.m_messageText;
            if (!t || !_plate) return;
            _plate.canvasRenderer.SetAlpha(Alpha(t));
            if (t.text == _topText) return;
            _topText = t.text;
            if (string.IsNullOrEmpty(_topText)) return;
            // Plate from the icon's left edge to the end of the text, both in the shared parent's space.
            var parent = (RectTransform)_plate.rectTransform.parent;
            var pad = ThemeTokens.GetFloat("size.message.plate.pad", 14f);
            var tr = t.rectTransform;
            // Icon only when this message has one: vanilla keeps the last sprite and sets the alpha to 0 for an
            // icon-less message (MessageHud.UpdateMessage, the m_icon == null branch). Before (user live test
            // 2026-10-22) the slot was reserved for every message and the text sat right of the plate centre.
            var iconImg = _hud.m_messageIcon;
            var icon = iconImg && iconImg.enabled && iconImg.sprite && iconImg.gameObject.activeInHierarchy && iconImg.canvasRenderer.GetAlpha() > 0.01f ? iconImg.rectTransform : null;
            var slot = icon ? ThemeTokens.GetFloat("size.message.icon", 28f) + ThemeTokens.GetFloat("size.message.icon.gap", 10f) : 0f;
            tr.anchoredPosition = _topBase + new Vector2(slot, -_topShift);
            // Rendered text bounds (the text rect is much taller than one line), not the rect; vertically the line's
            // ascender / descender box (glyph bounds shift with descenders, so the plate sat off-centre).
            t.ForceMeshUpdate();
            var b = t.textBounds;
            var info = t.textInfo;
            if (info != null && info.lineCount > 0)
            {
                var first = info.lineInfo[0];
                var last = info.lineInfo[info.lineCount - 1];
                b = new Bounds(new Vector3(b.center.x, (first.ascender + last.descender) * 0.5f, 0f), new Vector3(b.size.x, first.ascender - last.descender, 0f));
            }
            var textMin = (Vector2)parent.InverseTransformPoint(tr.TransformPoint(b.min));
            var textMax = (Vector2)parent.InverseTransformPoint(tr.TransformPoint(b.max));
            var left = textMin.x;
            var right = textMax.x;
            var bottom = textMin.y;
            var top = textMax.y;
            if (icon && icon.parent == parent)
            {
                // Notification row (mockup A): icon left of the text line, vertically centred on it.
                var s = ThemeTokens.GetFloat("size.message.icon", 34f);
                icon.sizeDelta = new Vector2(s, s);
                var ip = new Vector2(left - ThemeTokens.GetFloat("size.message.icon.gap", 10f) - s * 0.5f, (bottom + top) * 0.5f);
                var lossy = icon.lossyScale.x / Mathf.Max(1e-6f, parent.lossyScale.x);
                icon.localPosition = new Vector3(ip.x + (icon.pivot.x - 0.5f) * s * lossy, ip.y + (icon.pivot.y - 0.5f) * s * lossy, 0f);
                var iMin = (Vector2)parent.InverseTransformPoint(icon.TransformPoint(icon.rect.min));
                var iMax = (Vector2)parent.InverseTransformPoint(icon.TransformPoint(icon.rect.max));
                left = Mathf.Min(left, iMin.x);
                bottom = Mathf.Min(bottom, iMin.y);
                top = Mathf.Max(top, iMax.y);
            }
            // Icon + text inside the plate's full 9-slice border on every side (user screenshot 2026-10-22: the knot
            // clipped the skill icon), equal margins; the plate grows with the text.
            var rt = _plate.rectTransform;
            rt.anchorMin = rt.anchorMax = parent.pivot;
            rt.pivot = new Vector2(0.5f, 0.5f);
            var (cx, w) = PlayHudMath.PlateSpan(left, right, Border(true), pad);
            var (cy, h) = PlayHudMath.PlateSpan(bottom, top, Border(false), ThemeTokens.GetFloat("size.message.plate.vpad", 6f));
            rt.localPosition = new Vector3(cx, cy, 0f);
            rt.sizeDelta = new Vector2(w, h);
            _plateTopRaw = cy + h * 0.5f + _topShift;
        }

        // Per frame: the notification row (text, icon, plate) moves down just enough to clear the shown party frame grid
        // (PartyMath.NotifyShift); a changed shift re-lays the row out (TopLeft runs again for the same text).
        private void ClearPartyFrame()
        {
            if (!_plate || !(_plate.rectTransform.parent is RectTransform parent)) return;
            var gridBottom = float.NaN;
            // the party frame grid and / or the ammo plate below it (same top-left slot): the lower bottom edge
            var shown = SummonFrames.GridScreenBottom(out var screenY);
            if (AmmoFrame.ScreenBottom(out var ammoY))
            {
                screenY = shown ? Mathf.Min(screenY, ammoY) : ammoY;
                shown = true;
            }
            if (shown)
            {
                var canvas = parent.GetComponentInParent<Canvas>();
                var cam = canvas && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
                shown = RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, new Vector2(0f, screenY), cam, out var local);
                gridBottom = local.y;
            }
            var want = PartyMath.NotifyShift(shown, _plateTopRaw, gridBottom, ThemeTokens.GetFloat("size.message.notify.party.gap", 8f));
            if (Mathf.Abs(want - _topShift) <= 0.5f) return;
            _topShift = want;
            _topText = null;
        }

        // frame_small 9-slice border (theme geometry at reference 100, Restyler.CanvasPpuFactor convention), sides or
        // top / bottom.
        private float Border(bool sides)
        {
            var s = _plate ? _plate.sprite : null;
            if (!s) return 0f;
            var b = sides ? Mathf.Max(s.border.x, s.border.z) : Mathf.Max(s.border.y, s.border.w);
            return b * 100f / Mathf.Max(1f, s.pixelsPerUnit);
        }

        // Theme floor for small HUD texts (size.text.hud.min).
        internal static float HudReadable(float size) => PlayHudMath.Readable(size, ThemeTokens.GetFloat("size.text.hud.min", 18f));

        private void Banner()
        {
            var inst = _hud.m_biomeMsgInstance;
            if (inst != _bannerFor)
            {
                _bannerFor = inst;
                _banner = null;
                if (inst) BuildBanner(inst);
            }
            if (!_banner || !_bannerTitle) return;
            var a = Alpha(_bannerTitle);
            if (RuneSkin.Restyle.RenderGateMath.Concealed(a)) a = 0f;
            if (!Mathf.Approximately(_bannerGroup.alpha, a)) _bannerGroup.alpha = a;
            RuneSkin.Restyle.RenderGate.Set(_bannerGroup, "fade", a == 0f); // faded out = not drawn
            var tt = _bannerTitle.rectTransform;
            _banner.localPosition = tt.localPosition;
            _banner.localScale = tt.localScale;
            // Rules outside the rendered title (Codex r1: knotwork crossed the letters); bounds are in the title's
            // local space, the banner shares its position and scale.
            var b = _bannerTitle.textBounds;
            if (b.size.x > 1f && _ruleL && _ruleR)
            {
                var gap = ThemeTokens.GetFloat("size.banner.rule.gap", 18f);
                var y = b.center.y;
                var l = new Vector2(b.min.x - gap, y);
                var r = new Vector2(b.max.x + gap, y);
                if ((_ruleL.anchoredPosition - l).sqrMagnitude > 0.25f) _ruleL.anchoredPosition = l;
                if ((_ruleR.anchoredPosition - r).sqrMagnitude > 0.25f) _ruleR.anchoredPosition = r;
                var dw = b.size.x + 2f * (gap + _ruleL.sizeDelta.x);
                foreach (var d in _rules)
                    if (d && !Mathf.Approximately(d.sizeDelta.x, dw)) d.sizeDelta = new Vector2(dw, d.sizeDelta.y);
                var bw = dw + 2f * ThemeTokens.GetFloat("size.message.band.pad", 70f);
                if (_band && !Mathf.Approximately(_band.sizeDelta.x, bw)) { _band.sizeDelta = new Vector2(bw, _band.sizeDelta.y); _band.anchoredPosition = new Vector2(b.center.x, y); }
                foreach (var d in _rules)
                    if (d) d.anchoredPosition = new Vector2(b.center.x, Mathf.Sign(d.anchoredPosition.y) * Mathf.Abs(d.anchoredPosition.y));
            }
        }

        private void BuildBanner(GameObject inst)
        {
            var title = Utils.FindChild(inst.transform, "Title");
            if (!title || !title.TryGetComponent<TMP_Text>(out var text)) return;
            _bannerTitle = text;
            foreach (var img in inst.GetComponentsInChildren<Image>(true))
            {
                img.enabled = false;
                _art.Suppressed(img.transform, "vanilla biome banner decor (band + knotwork rules replace it)");
            }
            _art.Note(text, "banner.title", TextLook.Apply(text, "font.display", "banner"), "biome banner title (serif caps)");
            text.fontStyle |= FontStyles.UpperCase;
            text.color = ThemeTokens.GetColor("color.text.header", text.color);
            text.fontSize = ThemeTokens.GetFloat("size.banner.title", text.fontSize);
            text.enableAutoSizing = false;

            _banner = HudArt.Rect("RuneSkin_Banner", title.parent);
            _banner.SetSiblingIndex(title.GetSiblingIndex());
            _banner.anchorMin = _banner.anchorMax = text.rectTransform.anchorMin == text.rectTransform.anchorMax ? text.rectTransform.anchorMin : new Vector2(0.5f, 0.5f);
            _banner.pivot = new Vector2(0.5f, 0.5f);
            _banner.sizeDelta = Vector2.zero;
            _bannerGroup = _banner.gameObject.AddComponent<CanvasGroup>();
            _bannerGroup.blocksRaycasts = false;
            _bannerGroup.interactable = false;

            var w = text.GetPreferredValues(text.text, 0f, 0f).x;
            var h = ThemeTokens.GetFloat("size.banner.band.h", 92f);
            var gap = ThemeTokens.GetFloat("size.banner.rule.gap", 18f);
            var band = _art.Image("Band", _banner, "sprite.band.message", "color.frame", Image.Type.Sliced);
            Centered(band.rectTransform, Vector2.zero, new Vector2(w + 2f * ThemeTokens.GetFloat("size.message.band.pad", 70f) + 2f * 160f, h));
            var rule = ThemeTokens.GetSprite("sprite.banner.rule");
            var ruleSize = rule ? rule.rect.size * (100f / rule.pixelsPerUnit) : new Vector2(156f, 29f);
            var left = _art.Image("RuleLeft", _banner, "sprite.banner.rule", "color.frame", Image.Type.Sliced);
            var right = _art.Image("RuleRight", _banner, "sprite.banner.rule", "color.frame", Image.Type.Sliced);
            // banner_rule pivot = its inner end (0, 0.5): the left rule is mirrored so its flourish points outward.
            foreach (var r in new[] { left.rectTransform, right.rectTransform })
            {
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.pivot = new Vector2(0f, 0.5f);
                r.sizeDelta = ruleSize;
            }
            left.rectTransform.anchoredPosition = new Vector2(-(w * 0.5f + gap), 0f);
            left.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            right.rectTransform.anchoredPosition = new Vector2(w * 0.5f + gap, 0f);
            var d1 = _art.Image("DividerTop", _banner, "sprite.divider", "color.frame", Image.Type.Sliced);
            var d2 = _art.Image("DividerBottom", _banner, "sprite.divider", "color.frame", Image.Type.Sliced);
            var dw = w + 2f * (gap + ruleSize.x);
            Centered(d1.rectTransform, new Vector2(0f, h * 0.5f), new Vector2(dw, 11f));
            Centered(d2.rectTransform, new Vector2(0f, -h * 0.5f), new Vector2(dw, 11f));
            _ruleL = left.rectTransform;
            _ruleR = right.rectTransform;
            _band = band.rectTransform;
            _rules[0] = d1.rectTransform;
            _rules[1] = d2.rectTransform;
            Banner();
        }

        private static void Centered(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private void Unlocks()
        {
            var list = _hud.m_unlockMessages;
            if (list == null || list.Count == 0) return;
            foreach (var go in list)
            {
                if (!go || !_unlocks.Add(go.GetInstanceID())) continue;
                Image plate = null;
                foreach (var img in go.GetComponentsInChildren<Image>(true))
                {
                    if (img.name == "UnlockIcon") continue;
                    if (img.name == "icon_bkg") _art.Reskin(img, "sprite.slot.recess", "color.frame", "unlock message icon socket");
                    else if (img.sprite) { _art.Reskin(img, "sprite.hud.frame.small", "color.frame", "unlock message plate"); img.fillCenter = true; plate = img; _unlockPlates[go] = img.rectTransform; }
                }
                TMP_Text titleText = null, descText = null;
                foreach (var t in go.GetComponentsInChildren<TMP_Text>(true))
                {
                    var title = t.name == "UnlockTitle";
                    if (title)
                    {
                        // Bright gold Norsebold with the shared HUD outline + soft shadow (TextLook.ApplyDisplayTitle).
                        _art.Note(t, "unlock.title", TextLook.ApplyDisplayTitle(t), "unlock message title (display gold, outline + underlay)");
                        titleText = t;
                    }
                    else
                    {
                        _art.Note(t, "unlock.text", TextLook.Apply(t, "font.body", "notify"), "unlock message text");
                        t.color = ThemeTokens.GetColor("color.text.body", t.color);
                        // Clear of the plate's right rim (frame_small border; Codex r3: the body reached it).
                        t.rectTransform.offsetMax -= new Vector2(ThemeTokens.GetFloat("size.message.unlock.inset", 14f), 0f);
                        if (t.name == "UnlockDescription") descText = t;
                    }
                    t.fontSize = HudReadable(t.fontSize);
                    // Long texts shrink (down to the HUD floor) and end in an ellipsis instead of crossing the frame.
                    t.enableAutoSizing = true; t.fontSizeMax = t.fontSize; t.fontSizeMin = HudReadable(0f); t.overflowMode = TextOverflowModes.Ellipsis;
                }
                LayoutUnlock(plate, titleText, descText, go.transform.Find("UnlockMessage/icon_bkg") as RectTransform);
            }
            if (_unlocks.Count > 256) { _unlocks.Clear(); _unlockPlates.Clear(); }
            AlignUnlocks(list);
        }

        // Title fully inside the plate's content rect: below the top rail (the frame sprite's 9-slice top border, read
        // from the sprite, + size.message.unlock.vpad), within the side borders; the icon + description row below it
        // (size.message.unlock.gap); the plate grows down to fit (PlayHudMath.UnlockStack). Vanilla anchored the title on
        // the plate's top edge (user screenshot 2026-10-23: "НОВЫЙ ПРЕДМЕТ" sat on the rail). All rects share the
        // UnlockMessage parent (prefab: UnlockMessage/bkg, /UnlockTitle, /icon_bkg, /UnlockDescription).
        private static void LayoutUnlock(Image plate, TMP_Text title, TMP_Text desc, RectTransform icon)
        {
            if (!plate || !title || !(plate.rectTransform.parent is RectTransform msg) || title.rectTransform.parent != msg)
            {
                Plugin.Log.LogWarning($"[RuneSkin] unlock message layout skipped: plate={(bool)plate} title={(bool)title} (unexpected prefab hierarchy)");
                return;
            }
            var prt = plate.rectTransform;
            var border = SliceBorder(plate);
            var pad = ThemeTokens.GetFloat("size.message.unlock.vpad", 4f);
            var pr = Edges(prt, msg);
            var tr = title.rectTransform;
            var t0 = Edges(tr, msg);
            var innerL = pr.xMin + border.x + pad;
            var innerR = pr.xMax - border.z - pad;
            if (t0.xMin < innerL) tr.offsetMin += new Vector2(innerL - t0.xMin, 0f);
            if (t0.xMax > innerR) tr.offsetMax -= new Vector2(t0.xMax - innerR, 0f);
            var (tTop, tBottom) = LineBox(title, msg);
            float rTop = float.NegativeInfinity, rBottom = float.PositiveInfinity;
            if (icon && icon.parent == msg) { var ir = Edges(icon, msg); rTop = ir.yMax; rBottom = ir.yMin; }
            if (desc && desc.rectTransform.parent == msg) { var (dt, db) = LineBox(desc, msg); rTop = Mathf.Max(rTop, dt); rBottom = Mathf.Min(rBottom, db); }
            var gap = ThemeTokens.GetFloat("size.message.unlock.gap", 2f);
            if (float.IsInfinity(rTop)) rTop = rBottom = tBottom - gap;
            var (ts, rs, bottom) = PlayHudMath.UnlockStack(pr.yMax, tTop, tBottom, rTop, rBottom, border.w, border.y, pad, gap);
            tr.anchoredPosition += new Vector2(0f, ts);
            if (icon && icon.parent == msg) icon.anchoredPosition += new Vector2(0f, rs);
            if (desc && desc.rectTransform.parent == msg) desc.rectTransform.anchoredPosition += new Vector2(0f, rs);
            prt.offsetMin += new Vector2(0f, bottom - pr.yMin);
            UnlockRow(plate, desc, icon, msg);
        }

        // Icon + description row (vanilla order: icon_bkg, then UnlockDescription to its right) as one group centred in
        // the plate's content span under the title: the frame's drawn 9-slice side borders + size.message.plate.pad (the
        // insets of the top-left notification plate); the icon's recess centred on the description's line box. Vanilla
        // put the icon at the plate's left edge, on the frame's ornament (0.23.0 live report, "Новый чертеж").
        private static void UnlockRow(Image plate, TMP_Text desc, RectTransform icon, RectTransform msg)
        {
            if (!icon || icon.parent != msg || !desc || desc.rectTransform.parent != msg) return;
            var border = SliceBorder(plate);
            var pad = ThemeTokens.GetFloat("size.message.plate.pad", 10f);
            var pr = Edges(plate.rectTransform, msg);
            var ir = Edges(icon, msg);
            var dr = desc.rectTransform;
            desc.horizontalAlignment = HorizontalAlignmentOptions.Left;
            var gap = ThemeTokens.GetFloat("size.message.icon.gap", 10f);
            var size = desc.enableAutoSizing ? desc.fontSizeMax : desc.fontSize;
            var auto = desc.enableAutoSizing;
            desc.enableAutoSizing = false;
            var cur = desc.fontSize;
            desc.fontSize = size;
            // +2: the measured width as the rect width must not tip TMP into its ellipsis by rounding.
            var textW = desc.GetPreferredValues(desc.text ?? "", float.PositiveInfinity, float.PositiveInfinity).x + desc.margin.x + desc.margin.z + 2f;
            desc.fontSize = cur;
            desc.enableAutoSizing = auto;
            var (iconShift, textMin, textMax) = PlayHudMath.UnlockRow(pr.xMin + border.x + pad, pr.xMax - border.z - pad, ir.xMin, ir.xMax, gap, textW * Mathf.Abs(dr.localScale.x));
            icon.anchoredPosition += new Vector2(iconShift, 0f);
            var d0 = Edges(dr, msg);
            var k = Mathf.Max(1e-4f, Mathf.Abs(dr.localScale.x));
            dr.offsetMin += new Vector2((textMin - d0.xMin) / k, 0f);
            dr.offsetMax += new Vector2((textMax - d0.xMax) / k, 0f);
            // The (taller) recess keeps its stacked row slot; the line moves onto its centre.
            var (lt, lb) = LineBox(desc, msg);
            var ic = Edges(icon, msg).center.y;
            dr.anchoredPosition += new Vector2(0f, ic - (lt + lb) * 0.5f);
        }

        // 9-slice border of an Image in its rect units (x left, y bottom, z right, w top): sprite pixels over the
        // image's pixels per unit (sprite ppu / canvas reference ppu) times its multiplier.
        private static Vector4 SliceBorder(Image img)
        {
            var s = img ? img.sprite : null;
            if (!s) return Vector4.zero;
            return s.border / Mathf.Max(1e-4f, img.pixelsPerUnit * img.pixelsPerUnitMultiplier);
        }

        private static readonly Vector3[] _corners = new Vector3[4];

        // A rect's corners in another transform's local space.
        private static Rect Edges(RectTransform rt, Transform space)
        {
            rt.GetWorldCorners(_corners);
            var a = (Vector2)space.InverseTransformPoint(_corners[0]);
            var b = (Vector2)space.InverseTransformPoint(_corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        // A text's line box (first line ascender .. last line descender) in another transform's local space.
        private static (float top, float bottom) LineBox(TMP_Text t, Transform space)
        {
            t.ForceMeshUpdate();
            var b = t.textBounds;
            float top = b.max.y, bottom = b.min.y;
            var info = t.textInfo;
            if (info != null && info.lineCount > 0) { top = info.lineInfo[0].ascender; bottom = info.lineInfo[info.lineCount - 1].descender; }
            if (!(top > bottom)) { var r = t.rectTransform.rect; top = r.yMax; bottom = r.yMin; }
            var tt = space.InverseTransformPoint(t.transform.TransformPoint(new Vector3(0f, top, 0f))).y;
            var bb = space.InverseTransformPoint(t.transform.TransformPoint(new Vector3(0f, bottom, 0f))).y;
            return (Mathf.Max(tt, bb), Mathf.Min(tt, bb));
        }

        // Per frame: every unlock slot's y (vanilla slot spacing m_maxUnlockMsgSpace, widened when the grown plates
        // would overlap, below the top-left notification row by size.message.unlock.drop; run 20261022-043546: the
        // unlock plate covered it), and the left edge of every unlock plate on the notification stack's (Codex M4 r3;
        // the notification plate is placed once its text arrives, which can be after the unlock instance was restyled,
        // run 20261022-065352).
        private readonly Vector3[] _ca = new Vector3[4], _cb = new Vector3[4];

        private readonly Dictionary<GameObject, RectTransform> _unlockPlates = new Dictionary<GameObject, RectTransform>();

        private void AlignUnlocks(List<GameObject> list)
        {
            var prefab = _hud.m_unlockMsgPrefab ? _hud.m_unlockMsgPrefab.transform as RectTransform : null;
            var baseY = (prefab ? prefab.anchoredPosition.y : 0f) - ThemeTokens.GetFloat("size.message.unlock.drop", 72f) - _topShift;
            var plateH = 0f;
            foreach (var go in list)
                if (go && go.transform.parent is RectTransform p0 && _unlockPlates.TryGetValue(go, out var pl) && pl)
                    plateH = Mathf.Max(plateH, Edges(pl, p0).height);
            var gap = ThemeTokens.GetFloat("size.message.unlock.stack.gap", 6f);
            var align = _plate && !string.IsNullOrEmpty(_topText);
            if (align) _plate.rectTransform.GetWorldCorners(_ca);
            for (var i = 0; i < list.Count; i++)
            {
                var go = list[i];
                if (!go || !(go.transform is RectTransform urt) || !(urt.parent is RectTransform up)) continue;
                if (prefab)
                {
                    var y = PlayHudMath.UnlockSlotY(baseY, i, _hud.m_maxUnlockMsgSpace, plateH, gap);
                    if (Mathf.Abs(urt.anchoredPosition.y - y) > 0.25f) urt.anchoredPosition = new Vector2(urt.anchoredPosition.x, y);
                }
                if (!align) continue;
                // The visible plate's left edge (the instance root can be a zero-size anchor point).
                (_unlockPlates.TryGetValue(go, out var plate) && plate ? plate : urt).GetWorldCorners(_cb);
                var dx = up.InverseTransformPoint(_ca[0]).x - up.InverseTransformPoint(_cb[0]).x;
                if (Mathf.Abs(dx) > 0.5f) urt.anchoredPosition += new Vector2(dx, 0f);
            }
        }

        public void Record() => SkinDiagnostics.Record(_art.Report(transform));
    }

    [HarmonyPatch]
    public static class MessageAdapter
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly), batched by FastStartup).
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.MessageHud);

        [HarmonyPatch(typeof(MessageHud), nameof(MessageHud.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Awake_Postfix(MessageHud __instance)
        {
            try { MessageSkin.Attach(__instance); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] message HUD restyle failed (vanilla messages stay): {e}"); }
        }
    }
}
