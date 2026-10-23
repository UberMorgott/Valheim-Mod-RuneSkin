using System.Collections.Generic;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RuneSkin.Compat;

namespace RuneSkin.MapScreen
{
    // Large map legend as labelled rows (mockup m4-map-a): header "Pins" + divider, one row per pin-type button of the
    // vanilla panels (IconPanel Icon0..4, IconPanel2 Death / Boss / mod filters such as Epic Loot's, IconPingPanel):
    // recessed socket + the vanilla icon button (moved there, its onClick / right-click wiring and "Selected" frame
    // untouched) + localized name + checkbox. Checkbox = the vanilla visibility filter: Minimap.m_visibleIconTypes /
    // ToggleIconFilter (Minimap.cs:2625) for the types in m_selectedIcons; a mod filter button toggles itself on click,
    // so its checkbox clicks the button and reads its grey tint (the vanilla filtered look, :2632); Ping has no filter.
    // Then the Public / Shared toggle panels, "Players" (ZNet.GetPlayerList: name, dim when the position is hidden),
    // and a zoom row pinned to the bottom (minus | slider on Minimap.LargeZoom | plus). A key hint line runs under the
    // map when the game's KeyHints setting is on (Minimap.SetMapMode, :1237). Buttons stay children of their vanilla
    // panels (mods find them by path); only their LayoutGroups are off and their rects are placed each frame.
    public sealed partial class MinimapSkin
    {
        private sealed class PinRow
        {
            public RectTransform Root;
            public Image Socket, Box, Check;
            public TMP_Text Name;
            public Button Toggle;
            public Button Icon;
        }

        private RectTransform _rows, _footer;
        private readonly List<PinRow> _pinRows = new List<PinRow>();
        private readonly List<Button> _iconButtons = new List<Button>();
        private readonly List<TMP_Text> _playerNames = new List<TMP_Text>();
        private readonly List<Image> _playerIcons = new List<Image>();
        private TMP_Text _pinsHeader, _playersHeader, _zoomLabel;
        private Image _pinsDivider, _playersDivider;
        private Slider _zoom;
        private bool _zoomWrite;
        private int _loggedButtons = -1;
        private Dictionary<GameObject, Minimap.PinType> _types;

        private void BuildLegendRows()
        {
            _rows = HudArtRect("RuneSkin_LegendRows", _legend);
            _rows.SetAsFirstSibling(); // behind the vanilla pin panels: their icon buttons draw over the row sockets
            Top(_rows);
            _rows.offsetMin = _rows.offsetMax = Vector2.zero;
            _rows.anchorMin = Vector2.zero;
            _rows.anchorMax = Vector2.one;
            _pinsHeader = _art.Text("PinsHeader", _rows, "font.display", "color.text.header", ThemeTokens.GetFloat("size.map.legend.header", 21f), TextAlignmentOptions.Center);
            _pinsDivider = _art.Image("PinsDivider", _rows, "sprite.divider", "color.frame", Image.Type.Sliced);
            _playersHeader = _art.Text("PlayersHeader", _rows, "font.display", "color.text.header", ThemeTokens.GetFloat("size.map.legend.header", 21f), TextAlignmentOptions.Center);
            _playersDivider = _art.Image("PlayersDivider", _rows, "sprite.divider", "color.frame", Image.Type.Sliced);
            _zoomLabel = _art.Text("ZoomLabel", _rows, "font.body", "color.text.body", ThemeTokens.GetFloat("size.map.legend.font", 17f), TextAlignmentOptions.Left);
            BuildZoom();
            BuildHints(); // the vanilla key hint row, moved into the band under the map (MapHints.cs)
            // Vanilla panel hint graphics (mouse glyphs next to the icons) give way to the hint row under the map.
            foreach (var panel in LegendPanels)
            {
                var t = _legend.Find(panel);
                if (!t) continue;
                foreach (var group in t.GetComponents<LayoutGroup>()) group.enabled = false;
                if (t.TryGetComponent<ContentSizeFitter>(out var fitter)) fitter.enabled = false;
                for (var i = 0; i < t.childCount; i++)
                {
                    var c = t.GetChild(i);
                    if (c.GetComponentInChildren<Button>(true) || c.name == "Bkg") continue;
                    if (!c.TryGetComponent<CanvasGroup>(out var g)) g = c.gameObject.AddComponent<CanvasGroup>();
                    g.alpha = 0f;
                    g.blocksRaycasts = false;
                    RuneSkin.Restyle.RenderGate.Set(c, "maplegend", true); // not drawn
                    _art.Suppressed(c, "vanilla legend hint graphic (RuneSkin key hint line under the map replaces it)");
                }
            }
        }

        private void BuildZoom()
        {
            var root = HudArtRect("Zoom", _rows);
            _zoom = root.gameObject.AddComponent<Slider>();
            var track = _art.Image("Track", root, "sprite.bar.skill.trough", "color.frame", Image.Type.Sliced, raycast: true);
            Stretch(track.rectTransform);
            var fillArea = HudArtRect("FillArea", root);
            Stretch(fillArea);
            var fill = _art.Image("Fill", fillArea, "sprite.bar.skill.fill", "color.text.accent", Image.Type.Sliced);
            Stretch(fill.rectTransform);
            var handleArea = HudArtRect("HandleArea", root);
            Stretch(handleArea);
            var handle = _art.Image("Handle", handleArea, "sprite.slider.handle.normal", "color.frame", Image.Type.Simple, raycast: true);
            handle.preserveAspect = true;
            handle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _zoom.fillRect = fill.rectTransform;
            _zoom.handleRect = handle.rectTransform;
            _zoom.targetGraphic = handle;
            _zoom.direction = Slider.Direction.LeftToRight;
            _zoom.minValue = 0f;
            _zoom.maxValue = 1f;
            var hover = ThemeTokens.GetSprite("sprite.slider.handle.hover");
            if (hover)
            {
                _zoom.transition = Selectable.Transition.SpriteSwap;
                _zoom.spriteState = new SpriteState { highlightedSprite = hover, pressedSprite = hover, selectedSprite = handle.sprite, disabledSprite = ThemeTokens.GetSprite("sprite.slider.handle.disabled") };
            }
            _zoom.onValueChanged.AddListener(v =>
            {
                if (_zoomWrite || !_map) return;
                _map.LargeZoom = MinimapMath.SliderToZoom(v, _map.m_minZoom, _map.m_maxZoom);
            });
            ZoomButton("Minus", "sprite.glyph.minus", -1);
            ZoomButton("Plus", "sprite.glyph.plus", +1);
        }

        private void ZoomButton(string name, string glyph, int dir)
        {
            var b = _art.Image("Zoom" + name, _rows, "sprite.button.round", "color.frame", Image.Type.Simple, raycast: true);
            b.preserveAspect = true;
            var g = _art.Image("Glyph", b.transform, glyph, "color.text.body", Image.Type.Simple);
            g.preserveAspect = true;
            Stretch(g.rectTransform);
            var inset = ThemeTokens.GetFloat("size.map.zoom.glyph.inset", 7f);
            g.rectTransform.offsetMin = new Vector2(inset, inset);
            g.rectTransform.offsetMax = new Vector2(-inset, -inset);
            var button = b.gameObject.AddComponent<Button>();
            button.targetGraphic = b;
            button.onClick.AddListener(() =>
            {
                if (!_zoom) return;
                _zoom.value = Mathf.Clamp01(_zoom.value + dir * ThemeTokens.GetFloat("ratio.map.zoom.step", 0.125f));
            });
        }

        private static void Top(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
        }

        // Button GameObject -> pin type: the Selected frame images of Minimap.m_selectedIcons sit under their icon button
        // (ToggleIconFilter tints selectedIcon.transform.parent, Minimap.cs:2632); the ping frame likewise.
        private Dictionary<GameObject, Minimap.PinType> Types()
        {
            if (_types != null) return _types;
            _types = new Dictionary<GameObject, Minimap.PinType>();
            foreach (var pair in _map.m_selectedIcons)
                if (pair.Value && pair.Value.transform.parent) _types[pair.Value.transform.parent.gameObject] = pair.Key;
            if (_map.m_selectedIconPing && _map.m_selectedIconPing.transform.parent) _types[_map.m_selectedIconPing.transform.parent.gameObject] = Minimap.PinType.Ping;
            return _types;
        }

        private static string NameKey(Minimap.PinType type)
        {
            switch (type)
            {
                case Minimap.PinType.Icon0: return "$runeskin_pin_icon0";
                case Minimap.PinType.Icon1: return "$runeskin_pin_icon1";
                case Minimap.PinType.Icon2: return "$runeskin_pin_icon2";
                case Minimap.PinType.Icon3: return "$runeskin_pin_icon3";
                case Minimap.PinType.Icon4: return "$runeskin_pin_icon4";
                case Minimap.PinType.Death: return "$runeskin_pin_death";
                case Minimap.PinType.Boss: return "$runeskin_pin_boss";
                case Minimap.PinType.Ping: return "$runeskin_pin_ping";
                default: return null; // mod pin types (Epic Loot 800 / 801...): the button names itself (ModName)
            }
        }

        private static readonly List<Button> ButtonScratch = new List<Button>(16);
        // Mod button labels per button (reflection over Epic Loot fields, name string work): computed once per button
        // and language, not per frame (perf audit B2: the open map allocated ~2 KB per frame).
        // Re-read every RefreshFrames (a mod may set its tooltip / filter fields after the map first opened).
        private readonly Dictionary<Button, KeyValuePair<string, int>> _modNames = new Dictionary<Button, KeyValuePair<string, int>>();
        private const int RefreshFrames = 120;

        private string CachedModName(Button b)
        {
            if (_modNames.TryGetValue(b, out var cached) && Time.frameCount - cached.Value < RefreshFrames) return cached.Key;
            if (_modNames.Count >= 128) _modNames.Clear(); // buttons are rebuilt by their mods now and then
            var label = ModName(b);
            _modNames[b] = new KeyValuePair<string, int>(label, Time.frameCount);
            return label;
        }

        // Mod filter button: its own tooltip text, else its object name without the usual prefixes.
        private static string ModName(Button b)
        {
            var tip = b.GetComponent<UITooltip>();
            if (tip && !string.IsNullOrEmpty(tip.m_text)) return Words.Get(tip.m_text);
            var known = EpicLootFilterName(b);
            if (known != null) return known;
            if (tip && !string.IsNullOrEmpty(tip.m_topic)) return Words.Get(tip.m_topic);
            var n = b.name.Replace("(Clone)", "").Replace("PinFilter", " ").Replace("Filter", " ").Trim('_', ' ');
            // Words of a CamelCase object name ("AdventurePinFilter800" -> "Adventure 800").
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < n.Length; i++)
            {
                if (i > 0 && (char.IsUpper(n[i]) && char.IsLower(n[i - 1]) || char.IsDigit(n[i]) && char.IsLetter(n[i - 1])) && n[i - 1] != ' ') sb.Append(' ');
                sb.Append(n[i]);
            }
            var s = sb.ToString().Trim();
            return string.IsNullOrEmpty(s) ? Words.Get("$runeskin_pin_mod") : s;
        }

        // Epic Loot's adventure pin filters (static fields _bountyPinFilter / _treasurePinFilter, its own pin names
        // $mod_epicloot_merchant_bounties / $mod_epicloot_merchant_treasuremaps): untitled buttons, named here
        // with Epic Loot's own localized section names (its pin-name keys are "$1" formats) (Codex M4 r3: generic "mod pins" labels).
        private static System.Collections.Generic.List<(System.Reflection.FieldInfo Field, string Key)> _elFilters;

        private static string EpicLootFilterName(Button b)
        {
            if (_elFilters == null)
            {
                _elFilters = new System.Collections.Generic.List<(System.Reflection.FieldInfo, string)>();
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name != ModCompat.EpicLootAssembly) continue;
                    System.Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }
                    foreach (var t in types)
                    {
                        if (t == null) continue;
                        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                        var bounty = t.GetField("_bountyPinFilter", F);
                        var treasure = t.GetField("_treasurePinFilter", F);
                        if (bounty != null) _elFilters.Add((bounty, "$mod_epicloot_merchant_bounties"));
                        if (treasure != null) _elFilters.Add((treasure, "$mod_epicloot_merchant_treasuremaps"));
                    }
                }
                Plugin.Log.LogInfo($"[RuneSkin] map legend: Epic Loot pin filter fields {_elFilters.Count} [{string.Join(",", _elFilters.ConvertAll(f => f.Field.DeclaringType?.Name + "." + f.Field.Name + (f.Field.IsStatic ? " static" : "")))}]");
            }
            foreach (var (field, key) in _elFilters)
            {
                object v = null;
                try
                {
                    if (field.IsStatic) v = field.GetValue(null);
                    else if (typeof(Component).IsAssignableFrom(field.DeclaringType))
                        foreach (var owner in UnityEngine.Object.FindObjectsByType(field.DeclaringType, FindObjectsInactive.Include, FindObjectsSortMode.None))
                            if ((v = field.GetValue(owner)) != null) break;
                }
                catch (System.Exception) { continue; }
                var go = v is GameObject g ? g : (v as Component) ? ((Component)v).gameObject : null;
                var hit = go && (go == b.gameObject || b.transform.IsChildOf(go.transform) || go.transform.IsChildOf(b.transform));
                // The field holds Epic Loot's AdventurePinFilter object (run 20261022-071214), which keeps its button /
                // GameObject in its own fields.
                if (!hit && v != null && !go)
                    foreach (var f in v.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                    {
                        object inner;
                        try { inner = f.GetValue(v); }
                        catch (System.Exception) { continue; }
                        var ig = inner is GameObject ig0 ? ig0 : (inner as Component) ? ((Component)inner).gameObject : null;
                        if (ig && ig == b.gameObject) { hit = true; go = ig; break; } // exact: both filters share parent refs (run 20261022-072233 named both rows "bounties")
                    }
                if (!hit) continue;
                var label = Localization.instance != null ? Localization.instance.Localize(key) : key;
                return string.IsNullOrEmpty(label) || label.StartsWith("[", System.StringComparison.Ordinal) ? null : label;
            }
            return null;
        }

        private PinRow NewRow()
        {
            var r = new PinRow { Root = HudArtRect("PinRow", _rows) };
            Top(r.Root);
            r.Socket = _art.Image("Socket", r.Root, "sprite.slot.recess", "color.frame", Image.Type.Sliced);
            r.Name = _art.Text("Name", r.Root, "font.body", "color.text.body", ThemeTokens.GetFloat("size.map.legend.font", 17f), TextAlignmentOptions.Left);
            r.Box = _art.Image("Check", r.Root, "sprite.checkbox.box", "color.frame", Image.Type.Sliced, raycast: true);
            r.Check = _art.Image("Mark", r.Box.transform, "sprite.glyph.check", "color.text.accent", Image.Type.Simple);
            r.Check.preserveAspect = true;
            Stretch(r.Check.rectTransform);
            r.Toggle = r.Box.gameObject.AddComponent<Button>();
            r.Toggle.targetGraphic = r.Box;
            r.Toggle.onClick.AddListener(() => OnToggle(r));
            return r;
        }

        private void OnToggle(PinRow r)
        {
            if (!r.Icon) return;
            if (Types().TryGetValue(r.Icon.gameObject, out var type))
            {
                if (type != Minimap.PinType.Ping) _map.ToggleIconFilter(type);
            }
            else r.Icon.onClick.Invoke();
        }

        private bool Visible(Button icon, out bool hasFilter)
        {
            if (Types().TryGetValue(icon.gameObject, out var type))
            {
                hasFilter = type != Minimap.PinType.Ping && (int)type < _map.m_visibleIconTypes.Length;
                return !hasFilter || _map.m_visibleIconTypes[(int)type];
            }
            hasFilter = true;
            var c = icon.targetGraphic ? icon.targetGraphic.color : Color.white;
            return c.grayscale > 0.75f;
        }

        // Per frame while the large map is open: rows for the current buttons (mods may add filters any time).
        private void LegendRows(float inset)
        {
            if (!_rows) return;
            _iconButtons.Clear();
            foreach (var panel in LegendPanels)
            {
                var t = _legend.Find(panel);
                if (!t || !t.gameObject.activeSelf) continue;
                t.GetComponentsInChildren(false, ButtonScratch);
                _iconButtons.AddRange(ButtonScratch);
                ButtonScratch.Clear();
            }
            if (_iconButtons.Count != _loggedButtons)
            {
                _loggedButtons = _iconButtons.Count;
                var names = new List<string>();
                foreach (var b in _iconButtons) names.Add($"{b.transform.parent.name}/{b.name}" + (Types().TryGetValue(b.gameObject, out var ty) ? $"={ty}" : ""));
                Plugin.Log.LogInfo($"[RuneSkin] map legend rows: {string.Join(", ", names)}");
            }
            var w = _legend.rect.width;
            var h = _legend.rect.height;
            var pad = ThemeTokens.GetFloat("size.map.legend.pad", 16f);
            var headerH = ThemeTokens.GetFloat("size.map.legend.header.h", 30f);
            var y = pad;
            Place(_pinsHeader.rectTransform, pad, y, w - 2f * pad, headerH);
            _pinsHeader.text = Words.Get("$runeskin_map_pins");
            // The "Pins" title band drags the legend side panel (MovableWindow on _legend, BuildLarge).
            var band = RuneSkin.Restyle.WindowMove.Handle(_rows, RuneSkin.Restyle.WindowMove.MapLegend, _legend);
            if (band) Place(band.rectTransform, 0f, 0f, w, y + headerH);
            y += headerH;
            Place(_pinsDivider.rectTransform, pad, y, w - 2f * pad, ThemeTokens.GetFloat("size.map.legend.divider", 11f));
            y += ThemeTokens.GetFloat("size.map.legend.divider", 11f) + 4f;

            // Bottom block (zoom) reserved first; the pin rows take what the toggles / players leave.
            var zoomH = ThemeTokens.GetFloat("size.map.zoom.h", 34f);
            var zoomTop = h - pad - zoomH;
            var labelH = ThemeTokens.GetFloat("size.map.legend.label.h", 24f);
            var toggles = 0f;
            foreach (var panel in TogglePanels)
            {
                var t = _legend.Find(panel) as RectTransform;
                if (t && t.gameObject.activeSelf) toggles += t.rect.height + 6f;
            }
            var players = ZNet.instance ? ZNet.instance.GetPlayerList() : null;
            var playerRow = ThemeTokens.GetFloat("size.map.player.row", 26f);
            var playersH = headerH + 15f + (players?.Count ?? 0) * playerRow;
            var rowH = MinimapMath.RowHeight(zoomTop - labelH - y - toggles - playersH - 8f, _iconButtons.Count,
                ThemeTokens.GetFloat("size.map.legend.row", 40f), ThemeTokens.GetFloat("size.map.legend.row.min", 28f));
            while (_pinRows.Count < _iconButtons.Count) _pinRows.Add(NewRow());
            var k = _legend.lossyScale.x;
            for (var i = 0; i < _pinRows.Count; i++)
            {
                var r = _pinRows[i];
                var on = i < _iconButtons.Count;
                if (r.Root.gameObject.activeSelf != on) r.Root.gameObject.SetActive(on);
                if (!on) { r.Icon = null; continue; }
                r.Icon = _iconButtons[i];
                Place(r.Root, pad, y, w - 2f * pad, rowH);
                var cell = rowH - 4f;
                Place(r.Socket.rectTransform, 0f, 2f, cell, cell);
                var box = Mathf.Min(ThemeTokens.GetFloat("size.map.legend.check", 24f), rowH - 8f);
                var rowW = w - 2f * pad;
                Place(r.Box.rectTransform, rowW - box, (rowH - box) * 0.5f, box, box);
                Place(r.Name.rectTransform, cell + 10f, 0f, rowW - cell - 10f - box - 6f, rowH);
                var key = Types().TryGetValue(r.Icon.gameObject, out var type) ? NameKey(type) : null;
                var label = key != null ? Words.Get(key) : CachedModName(r.Icon);
                if (r.Name.text != label) r.Name.text = label;
                var visible = Visible(r.Icon, out var filter);
                if (r.Box.gameObject.activeSelf != filter) r.Box.gameObject.SetActive(filter);
                if (r.Check.enabled != visible) r.Check.enabled = visible;
                r.Name.color = ThemeTokens.GetColor(visible ? "color.text.body" : "color.state.disabled", Color.white);
                // The vanilla button sits in the socket (world placement: it stays in its own panel).
                var icon = (RectTransform)r.Icon.transform;
                icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(0.5f, 0.5f);
                var parentK = icon.parent ? icon.parent.lossyScale.x : 1f;
                var s = (cell - ThemeTokens.GetFloat("size.map.legend.icon.inset", 8f)) * (parentK > 0f ? k / parentK : 1f);
                if (Mathf.Abs(icon.sizeDelta.x - s) > 0.1f || Mathf.Abs(icon.sizeDelta.y - s) > 0.1f) icon.sizeDelta = new Vector2(s, s);
                var socket = r.Socket.rectTransform;
                var world = socket.TransformPoint(socket.rect.center);
                if ((icon.position - world).sqrMagnitude > 1e-4f) icon.position = world;
                y += rowH;
            }

            // Public / Shared toggle panels under the rows (vanilla toggles + labels).
            y += 6f;
            foreach (var panel in TogglePanels)
            {
                var t = _legend.Find(panel) as RectTransform;
                if (!t || !t.gameObject.activeSelf) continue;
                MinimapSkin.Place(t, new Vector2(0f, -y));
                y += t.rect.height + 6f;
            }

            // Players.
            Place(_playersHeader.rectTransform, pad, y, w - 2f * pad, headerH);
            _playersHeader.text = Words.Get("$runeskin_map_players");
            y += headerH;
            Place(_playersDivider.rectTransform, pad, y, w - 2f * pad, 11f);
            y += 15f;
            var n = players?.Count ?? 0;
            while (_playerNames.Count < n)
            {
                var icon = _art.Image("PlayerIcon", _rows, null, "color.text.body", Image.Type.Simple);
                icon.preserveAspect = true;
                _playerIcons.Add(icon);
                _playerNames.Add(_art.Text("PlayerName", _rows, "font.body", "color.text.body", ThemeTokens.GetFloat("size.map.legend.font", 17f), TextAlignmentOptions.Left));
            }
            var localName = Game.instance && Game.instance.GetPlayerProfile() != null ? Game.instance.GetPlayerProfile().GetName() : null;
            for (var i = 0; i < _playerNames.Count; i++)
            {
                var on = i < n && y + playerRow <= zoomTop - labelH;
                if (_playerNames[i].gameObject.activeSelf != on) { _playerNames[i].gameObject.SetActive(on); _playerIcons[i].gameObject.SetActive(on); }
                if (!on) continue;
                var p = players[i];
                var me = p.m_name == localName;
                var sprite = me && _map.m_largeMarker && _map.m_largeMarker.TryGetComponent<Image>(out var mk) ? mk.sprite : _map.GetSprite(Minimap.PinType.Player);
                if (_playerIcons[i].sprite != sprite) { _playerIcons[i].sprite = sprite; _playerIcons[i].enabled = sprite; }
                var ic = playerRow - 8f;
                Place(_playerIcons[i].rectTransform, pad + 6f, y + 4f, ic, ic);
                Place(_playerNames[i].rectTransform, pad + ic + 16f, y, w - 2f * pad - ic - 16f, playerRow);
                if (_playerNames[i].text != p.m_name) _playerNames[i].text = p.m_name;
                _playerNames[i].color = ThemeTokens.GetColor(me || p.m_publicPosition ? "color.text.body" : "color.state.disabled", Color.white);
                y += playerRow;
            }

            // Zoom row pinned to the bottom: label above, minus | slider | plus.
            Place(_zoomLabel.rectTransform, pad, zoomTop - labelH, w - 2f * pad, labelH);
            _zoomLabel.text = Words.Get("$runeskin_map_zoom");
            var minus = _rows.Find("ZoomMinus") as RectTransform;
            var plus = _rows.Find("ZoomPlus") as RectTransform;
            if (minus) Place(minus, pad, zoomTop, zoomH, zoomH);
            if (plus) Place(plus, w - pad - zoomH, zoomTop, zoomH, zoomH);
            var trackH = ThemeTokens.GetFloat("size.map.zoom.track", 12f);
            var zr = (RectTransform)_zoom.transform;
            Place(zr, pad + zoomH + 10f, zoomTop + (zoomH - trackH) * 0.5f, w - 2f * pad - 2f * zoomH - 20f, trackH);
            var hs = ThemeTokens.GetFloat("size.map.zoom.handle", 26f);
            var hr = _zoom.handleRect;
            if (hr && Mathf.Abs(hr.sizeDelta.x - hs) > 0.1f) hr.sizeDelta = new Vector2(hs, hs - trackH);
            var v = MinimapMath.ZoomToSlider(_map.LargeZoom, _map.m_minZoom, _map.m_maxZoom);
            if (Mathf.Abs(_zoom.value - v) > 0.002f)
            {
                _zoomWrite = true;
                _zoom.value = v;
                _zoomWrite = false;
            }

            LayoutHints(inset);
        }

        // Band under the map image that holds the key hint row (0 while the game's KeyHints setting is off: vanilla then
        // hides m_hints, Minimap.cs:1238-1242).
        private float HintBand() => _footer && PlatformPrefs.GetInt("KeyHints", 1) == 1 ? ThemeTokens.GetFloat("size.map.hint.h", 28f) : 0f;

        // Top-left placement inside the legend (y down from the top edge).
        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            if (rt.anchorMin != new Vector2(0f, 1f) || rt.anchorMax != new Vector2(0f, 1f) || rt.pivot != new Vector2(0f, 1f)) Top(rt);
            var p = new Vector2(x, -y);
            if ((rt.anchoredPosition - p).sqrMagnitude > 0.01f) rt.anchoredPosition = p;
            var s = new Vector2(w, h);
            if ((rt.sizeDelta - s).sqrMagnitude > 0.01f) rt.sizeDelta = s;
        }
    }
}
