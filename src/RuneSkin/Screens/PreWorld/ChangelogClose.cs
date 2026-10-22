using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.PreWorld
{
    // Close control of the changelog window (FejdStartup.m_changeLog). Vanilla has none: the window closes only through
    // the bottom-left toggle that opened it (FejdStartup.OnButtonShowChangelog :1222-1227, no Esc handling in
    // FejdStartup.Update). RuneSkin adds a square icon button (role button.icon, label "×") in the window's top-right
    // corner inside the corner-cap keep-out, and Esc / gamepad B while the window is up; both call the vanilla toggle,
    // so the "seen" hash (PlatformPrefs ChangeLogHash) is written as vanilla writes it. Revert destroys the button.
    [DisallowMultipleComponent]
    public sealed class ChangelogClose : MonoBehaviour, IRevertible
    {
        public const string ButtonName = "RuneSkin_ChangelogClose";
        private FejdStartup _fejd;
        private GameObject _button;

        public static ChangelogClose Ensure(FejdStartup fejd, RectTransform window)
        {
            if (!fejd || !window) return null;
            if (!window.TryGetComponent<ChangelogClose>(out var c))
            {
                c = window.gameObject.AddComponent<ChangelogClose>();
                c.hideFlags = HideFlags.HideInInspector;
            }
            c._fejd = fejd;
            if (!c._button) c._button = Build(fejd, window);
            return c;
        }

        private static GameObject Build(FejdStartup fejd, RectTransform window)
        {
            var go = new GameObject(ButtonName, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(window, false);
            rt.SetAsLastSibling();
            var size = ThemeTokens.GetFloat("size.changelog.close", 40f);
            var inset = ThemeTokens.GetFloat("size.changelog.close.inset", 14f);
            var decor = window.GetComponentInChildren<PanelDecor>(true);
            if (decor) inset = Mathf.Max(inset, decor.ContentInset());
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.one;
            rt.anchoredPosition = new Vector2(-inset, -inset);
            rt.sizeDelta = new Vector2(size, size);
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            var img = go.AddComponent<Image>();
            img.sprite = ThemeTokens.GetSprite("sprite.button.icon.normal");
            img.type = Image.Type.Sliced;
            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(() => { if (fejd && fejd.m_changeLog && fejd.m_changeLog.activeInHierarchy) fejd.OnButtonShowChangelog(); });
            var label = new GameObject("Label", typeof(RectTransform));
            var lr = (RectTransform)label.transform;
            lr.SetParent(rt, false);
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = lr.offsetMax = Vector2.zero;
            var text = label.AddComponent<TextMeshProUGUI>();
            text.text = "×";
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = ThemeTokens.GetFloat("size.changelog.close.font", 30f);
            text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return go;
        }

        private void Update()
        {
            if (!_fejd || !_fejd.m_changeLog || !_fejd.m_changeLog.activeInHierarchy) return;
            if (ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB")) _fejd.OnButtonShowChangelog();
        }

        public void Revert()
        {
            if (_button) Destroy(_button);
            Destroy(this);
        }
    }
}
