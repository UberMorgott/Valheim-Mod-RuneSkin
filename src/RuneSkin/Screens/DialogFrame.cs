using RuneSkin.PreWorld;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.Screens
{
    // Confirm dialogs of the pause menu (Menu.m_quitDialog "ExitConfirm", m_logoutDialog "LogoutConfirm",
    // m_cloudStorageWarning / m_cloudStorageWarningNextSave): ONE framed box.
    // Root cause (user 2026-10-23, "Выйти из игры?" with a plate sticking out): the role pass styled each vanilla panel
    // image on its own - ExitConfirm/dialog/"border (1)" (woodpanel_512x512) took role "panel" = the frame sprite with
    // fillCenter off and ExitConfirm has no backdrop image, so the dialog drew a bare frame over the world (the dark
    // plate in the screenshot was a speech bubble behind it); LogoutConfirm got frame + backdrop as two separate
    // images, the cloud warnings a bare frame too. Now every panel image directly on the dialog is switched off
    // (PanelSwap, reported skipped) and the RuneSkin panel stack (backing + glass + frame + caps, PanelDecor) is the
    // dialog's one box, sized to the text + buttons with size.dialog.pad (DialogFit), on the modal band (ModalLayer).
    public static class DialogFrame
    {
        public static void Apply(Transform dialogRoot)
        {
            if (!dialogRoot) return;
            var host = Host(dialogRoot);
            if (!host) return;
            foreach (Transform child in host)
                if (child.TryGetComponent<Image>(out var img) && !(img.GetComponent<SkinMark>() is SkinMark m && m.Internal))
                {
                    var sprite = img.sprite ? img.sprite.name : null;
                    if (img.TryGetComponent<PanelSwap>(out var s)) sprite = s.OrigSprite;
                    if (PreWorldKit.IsPanelSprite(sprite)) PanelSwap.Apply(img, sprite);
                }
            if (host.TryGetComponent<Image>(out var own) && PreWorldKit.IsPanelSprite(own.sprite ? own.sprite.name : null)) PanelSwap.Apply(own, own.sprite.name);
            PanelDecor.Ensure(host, "size.dialog.pad", glass: true, plaque: false);
            if (!host.GetComponent<DialogFit>()) host.gameObject.AddComponent<DialogFit>().hideFlags = HideFlags.HideInInspector;
            ModalLayer.Ensure(dialogRoot);
        }

        // The box holder: vanilla nests the content in a "dialog" / "Dialog" child; else the root itself.
        public static RectTransform Host(Transform root)
        {
            if (!root) return null;
            foreach (Transform c in root)
                if (string.Equals(c.name, "dialog", System.StringComparison.OrdinalIgnoreCase)) return c as RectTransform;
            return root as RectTransform;
        }
    }

    // Refits the dialog's panel stack to its content when it shows and whenever a text in it changes (the cloud
    // warnings are filled before Show). Two frames after enable: vanilla / RuneSkin layout passes ran.
    [DisallowMultipleComponent]
    public sealed class DialogFit : MonoBehaviour, IRevertible
    {
        private int _wait;
        private int _hash;
        private int _revision = -1;

        private void OnEnable() => _wait = 2;

        private void LateUpdate()
        {
            if (_wait > 0) { _wait--; if (_wait > 0) return; Fit(); return; }
            if (Hash() != _hash || _revision != ThemeTokens.Revision) Fit();
        }

        private static readonly System.Collections.Generic.List<TMP_Text> Texts = new System.Collections.Generic.List<TMP_Text>(8);

        private int Hash()
        {
            var h = 17;
            Texts.Clear();
            GetComponentsInChildren(false, Texts);
            foreach (var t in Texts) h = h * 31 + System.StringComparer.Ordinal.GetHashCode(t.text ?? "");
            Texts.Clear();
            return h;
        }

        private void Fit()
        {
            _hash = Hash();
            _revision = ThemeTokens.Revision;
            var panel = transform.Find(PanelDecor.ChildName);
            if (panel && panel.TryGetComponent<PanelDecor>(out var decor))
                decor.FitToContent("size.dialog.pad", ThemeTokens.GetFloat("size.dialog.minw", 320f), ThemeTokens.GetFloat("size.dialog.minh", 150f));
        }

        public void Revert() => Destroy(this);
    }
}
