using RuneSkin.InventoryScreen;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;

namespace RuneSkin.InfoScreens
{
    // Achievements (InventoryGui.m_achievementsPanel: a full-screen root - blur, darken, the AchFrame window, the AchDetails
    // popup) as an info window like skills / trophies: AchFrame fills the docked right panel (InfoArt.Panel: right edge =
    // the crafting column's, size.info.achievements.w wide, never past the centre column), moved by its own header
    // (WindowMove.Achievements) on top of the inventory block offset. The header is the one RuneWindowHeader: title = the frame's "topic", the completion
    // count ("completionRate") in its aux slot; the list container fills the body above the close row (close button
    // centred, the cheated note right of it). Before: the vanilla frame stayed centred on the screen (1310 x 800 at
    // 1920x1080) over the character column (run 20261024-200455, info-window-overlap-Achievements). Without the inventory
    // layout (no info box) the vanilla placement stays.
    public sealed class AchievementsWindow : MonoBehaviour
    {
        private RectTransform _frame, _canvas;

        public static void Ensure(InventoryGui gui)
        {
            if (!gui || !gui.m_achievementsPanel) return;
            var root = gui.m_achievementsPanel.transform;
            if (root.GetComponent<AchievementsWindow>()) return;
            if (!(root.Find("AchFrame") is RectTransform frame))
            {
                Plugin.Log.LogWarning("[RuneSkin] achievements: no AchFrame under the panel (vanilla placement stays)");
                return;
            }
            var canvas = gui.GetComponentInParent<Canvas>();
            var w = root.gameObject.AddComponent<AchievementsWindow>();
            w._frame = frame;
            w._canvas = canvas ? (RectTransform)canvas.rootCanvas.transform : null;
        }

        private void LateUpdate() => Layout();

        private void Layout()
        {
            var inv = InventorySkin.Instance;
            if (!inv || !_canvas || !_frame) return;
            var k = inv.SkinScaleValue;
            var box = InfoArt.Panel(inv, WindowMove.Achievements, ThemeTokens.GetFloat("size.info.achievements.w", 1000f) * k, _canvas);
            if (box.IsEmpty) return;
            Geo.SetScale(_frame, 1f);
            Geo.Place(_canvas, _frame, box);
            var pad = ThemeTokens.GetFloat("size.info.pad", 22f) * k;
            var gap = 8f * k;
            var headerH = WindowHeaderMath.HeaderHeight(ThemeTokens.GetFloat("size.win.header", 40f), k);
            var header = RuneWindowHeader.Ensure(_frame, WindowMove.Achievements);
            TMP_Text count = null;
            RectTransform container = null, close = null, cheated = null;
            foreach (var c in Children.Of(_frame))
            {
                if (!(c is RectTransform rt)) continue;
                switch (c.name)
                {
                    case "topic" when c.TryGetComponent<TMP_Text>(out var title):
                        if (header) header.Adopt(title, "$runeskin_win_achievements");
                        break;
                    case "completionRate" when c.TryGetComponent<TMP_Text>(out var rate):
                        count = rate;
                        break;
                    case "AchContainer": container = rt; break;
                    case "Closebutton": close = rt; break;
                    case "cheatedText": cheated = rt; break;
                }
            }
            var countW = count ? ThemeTokens.GetFloat("size.info.count.w", 150f) * k : 0f;
            if (header)
            {
                header.Place(_canvas, new Box(box.X, box.Y, box.W, headerH), null, countW);
                if (count)
                {
                    Geo.Place(_canvas, count.rectTransform, header.AuxBox);
                    InfoArt.Label(count, TextAlignmentOptions.MidlineRight, header.AuxBox.H * 0.5f, 13f);
                }
            }
            var inner = new Box(box.X + pad, box.Y + headerH + gap, Mathf.Max(0f, box.W - 2f * pad), Mathf.Max(0f, box.H - headerH - gap - pad));
            var rowH = close && close.gameObject.activeSelf ? 46f * k : 0f;
            var bodyH = Mathf.Max(0f, inner.H - (rowH > 0f ? rowH + gap : 0f));
            if (container) Geo.Place(_canvas, container, new Box(inner.X, inner.Y, inner.W, bodyH));
            if (rowH <= 0f) return;
            var y = inner.Y + bodyH + gap;
            var closeW = Mathf.Min(170f * k, inner.W);
            Geo.Place(_canvas, close, new Box(inner.X + (inner.W - closeW) / 2f, y, closeW, rowH));
            if (cheated && cheated.gameObject.activeSelf)
            {
                var x = inner.X + (inner.W + closeW) / 2f + gap;
                Geo.Place(_canvas, cheated, new Box(x, y, Mathf.Max(0f, inner.Right - x), rowH));
            }
        }
    }
}
