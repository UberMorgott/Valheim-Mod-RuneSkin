using RuneSkin.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.WorldHud
{
    // Staggered (draining) state, enemy-b STAGGERED column: the stagger line turns white-hot and throws ember sparks,
    // strongest on rare / unique. Four Codex frames (fx_stagger_0..3, cut from one sheet generated with the mockup crop as
    // reference) cycle at FPS with a cross-fade between two Filled images whose fill follows the draining line. Sprites
    // are resolved once; per frame only fillAmount / sprite / colour structs are written (no allocation).
    public static partial class EnemyHudSkin
    {
        public const string SparksName = "RuneSkin_StaggerSparks";
        private const float SparksFps = 10f;
        private static readonly Sprite[] SparkFrames = new Sprite[4];
        private static bool _sparks;

        private static void LoadSparks()
        {
            _sparks = true;
            for (var i = 0; i < SparkFrames.Length; i++)
            {
                SparkFrames[i] = ThemeTokens.GetSprite("sprite.fx.stagger." + i);
                _sparks &= SparkFrames[i];
            }
        }

        // Two Filled images over the frame (sparks rise over the rail), their line (sprite pivot 0.29) on the stagger line.
        private static void NewSparks(RectTransform health, float lineHeight)
        {
            if (!_sparks) return;
            var h = health.rect.height * ThemeTokens.GetFloat("size.enemy.sparks.h", 2.8f);
            var pivotY = SparkFrames[0].pivot.y / Mathf.Max(1f, SparkFrames[0].rect.height);
            for (var i = 0; i < 2; i++)
            {
                var go = new GameObject(SparksName + (i == 0 ? "" : "B"), typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var rect = (RectTransform)go.transform;
                rect.SetParent(health, false);
                rect.SetAsLastSibling();
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0f, pivotY);
                rect.sizeDelta = new Vector2(0f, h);
                rect.anchoredPosition = new Vector2(0f, lineHeight * 0.5f);
                var img = go.GetComponent<Image>();
                img.raycastTarget = false;
                img.sprite = SparkFrames[i];
                img.type = Image.Type.Filled;
                img.fillMethod = Image.FillMethod.Horizontal;
                img.fillOrigin = 0;
                img.fillAmount = 0f;
                img.enabled = false;
                Art.Note(img, "enemy.stagger.sparks", true, "staggered sparks (Codex frames from enemy-b, cycled + cross-faded)");
            }
        }

        private static void UpdateSparks(State s, bool active, float fill, float now)
        {
            if (!_sparks) return;
            if (!s.Sparks)
            {
                if (s.SparksLooked) return; // looked once, none on this HUD
                s.SparksLooked = true;
                s.Sparks = Child(s.Health, SparksName);
                s.SparksB = Child(s.Health, SparksName + "B");
                if (!s.Sparks || !s.SparksB) return;
            }
            if (s.Sparks.enabled != active) s.Sparks.enabled = active;
            if (s.SparksB.enabled != active) s.SparksB.enabled = active;
            if (!active) return;
            var i = EnemyHudMath.SheetFrame(now, SparksFps, SparkFrames.Length, out var blend);
            var a = EnemyHudMath.SparkAlpha(s.Tier);
            if (i != s.SparkFrame)
            {
                s.SparkFrame = i;
                s.Sparks.sprite = SparkFrames[i];
                s.SparksB.sprite = SparkFrames[(i + 1) % SparkFrames.Length];
            }
            s.Sparks.color = new Color(1f, 1f, 1f, a * (1f - blend));
            s.SparksB.color = new Color(1f, 1f, 1f, a * blend);
            s.Sparks.fillAmount = fill;
            s.SparksB.fillAmount = fill;
        }

    }
}
