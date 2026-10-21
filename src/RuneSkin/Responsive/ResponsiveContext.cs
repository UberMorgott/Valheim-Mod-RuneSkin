using System;
using System.Collections.Generic;
using RuneSkin.Restyle;
using UnityEngine;

namespace RuneSkin.Responsive
{
    // Responsive base. Vanilla GuiScaler rewrites every CanvasScaler.scaleFactor each frame with
    // min(W/1920, H/1080) x GuiScale (GuiScaler.cs:43-58) and has no change event, so RuneSkin never touches the
    // canvas scale: it reads the same inputs, multiplies its own SkinScale onto the content roots of owned panels
    // (localScale, journaled in ScaleMark) and compares a cached tuple once per frame (no allocation) to raise Changed.
    public sealed class ResponsiveContext : MonoBehaviour
    {
        public const float MinSkinScale = 0.85f;
        public const float MaxSkinScale = 1.15f;

        public static ResponsiveContext Instance { get; private set; }
        public static event Action Changed;

        private static readonly List<Transform> Targets = new List<Transform>();

        private int _w, _h;
        private float _guiScale, _skinScale;
        private Rect _safe;
        private string _language;

        public static float SkinScale => Mathf.Clamp(Plugin.SkinScale?.Value ?? 1f, MinSkinScale, MaxSkinScale);

        // Same factor GuiScaler gives every canvas (GuiScaler.cs:54-58).
        public static float GuiFactor => Mathf.Min(Screen.width / 1920f, Screen.height / 1080f) * GuiScaler.m_largeGuiScale;

        // Physical pixels per reference pixel inside an owned panel.
        public static float EffectiveScale => GuiFactor * SkinScale;

        public static Rect SafeArea => Screen.safeArea;

        private void Awake()
        {
            Instance = this;
            Snapshot();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            var language = Localization.instance != null ? Localization.instance.GetSelectedLanguage() : null;
            if (_w == Screen.width && _h == Screen.height && _guiScale == GuiScaler.m_largeGuiScale && _skinScale == SkinScale &&
                _safe == Screen.safeArea && ReferenceEquals(_language, language))
                return;
            var scaleChanged = _skinScale != SkinScale;
            Snapshot();
            _language = language;
            if (scaleChanged) ReapplyScales();
            try { Changed?.Invoke(); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] responsive listener failed: {e}"); }
        }

        private void Snapshot()
        {
            _w = Screen.width;
            _h = Screen.height;
            _guiScale = GuiScaler.m_largeGuiScale;
            _skinScale = SkinScale;
            _safe = Screen.safeArea;
        }

        // Puts SkinScale on an owned panel's content root (not on a canvas): localScale = original x SkinScale.
        public static void ApplyScale(Transform t)
        {
            if (!t) return;
            var mark = t.GetComponent<ScaleMark>();
            if (!mark)
            {
                mark = t.gameObject.AddComponent<ScaleMark>();
                mark.Original = t.localScale;
                mark.BoundInstance = t.gameObject.GetInstanceID();
                mark.hideFlags = HideFlags.HideInInspector;
            }
            else if (mark.BoundInstance != t.gameObject.GetInstanceID())
            {
                mark.BoundInstance = t.gameObject.GetInstanceID(); // clone: Original copied from the template
            }
            if (!Targets.Contains(t))
            {
                Targets.RemoveAll(x => !x); // destroyed tooltip roots
                Targets.Add(t);
            }
            var target = mark.Original * SkinScale;
            if (t.localScale != target) t.localScale = mark.Written = target;
            else mark.Written = target;
        }

        public static void RevertScale(ScaleMark mark)
        {
            if (!mark) return;
            if (mark.transform.localScale == mark.Written) mark.transform.localScale = mark.Original;
            Targets.Remove(mark.transform);
            UnityEngine.Object.Destroy(mark);
        }

        private static void ReapplyScales()
        {
            Targets.RemoveAll(t => !t);
            foreach (var t in Targets) ApplyScale(t);
        }
    }
}
