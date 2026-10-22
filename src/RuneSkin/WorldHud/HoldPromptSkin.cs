using System;
using HarmonyLib;
using RuneSkin.Api;
using RuneSkin.API;
using RuneSkin.Ownership;
using RuneSkin.PlayerHud;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.WorldHud
{
    // Hold-to-confirm prompt of the action HUD family (API Skin.RegisterHoldPrompt, ApiVersion 10), e.g. Hamingja's
    // "hold G to give up" while downed: the key letter on plate_key inside the guardian-power socket, its ring
    // (gp_ring, Filled Radial360 from the top, clockwise) filling with the hold in the rune glow over a dim track, the
    // action label right of it and an optional caption (bleed-out countdown) above in the ember serif. Bottom centre
    // under Hud.m_rootObject, so the HUD hide toggle applies. The owning mod drives everything; nothing here reads input.
    public sealed class HoldPromptSkin : MonoBehaviour
    {
        public static HoldPromptSkin Instance { get; private set; }
        private readonly HudArt _art = new HudArt(OwnershipRegistry.ActionHud);
        private Transform _parent;
        private RectTransform _root, _socket;
        private Image _fill;
        private TMP_Text _key, _label, _caption;
        private string _shownKey, _shownLabel, _shownCaption;
        private float _ring, _gap;

        // Harness read side.
        public bool Shown => _root && _root.gameObject.activeSelf;
        public float Fill => _fill && _fill.enabled ? _fill.fillAmount : 0f;
        public string KeyText => _key ? _key.text : null;
        public string LabelText => _label ? _label.text : null;
        public string CaptionText => _caption && _caption.gameObject.activeSelf ? _caption.text : null;
        public RectTransform Root => _root;

        public static void Attach(Hud hud)
        {
            if (!hud || !hud.m_rootObject || hud.GetComponent<HoldPromptSkin>()) return;
            hud.gameObject.AddComponent<HoldPromptSkin>().Init(hud);
        }

        private void Init(Hud hud)
        {
            Instance = this;
            _parent = hud.m_rootObject.transform;
            PlayHud.SyncPpu(_art, _parent);
            _ring = ThemeTokens.GetFloat("size.hold.ring", 64f);
            _gap = ThemeTokens.GetFloat("size.hold.gap", 10f);

            _root = HudArt.Rect("RuneSkin_HoldPrompt", _parent);
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0f);
            _root.pivot = new Vector2(0.5f, 0f);
            _root.anchoredPosition = new Vector2(0f, ThemeTokens.GetFloat("size.hold.y", 184f));
            _root.sizeDelta = new Vector2(_ring, _ring);

            _socket = HudArt.Rect("Socket", _root);
            _socket.anchorMin = _socket.anchorMax = new Vector2(0.5f, 0f);
            _socket.pivot = new Vector2(0.5f, 0.5f);
            _socket.sizeDelta = new Vector2(_ring, _ring);
            Stretch(_art.Image("Rim", _socket, "sprite.hud.gp.socket", "color.hud.socket", Image.Type.Simple).rectTransform, 0f);
            var track = _art.Image("Track", _socket, "sprite.hud.gp.ring", "color.hud.trough", Image.Type.Simple);
            Stretch(track.rectTransform, _ring * 0.15f); // over the socket's inner rim, as GuardianSocket
            _fill = _art.Image("Fill", _socket, "sprite.hud.gp.ring", "color.hud.glyph", Image.Type.Simple);
            Stretch(_fill.rectTransform, _ring * 0.15f);
            if (_fill.sprite)
            {
                _fill.type = Image.Type.Filled;
                _fill.fillMethod = Image.FillMethod.Radial360;
                _fill.fillOrigin = (int)Image.Origin360.Top;
                _fill.fillClockwise = true;
                _fill.fillAmount = 0f;
            }
            var plate = _art.Image("Plate", _socket, "sprite.hud.plate.key", "color.frame", Image.Type.Sliced);
            Stretch(plate.rectTransform, _ring * 0.3f);
            _key = _art.Text("Key", plate.rectTransform, "font.body", "color.text.body", ThemeTokens.GetFloat("size.hold.key.font", 20f), TextAlignmentOptions.Center);
            _key.fontStyle = FontStyles.Bold;
            Stretch(_key.rectTransform, 0f);

            _label = _art.Text("Label", _root, "font.display", "color.text.body", ThemeTokens.GetFloat("size.hold.font", 24f), TextAlignmentOptions.Left);
            var lr = _label.rectTransform;
            lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 0f);
            lr.pivot = new Vector2(0f, 0.5f);
            lr.sizeDelta = new Vector2(320f, _ring);

            _caption = _art.Text("Caption", _root, "font.display", "color.text.ember", ThemeTokens.GetFloat("size.hold.caption.font", 26f), TextAlignmentOptions.Bottom);
            var cr = _caption.rectTransform;
            cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0f);
            cr.pivot = new Vector2(0.5f, 0f);
            cr.sizeDelta = new Vector2(480f, 36f);
            cr.anchoredPosition = new Vector2(0f, _ring + ThemeTokens.GetFloat("size.hold.caption.gap", 6f));
            _caption.gameObject.SetActive(false);

            Layout();
            _root.gameObject.SetActive(false);
            Plugin.Log.LogInfo("[RuneSkin] hold prompt attached");
        }

        // After every Awake postfix (PlayHudAdapter clears the report list there).
        private void Start()
        {
            var root = _parent;
            PlayHud.Reports.Add(() => Instance == this && root ? _art.Report(root) : null);
        }

        private void LateUpdate()
        {
            if (!_root) return;
            var player = Player.m_localPlayer;
            var state = default(HoldPromptState);
            var on = player && Providers.TryHold(player, out state);
            if (_root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
            if (!on) return;

            var relayout = false;
            if (!string.Equals(state.Key, _shownKey, StringComparison.Ordinal))
            {
                _shownKey = state.Key;
                _key.text = state.Key ?? "";
            }
            if (!string.Equals(state.Label, _shownLabel, StringComparison.Ordinal))
            {
                _shownLabel = state.Label;
                _label.text = state.Label ?? "";
                relayout = true;
            }
            if (!string.Equals(state.Caption, _shownCaption, StringComparison.Ordinal))
            {
                _shownCaption = state.Caption;
                var has = !string.IsNullOrEmpty(state.Caption);
                if (has) _caption.text = state.Caption;
                if (_caption.gameObject.activeSelf != has) _caption.gameObject.SetActive(has);
            }
            if (relayout) Layout();

            var want = state.Progress;
            var showFill = want > 0f;
            if (_fill.enabled != showFill && _fill.sprite) _fill.enabled = showFill;
            if (HoldPromptMath.FillChanged(_fill.fillAmount, want, _ring)) _fill.fillAmount = want;
        }

        private void Layout()
        {
            var labelWidth = string.IsNullOrEmpty(_label.text) ? 0f : _label.preferredWidth;
            _socket.anchoredPosition = new Vector2(HoldPromptMath.RingCentreX(_ring, _gap, labelWidth), _ring * 0.5f);
            _label.rectTransform.anchoredPosition = new Vector2(HoldPromptMath.LabelLeftX(_ring, _gap, labelWidth), _ring * 0.5f);
            _label.rectTransform.sizeDelta = new Vector2(Mathf.Max(labelWidth, 1f) + 4f, _ring);
        }

        private static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }

    [HarmonyPatch]
    public static class HoldPromptAdapter
    {
        [HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Awake_Postfix(Hud __instance)
        {
            if (!PlayHud.Owned(OwnershipRegistry.ActionHud)) return;
            try { HoldPromptSkin.Attach(__instance); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] hold prompt failed (owning mods draw their own): {e}"); }
        }
    }
}
