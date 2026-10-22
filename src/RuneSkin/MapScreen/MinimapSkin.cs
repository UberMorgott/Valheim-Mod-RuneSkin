using System;
using System.Collections.Generic;
using HarmonyLib;
using RuneSkin.Diagnostics;
using RuneSkin.PlayerHud;
using RuneSkin.Responsive;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RuneSkin.Ownership;

namespace RuneSkin.MapScreen
{
    // Family "Minimap" (art\README.md "M4 sprites", mockup m4-hud-a): the vanilla small map (Minimap.m_smallRoot,
    // hudroot/MiniMap/small: Image TextBackdrop + MapClick + RawImage "map" {markers, small_mapPin_root} +
    // small_mapNamePin_root + small_biome + "Key tip") becomes a round rune ring in the top-right safe corner:
    // a Unity Mask (minimap_mask disc) holds the vanilla map image, its pins and name pins (Jotunn MinimapManager's
    // CustomLayerSmall is a child of the map image, so it is clipped too); the ring, N/E/S/W rune stones, the wind
    // socket (glyph rotated like the vanilla wind marker, Minimap.cs:1702-1706; only while on a ship) and the day-phase
    // glyph on the top arc (EnvMan.GetDayFraction / IsDay) are RuneSkin graphics; the biome name sits on a plaque under
    // the ring. Everything vanilla writes per frame (uvRect zoom :1256, marker rotations :1705-1738, pin positions
    // :1601-1660, biome text :2646) keeps running: RuneSkin only reparents / resizes the containers once and culls pins
    // that fall outside the hole. Large map: LargeMap (same component).
    public sealed partial class MinimapSkin : MonoBehaviour
    {
        public const string ScreenId = "Minimap";
        public static MinimapSkin Instance { get; private set; }

        private readonly HudArt _art = new HudArt(ScreenId);
        private Minimap _map;
        private RectTransform _small, _mask, _plaque;
        private Image _maskImage, _ring, _windSocket, _windGlyph, _time, _timeSocket;
        private readonly Image[] _runes = new Image[4];
        private readonly TMP_Text[] _letters = new TMP_Text[4];
        private RawImage _mapImage;
        private TMP_Text _biome, _keyTip;
        private Canvas _canvas;
        private bool _maskChecked, _masked = true;
        private float _box = -1f, _pxPerRef = -1f, _plaqueScale = 1f, _plaqueMin;
        private Vector2 _screenCorner;
        private string _biomeText;
        private static readonly string[] Letters = { "N", "E", "S", "W" };

        public static void Attach(Minimap map)
        {
            if (!map || map.GetComponent<MinimapSkin>()) return;
            map.gameObject.AddComponent<MinimapSkin>().Init(map);
        }

        private void Init(Minimap map)
        {
            Instance = this;
            _map = map;
            try { BuildSmall(); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] minimap ring build failed (vanilla small map stays): {e}"); }
            try { BuildLarge(); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] large map restyle failed (vanilla large map stays): {e}"); }
            Plugin.Log.LogInfo("[RuneSkin] minimap attached");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---------------------------------------------------------------- small map

        private void BuildSmall()
        {
            _small = _map.m_smallRoot ? _map.m_smallRoot.transform as RectTransform : null;
            _mapImage = _map.m_mapImageSmall;
            if (!_small || !_mapImage) return;
            _canvas = _small.GetComponentInParent<Canvas>(true);

            // Vanilla square backdrop off: the ring and the masked map replace it.
            if (_small.TryGetComponent<Image>(out var backdrop))
            {
                backdrop.enabled = false;
                _art.Suppressed(backdrop.transform, "vanilla square minimap backdrop (TextBackdrop) replaced by the rune ring");
            }

            // Mask container (first child: behind the ring) with the vanilla map, its click area and name pins.
            _mask = HudArt.Rect("RuneSkin_MapMask", _small);
            Stretch(_mask);
            _mask.SetAsFirstSibling();
            _maskImage = _mask.gameObject.AddComponent<Image>();
            _art.Restyle(_maskImage, "sprite.minimap.mask", "color.frame", Image.Type.Simple);
            _maskImage.raycastTarget = false;
            if (_maskImage.sprite)
            {
                _maskImage.enabled = true;
                _mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            }
            var click = _small.Find("MapClick");
            if (click) click.SetParent(_mask, false);
            _mapImage.rectTransform.SetParent(_mask, false);
            MatchNameRoots(); // name pins inside the map image, over its pin root (PinLabels.cs): clipped by the mask too
            var mapRt = _mapImage.rectTransform;
            mapRt.anchorMin = mapRt.anchorMax = mapRt.pivot = new Vector2(0.5f, 0.5f);
            mapRt.anchoredPosition = Vector2.zero;

            // Vanilla wind arrow (map centre) off: the ring's wind socket shows the direction.
            if (_map.m_windMarker && _map.m_windMarker.TryGetComponent<Image>(out var wind))
            {
                wind.enabled = false;
                _art.Suppressed(wind.transform, "vanilla wind arrow replaced by the ring's wind socket");
            }

            _ring = _art.Image("RuneSkin_Ring", _small, "sprite.minimap.ring", "color.frame", Image.Type.Simple);
            for (var i = 0; i < 4; i++)
            {
                _runes[i] = _art.Image("RuneSkin_Rune" + Letters[i], _small, "sprite.minimap.rune", "color.frame", Image.Type.Simple);
                _letters[i] = _art.Text("Letter", _runes[i].transform, "font.display", "color.minimap.rune", ThemeTokens.GetFloat("size.minimap.letter", 17f), TextAlignmentOptions.Center, glow: true);
                _letters[i].text = Letters[i];
                Stretch(_letters[i].rectTransform);
            }
            _windSocket = _art.Image("RuneSkin_WindSocket", _small, "sprite.minimap.wind.socket", "color.frame", Image.Type.Simple);
            _windGlyph = _art.Image("Glyph", _windSocket.transform, "sprite.glyph.wind", "color.minimap.rune", Image.Type.Simple);
            // Day phase: the sun / moon glyph seated in a round stone socket riding the ring's top arc (Codex r1).
            _timeSocket = _art.Image("RuneSkin_DayPhase", _small, "sprite.button.round", "color.frame", Image.Type.Simple);
            _timeSocket.preserveAspect = true;
            _time = _art.Image("Glyph", _timeSocket.transform, "sprite.glyph.time.sun", "color.minimap.sun", Image.Type.Simple);
            _time.preserveAspect = true;
            Stretch(_time.rectTransform);
            _time.rectTransform.offsetMin = _time.rectTransform.offsetMax = Vector2.zero;

            _plaque = _art.Image("RuneSkin_BiomePlaque", _small, "sprite.plaque.header", "color.frame", Image.Type.Sliced).rectTransform;
            _biome = _map.m_biomeNameSmall;
            if (_biome)
            {
                _biome.rectTransform.SetParent(_plaque, false);
                Stretch(_biome.rectTransform);
                _biome.alignment = TextAlignmentOptions.Center;
                _biome.textWrappingMode = TextWrappingModes.NoWrap;
                _biome.enableAutoSizing = false;
                _biome.fontSize = ThemeTokens.GetFloat("size.minimap.font", 17f);
                var font = ThemeTokens.GetFont("font.body");
                if (font) _biome.font = font;
                _biome.color = ThemeTokens.GetColor("color.text.body", Color.white);
            }
            var tip = _small.Find("Key tip");
            if (tip && tip.TryGetComponent<TMP_Text>(out var tipText))
            {
                _keyTip = tipText;
                var font = ThemeTokens.GetFont("font.body");
                if (font) _keyTip.font = font;
            }
            Layout(true);
        }

        private void LateUpdate()
        {
            if (!_map) return;
            if (_small && _small.gameObject.activeInHierarchy)
            {
                if (!_maskChecked) CheckMask();
                SyncMapMaterial();
                Layout(false);
                UpdateDials();
                // Pins move only when vanilla re-places them (Minimap.UpdatePins, on m_pinUpdateRequired; a frame
                // throttle such as Skidbladnir's skips whole Updates) or the ring is re-laid out: culled then, not per frame.
                if (_pinsDirty || PinsVersion != _pinsCulled)
                {
                    _pinsDirty = false;
                    _pinsCulled = PinsVersion;
                    CullPins();
                    PlaceLabels();
                }
                FitPlaque();
            }
            var largeOn = _large && _large.gameObject.activeInHierarchy;
            if (largeOn) LargeLateUpdate();
            HideHudCluster(largeOn);
        }

        // The map RawImage draws with Minimap.m_mapSmallShader's material (Minimap.cs:541); a Mask clips it only when
        // that shader takes the UI stencil. Without it the map shrinks to the hole's inscribed square (diagnostic note).
        private void CheckMask()
        {
            _maskChecked = true;
            var mat = _mapImage ? _mapImage.materialForRendering : null;
            _masked = _maskImage && _maskImage.sprite && mat && mat.HasProperty("_Stencil");
            SkinDiagnostics.Note(_masked
                ? $"minimap: map clipped by the ring mask (shader '{(mat ? mat.shader.name : "-")}' takes the stencil)"
                : $"minimap: map shader '{(mat && mat.shader ? mat.shader.name : "-")}' ignores the UI stencil (or mask art missing): map = inscribed square of the hole");
            _box = -1f; // re-layout with the map side for this mode
        }

        // The Mask draws the map with a stencil copy of Minimap.m_mapSmallShader (StencilMaterial), made once. Vanilla
        // writes its per-frame map values on m_mapSmallShader only (CenterMap _zoom / _pixelSize / _mapCenter,
        // Minimap.cs:1280-1282; _SharedFade :737-748), and the copy does not even carry them (they are not in the
        // shader's property block): the fog shader read 0 and the minimap lost its fog of war. Mirror them onto the
        // material actually rendered, after Minimap.Update, before the canvas draws.
        private static readonly int ZoomId = Shader.PropertyToID("_zoom"), PixelSizeId = Shader.PropertyToID("_pixelSize"),
            CenterId = Shader.PropertyToID("_mapCenter"), FadeId = Shader.PropertyToID("_SharedFade");

        private void SyncMapMaterial()
        {
            var src = _map.m_mapSmallShader;
            var dst = _mapImage ? _mapImage.materialForRendering : null;
            if (!src || !dst || dst == src) return;
            dst.SetFloat(ZoomId, src.GetFloat(ZoomId));
            dst.SetFloat(PixelSizeId, src.GetFloat(PixelSizeId));
            dst.SetVector(CenterId, src.GetVector(CenterId));
            dst.SetFloat(FadeId, src.GetFloat(FadeId));
        }

        private void Layout(bool force)
        {
            var canvas = _canvas ? _canvas.rootCanvas : null;
            var pxPerRef = canvas ? canvas.scaleFactor : 1f;
            if (canvas) _art.SyncPpu(canvas.referencePixelsPerUnit);
            var safe = ResponsiveContext.SafeArea;
            var box = MinimapMath.RingSize(ThemeTokens.GetFloat("size.minimap.ring", 320f) * (Plugin.HudScale?.Value ?? 1f) * (Plugin.MinimapScale?.Value ?? 1f),
                safe.height, pxPerRef, ThemeTokens.GetFloat("share.minimap.ring.h", 0.26f) * (Plugin.MinimapScale?.Value ?? 1f));
            HudMath.HudBox(safe.x, safe.width, safe.height, Plugin.UltrawideBox?.Value ?? true,
                ThemeTokens.GetFloat("ratio.hud.ultrawide", 2.2f), out var boxX, out var boxW);
            var margin = ThemeTokens.GetFloat("size.hud.margin", 24f) * pxPerRef;
            var corner = new Vector2(boxX + boxW - margin, safe.yMax - margin);
            if (!force && Mathf.Approximately(box, _box) && Mathf.Approximately(pxPerRef, _pxPerRef) && corner == _screenCorner) return;
            _box = box;
            _pxPerRef = pxPerRef;
            _screenCorner = corner;
            _pinsDirty = true;

            _small.anchorMin = _small.anchorMax = _small.pivot = Vector2.one;
            _small.sizeDelta = new Vector2(box, box);
            var parent = _small.parent as RectTransform;
            if (parent)
            {
                var cam = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, corner, cam, out var world)) _small.position = world;
            }

            var k = box / 220f;
            var side = MinimapMath.MapSide(box, ThemeTokens.GetFloat("ratio.minimap.hole", 0.777f), _masked);
            _mapImage.rectTransform.sizeDelta = new Vector2(side, side);
            Center(_ring.rectTransform, Vector2.zero, new Vector2(box, box));
            var band = box * 0.5f * ThemeTokens.GetFloat("ratio.minimap.band", 0.889f);
            var rune = ThemeTokens.GetFloat("size.minimap.rune", 32f) * k;
            for (var i = 0; i < 4; i++)
            {
                MinimapMath.Cardinal(i, band, out var x, out var y);
                Center(_runes[i].rectTransform, new Vector2(x, y), new Vector2(rune, rune));
                _letters[i].fontSize = ThemeTokens.GetFloat("size.minimap.letter", 17f) * k;
            }
            var w = ThemeTokens.GetFloat("size.minimap.wind", 30f) * k;
            // Upper-right on the band (45 deg), socket 60x74 sprite px (teardrop, tip top-right). The round recess the
            // wind arrow turns in spans x 7..49, y 24..64 px from the TOP: centre (28.5, 44) = anchor (0.475, 0.40) in
            // Unity's bottom-up space, diameter 43 px = 0.72 of the width. The old anchor (0.45, 0.58) read the y from the
            // top, so the arrow spun on the recess rim, off-centre (user screenshot 2026-10-22: "arrow is crooked").
            var diag = band * 0.7071f;
            Center(_windSocket.rectTransform, new Vector2(diag, diag), new Vector2(w, w * 74f / 60f));
            var gs = w * ThemeTokens.GetFloat("ratio.minimap.wind.glyph", 0.7f);
            var g = _windGlyph.rectTransform;
            g.anchorMin = g.anchorMax = new Vector2(MinimapMath.WindRecessX, MinimapMath.WindRecessY);
            _windGlyph.preserveAspect = true;
            g.pivot = new Vector2(0.5f, 0.5f);
            g.anchoredPosition = Vector2.zero;
            g.sizeDelta = new Vector2(gs, gs);
            var t = ThemeTokens.GetFloat("size.minimap.time", 26f) * k;
            _timeSocket.rectTransform.sizeDelta = new Vector2(t, t);
            var inner = t * 0.18f;
            _time.rectTransform.offsetMin = new Vector2(inner, inner);
            _time.rectTransform.offsetMax = new Vector2(-inner, -inner);

            var ph = ThemeTokens.GetFloat("size.minimap.plaque.h", 32f) * k;
            // header_plaque drawn at the plaque height (its knot ends keep their proportions).
            var plaqueSprite = ThemeTokens.GetSprite("sprite.plaque.header");
            var native = plaqueSprite ? plaqueSprite.rect.height * 100f / plaqueSprite.pixelsPerUnit : ph;
            _plaqueScale = native > 0f ? ph / native : 1f;
            _art.Track(_plaque.GetComponent<Image>(), _plaqueScale);
            _plaqueMin = Mathf.Max(ThemeTokens.GetFloat("size.minimap.plaque.min", 120f) * k, plaqueSprite ? (plaqueSprite.border.x + plaqueSprite.border.z) * 100f / plaqueSprite.pixelsPerUnit * _plaqueScale * 0.9f : 0f);
            _plaque.anchorMin = _plaque.anchorMax = new Vector2(0.5f, 0f);
            _plaque.pivot = new Vector2(0.5f, 1f);
            _plaque.anchoredPosition = new Vector2(0f, box * 0.02f);
            _plaque.sizeDelta = new Vector2(Mathf.Max(_plaque.sizeDelta.x, _plaqueMin), ph);
            if (_biome) _biome.fontSize = ThemeTokens.GetFloat("size.minimap.font", 17f) * k;
            if (_keyTip)
            {
                var tr = _keyTip.rectTransform;
                tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0f);
                tr.pivot = new Vector2(0.5f, 1f);
                tr.anchoredPosition = new Vector2(0f, box * 0.02f - ph - 2f);
                _keyTip.alignment = TextAlignmentOptions.Top;
            }
            _biomeText = null; // plaque width again
        }

        private void FitPlaque()
        {
            if (!_biome || _biome.text == _biomeText) return;
            _biomeText = _biome.text;
            var k = _box / 220f;
            // The text sits between the plaque's knot ends (sliced borders), not over them.
            var ends = _plaqueMin * 0.5f * 1.05f;
            var pad = Mathf.Max(ThemeTokens.GetFloat("size.minimap.plaque.pad", 22f) * k, ends);
            var w = string.IsNullOrEmpty(_biomeText) ? 0f : _biome.GetPreferredValues(_biomeText).x + 2f * pad;
            _plaque.sizeDelta = new Vector2(Mathf.Max(w, _plaqueMin), _plaque.sizeDelta.y);
        }

        private void UpdateDials()
        {
            var env = EnvMan.instance;
            if (env && _time)
            {
                var day = EnvMan.IsDay();
                var token = day ? "sprite.glyph.time.sun" : "sprite.glyph.time.moon";
                var sprite = ThemeTokens.GetSprite(token);
                if (_time.sprite != sprite)
                    _art.Restyle(_time, token, day ? "color.minimap.sun" : "color.minimap.moon", Image.Type.Simple);
                // Outside the ring edge so it rides the stone, clear of the N rune.
                MinimapMath.ArcPoint(env.GetDayFraction(), day, _box * 0.5f + _timeSocket.rectTransform.sizeDelta.x * 0.2f, out var x, out var y);
                var p = new Vector2(x, y);
                var rt = _timeSocket.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                if ((rt.anchoredPosition - p).sqrMagnitude > 0.01f) rt.anchoredPosition = p;
            }
            if (_windSocket)
            {
                var sailing = Ship.GetLocalShip() != null;
                if (_windSocket.gameObject.activeSelf != sailing) _windSocket.gameObject.SetActive(sailing);
                if (sailing && _map.m_windMarker) _windGlyph.rectTransform.rotation = _map.m_windMarker.rotation;
            }
        }

        // Pins inside the map's square but outside the hole would draw over the ring (their material may ignore the
        // stencil): a CanvasGroup per pin hides them (vanilla never writes it; it re-creates pin objects on a mode switch).
        // Bumped by the Minimap.UpdatePins postfix (MinimapAdapter).
        internal static int PinsVersion;
        private int _pinsCulled = -1;
        private bool _pinsDirty = true;

        private void CullPins()
        {
            if (_map.m_mode != Minimap.MapMode.Small || !_mapImage) return;
            var map = _mapImage.rectTransform;
            var r = _box * 0.5f * ThemeTokens.GetFloat("ratio.minimap.hole", 0.777f);
            if (!_masked) r = _mapImage.rectTransform.sizeDelta.x * 0.5f * 1.4142f;
            foreach (var pin in _map.m_pins)
            {
                if (pin.m_uiElement == null) continue;
                var local = (Vector2)map.InverseTransformPoint(pin.m_uiElement.position) - map.rect.center;
                var square = Mathf.Abs(local.x) <= map.rect.width * 0.5f && Mathf.Abs(local.y) <= map.rect.height * 0.5f;
                var visible = square && MinimapMath.InsideDisc(local.x, local.y, r, pin.m_uiElement.rect.width * 0.5f);
                Show(pin.m_uiElement.gameObject, visible); // its label follows in PlaceLabels (PinLabels.cs)
            }
        }

        private static void Show(GameObject go, bool visible)
        {
            if (!go.TryGetComponent<CanvasGroup>(out var g))
            {
                if (visible) return;
                g = go.AddComponent<CanvasGroup>();
                g.interactable = false;
                g.blocksRaycasts = false;
            }
            var a = visible ? 1f : 0f;
            if (g.alpha != a) g.alpha = a;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static void Center(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        public void Record()
        {
            if (_small) SkinDiagnostics.Record(_art.Report(_small));
            RecordLarge();
        }
    }

    // Family "Minimap" hook: Minimap.Start (Minimap.cs:513) after every other Start postfix (MorgottTweaks
    // ForcePublicPosition, Jotunn MinimapManager); SetMapMode (:1209) restyles the large map when it opens.
    [HarmonyPatch]
    public static class MinimapAdapter
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly), batched by FastStartup).
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.Minimap);

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.Start))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Start_Postfix(Minimap __instance)
        {
            try { MinimapSkin.Attach(__instance); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] minimap restyle failed (vanilla map stays): {e}"); }
        }

        // Pin markers re-placed (Minimap.cs:1601-1655): the small-map cull runs on the next LateUpdate.
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdatePins))]
        [HarmonyPostfix]
        private static void UpdatePins_Postfix() => MinimapSkin.PinsVersion++;

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.SetMapMode))]
        [HarmonyPostfix]
        private static void SetMapMode_Postfix(Minimap.MapMode mode)
        {
            if (mode == Minimap.MapMode.Large) MinimapSkin.Instance?.MarkLargeDirty();
        }
    }
}
