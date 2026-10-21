using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.PlayerHud
{
    // Guardian (boss) power socket: own carved round socket instead of vanilla Hud.m_gpRoot (Hud.UpdateGuardianPower,
    // fed by Player.GetGuardianPowerHUD: m_guardianSE + m_guardianPowerCooldown; Player.ActivateGuardianPower adds the
    // SE to SEMan and sets the cooldown to m_guardianSE.m_cooldown). States: none (hidden), ready (bright icon, full
    // ring), cooldown (dim icon, ring = cooldown left / m_cooldown, timer), active (bright icon, glyph-coloured ring =
    // remaining / ttl of the power's own SE, timer). The active SE itself is kept out of both effect strips.
    public sealed class GuardianSocket
    {
        public readonly RectTransform Root;
        private readonly Image _rim, _ring, _icon;
        private readonly TMP_Text _timer;
        private StatusEffect _shownSe;
        private int _shownSeconds = int.MinValue;
        private int _shownState = -1;
        public int ActiveHash { get; private set; }
        public bool Visible => Root.gameObject.activeSelf;
        public float Size => ThemeTokens.GetFloat("size.hud.gp.cell", 56f);

        public GuardianSocket(HudArt art, Transform parent)
        {
            Root = HudArt.Rect("GuardianPower", parent);
            var size = Size;
            Root.sizeDelta = new Vector2(size, size);
            _rim = art.Image("Rim", Root, "sprite.hud.gp.socket", "color.hud.socket", Image.Type.Simple, raycast: true);
            // Pointer / touch activation like vanilla's gp button (Hud.OnGuardianPowerClicked -> Player.StartGuardianPower).
            var button = Root.gameObject.AddComponent<Button>();
            button.targetGraphic = _rim;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => { var p = Player.m_localPlayer; if (p) p.StartGuardianPower(); });
            Stretch(_rim.rectTransform, 0f);
            _ring = art.Image("Ring", Root, "sprite.hud.gp.ring", "color.hud.food.ring", Image.Type.Simple);
            Stretch(_ring.rectTransform, size * 0.15f); // over the socket's inner rim (art notes)
            if (_ring.sprite)
            {
                _ring.type = Image.Type.Filled;
                _ring.fillMethod = Image.FillMethod.Radial360;
                _ring.fillOrigin = (int)Image.Origin360.Top;
                _ring.fillClockwise = false;
            }
            _icon = art.Image("Icon", Root, null, "color.frame", Image.Type.Simple);
            _icon.preserveAspect = true;
            Stretch(_icon.rectTransform, size * 0.22f);
            _timer = art.Text("Timer", Root, "font.body", "color.hud.timer", ThemeTokens.GetFloat("size.hud.font.timer", 13f), TextAlignmentOptions.Bottom);
            var tr = _timer.rectTransform;
            tr.sizeDelta = new Vector2(size + 16f, size * 0.4f);
            tr.anchoredPosition = new Vector2(-8f, -size * 0.3f);
            Root.gameObject.SetActive(false);
        }

        public void Update(Player player)
        {
            player.GetGuardianPowerHUD(out var se, out var cooldown);
            var on = se;
            if (Root.gameObject.activeSelf != on) Root.gameObject.SetActive(on);
            if (!on)
            {
                ActiveHash = 0;
                _shownSe = null;
                return;
            }
            if (_shownSe != se)
            {
                _shownSe = se;
                _icon.sprite = se.m_icon;
                _icon.enabled = se.m_icon;
                _shownState = -1;
                _shownSeconds = int.MinValue;
            }
            var hash = se.NameHash();
            var active = player.GetSEMan().GetStatusEffect(hash);
            ActiveHash = hash;
            int state;
            float fill, left;
            if (active)
            {
                state = 2;
                left = StatusSource.Remaining(active);
                fill = active.m_ttl > 0f ? Mathf.Clamp01(left / active.m_ttl) : 1f;
            }
            else if (cooldown > 0f)
            {
                state = 1;
                left = cooldown;
                fill = se.m_cooldown > 0f ? Mathf.Clamp01(cooldown / se.m_cooldown) : 0f;
            }
            else
            {
                state = 0;
                left = 0f;
                fill = 1f;
            }
            if (state != _shownState)
            {
                _shownState = state;
                _icon.color = state == 1 ? ThemeTokens.GetColor("color.hud.gp.cooldown", new Color(1f, 1f, 1f, 0.35f)) : Color.white;
                _ring.color = ThemeTokens.GetColor(state == 2 ? "color.hud.glyph" : state == 1 ? "color.hud.gp.ring.cooldown" : "color.hud.food.ring", Color.white);
            }
            if (_ring.sprite && Mathf.Abs(_ring.fillAmount - fill) > 0.002f) _ring.fillAmount = fill;
            var seconds = left > 0f ? Mathf.CeilToInt(left) : 0;
            if (seconds == _shownSeconds) return;
            _shownSeconds = seconds;
            if (seconds <= 0) _timer.text = "";
            else if (seconds < 60) _timer.SetText("{0}", seconds);
            else if (seconds < 3600) _timer.SetText("{0}:{1:00}", seconds / 60, seconds % 60);
            else _timer.SetText("{0}m", Mathf.CeilToInt(seconds / 60f));
        }

        private static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
