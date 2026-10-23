using RuneSkin.Restyle;
using UnityEngine;

namespace RuneSkin.WorldHud
{
    // World-anchored texts (Chat NPC / raven / creature bubbles m_npcTexts, say / shout / ping m_worldTexts; both
    // instantiated under the Chat object, Chat.cs:383/692) are hidden while a modal is open (pause menu with its quit /
    // logout dialogs, UnifiedPopup; ModalLayer.AnyOpen). User 2026-10-23: "Я принес весть." drew inside the quit
    // dialog. Vanilla keeps updating and timing them out (Chat.LateUpdate); this only zeroes a CanvasGroup alpha on
    // each live instance, so they reappear in place when the modal closes. Lives on the Chat object.
    [DisallowMultipleComponent]
    public sealed class WorldTextGate : MonoBehaviour, IRevertible
    {
        private Chat _chat;
        private bool _hidden;

        public static bool Hidden { get; private set; }

        public static void Ensure(Chat chat)
        {
            if (!chat || chat.GetComponent<WorldTextGate>()) return;
            var g = chat.gameObject.AddComponent<WorldTextGate>();
            g.hideFlags = HideFlags.HideInInspector;
            g._chat = chat;
        }

        private void LateUpdate()
        {
            if (!_chat) return;
            var hide = ModalLayer.AnyOpen();
            Hidden = hide;
            // Instances appear any frame (a raven line while the menu is open): applied every frame while hidden,
            // once on the change back.
            if (!hide && !_hidden) return;
            _hidden = hide;
            var a = hide ? 0f : 1f;
            foreach (var n in _chat.m_npcTexts) if (n != null) Set(n.m_gui, a);
            foreach (var w in _chat.m_worldTexts) if (w != null) Set(w.m_gui, a);
        }

        private static void Set(GameObject gui, float alpha)
        {
            if (!gui) return;
            if (!gui.TryGetComponent<CanvasGroup>(out var g))
            {
                if (alpha >= 1f) return;
                g = gui.AddComponent<CanvasGroup>();
                g.interactable = false;
                g.blocksRaycasts = false;
            }
            if (!Mathf.Approximately(g.alpha, alpha)) g.alpha = alpha;
        }

        public void Revert()
        {
            if (_chat)
            {
                foreach (var n in _chat.m_npcTexts) if (n != null) Set(n.m_gui, 1f);
                foreach (var w in _chat.m_worldTexts) if (w != null) Set(w.m_gui, 1f);
            }
            Hidden = false;
            Destroy(this);
        }
    }
}
