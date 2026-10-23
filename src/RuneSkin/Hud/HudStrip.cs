using System.Collections.Generic;
using RuneSkin.Api;
using RuneSkin.API;
using RuneSkin.InventoryScreen;
using RuneSkin.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.PlayerHud
{
    // The bottom HUD strip, mockup hud-4M3 (user approved 2026-10-22). Frame pieces are Codex redraws of the mockup at
    // 4x (art\src\kit\cuts.json strip4_*): the two wings (9-slice, knot end-cap = fixed left border, rails tiled; the
    // right wing is the left one mirrored), the bar block and the hotbar block (9-slice, braided borders tiled), the
    // hotbar | ability divider, the two medallions (knot frame + the shared triquetra glyph sprite.ornament.triquetra)
    // and the knot dividers between split resource bars. Hotbar = player inventory row 0 in 8 cells, abilities 1..2 from
    // the RuneSkin.API ability registry (guardian power, Epic Loot set skill), food 0..N (capacity list "food", vanilla
    // 3), potions 0..N (capacity list "potions" = MorgottTweaks PotionSlotCount, else the active EAQS quick cells). Counts
    // are polled every frame (pull providers, cheap) and the geometry is recomputed only when one of them or the screen /
    // config changed (LayoutKey); every cell write is change-only, cells are pooled, nothing allocates per frame.
    public sealed class HudStrip
    {
        public const int HotbarCells = 8;
        public const int MaxRowDividers = 3;

        private readonly HudArt _art;
        private readonly RectTransform _root, _back;
        private RectTransform _front;
        private readonly Image _wingL, _wingR, _bars, _block, _divider;
        private Image _medL, _medR, _glyphL, _glyphR;
        private readonly Image[] _rowDiv = new Image[MaxRowDividers];
        private readonly StripCell[] _hotbar = new StripCell[HotbarCells];
        private readonly List<StripCell> _food = new List<StripCell>(5), _potions = new List<StripCell>(6), _abilities = new List<StripCell>(2);
        private readonly List<EaqsBridge.QuickInfo> _quick = new List<EaqsBridge.QuickInfo>(6);
        private float _quickReadAt;
        private int _quickRevision;

        // Valid ability slots this frame: provider index + state.
        private readonly int[] _abilityIndex = new int[4];
        private readonly AbilitySlotState[] _abilityState = new AbilitySlotState[4];

        public int FoodCount { get; private set; }
        public int PotionCount { get; private set; }
        public int AbilityCount { get; private set; }
        public StripMath.Layout L;

        // Extra scale of the strip4 frame pieces (1: the cuts are 2x the mockup's px and ship at PPU 200 = 2 px per unit).
        private static float ArtScale => ThemeTokens.GetFloat("share.hud.strip4.art", 1f);

        public HudStrip(HudArt art, RectTransform root)
        {
            _art = art;
            _root = root;
            _back = HudArt.Rect("StripBack", root);
            // Draw order: wings (tucked under the centre) -> bar block -> hotbar block -> divider; the bars follow (HudCluster),
            // then the medallions and the cells (BuildFront).
            _wingL = Piece("WingLeft", _back, "sprite.hud.strip4.wing", Image.Type.Tiled);
            _wingR = Piece("WingRight", _back, "sprite.hud.strip4.wing", Image.Type.Tiled);
            _bars = Piece("BarBlock", _back, "sprite.hud.strip4.bars", Image.Type.Tiled);
            _block = Piece("HotbarBlock", _back, "sprite.hud.strip4.block", Image.Type.Tiled);
            _divider = Piece("Divider", _back, "sprite.hud.strip4.divider", Image.Type.Simple);
        }

        private Image Piece(string name, Transform parent, string token, Image.Type type)
        {
            var img = _art.Image(name, parent, token, "color.hud.strip.frame", type);
            _art.Track(img, ArtScale);
            return img;
        }

        // Resource-row dividers (knots between split bars) live in the bar layer: HudCluster parents them after the bars.
        public void BuildRowDividers(Transform parent)
        {
            for (var i = 0; i < MaxRowDividers; i++)
            {
                _rowDiv[i] = Piece("RowDivider" + i, parent, "sprite.hud.strip4.rowdiv", Image.Type.Simple);
                _rowDiv[i].gameObject.SetActive(false);
            }
        }

        // Medallions and cells over the bars and panels: created after the bar views (sibling order = draw order).
        public void BuildFront()
        {
            _front = HudArt.Rect("StripCells", _root);
            _medL = Piece("MedallionLeft", _front, "sprite.hud.strip4.medallion", Image.Type.Simple);
            _medR = Piece("MedallionRight", _front, "sprite.hud.strip4.medallion", Image.Type.Simple);
            _glyphL = _art.Image("GlyphLeft", _medL.rectTransform, "sprite.ornament.triquetra", "color.ornament.glow", Image.Type.Simple);
            _glyphR = _art.Image("GlyphRight", _medR.rectTransform, "sprite.ornament.triquetra", "color.ornament.glow", Image.Type.Simple);
            _glyphL.preserveAspect = _glyphR.preserveAspect = true;
            for (var i = 0; i < HotbarCells; i++)
            {
                _hotbar[i] = new StripCell(_art, _front, "Hotbar" + i, CellKind.Hotbar);
                _hotbar[i].SetGhost(_art, "sprite.hud.strip.ghost.hotbar");
                var input = _hotbar[i].Root.gameObject.AddComponent<StripInput>();
                input.Hotbar = i;
            }
        }

        public void SetActive(bool on)
        {
            if (_back.gameObject.activeSelf != on) _back.gameObject.SetActive(on);
            if (_front && _front.gameObject.activeSelf != on) _front.gameObject.SetActive(on);
        }

        // ---- counts ----------------------------------------------------------------------------------------------

        public void Counts(Player p)
        {
            var foods = p.GetFoods();
            var cap = FoodRowCapacity(p);
            FoodCount = Mathf.Max(cap, foods.Count);
            if (Time.unscaledTime >= _quickReadAt)
            {
                _quickReadAt = Time.unscaledTime + 2f; // EAQS slot set changes only with its config / equipment
                var before = _quick.Count;
                EaqsBridge.ReadQuick(_quick);
                if (_quick.Count != before) _quickRevision++;
            }
            // Potion cells = MorgottTweaks PotionSlotCount (capacity list "potions"), clamped to EAQS's quick cells; without
            // the provider, EAQS's own quick slot count.
            PotionCount = Providers.TryCapacity("potions", p, out var pc) && pc.Valid ? Mathf.Clamp(pc.Count, 0, _quick.Count > 0 ? _quick.Count : 6) : ActiveQuick();
            // 1..2 ability cells (mockup hud-4M3): the guardian power and the equipped Epic Loot set skill; only valid
            // slots take a cell, the block closes its width around them.
            var max = Mathf.Clamp((int)ThemeTokens.GetFloat("count.hud.ability.max", 2f), 0, _abilityIndex.Length);
            var n = 0;
            for (var i = 0; i < Providers.AbilitySlotCount && n < max; i++)
            {
                if (!Providers.TryAbility(i, p, out var s, out _)) continue;
                _abilityIndex[n] = i;
                _abilityState[n] = s;
                n++;
            }
            AbilityCount = n;
        }

        private int ActiveQuick()
        {
            var n = 0;
            for (var i = 0; i < _quick.Count; i++) if (_quick[i].Active) n++;
            return n;
        }

        private static int FoodRowCapacity(Player p) =>
            Providers.TryCapacity("food", p, out var c) && c.Valid ? Mathf.Max(0, c.Count) : 3; // Player.m_maxFoods (Player.cs:330)

        public int LayoutKey()
        {
            unchecked
            {
                var h = FoodCount;
                h = h * 31 + PotionCount;
                h = h * 31 + AbilityCount;
                h = h * 31 + _quickRevision;
                return h;
            }
        }

        // ---- layout ----------------------------------------------------------------------------------------------

        public StripMath.Input Input(float width, float barsH)
        {
            return new StripMath.Input
            {
                Width = width,
                Cell = ThemeTokens.GetFloat("size.hud.strip.cell", 78f),
                CellH = ThemeTokens.GetFloat("size.hud.strip.cell.h", 94f),
                Gap = ThemeTokens.GetFloat("size.hud.strip.gap", 11f),
                ZoneGap = ThemeTokens.GetFloat("size.hud.strip.zone.gap", 34f),
                BlockPadX = ThemeTokens.GetFloat("size.hud.strip.block.pad.x", 26f),
                BlockPadT = ThemeTokens.GetFloat("size.hud.strip.block.pad.t", 16f),
                BlockPadB = ThemeTokens.GetFloat("size.hud.strip.block.pad.b", 8f),
                DurH = ThemeTokens.GetFloat("size.hud.strip.dur", 14f),
                BarsH = barsH,
                MinCenterW = ThemeTokens.GetFloat("size.hud.strip.min.bars", 700f),
                WingH = ThemeTokens.GetFloat("size.hud.strip.wing.h", 130f),
                KnotW = ThemeTokens.GetFloat("size.hud.strip.knot", 70f),
                WingTuck = ThemeTokens.GetFloat("size.hud.strip.wing.tuck", 14f),
                MedW = ThemeTokens.GetFloat("size.hud.strip.med.w", 200f),
                MedH = ThemeTokens.GetFloat("size.hud.strip.med.h", 150f),
                MedY = ThemeTokens.GetFloat("size.hud.strip.med.y", 172f),
                MedTuck = ThemeTokens.GetFloat("size.hud.strip.med.tuck", 12f),
                FoodCell = ThemeTokens.GetFloat("size.hud.strip.food.cell", 84f),
                PotionCell = ThemeTokens.GetFloat("size.hud.strip.potion.cell", 76f),
                PotionH = ThemeTokens.GetFloat("size.hud.strip.potion.h", 88f),
                TimerH = ThemeTokens.GetFloat("size.hud.strip.timer", 24f),
                MinCell = ThemeTokens.GetFloat("size.hud.strip.wing.min", 52f),
                SlotPad = ThemeTokens.GetFloat("size.hud.strip.slot.pad", 8f),
                Hotbar = HotbarCells,
                Abilities = AbilityCount,
                Food = FoodCount,
                Potions = PotionCount
            };
        }

        public void Apply(in StripMath.Layout l, in StripMath.Input i)
        {
            L = l;
            Box(_wingL.rectTransform, 0f, 0f, l.LeftW, l.WingH, false);
            Box(_wingR.rectTransform, l.RightX, 0f, l.RightW, l.WingH, true);
            var lap = ThemeTokens.GetFloat("size.hud.strip.block.overlap", 6f);
            Box(_block.rectTransform, l.CenterX, l.BlockY - lap, l.CenterW, l.BlockH + lap, false);
            var divOn = AbilityCount > 0 && _divider.sprite;
            if (_divider.gameObject.activeSelf != divOn) _divider.gameObject.SetActive(divOn);
            if (divOn) Native(_divider, l.DividerX, l.CellsY - i.DurH, i.CellH + i.DurH, 0.5f);
            Box(_medL.rectTransform, l.MedLX, l.MedY, i.MedW, i.MedH, false);
            Box(_medR.rectTransform, l.MedRX, l.MedY, i.MedW, i.MedH, true);
            Glyph(_glyphL, i.MedW, i.MedH);
            Glyph(_glyphR, i.MedW, i.MedH);
            for (var n = 0; n < HotbarCells; n++)
            {
                var c = _hotbar[n];
                c.Size(i.Cell, i.CellH, i.DurH, false);
                c.Place(StripMath.CellX(l.HotbarX, n, i.Cell, i.Gap), l.CellsY);
            }
            Pool(_abilities, AbilityCount, "Ability", CellKind.Hotbar);
            for (var n = 0; n < AbilityCount; n++)
            {
                var c = _abilities[n];
                c.Size(i.Cell, i.CellH, 0f, false);
                c.Place(StripMath.CellX(l.AbilityX, n, i.Cell, i.Gap), l.CellsY);
                c.Root.GetComponent<StripInput>().Ability = _abilityIndex[n];
            }
            Pool(_food, FoodCount, "Food", CellKind.Food);
            for (var n = 0; n < FoodCount; n++)
            {
                _food[n].Size(l.FoodCell, 0f, true);
                _food[n].Place(l.FoodX + n * l.FoodStep, l.FoodY);
            }
            Pool(_potions, PotionCount, "Potion", CellKind.Potion);
            for (var n = 0; n < PotionCount; n++)
            {
                _potions[n].Size(l.PotionCell, l.PotionH, 0f, false);
                _potions[n].Place(l.PotionX + n * l.PotionStep, l.PotionY);
                _potions[n].Key(n < _quick.Count ? EaqsKey(_quick[n].Key) : null);
            }
        }

        // The bar block frame: its top is fixed under the hotbar block, its height follows the bar rows (no resource
        // row: the frame closes right under the HP row and the space below shows the scene, mockup hud-4M3-nostam).
        public void BarFrame(float height)
        {
            var top = L.BarsY + L.BarsH;
            Box(_bars.rectTransform, L.CenterX, top - height, L.CenterW, height, false);
        }

        // Knot divider i between split resource bars: centred at x (strip units), over the row [y, y + h]; hidden when
        // h <= 0 (fewer entries).
        public void RowDivider(int i, float x, float y, float h, float alpha)
        {
            if (i < 0 || i >= MaxRowDividers || !_rowDiv[i]) return;
            var on = h > 0f && alpha > 0.01f && _rowDiv[i].sprite;
            if (_rowDiv[i].gameObject.activeSelf != on) _rowDiv[i].gameObject.SetActive(on);
            if (!on) return;
            Native(_rowDiv[i], x, y, h, 0.5f);
            if (!Mathf.Approximately(_rowDiv[i].canvasRenderer.GetAlpha(), alpha)) _rowDiv[i].canvasRenderer.SetAlpha(alpha);
        }

        // Triquetra glyph in the medallion's disc (sprite-relative centre / diameter, theme floats of the art).
        private static void Glyph(Image g, float w, float h)
        {
            if (!g) return;
            var rt = g.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            var d = h * ThemeTokens.GetFloat("share.hud.strip4.med.glyph", 0.42f);
            var p = new Vector2(w * ThemeTokens.GetFloat("share.hud.strip4.med.disc.x", 0.58f), h * ThemeTokens.GetFloat("share.hud.strip4.med.disc.y", 0.5f));
            if (rt.anchoredPosition != p) rt.anchoredPosition = p;
            var s = new Vector2(d, d);
            if (rt.sizeDelta != s) rt.sizeDelta = s;
        }

        private static string EaqsKey(string text) => string.IsNullOrEmpty(text) ? null : EpicLootBridge.Label(text);

        private StripCell MakeAbility(int n)
        {
            var c = new StripCell(_art, _front, "Ability" + n, CellKind.Hotbar);
            c.SetGhost(_art, "sprite.hud.strip.ghost.ability");
            c.Root.gameObject.AddComponent<StripInput>();
            return c;
        }

        private void Pool(List<StripCell> list, int n, string name, CellKind kind)
        {
            while (list.Count < n)
            {
                var c = name == "Ability" ? MakeAbility(list.Count) : new StripCell(_art, _front, name + list.Count, kind);
                if (name == "Food") c.SetGhost(_art, "sprite.hud.strip.ghost.food");
                else if (name == "Potion") c.SetGhost(_art, "sprite.hud.strip.ghost.potion");
                list.Add(c);
            }
            for (var i = 0; i < list.Count; i++) list[i].SetActive(i < n);
        }

        // ---- per-frame content -----------------------------------------------------------------------------------

        public void Fill(Player p, HotkeyBar mainBar, HotkeyBar quickBar)
        {
            var inv = p.GetInventory();
            var pad = ZInput.IsGamepadActive();
            var selected = pad && mainBar ? mainBar.m_selected : -1;
            for (var i = 0; i < HotbarCells; i++)
            {
                var c = _hotbar[i];
                var item = i < inv.GetWidth() ? inv.GetItemAt(i, 0) : null;
                c.Key(pad ? null : KeyLabel(i));
                ItemCell(c, p, item, i == selected);
            }
            var quickSel = pad && quickBar ? quickBar.m_selected : -1;
            for (var i = 0; i < PotionCount; i++)
            {
                var item = i < _quick.Count ? inv.GetItemAt(_quick[i].Pos.x, _quick[i].Pos.y) : null;
                ItemCell(_potions[i], p, item, i == quickSel);
                _potions[i].Durability(-1f, false);
            }
            FoodCells(p);
            for (var i = 0; i < AbilityCount; i++) AbilityFill(_abilities[i], _abilityState[i]);
        }

        private static readonly string[] Digits = { "1", "2", "3", "4", "5", "6", "7", "8" };
        private static string KeyLabel(int i) => i < Digits.Length ? Digits[i] : null;

        private static void ItemCell(StripCell c, Player p, ItemDrop.ItemData item, bool selected)
        {
            c.Empty(item == null);
            if (item == null)
            {
                c.Icon(null, Color.white);
                c.Count(0);
                c.Glow(selected);
                c.Rim(false, default);
                c.Mark(false);
                c.Durability(-1f, false);
                return;
            }
            c.Icon(item.GetIcon(), Color.white);
            c.Count(item.m_shared.m_maxStackSize > 1 ? item.m_stack : 0);
            // Selection = teal glow outside the cell: equipped / queued for equip, or the gamepad's hotbar cursor.
            c.Glow(selected || item.m_equipped || p.IsEquipActionQueued(item));
            if (!ReferenceEquals(c.RarityItem, item) || Time.unscaledTime >= c.RarityAt)
            {
                c.RarityItem = item;
                c.RarityAt = Time.unscaledTime + 5f; // Epic Loot rebuilds its magic data per read (allocates): rarely
                c.RarityMagic = EpicLootBridge.Rarity(item, out c.RarityColor, out c.RaritySet);
            }
            c.Rim(c.RarityMagic, c.RarityColor);
            c.Mark(c.RaritySet);
            var dur = item.m_shared.m_useDurability && item.m_durability < item.GetMaxDurability();
            c.Durability(dur ? item.GetDurabilityPercentage() : -1f, dur && item.m_durability <= 0f);
        }

        private void FoodCells(Player p)
        {
            var foods = p.GetFoods();
            var ring = ThemeTokens.GetColor("color.hud.strip.food.ring", Color.white);
            for (var i = 0; i < FoodCount; i++)
            {
                var c = _food[i];
                var food = i < foods.Count ? foods[i] : null;
                var has = food != null && food.m_item != null;
                c.Empty(!has);
                if (!has)
                {
                    c.Icon(null, Color.white);
                    c.Ring(-1f, ring);
                    c.Timer(-1f, true);
                    continue;
                }
                // Pulses when it can be eaten again (Hud.cs:1028-1031); ring = time left / burn time; timer on its arc.
                var tint = food.CanEatAgain() ? new Color(1f, 1f, 1f, 0.7f + Mathf.Sin(Time.time * 5f) * 0.3f) : Color.white;
                c.Icon(food.m_item.GetIcon(), tint);
                var burn = food.m_item.m_shared.m_foodBurnTime;
                c.Ring(burn > 0f ? Mathf.Clamp01(food.m_time / burn) : 0f, ring);
                c.Timer(food.m_time / Game.m_foodRate, true);
            }
        }

        // Ability cell (mockup hud-4M3 F / G cells): icon, key top-left, cooldown = cyan arc running out + dimmed icon,
        // active effect = ring.
        private static void AbilityFill(StripCell c, in AbilitySlotState s)
        {
            c.Empty(false);
            var cooling = s.CooldownLeft > 0f;
            c.Icon(s.Icon, cooling ? ThemeTokens.GetColor("color.hud.gp.cooldown", new Color(1f, 1f, 1f, 0.45f)) : Color.white);
            c.Key(s.Key);
            c.Sweep(0f);
            var active = s.ActiveLeft > 0f;
            var arc = cooling && s.CooldownTotal > 0f ? 1f - Mathf.Clamp01(s.CooldownLeft / s.CooldownTotal)
                : active && s.ActiveTotal > 0f ? Mathf.Clamp01(s.ActiveLeft / s.ActiveTotal) : -1f;
            c.Ring(arc, ThemeTokens.GetColor("color.hud.glyph", Color.cyan));
            c.Timer(cooling ? s.CooldownLeft : active ? s.ActiveLeft : -1f, false);
            c.Glow(false);
        }

        // Potion cell i as a box of the strip root (inventory EAQS quick cells sit on it while the inventory is open).
        public bool PotionBox(int i, out Vector2 bottomLeft, out float side)
        {
            bottomLeft = default;
            side = 0f;
            if (i < 0 || i >= PotionCount) return false;
            bottomLeft = new Vector2(L.PotionX + i * L.PotionStep, L.PotionY);
            side = Mathf.Min(L.PotionCell, L.PotionH);
            return true;
        }

        public RectTransform HotbarRect(int i) => i >= 0 && i < HotbarCells && _hotbar[i] != null ? _hotbar[i].Root : null;

        // Box with its bottom-left at (x, y); mirrored = flipped horizontally inside the same box (right wing / medallion).
        private static void Box(RectTransform rt, float x, float y, float w, float h, bool mirrored)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            var pivot = mirrored ? new Vector2(1f, 0f) : Vector2.zero;
            if (rt.pivot != pivot) rt.pivot = pivot;
            var scale = mirrored ? new Vector3(-1f, 1f, 1f) : Vector3.one;
            if (rt.localScale != scale) rt.localScale = scale;
            // Mirrored around the right edge pivot: the flipped box still covers [x, x + w].
            var p = new Vector2(x, y);
            var s = new Vector2(Mathf.Max(0f, w), Mathf.Max(0f, h));
            if (rt.anchoredPosition != p) rt.anchoredPosition = p;
            if (rt.sizeDelta != s) rt.sizeDelta = s;
        }

        // Simple sprite at native aspect, height h, bottom at y, horizontal anchor share px of its width at x.
        private static void Native(Image img, float x, float y, float h, float px)
        {
            var s = img.sprite;
            var w = s ? h * s.rect.width / Mathf.Max(1f, s.rect.height) : 0f;
            Box(img.rectTransform, x - w * px, y, w, h, false);
        }
    }
}
