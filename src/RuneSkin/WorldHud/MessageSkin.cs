using System;
using System.Collections.Generic;
using HarmonyLib;
using RuneSkin.Diagnostics;
using RuneSkin.PlayerHud;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
    //   UnlockDescription): plate + icon recess + fonts, restyled once per instance.
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
                _art.Note(center, "message.center", TextLook.Apply(center, "font.display", "message"), "centre message (display serif, outline)");
                center.color = ThemeTokens.GetColor("color.text.body", center.color);
                center.enableAutoSizing = false;
                center.fontSize = ThemeTokens.GetFloat("size.message.font", 26f);
                _centerBand = Behind("RuneSkin_CenterBand", center.rectTransform, "sprite.band.message");
            }
            var top = hud.m_messageText;
            if (top)
            {
                _art.Note(top, "message.topleft", TextLook.Apply(top, "font.display", "notify"), "top-left notification (display serif)");
                top.color = ThemeTokens.GetColor("color.text.body", top.color);
                top.fontSize = ThemeTokens.GetFloat("size.message.notify.font", top.fontSize);
                _plate = Behind("RuneSkin_NotifyPlate", top.rectTransform, "sprite.hud.frame.small");
                // Room for the icon left of the text (the vanilla icon sits under the line).
                // ...and below the hotbar's summon frames (Codex r1: the plate overlapped them).
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

        private Image Behind(string name, RectTransform text, string token)
        {
            if (!text.parent) return null;
            var img = _art.Image(name, text.parent, token, "color.frame", Image.Type.Sliced);
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
            _centerBand.canvasRenderer.SetAlpha(Alpha(t));
            if (t.text == _centerText) return;
            _centerText = t.text;
            if (string.IsNullOrEmpty(_centerText)) return;
            var size = t.GetPreferredValues(_centerText, t.rectTransform.rect.width, 0f);
            var pad = ThemeTokens.GetFloat("size.message.band.pad", 70f);
            var vpad = ThemeTokens.GetFloat("size.message.band.vpad", 14f);
            Around(_centerBand.rectTransform, t.rectTransform, new Vector2(Mathf.Min(size.x, t.rectTransform.rect.width) + 2f * pad, size.y + 2f * vpad), t.alignment);
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
            tr.anchoredPosition = _topBase + new Vector2(slot, 0f);
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
            var rt = _plate.rectTransform;
            rt.anchorMin = rt.anchorMax = parent.pivot;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localPosition = new Vector3((left + right) * 0.5f, (bottom + top) * 0.5f, 0f);
            rt.sizeDelta = new Vector2(right - left + 2f * pad + 2f * KnotInset(), top - bottom + 2f * ThemeTokens.GetFloat("size.message.plate.vpad", 6f));
        }

        // frame_small knot ends (the plate's 9-slice side border, ref px): the text keeps clear of them on both sides.
        private float KnotInset()
        {
            var s = _plate ? _plate.sprite : null;
            return s ? Mathf.Max(s.border.x, s.border.z) * 100f / Mathf.Max(1f, s.pixelsPerUnit) * ThemeTokens.GetFloat("share.message.plate.knot", 0.6f) : 0f;
        }

        // A band centred on the text's visible line (alignment-aware horizontally).
        private static void Around(RectTransform band, RectTransform text, Vector2 size, TextAlignmentOptions align)
        {
            band.anchorMin = text.anchorMin;
            band.anchorMax = text.anchorMax;
            band.pivot = new Vector2(0.5f, 0.5f);
            band.sizeDelta = size;
            if (band.anchorMin != band.anchorMax) band.sizeDelta = size - (band.anchorMax - band.anchorMin) * ((RectTransform)text.parent).rect.size;
            var r = text.rect;
            band.localPosition = text.localPosition + new Vector3((0.5f - text.pivot.x) * r.width, (0.5f - text.pivot.y) * r.height, 0f);
        }

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
            if (!Mathf.Approximately(_bannerGroup.alpha, a)) _bannerGroup.alpha = a;
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
                // Below the top-left notification row (moved down by size.message.notify.drop; run 20261022-043546: the
                // unlock plate covered it). Vanilla sets the slot position once at Instantiate (UpdateUnlockMsg :337-341).
                if (go.transform is RectTransform urt) urt.anchoredPosition -= new Vector2(0f, ThemeTokens.GetFloat("size.message.unlock.drop", 72f));
                foreach (var img in go.GetComponentsInChildren<Image>(true))
                {
                    if (img.name == "UnlockIcon") continue;
                    if (img.name == "icon_bkg") _art.Reskin(img, "sprite.slot.recess", "color.frame", "unlock message icon socket");
                    else if (img.sprite) { _art.Reskin(img, "sprite.hud.frame.small", "color.frame", "unlock message plate"); img.fillCenter = true; _unlockPlates[go] = img.rectTransform; }
                }
                foreach (var t in go.GetComponentsInChildren<TMP_Text>(true))
                {
                    var title = t.name == "UnlockTitle";
                    _art.Note(t, title ? "unlock.title" : "unlock.text", TextLook.Apply(t, title ? "font.display" : "font.body", "notify"), "unlock message text");
                    t.color = ThemeTokens.GetColor(title ? "color.text.item.title" : "color.text.body", t.color);
                    // Clear of the plate's right rim (frame_small border; Codex r3: the body reached it), long texts shrink.
                    var tr = t.rectTransform;
                    tr.offsetMax -= new Vector2(ThemeTokens.GetFloat("size.message.unlock.inset", 14f), 0f);
                    if (!title) { t.enableAutoSizing = true; t.fontSizeMax = t.fontSize; t.fontSizeMin = Mathf.Max(10f, t.fontSize * 0.7f); t.overflowMode = TextOverflowModes.Ellipsis; }
                }
            }
            if (_unlocks.Count > 256) { _unlocks.Clear(); _unlockPlates.Clear(); }
            AlignUnlocks(list);
        }

        // Left edge of every unlock plate on the notification stack's (Codex M4 r3), per frame: the notification plate
        // is placed once its text arrives (TopLeft), which can be after the unlock instance was restyled (run
        // 20261022-065352: a one-time shift at restyle found the plate not yet placed). Only while the plate exists.
        private readonly Vector3[] _ca = new Vector3[4], _cb = new Vector3[4];

        private readonly Dictionary<GameObject, RectTransform> _unlockPlates = new Dictionary<GameObject, RectTransform>();

        private void AlignUnlocks(List<GameObject> list)
        {
            if (!_plate || string.IsNullOrEmpty(_topText)) return;
            _plate.rectTransform.GetWorldCorners(_ca);
            foreach (var go in list)
            {
                if (!go || !(go.transform is RectTransform urt) || !(urt.parent is RectTransform up)) continue;
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
