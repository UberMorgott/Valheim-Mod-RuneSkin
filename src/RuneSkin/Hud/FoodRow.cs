using System.Collections.Generic;
using RuneSkin.Api;
using RuneSkin.API;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.PlayerHud
{
    // Food sockets (ui-skin-engine-plan §8a): N = max(capacity, eaten foods). Capacity from the "food" capacity
    // provider (MorgottTweaks Alchemist gem: 4), else vanilla 3 (Player.m_maxFoods const, Player.cs:330). Own pooled
    // sockets, never Hud.m_foodBars (a fixed prefab array, Hud.cs:1017). Per socket: rim (gem kind from the provider),
    // icon (pulses when the food can be eaten again, Hud.cs:1028-1031), remaining-time ring and vanilla's timer text
    // units (m_time / Game.m_foodRate: >= 60 -> "Xm", else "Xs", Hud.cs:1037-1046).
    public sealed class FoodRow
    {
        public const int VanillaCapacity = 3;

        private sealed class Socket
        {
            public RectTransform Root;
            public Image Rim, Icon, Ring;
            public TMP_Text Timer;
            public bool Gem;
            public int ShownValue = int.MinValue;
            public bool ShownMinutes;
            public ItemDrop.ItemData ShownItem;
        }

        private readonly HudArt _art;
        public readonly RectTransform Root;
        private readonly List<Socket> _sockets = new List<Socket>(5);
        public int Count { get; private set; }
        public float Width { get; private set; }
        public float Height { get; private set; }
        // How far the timer text reaches below a socket (ref px).
        public float TimerDrop => ThemeTokens.GetFloat("size.hud.food.cell", 46f) * 0.28f;

        public FoodRow(HudArt art, Transform parent)
        {
            _art = art;
            Root = HudArt.Rect("Food", parent);
            // Dark small-frame group behind the sockets (mockup-1 bottom-left), padded by size.hud.food.pad.
            _group = art.Image("Group", Root, "sprite.hud.frame.small", "color.frame", Image.Type.Sliced);
        }

        private readonly Image _group;

        // zigzag: the HUD checkerboard (stacked profile); false = one straight row (inventory band, layout-a2 bottom:
        // diamonds side by side, the band height is one socket).
        public void Update(Player player, float maxWidth, bool zigzag = true)
        {
            var foods = player.GetFoods();
            var capacity = VanillaCapacity;
            var extraFrom = int.MaxValue;
            string extraGlyph = null;
            if (Providers.TryCapacity("food", player, out var cap))
            {
                capacity = cap.Count;
                extraFrom = cap.ExtraFrom;
                extraGlyph = cap.ExtraGlyphToken;
            }
            var n = Mathf.Max(capacity, foods.Count);
            var cell = ThemeTokens.GetFloat("size.hud.food.cell", 46f);
            var gap = ThemeTokens.GetFloat("size.hud.food.gap", 6f);
            while (_sockets.Count < n) _sockets.Add(Make(_sockets.Count, cell));
            _ = maxWidth; // one zigzag row: food counts stay small (vanilla 3, gem 4)
            // Checkerboard zigzag (AugaSkin geometry, user call): equal diagonal steps, odd sockets raised, facing
            // edges parallel with an even gap (HudMath.DiamondStep), every cell on whole physical pixels.
            // Step of whole physical px (lossy scale = physical px per unit on the overlay HUD canvas): every socket
            // lands on the same sub-pixel phase and the diamonds rasterise identically.
            // Icon inset: the stacked HUD keeps the checkerboard's 22%; the band's larger sockets show the food bigger
            // (mockup layout-a2: the dish fills the diamond).
            var inset = cell * (zigzag ? 0.22f : ThemeTokens.GetFloat("share.hud.band.food.icon.inset", 0.15f));
            var step = HudMath.Snap(zigzag ? HudMath.DiamondStep(cell, gap) : cell + gap, Root.lossyScale.y);
            Count = n;
            Width = n == 0 ? 0f : (n - 1) * step + cell;
            Height = n == 0 ? 0f : n == 1 || !zigzag ? cell : cell + step;
            Root.sizeDelta = new Vector2(Width, Height);
            var pad = ThemeTokens.GetFloat("size.hud.food.pad", 8f);
            var gr = _group.rectTransform;
            gr.anchorMin = Vector2.zero;
            gr.anchorMax = Vector2.one;
            gr.offsetMin = new Vector2(-pad, -pad);
            gr.offsetMax = new Vector2(pad, pad);
            if (_group.gameObject.activeSelf != (n > 0)) _group.gameObject.SetActive(n > 0);
            for (var i = 0; i < _sockets.Count; i++)
            {
                var s = _sockets[i];
                var on = i < n;
                if (s.Root.gameObject.activeSelf != on) s.Root.gameObject.SetActive(on);
                if (!on) continue;
                HudMath.DiamondCell(i, step, out var cx, out var cy);
                var pos = new Vector2(cx, zigzag ? cy : 0f);
                if (s.Root.anchoredPosition != pos) s.Root.anchoredPosition = pos;
                var ir = s.Icon.rectTransform;
                if (Mathf.Abs(ir.offsetMin.x - inset) > 0.01f) { ir.offsetMin = new Vector2(inset, inset); ir.offsetMax = new Vector2(-inset, -inset); }
                var gem = i >= extraFrom;
                if (gem != s.Gem)
                {
                    s.Gem = gem;
                    _art.Restyle(s.Rim, gem ? extraGlyph ?? "sprite.hud.socket.gem" : "sprite.hud.socket", "color.hud.socket", Image.Type.Simple);
                }
                Fill(s, i < foods.Count ? foods[i] : null);
            }
        }

        private Socket Make(int index, float cell)
        {
            var s = new Socket { Root = HudArt.Rect("Socket" + index, Root) };
            s.Root.sizeDelta = new Vector2(cell, cell);
            s.Rim = _art.Image("Rim", s.Root, "sprite.hud.socket", "color.hud.socket", Image.Type.Simple);
            Stretch(s.Rim.rectTransform, 0f);
            s.Ring = _art.Image("Ring", s.Root, "sprite.hud.food.ring", "color.hud.food.ring", Image.Type.Simple);
            Stretch(s.Ring.rectTransform, 0f);
            if (s.Ring.sprite)
            {
                s.Ring.type = Image.Type.Filled;
                s.Ring.fillMethod = Image.FillMethod.Radial360;
                s.Ring.fillOrigin = (int)Image.Origin360.Top;
                s.Ring.fillClockwise = false;
            }
            s.Icon = _art.Image("Icon", s.Root, null, "color.frame", Image.Type.Simple);
            s.Icon.preserveAspect = true;
            Stretch(s.Icon.rectTransform, cell * 0.22f);
            s.Timer = _art.Text("Timer", s.Root, "font.body", "color.hud.timer", ThemeTokens.GetFloat("size.hud.font.timer", 13f), TextAlignmentOptions.Bottom);
            var tr = s.Timer.rectTransform;
            tr.sizeDelta = new Vector2(cell, cell * 0.4f);
            tr.anchoredPosition = new Vector2(0f, -cell * 0.28f);
            return s;
        }

        private static void Fill(Socket s, Player.Food food)
        {
            var has = food != null && food.m_item != null;
            if (s.Icon.gameObject.activeSelf != has) s.Icon.gameObject.SetActive(has);
            // The ring carries the time (mockup); the text shows only in the last minute (vanilla blinks it, Hud.cs:1043-1046).
            var urgent = has && food.m_time / Game.m_foodRate < 60f;
            if (s.Timer.gameObject.activeSelf != urgent) s.Timer.gameObject.SetActive(urgent);
            if (s.Ring.gameObject.activeSelf != (has && s.Ring.sprite)) s.Ring.gameObject.SetActive(has && s.Ring.sprite);
            var empty = ThemeTokens.GetColor(has ? "color.hud.socket" : "color.hud.socket.empty", Color.white);
            if (s.Rim.color != empty) s.Rim.color = empty;
            if (!has)
            {
                s.ShownItem = null;
                return;
            }
            if (s.ShownItem != food.m_item)
            {
                s.ShownItem = food.m_item;
                s.Icon.sprite = food.m_item.GetIcon();
                s.ShownValue = int.MinValue;
            }
            var c = food.CanEatAgain() ? new Color(1f, 1f, 1f, 0.7f + Mathf.Sin(Time.time * 5f) * 0.3f) : Color.white;
            if (s.Icon.color != c) s.Icon.color = c;
            var burn = food.m_item.m_shared.m_foodBurnTime;
            if (s.Ring.gameObject.activeSelf)
            {
                var f = burn > 0f ? Mathf.Clamp01(food.m_time / burn) : 0f;
                if (Mathf.Abs(s.Ring.fillAmount - f) > 0.002f) s.Ring.fillAmount = f;
            }
            var t = food.m_time / Game.m_foodRate;
            var minutes = t >= 60f;
            var v = minutes ? Mathf.CeilToInt(t / 60f) : Mathf.FloorToInt(t);
            if (v != s.ShownValue || minutes != s.ShownMinutes)
            {
                s.ShownValue = v;
                s.ShownMinutes = minutes;
                s.Timer.SetText(minutes ? "{0}m" : "{0}s", v);
            }
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
