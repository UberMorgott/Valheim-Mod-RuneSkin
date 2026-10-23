using System;
using HarmonyLib;
using RuneSkin.Ownership;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.WorldHud
{
    // Speech over the world: NPC / creature bubbles (Chat.m_npcTextBase / m_npcTextBaseLarge, cloned per line by
    // Chat.SetNpcText :679 for NpcTalk, RandomSpeak, Raven, Trader and mods such as Wizardry's mage lines) and chat
    // say / shout / ping texts (m_worldTextBase, AddInworldText :376).
    // Root cause of the "giant framed panel" (user 2026-10-23, "Я был избран Древним!"): the bubble templates have a
    // FIXED rect (NpcDialog 450x120, NpcDialogLarge 700x250 reference px) with vanilla's soft darken_blob sprite, nearly
    // invisible at its edges; the role pass gave that Image the popup role (tooltip_frame, framed + filled), which drew
    // the whole fixed rect as a window panel over a third of the screen. Now: no panel role ever; the vanilla soft blob
    // stays as a subtle backing tinted color.speech.plate, the bubble is sized to its text (wrap at size.speech.maxw /
    // .large), pivot at its bottom centre so it sits above the anchor vanilla projects (talker + offset, clamped to the
    // screen edge, culled past m_cullDistance), fading out over the last size.speech.fade share of that distance.
    // Text: body sans (PT Sans) with the shared outline + soft underlay (TextLook).
    public static class SpeechBubbleSkin
    {
        public const string Look = "speech";

        public static void StyleNpcTemplate(GameObject template, bool large)
        {
            if (!template) return;
            var root = (RectTransform)template.transform;
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0f);
            foreach (var img in template.GetComponentsInChildren<Image>(true))
            {
                img.raycastTarget = false;
                img.color = ThemeTokens.GetColor("color.speech.plate", new Color(0.02f, 0.035f, 0.045f, 0.62f));
                Stretch(img.rectTransform, 0f, 0f);
            }
            foreach (var t in template.GetComponentsInChildren<TMP_Text>(true)) StyleText(t);
            var fit = template.GetComponent<SpeechBubbleFit>() ?? template.AddComponent<SpeechBubbleFit>();
            fit.Large = large;
        }

        public static void StyleWorldTextTemplate(GameObject template)
        {
            if (!template) return;
            foreach (var t in template.GetComponentsInChildren<TMP_Text>(true)) StyleText(t);
        }

        private static void StyleText(TMP_Text t)
        {
            if (!TextLook.Apply(t, "font.body", Look)) return;
            TextLook.Underlay(t.fontSharedMaterial);
            t.enableAutoSizing = false;
            t.fontSize = ThemeTokens.GetFloat("size.speech.font", 20f);
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
        }

        internal static void Stretch(RectTransform rt, float pad, float vpad)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(pad, vpad);
            rt.offsetMax = new Vector2(-pad, -vpad);
        }

        internal static void Bind(Chat chat, GameObject talker)
        {
            if (!chat || !talker || chat.m_npcTexts == null) return;
            foreach (var n in chat.m_npcTexts)
            {
                if (n == null || n.m_go != talker || !n.m_gui) continue;
                var fit = n.m_gui.GetComponent<SpeechBubbleFit>();
                if (fit) fit.Bind(n);
            }
        }
    }

    // On every bubble (template + clones): sizes the root to its text whenever the text changes, keeps that size
    // (anything else writing the rect is undone next frame) and fades the bubble near the cull distance.
    public sealed class SpeechBubbleFit : MonoBehaviour
    {
        public bool Large;

        private Chat.NpcText _npc;
        private TMP_Text _text;
        private string _shown;
        private Vector2 _size;
        private int _revision = -1;
        private Graphic[] _graphics;

        internal void Bind(Chat.NpcText npc)
        {
            _npc = npc;
            _text = npc.m_textField;
            _shown = null;
            Refit();
        }

        private void LateUpdate()
        {
            try { Refit(); }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[RuneSkin] speech bubble fit failed (disabled on this bubble): {e}");
                enabled = false;
            }
        }

        private void Refit()
        {
            if (!_text) _text = transform.Find("Text") is Transform tt ? tt.GetComponent<TMP_Text>() : null;
            if (!_text) return;
            var root = (RectTransform)transform;
            if (_text.text != _shown || _revision != ThemeTokens.Revision)
            {
                _shown = _text.text;
                _revision = ThemeTokens.Revision;
                var pad = ThemeTokens.GetFloat("size.speech.pad", 10f);
                var vpad = ThemeTokens.GetFloat("size.speech.vpad", 6f);
                var max = ThemeTokens.GetFloat(Large ? "size.speech.maxw.large" : "size.speech.maxw", Large ? 440f : 360f);
                var oneLine = string.IsNullOrEmpty(_shown) ? 0f : _text.GetPreferredValues(_shown, float.PositiveInfinity, float.PositiveInfinity).x;
                var wrap = SpeechMath.WrapWidth(oneLine, max);
                var h = string.IsNullOrEmpty(_shown) ? 0f : _text.GetPreferredValues(_shown, wrap, 0f).y;
                SpeechMath.BubbleSize(wrap, h, pad, vpad, out var w, out var bh);
                _size = new Vector2(w, bh);
                foreach (var t in GetComponentsInChildren<TMP_Text>(true))
                {
                    SpeechBubbleSkin.Stretch(t.rectTransform, pad, vpad);
                    t.alignment = TextAlignmentOptions.Center;
                }
                _graphics = GetComponentsInChildren<Graphic>(true);
            }
            if (root.anchorMin != root.anchorMax) root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            if (root.pivot != new Vector2(0.5f, 0f)) root.pivot = new Vector2(0.5f, 0f);
            if (root.sizeDelta != _size) root.sizeDelta = _size;
            Fade();
        }

        private void Fade()
        {
            if (_npc == null || !_npc.m_go) return;
            var cam = Utils.GetMainCamera();
            if (!cam) return;
            var d = Vector3.Distance(cam.transform.position, _npc.m_go.transform.position + _npc.m_offset);
            var a = SpeechMath.DistanceFade(d, _npc.m_cullDistance, ThemeTokens.GetFloat("size.speech.fade", 0.25f));
            if (_graphics == null) return;
            foreach (var g in _graphics)
                if (g && !Mathf.Approximately(g.canvasRenderer.GetAlpha(), a)) g.canvasRenderer.SetAlpha(a);
        }
    }

    [HarmonyPatch]
    public static class SpeechBubbleAdapter
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly), batched by FastStartup).
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.Chat);

        [HarmonyPatch(typeof(Chat), nameof(Chat.SetNpcText))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void SetNpcText_Postfix(Chat __instance, GameObject talker)
        {
            try { SpeechBubbleSkin.Bind(__instance, talker); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] speech bubble bind failed: {e}"); }
        }
    }
}
