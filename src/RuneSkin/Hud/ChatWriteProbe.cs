using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RuneSkin.PlayerHud
{
    // ONE-RUN DIAGNOSTIC (run 20261023-202815: the chat lift lands per HudCluster's log, yet the shot and the check show the
    // chat at home): logs every write to the chat window's placement (anchoredPosition / localPosition / position /
    // anchors / pivot / sizeDelta / localScale / parent) with the caller's stack, each distinct stack once (cap 60).
    // Remove after the writer is named.
    internal static class ChatWriteProbe
    {
        private static readonly HashSet<string> Seen = new HashSet<string>();
        private static int _logged;

        public static void Init(Harmony h)
        {
            var prefix = new HarmonyMethod(typeof(ChatWriteProbe), nameof(Prefix));
            foreach (var (t, name) in new[]
            {
                (typeof(RectTransform), "set_anchoredPosition"), (typeof(RectTransform), "set_anchoredPosition3D"),
                (typeof(RectTransform), "set_anchorMin"), (typeof(RectTransform), "set_anchorMax"),
                (typeof(RectTransform), "set_pivot"), (typeof(RectTransform), "set_sizeDelta"),
                (typeof(RectTransform), "set_offsetMin"), (typeof(RectTransform), "set_offsetMax"),
                (typeof(Transform), "set_localPosition"), (typeof(Transform), "set_position"),
                (typeof(Transform), "set_localScale"),
            })
            {
                var m = AccessTools.Method(t, name);
                if (m == null) continue;
                try { h.Patch(m, prefix: prefix); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"[RuneSkin] chat probe: {name} not patched ({e.Message})"); }
            }
            var setParent = AccessTools.Method(typeof(Transform), nameof(Transform.SetParent), new[] { typeof(Transform), typeof(bool) });
            if (setParent != null)
                try { h.Patch(setParent, prefix: prefix); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"[RuneSkin] chat probe: SetParent not patched ({e.Message})"); }
        }

        private static void Prefix(Transform __instance, MethodBase __originalMethod)
        {
            if (_logged >= 60) return;
            var chat = Chat.instance;
            if (!chat || !chat.m_chatWindow || __instance != chat.m_chatWindow) return;
            var st = new StackTrace(2, false);
            var frames = new List<string>();
            for (var i = 0; i < st.FrameCount && frames.Count < 7; i++)
            {
                var mm = st.GetFrame(i).GetMethod();
                if (mm == null) continue;
                frames.Add((mm.DeclaringType != null ? mm.DeclaringType.Name : "?") + "." + mm.Name);
            }
            var key = __originalMethod.Name + "|" + string.Join("<", frames);
            if (!Seen.Add(key)) return;
            _logged++;
            var w = chat.m_chatWindow;
            Plugin.Log.LogInfo($"[RuneSkin] chat probe: {__originalMethod.Name} frame {Time.frameCount} pos {w.anchoredPosition} by {string.Join(" < ", frames)}");
        }
    }
}
