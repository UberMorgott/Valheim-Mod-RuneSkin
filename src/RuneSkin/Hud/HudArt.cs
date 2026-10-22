using System.Collections.Generic;
using RuneSkin.Classify;
using RuneSkin.Diagnostics;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.PlayerHud
{
    // Builds the HUD's own graphics from theme tokens and keeps one diagnostics row per graphic (styled / degraded
    // with the missing token). Everything here is decoration or data display: raycastTarget off unless asked.
    public sealed class HudArt
    {
        private readonly List<KeyValuePair<Graphic, Classification>> _rows = new List<KeyValuePair<Graphic, Classification>>();
        private readonly string _screenId;

        public HudArt(string screenId = "Hud") => _screenId = screenId;
        private readonly List<KeyValuePair<Transform, string>> _suppressed = new List<KeyValuePair<Transform, string>>();

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            return rt;
        }

        // Image from a sprite token (null token = flat colour). Tiled/Sliced from the sprite's own border data.
        public Image Image(string name, Transform parent, string spriteToken, string colorToken, Image.Type type, bool raycast = false)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.raycastTarget = raycast;
            Restyle(img, spriteToken, colorToken, type);
            return img;
        }

        public void Restyle(Image img, string spriteToken, string colorToken, Image.Type type)
        {
            var sprite = ThemeTokens.GetSprite(spriteToken);
            img.sprite = sprite;
            img.type = sprite ? type : UnityEngine.UI.Image.Type.Simple;
            img.color = ThemeTokens.GetColor(colorToken, Color.white);
            Track(img, _ppuScale.TryGetValue(img, out var scale) ? scale : 1f);
            var role = _screenId.ToLowerInvariant() + ":" + (spriteToken ?? colorToken);
            Classification c;
            if (spriteToken != null && !sprite)
                c = new Classification { Role = role, Outcome = Outcome.Degraded, Reason = $"sprite token '{spriteToken}' has no asset (hidden)" };
            else if (ThemeTokens.IsFallback(spriteToken))
                c = new Classification { Role = role, Outcome = Outcome.Styled, Reason = $"fallback art: {ThemeTokens.FallbackTokens[spriteToken]}" };
            else
                c = new Classification { Role = role, Outcome = Outcome.Styled, Reason = "RuneSkin HUD graphic" };
            if (spriteToken != null && !sprite) img.enabled = false;
            Set(img, c);
        }

        // One shared glow material per font (mockup: values in soft light): the font's material with a soft
        // underlay in color.hud.text.glow. Built once, never per text.
        private static readonly Dictionary<TMP_FontAsset, Material> Glow = new Dictionary<TMP_FontAsset, Material>();
        private static int _glowRevision = -1;

        private static Material GlowMaterial(TMP_FontAsset font)
        {
            if (!font || !font.material) return null;
            if (_glowRevision != ThemeTokens.Revision) { Glow.Clear(); _glowRevision = ThemeTokens.Revision; }
            if (Glow.TryGetValue(font, out var m) && m) return m;
            m = new Material(font.material) { name = font.material.name + " (RuneSkin glow)" };
            m.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            m.SetColor(ShaderUtilities.ID_UnderlayColor, ThemeTokens.GetColor("color.hud.text.glow", new Color(0.32f, 0.88f, 0.94f, 0.45f)));
            m.SetFloat(ShaderUtilities.ID_UnderlaySoftness, ThemeTokens.GetFloat("size.hud.text.glow.softness", 0.6f));
            m.SetFloat(ShaderUtilities.ID_UnderlayDilate, ThemeTokens.GetFloat("size.hud.text.glow.dilate", 0.3f));
            Glow[font] = m;
            return m;
        }

        public TMP_Text Text(string name, Transform parent, string fontToken, string colorToken, float size, TextAlignmentOptions align, bool glow = false)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.raycastTarget = false;
            t.richText = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.alignment = align;
            t.fontSize = size;
            var font = ThemeTokens.GetFont(fontToken);
            if (font) t.font = font;
            if (glow && font && GlowMaterial(font) is Material gm) t.fontSharedMaterial = gm;
            t.color = ThemeTokens.GetColor(colorToken, Color.white);
            rt.gameObject.AddComponent<TextFloor>().Px = size >= ThemeTokens.GetFloat("size.hud.font.number", 16f)
                ? ThemeTokens.GetFloat("floor.text.number.px", 14f)
                : ThemeTokens.GetFloat("floor.text.timer.px", 12f);
            Set(t, font
                ? new Classification { Role = "hud:" + fontToken, Outcome = Outcome.Styled, Reason = "RuneSkin HUD text" }
                : new Classification { Role = "hud:" + fontToken, Outcome = Outcome.Degraded, Reason = $"font token '{fontToken}' missing (TMP default)" });
            return t;
        }

        // Sliced / tiled borders and tile periods in reference px. Unity draws a sprite px as
        // canvas.referencePixelsPerUnit / (sprite PPU x pixelsPerUnitMultiplier) units (Image.pixelsPerUnit), and the
        // HUD canvas does not use the reference 100 the geometry is measured in (sprite px x 100 / PPU, see
        // BarView.FrameGeometry): the multiplier cancels the canvas value, times 1 / scale for frames drawn smaller.
        private readonly Dictionary<Image, float> _ppuScale = new Dictionary<Image, float>();
        private float _refPpu = 100f;

        public void Track(Image img, float scale)
        {
            _ppuScale[img] = Mathf.Clamp(scale, 0.25f, 4f);
            img.pixelsPerUnitMultiplier = _refPpu / 100f / _ppuScale[img];
        }

        // Called with the HUD canvas' referencePixelsPerUnit each frame; writes only on a change.
        public void SyncPpu(float refPpu)
        {
            if (!(refPpu > 0f) || Mathf.Approximately(refPpu, _refPpu)) return;
            _refPpu = refPpu;
            foreach (var p in _ppuScale)
                if (p.Key) p.Key.pixelsPerUnitMultiplier = _refPpu / 100f / p.Value;
        }

        // Diagnostics row for a vanilla graphic restyled in place (no journal: the owning family never hands it back).
        public void Note(Graphic g, string role, bool styled, string reason)
        {
            if (!g) return;
            Set(g, new Classification { Role = _screenId.ToLowerInvariant() + ":" + role, Outcome = styled ? Outcome.Styled : Outcome.Degraded, Reason = reason });
        }

        // Restyles a vanilla Image from a sprite token (type from the art: Sliced when it has borders); colour kept
        // unless a colour token is given. Missing art leaves the vanilla look (degraded row).
        public bool Reskin(Image img, string spriteToken, string colorToken, string reason)
        {
            if (!img) return false;
            var sprite = ThemeTokens.GetSprite(spriteToken);
            if (!sprite)
            {
                Note(img, spriteToken, false, $"sprite token '{spriteToken}' has no asset (vanilla look kept)");
                return false;
            }
            img.sprite = sprite;
            img.type = sprite.border != Vector4.zero ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            if (colorToken != null) img.color = ThemeTokens.GetColor(colorToken, img.color);
            Track(img, 1f);
            Note(img, spriteToken, true, reason);
            return true;
        }

        public void Suppressed(Transform t, string reason) => _suppressed.Add(new KeyValuePair<Transform, string>(t, reason));

        private void Set(Graphic g, Classification c)
        {
            for (var i = 0; i < _rows.Count; i++)
                if (_rows[i].Key == g)
                {
                    _rows[i] = new KeyValuePair<Graphic, Classification>(g, c);
                    return;
                }
            _rows.Add(new KeyValuePair<Graphic, Classification>(g, c));
        }

        public ScreenReport Report(Transform root)
        {
            var report = new ScreenReport(_screenId, root);
            foreach (var p in _rows)
                if (p.Key) report.Add(p.Key, root, p.Value);
            foreach (var p in _suppressed)
            {
                if (!p.Key) continue;
                foreach (var g in p.Key.GetComponentsInChildren<Graphic>(true))
                    report.Add(g, p.Key.root, new Classification { Role = "suppressed", Outcome = Outcome.Skipped, Reason = p.Value });
            }
            return report;
        }
    }

    // Readable-floor override for diagnostics (HUD numbers 14 px, timers 12 px instead of body 16 px).
    public sealed class TextFloor : MonoBehaviour
    {
        public float Px;
    }
}
