using TMPro;
using UnityEngine;

namespace RuneSkin.Restyle
{
    // The readable floor measured where a text renders (m5-text-min): font units of a text that draw floorPx screen
    // pixels on a 1080 px screen (canvas scaler, GUI scale and scaled subtrees included). One measure for every writer
    // of a floor: the pre-world sweep raised a save-row column to this size every 30 frames while TextInset lowered its
    // auto-size minimum to the canvas-unit floor every 10, and the name flipped between full and ellipsized (0.23.0
    // live report, Управление сохранениями).
    public static class Readable
    {
        // 0 when the text is not on a screen-space canvas (no screen measure).
        public static float Units(TMP_Text t, float floorPx)
        {
            if (!t || Screen.height <= 0) return 0f;
            var canvas = t.canvas;
            if (!canvas) return 0f;
            var root = canvas.rootCanvas;
            if (root.renderMode == RenderMode.WorldSpace) return 0f;
            var cam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            var p0 = RectTransformUtility.WorldToScreenPoint(cam, t.transform.TransformPoint(Vector3.zero));
            var p1 = RectTransformUtility.WorldToScreenPoint(cam, t.transform.TransformPoint(Vector3.up));
            return LayoutMath.ReadableUnits(floorPx, Vector2.Distance(p0, p1), Screen.height);
        }
    }
}
