using System.Collections.Generic;
using System.Text;
using RuneSkin.Ownership;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.PlayerHud
{
    // Ammo plate (user decision 2026-10-27 "1A", mockup docs\mockups\ammo-hud\f1-varA-plate-zoom.png): while the current
    // weapon is a bow or crossbow, the screen's top-left corner (the party frame's slot: its origin / scale tokens; the
    // party frame shows only with a summoning weapon, and if both ever show, this plate moves below its grid) holds a rune
    // tile with the icon of the ammo vanilla would fire next and a content-sized nameplate (FitPlate): localized ammo name over
    // "×<total>" (accent). Crossbow (MorgottTweaks magazine): the second line adds the magazine readout, up to 7 dots +
    // "n/N", larger magazines one dot + "n/N" (AmmoMath.PlateRow). No ammo: the weapon's ammo type name + "×0", dimmed.
    // Ammo = Attack.FindAmmo's choice (AmmoMath.SelectAmmo), total = that prefab's stacks in the player's inventory
    // (AmmoMath.Total). Data refreshed 4x a second and on a MorgottTweaks magazine revision.
    public sealed class AmmoFrame : MonoBehaviour
    {
        public const string RootName = "RuneSkin_AmmoFrame";
        private const float Refresh = 0.25f;

        private readonly HudArt _art = new HudArt("AmmoFrame");
        private readonly List<AmmoCandidate> _candidates = new List<AmmoCandidate>();
        private readonly List<ItemDrop.ItemData> _items = new List<ItemDrop.ItemData>();
        private readonly List<Image> _dots = new List<Image>();
        private readonly Vector3[] _corners = new Vector3[4];
        private RectTransform _rt, _plateRt, _readout;
        private Image _socket, _icon, _plate;
        private TMP_Text _name, _count, _mag;
        private float _timer, _pw, _magRight;
        private int _revision = int.MinValue;
        private bool _shown;

        // Last shown state (change-only writes + harness).
        private ItemDrop.ItemData _weapon, _ammo;
        private string _nameText = "", _countText = "";
        private int _total = -1, _loaded = -1, _capacity = -1, _theme = -1;
        private bool _empty;

        public static AmmoFrame Instance { get; private set; }

        public static void Attach(Hud hud)
        {
            var parent = hud && hud.m_rootObject ? hud.m_rootObject.transform : null;
            if (!parent || parent.Find(RootName)) return;
            var rt = new GameObject(RootName, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            var f = rt.gameObject.AddComponent<AmmoFrame>();
            f._rt = rt;
            f.Build();
            Instance = f;
            Plugin.Log.LogInfo("[RuneSkin] ammo plate attached under the HUD root");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private static float F(string token, float fallback) => ThemeTokens.GetFloat(token, fallback);

        // ---- visibility

        // A weapon that fires ammo: bow or crossbow (Skills.SkillType.Bows / Crossbows; every reload weapon, modded
        // crossbows included) with an m_ammoType (Attack.FindAmmo returns nothing without one).
        public static bool IsAmmoWeapon(ItemDrop.ItemData w)
        {
            var s = w?.m_shared;
            if (s == null || string.IsNullOrEmpty(s.m_ammoType)) return false;
            return s.m_skillType == Skills.SkillType.Bows || s.m_skillType == Skills.SkillType.Crossbows ||
                   (s.m_attack != null && s.m_attack.m_requiresReload);
        }

        private void Update()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas) _art.SyncPpu(canvas.rootCanvas.referencePixelsPerUnit);
            var player = Player.m_localPlayer;
            var mapOpen = Minimap.instance && Minimap.instance.m_mode == Minimap.MapMode.Large;
            var weapon = player && !player.IsDead() ? player.GetCurrentWeapon() : null;
            var show = IsAmmoWeapon(weapon) && !mapOpen;
            if (_shown != show)
            {
                _shown = show;
                SetChildren(show);
                if (show)
                {
                    _timer = 0f;
                    _weapon = null; // full refill (the readout was switched back on with the rest)
                }
            }
            if (!show) return;
            var revision = MagazineSource.Revision;
            _timer -= Time.unscaledDeltaTime;
            if (_timer <= 0f || revision != _revision || !ReferenceEquals(weapon, _weapon))
            {
                _timer = Refresh;
                _revision = revision;
                Collect(player, weapon);
            }
            Place();
        }

        // The root stays active (Update runs); its children hide.
        private void SetChildren(bool on)
        {
            for (var i = 0; i < _rt.childCount; i++)
            {
                var c = _rt.GetChild(i).gameObject;
                if (c.activeSelf != on) c.SetActive(on);
            }
        }

        // ---- data

        private void Collect(Player player, ItemDrop.ItemData weapon)
        {
            var inv = player.GetInventory();
            _candidates.Clear();
            _items.Clear();
            var equipped = -1;
            var ammoItem = player.GetAmmoItem();
            if (inv != null)
                foreach (var it in inv.GetAllItems())
                {
                    if (it?.m_shared == null || string.IsNullOrEmpty(it.m_shared.m_ammoType)) continue;
                    if (ReferenceEquals(it, ammoItem)) equipped = _items.Count;
                    _items.Add(it);
                    _candidates.Add(new AmmoCandidate
                    {
                        ItemType = (int)it.m_shared.m_itemType,
                        AmmoType = it.m_shared.m_ammoType,
                        Prefab = it.m_dropPrefab ? it.m_dropPrefab.name : it.m_shared.m_name,
                        GridX = it.m_gridPos.x,
                        GridY = it.m_gridPos.y,
                        Stack = it.m_stack
                    });
                }
            var sel = AmmoMath.SelectAmmo(weapon.m_shared.m_ammoType, _candidates, equipped, inv != null ? inv.GetWidth() : 0);
            var ammo = sel >= 0 ? _items[sel] : null;
            var total = AmmoMath.Total(_candidates, sel);
            MagazineSource.Read(weapon, out var loaded, out var capacity);
            Fill(weapon, ammo, total, loaded, capacity);
        }

        // ---- build / fill

        private void Build()
        {
            _socket = _art.Image("Socket", _rt, "sprite.hud.socket.hotbar", "color.frame", Image.Type.Simple);
            _icon = _art.Image("Icon", _rt, null, "color.frame", Image.Type.Simple);
            _icon.preserveAspect = true;
            _plate = _art.Image("Plate", _rt, "sprite.hud.ammo.plate", "color.frame", Image.Type.Sliced);
            // Sliced ends at the sprite's authored density: 576 px = 144 units (4 px a unit); at 200 PPU a sprite px is
            // 0.5 / scale units (HudArt.Track), so scale 0.5 (scale 1 drew the knotwork ends twice as wide).
            _art.Track(_plate, 0.5f);
            _plateRt = _plate.rectTransform;
            _name = _art.Text("Name", _plateRt, "font.body.bold", "color.hud.text", F("size.hud.ammo.name", 10f), TextAlignmentOptions.Left);
            _name.overflowMode = TextOverflowModes.Ellipsis;
            _name.enableAutoSizing = true;
            _count = _art.Text("Count", _plateRt, "font.body.bold", "color.text.accent", F("size.hud.ammo.count", 12f), TextAlignmentOptions.Left);
            _readout = HudArt.Rect("Magazine", _plateRt);
            _readout.anchorMin = _readout.anchorMax = _readout.pivot = new Vector2(0f, 1f);
            _mag = _art.Text("Text", _readout, "font.body.bold", "color.hud.text", F("size.hud.ammo.mag", 9f), TextAlignmentOptions.Left);
            foreach (var t in new[] { _name, _count, _mag }) Outline(t);
            Layout();
            SetChildren(false);
        }

        // Fixed geometry (units before the root's scale; mockup f1-varA at 2560 x 1440 / (1.333 x 2)).
        private void Layout()
        {
            _theme = ThemeTokens.Revision;
            var tile = F("size.hud.ammo.tile", 42f);
            MagazinePips.Place(_socket.rectTransform, 0f, 0f, tile, tile);
            var inset = Mathf.Round(tile * F("share.hud.ammo.icon.inset", 0.125f));
            MagazinePips.Place(_icon.rectTransform, inset, inset, tile - 2f * inset, tile - 2f * inset);
            var nameSize = F("size.hud.ammo.name", 10f);
            _name.fontSizeMax = nameSize;
            _name.fontSizeMin = nameSize * F("share.hud.ammo.name.min", 0.7f);
            MagazinePips.Place(_readout, 0f, 0f, 0f, 0f);
            _pw = -1f;
            SetPlateWidth(F("size.hud.ammo.plate.w", 160f));
        }

        // Plate width (Sliced sprite: the knotwork ends keep their size) + the boxes that depend on it.
        private void SetPlateWidth(float pw)
        {
            if (Mathf.Approximately(pw, _pw)) return;
            _pw = pw;
            var tile = F("size.hud.ammo.tile", 42f);
            var pad = F("size.hud.ammo.pad", 11.25f);
            MagazinePips.Place(_plateRt, tile + F("size.hud.ammo.gap", 1.5f), F("size.hud.ammo.plate.y", 3.75f), pw, F("size.hud.ammo.plate.h", 34.5f));
            var nameSize = F("size.hud.ammo.name", 10f);
            MagazinePips.Place(_name.rectTransform, pad, F("size.hud.ammo.name.y", 3f), Mathf.Max(1f, pw - 2f * pad), nameSize * 1.3f);
            var line = F("size.hud.ammo.line.y", 24f); // second line centre
            var countSize = F("size.hud.ammo.count", 12f);
            MagazinePips.Place(_count.rectTransform, pad, line - countSize * 0.65f, Mathf.Max(1f, pw - 2f * pad), countSize * 1.3f);
            var s = new Vector2(tile + F("size.hud.ammo.gap", 1.5f) + pw, tile);
            if (_rt.sizeDelta != s) _rt.sizeDelta = s;
        }

        // Content-sized plate (mockup f1-varA): the longer of the name (at full size; TMP measures an autosized text at
        // fontSizeMax) and the second line (count + magazine readout), plus the side padding, within [w.min, w].
        private void FitPlate()
        {
            var pad = F("size.hud.ammo.pad", 11.25f);
            var nameRight = pad + _name.GetPreferredValues(_nameText).x;
            var lineRight = pad + _count.GetPreferredValues(_countText).x;
            if (_readout.gameObject.activeSelf) lineRight = Mathf.Max(lineRight, _magRight);
            SetPlateWidth(AmmoMath.PlateWidth(nameRight, lineRight, pad, F("size.hud.ammo.plate.w.min", 96f), F("size.hud.ammo.plate.w", 160f)));
        }

        private void Fill(ItemDrop.ItemData weapon, ItemDrop.ItemData ammo, int total, int loaded, int capacity)
        {
            var theme = _theme != ThemeTokens.Revision;
            if (theme) Layout();
            var empty = ammo == null;
            var label = Localization.instance != null ? Localization.instance.Localize(empty ? weapon.m_shared.m_ammoType : ammo.m_shared.m_name) : "";
            var count = AmmoMath.CountText(total);
            var same = !theme && ReferenceEquals(weapon, _weapon) && ReferenceEquals(ammo, _ammo) && empty == _empty && total == _total &&
                       loaded == _loaded && capacity == _capacity && label == _nameText;
            if (same) return;
            var iconChanged = !ReferenceEquals(ammo, _ammo) || empty != _empty || !ReferenceEquals(weapon, _weapon) || theme;
            _weapon = weapon;
            _ammo = ammo;
            _empty = empty;
            _total = total;
            _loaded = loaded;
            _capacity = capacity;
            if (iconChanged)
            {
                var crossbow = weapon.m_shared.m_skillType == Skills.SkillType.Crossbows || weapon.m_shared.m_attack.m_requiresReload;
                var icon = empty ? ThemeTokens.GetSprite(crossbow ? "sprite.glyph.sub.bolts" : "sprite.glyph.sub.arrows") : ammo.GetIcon();
                _icon.sprite = icon;
                _icon.enabled = icon;
                _icon.color = empty ? ThemeTokens.GetColor("color.hud.inert", Color.gray) : Color.white;
            }
            var inert = ThemeTokens.GetColor("color.hud.inert", Color.gray);
            if (_nameText != label) _name.text = _nameText = label;
            _name.color = empty ? inert : ThemeTokens.GetColor("color.hud.text", Color.white);
            if (_countText != count) _count.text = _countText = count;
            _count.color = empty ? inert : ThemeTokens.GetColor("color.text.accent", Color.cyan);
            Readout();
            FitPlate();
        }

        // Magazine readout right of the count, on the second line.
        private void Readout()
        {
            var mode = AmmoMath.Mode(_capacity);
            var on = mode != MagazineMode.None;
            if (_readout.gameObject.activeSelf != on) _readout.gameObject.SetActive(on);
            if (!on) return;
            var pad = F("size.hud.ammo.pad", 11.25f);
            var x = pad + _count.GetPreferredValues(_countText).x + F("size.hud.ammo.readout.gap", 9f);
            var dot = F("size.hud.ammo.pip", 7.5f);
            var r = AmmoMath.PlateRow(x, F("size.hud.ammo.line.y", 24f), _loaded, _capacity, dot, F("size.hud.ammo.pip.gap", 1.5f), F("size.hud.ammo.mag.gap", 4.5f));
            MagazinePips.Fill(_dots, _readout, r, "RuneSkin ammo plate magazine dot");
            _mag.text = AmmoMath.MagazineText(_loaded, _capacity);
            var size = F("size.hud.ammo.mag", 9f);
            var magW = _mag.GetPreferredValues(_mag.text).x;
            _magRight = r.TextX + magW;
            MagazinePips.Place(_mag.rectTransform, r.TextX, r.Y + r.Dot / 2f - size * 0.65f, Mathf.Max(1f, magW + pad * 0.5f), size * 1.3f);
        }

        private static void Outline(TMP_Text t)
        {
            t.outlineWidth = 0.2f;
            t.outlineColor = ThemeTokens.GetColor("color.text.outline", Color.black);
        }

        // ---- placement (the party frame's: size.hud.party.x / .y reference px of a 1080p screen from the safe area's
        // top-left, scale.hud.party with the screen height; SummonFrames.Place)

        private void Place()
        {
            var parent = _rt.parent as RectTransform;
            if (!parent) return;
            var px = Screen.height / 1080f;
            var ls = Mathf.Abs(parent.lossyScale.x);
            var canvas = parent.GetComponentInParent<Canvas>();
            if (canvas && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay && canvas.rootCanvas.scaleFactor > 0f)
                ls = canvas.rootCanvas.scaleFactor * Mathf.Abs(parent.lossyScale.x) / Mathf.Max(1e-6f, Mathf.Abs(canvas.rootCanvas.transform.lossyScale.x));
            if (!(ls > 0f)) return;
            var k = px / ls;
            var safe = Screen.safeArea;
            PartyMath.Origin(F("size.hud.party.x", 16f), F("size.hud.party.y", 16f), k, safe.xMin, Screen.height - safe.yMax, 1f / ls, out var ox, out var oy);
            var g = Mathf.Max(0.1f, F("scale.hud.party", 2f));
            // Below a shown party frame grid (both weapons in hand): its bottom edge in this parent's space, minus a gap.
            if (SummonFrames.GridScreenBottom(out var gridY))
            {
                var cam = canvas && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, new Vector2(0f, gridY), cam, out var local))
                {
                    var below = local.y - parent.rect.yMax - F("size.hud.ammo.party.gap", 6f) * k;
                    if (below < oy) oy = below;
                }
            }
            var want = new Vector2(ox, oy);
            if ((_rt.anchoredPosition - want).sqrMagnitude > 0.01f) _rt.anchoredPosition = want;
            var scale = new Vector3(k * g, k * g, 1f);
            if (_rt.localScale != scale) _rt.localScale = scale;
        }

        // Bottom edge of the shown plate in screen px (bottom-left origin) for MessageSkin; false when hidden.
        public static bool ScreenBottom(out float y)
        {
            y = 0f;
            var f = Instance;
            if (!f || !f._shown || !f._rt.gameObject.activeInHierarchy) return false;
            var canvas = f.GetComponentInParent<Canvas>();
            var cam = canvas && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            f._rt.GetWorldCorners(f._corners);
            y = Mathf.Min(RectTransformUtility.WorldToScreenPoint(cam, f._corners[0]).y, RectTransformUtility.WorldToScreenPoint(cam, f._corners[2]).y);
            return true;
        }

        // Harness: shown, weapon, ammo, texts, magazine, dots (filled) and the tile / plate screen rects (x,y,w,h
        // bottom-left origin), invariant culture.
        public static string DebugState()
        {
            var thread = System.Threading.Thread.CurrentThread;
            var culture = thread.CurrentCulture;
            thread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            try
            {
                var f = Instance;
                if (!f) return "no ammo plate";
                var sb = new StringBuilder();
                sb.Append($"shown={(f._shown ? 1 : 0)} weapon={(f._weapon != null ? f._weapon.m_shared.m_name : "-")} ammo={(f._ammo != null ? f._ammo.m_shared.m_name : "-")}");
                sb.Append($" name='{f._nameText}' count='{f._countText}' empty={(f._empty ? 1 : 0)} mag={f._loaded}/{f._capacity} mode={AmmoMath.Mode(f._capacity)}");
                var on = 0;
                var filled = 0;
                var full = ThemeTokens.GetSprite("sprite.hud.pip.full");
                foreach (var d in f._dots)
                    if (d.gameObject.activeInHierarchy) { on++; if (d.sprite && d.sprite == full) filled++; }
                sb.Append($" dots={on}({filled}) magText='{(f._mag.gameObject.activeInHierarchy ? f._mag.text : "")}'");
                sb.Append($" tile={Rect(f, f._socket.rectTransform)} plate={Rect(f, f._plateRt)} platew={f._pw:0.##}");
                return sb.ToString();
            }
            finally { thread.CurrentCulture = culture; }
        }

        private static string Rect(AmmoFrame f, RectTransform rt)
        {
            var canvas = f.GetComponentInParent<Canvas>();
            var cam = canvas && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            rt.GetWorldCorners(f._corners);
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, f._corners[0]), b = RectTransformUtility.WorldToScreenPoint(cam, f._corners[2]);
            return $"{a.x:0},{a.y:0},{b.x - a.x:0},{b.y - a.y:0}";
        }
    }

    // Installed only when RuneSkin owns the HUD (Plugin).
    [HarmonyLib.HarmonyPatch]
    public static class AmmoFrameAdapter
    {
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.Hud);

        [HarmonyLib.HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
        [HarmonyLib.HarmonyPostfix]
        private static void Awake_Postfix(Hud __instance) => AmmoFrame.Attach(__instance);
    }
}
