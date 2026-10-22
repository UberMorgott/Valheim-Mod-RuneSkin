using RuneSkin.Theme;
using UnityEngine;

namespace RuneSkin.PreWorld
{
    // Character select (mockup m5-char-a): the selected character stands large in the free area right of the
    // character list, feet above the action bar. Vanilla flies the main camera to FejdStartup.m_cameraMarkerCharacter
    // while m_characterSelectScreen is active (UpdateCamera, FejdStartup.cs:2599-2621, SmoothDamp to the marker every
    // frame) and the preview stands at m_characterPreviewPoint (:3051); its marker frames the character small at the
    // right edge (Codex M5: "preview small and far from the list"). This moves the marker, keeping its rotation, so the
    // preview point lands at a chosen viewport spot and height for the current aspect and list width; the create panel
    // and every other screen get the vanilla marker back.
    public sealed class CharacterFraming : MonoBehaviour
    {
        private FejdStartup _fejd;
        private Transform _marker;
        private Vector3 _origPos;
        private Camera _cam;
        private readonly Vector3[] _corners = new Vector3[4];

        public static void Ensure(FejdStartup fejd)
        {
            if (!fejd || !fejd.m_cameraMarkerCharacter || !fejd.m_characterPreviewPoint || fejd.GetComponent<CharacterFraming>()) return;
            var f = fejd.gameObject.AddComponent<CharacterFraming>();
            f._fejd = fejd;
            f._marker = fejd.m_cameraMarkerCharacter;
            f._origPos = f._marker.position;
            f._cam = fejd.m_mainCamera ? fejd.m_mainCamera.GetComponent<Camera>() : null;
        }

        private void Update()
        {
            if (!_marker) return;
            var want = _origPos;
            if (_fejd && _cam && _fejd.m_selectCharacterPanel && _fejd.m_selectCharacterPanel.activeInHierarchy)
                want = Framed(want);
            if ((_marker.position - want).sqrMagnitude > 1e-6f) _marker.position = want;
        }

        private void OnDestroy()
        {
            if (_marker) _marker.position = _origPos;
        }

        private Vector3 Framed(Vector3 fallback)
        {
            var list = _fejd.m_selectCharacterPanel.transform.Find(CharacterList.PanelName) as RectTransform;
            if (!list || Screen.width <= 0 || Screen.height <= 0) return fallback;
            var listRight = ScreenRect(list).xMax / Screen.width;
            var bar = _fejd.m_selectCharacterPanel.transform.Find("BottomWindow") as RectTransform;
            var floor = bar ? ScreenRect(bar).yMax / Screen.height : 0.12f;
            var bottom = Mathf.Clamp01(floor + ThemeTokens.GetFloat("share.charframe.floor.gap", 0.02f));
            var top = ThemeTokens.GetFloat("share.charframe.top", 0.93f);
            var share = ThemeTokens.GetFloat("share.charframe.x", 0.4f);
            var vx = listRight + (1f - listRight) * share;
            var body = ThemeTokens.GetFloat("size.charframe.body", 1.9f);
            return PreWorldMath.FrameMarker(_marker.rotation, _fejd.m_characterPreviewPoint.position, body, _cam.fieldOfView, _cam.aspect, vx, bottom, top, fallback);
        }

        private Rect ScreenRect(RectTransform rt)
        {
            var canvas = rt.GetComponentInParent<Canvas>();
            var uiCam = canvas && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            rt.GetWorldCorners(_corners);
            var min = RectTransformUtility.WorldToScreenPoint(uiCam, _corners[0]);
            var max = RectTransformUtility.WorldToScreenPoint(uiCam, _corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
