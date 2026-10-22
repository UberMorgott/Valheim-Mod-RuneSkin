using System.Collections.Generic;
using RuneSkin.Api;
using RuneSkin.API;
using RuneSkin.InventoryScreen;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.PlayerHud
{
    // The bottom HUD strip (Track H, mockup hud-2-final, decisions 2026-10-22): panels (two wings behind a centre
    // block, knotwork ends), hotbar = player inventory row 0 in 8 bowl cells, ability zone 0..4 from the RuneSkin.API
    // ability registry, food 0..N (capacity list "food", vanilla 3), potions 0..N (capacity list "potions" =
    // MorgottTweaks PotionSlotCount, else the active EAQS quick cells), captions. Counts are polled every frame (pull
    // providers, cheap) and the geometry is recomputed only when one of them or the screen / config changed
    // (LayoutKey); every cell write is change-only, cells are pooled, nothing allocates per frame.
    public sealed class HudStrip
    {
        public const int HotbarCells = 8;

        private readonly HudArt _art;
        private readonly RectTransform _root, _back;
        private RectTransform _front;
        private readonly Image _body, _rail, _endL, _endR, _jointL, _jointR, _barKnotL, _barKnotR;
        private readonly StripCell[] _hotbar = new StripCell[HotbarCells];
        private readonly List<StripCell> _food = new List<StripCell>(5), _potions = new List<StripCell>(6), _abilities = new List<StripCell>(4);
        private readonly List<bool> _abilityRound = new List<bool>(4);
        private readonly List<EaqsBridge.QuickInfo> _quick = new List<EaqsBridge.QuickInfo>(6);
        private float _quickReadAt;
        private int _quickRevision;

        // Valid ability slots this frame: provider index + state.
        private readonly int[] _abilityIndex = new int[8];
        private readonly AbilitySlotState[] _abilityState = new AbilitySlotState[8];
        private readonly bool[] _abilityIsRound = new bool[8];
        private readonly bool[] _abilityValid = new bool[8];

        public int FoodCount { get; private set; }
        public int PotionCount { get; private set; }
        public int AbilityCount { get; private set; }
        public StripMath.Layout L;

        public HudStrip(HudArt art, RectTransform root)
        {
            _art = art;
            _root = root;
            _back = HudArt.Rect("StripBack", root);
            // Frame pieces cut from mockup hud-3 (art\src\kit\cuts.json strip3_*): one tiled body (double rails + slate fill)
            // over the whole width, knotwork ends, joints between the wings and the centre block, knot blocks at the
            // ends of the bar bed. Untinted: the mockup's own colours.
            _body = art.Image("Body", _back, "sprite.hud.strip3.body", "color.hud.strip.plate", Image.Type.Tiled);
            _rail = art.Image("Rails", _back, "sprite.hud.strip3.rail", "color.hud.strip.frame", Image.Type.Tiled);
            _endL = art.Image("KnotLeft", _back, "sprite.hud.strip3.end.l", "color.hud.strip.frame", Image.Type.Simple);
            _endR = art.Image("KnotRight", _back, "sprite.hud.strip3.end.r", "color.hud.strip.frame", Image.Type.Simple);
            _jointL = art.Image("JointLeft", _back, "sprite.hud.strip3.joint.l", "color.hud.strip.frame", Image.Type.Simple);
            _jointR = art.Image("JointRight", _back, "sprite.hud.strip3.joint.r", "color.hud.strip.frame", Image.Type.Simple);
            _barKnotL = art.Image("BarKnotLeft", _back, "sprite.hud.strip3.barknot.l", "color.hud.strip.frame", Image.Type.Simple);
            _barKnotR = art.Image("BarKnotRight", _back, "sprite.hud.strip3.barknot.r", "color.hud.strip.frame", Image.Type.Simple);
        }
        // Cells over the bars and panels: created after the bar views (sibling order = draw order).
        public void BuildFront()
        {
            _front = HudArt.Rect("StripCells", _root);
            for (var i = 0; i < HotbarCells; i++)
            {
                _hotbar[i] = new StripCell(_art, _front, "Hotbar" + i, false);
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
            var max = Mathf.Clamp((int)ThemeTokens.GetFloat("count.hud.ability.max", 4f), 0, _abilityIndex.Length);
            var n = 0;
            // Only valid slots take a cell (0..4, empty ones collapse: user call 2026-10-22 after the in-game test).
            for (var i = 0; i < Providers.AbilitySlotCount && n < max; i++)
            {
                var valid = Providers.TryAbility(i, p, out var s, out var round);
                if (!valid) continue;
                _abilityIndex[n] = i;
                _abilityState[n] = s;
                _abilityIsRound[n] = round;
                _abilityValid[n] = valid;
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
                for (var i = 0; i < AbilityCount; i++) h = h * 3 + (_abilityIsRound[i] ? 1 : 2);
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
                Cell = ThemeTokens.GetFloat("size.hud.strip.cell", 76f),
                CellH = ThemeTokens.GetFloat("size.hud.strip.cell.h", 62f),
                Gap = ThemeTokens.GetFloat("size.hud.strip.gap", 9f),
                ZoneGap = ThemeTokens.GetFloat("size.hud.strip.zone.gap", 26f),
                Pad = ThemeTokens.GetFloat("size.hud.strip.pad", 12f),
                DurH = ThemeTokens.GetFloat("size.hud.strip.dur", 10f),
                BarsH = barsH,
                MinBarsW = ThemeTokens.GetFloat("size.hud.strip.min.bars", 600f),
                KnotW = ThemeTokens.GetFloat("size.hud.strip.knot", 40f),
                WingOverlap = 0f,
                FoodCell = ThemeTokens.GetFloat("size.hud.strip.food.cell", 72f),
                PotionCell = ThemeTokens.GetFloat("size.hud.strip.potion.cell", 68f),
                PotionH = ThemeTokens.GetFloat("size.hud.strip.potion.h", 66f),
                TimerH = ThemeTokens.GetFloat("size.hud.strip.timer", 18f),
                MinCell = ThemeTokens.GetFloat("size.hud.strip.wing.min", 48f),
                Hotbar = HotbarCells,
                Abilities = AbilityCount,
                Food = FoodCount,
                Potions = PotionCount
            };
        }

        public void Apply(in StripMath.Layout l, in StripMath.Input i)
        {
            L = l;
            // Body over the whole width, ends at native aspect (strip height), joints at the centre block edges (from the
            // top down to the bar bed).
            Box(_body.rectTransform, 0f, 0f, i.Width, l.Height);
            // One plate row: the tiled texture drawn at the strip height (HudArt.Track scales the tile).
            if (_body.sprite) _art.Track(_body, ThemeTokens.GetFloat("share.hud.strip.plate", 2f) * l.Height / Mathf.Max(1f, _body.sprite.rect.height * 100f / _body.sprite.pixelsPerUnit));
            Box(_rail.rectTransform, 0f, 0f, i.Width, l.Height);
            Native(_endL, 0f, 0f, l.Height, 0f);
            Native(_endR, i.Width, 0f, l.Height, 1f);
            // Joints from the top down to the bar bed's knot blocks (they start a pad above the bars).
            var jointH = l.Height - (l.BarsY + i.BarsH + i.Pad);
            Native(_jointL, l.CenterX, l.Height - jointH, jointH, 0.5f);
            Native(_jointR, l.CenterX + l.CenterW, l.Height - jointH, jointH, 0.5f);
            var cell = i.Cell;
            for (var n = 0; n < HotbarCells; n++)
            {
                var c = _hotbar[n];
                c.Size(cell, i.DurH, false);
                c.Place(StripMath.CellX(l.HotbarX, n, cell, i.Gap), l.CellsY);
            }
            Pool(_abilities, AbilityCount, "Ability", false);
            for (var n = 0; n < AbilityCount; n++)
            {
                var c = AbilityCell(n);
                // The round guardian socket reads lighter than a square cell at the same box: a little larger, centred.
                var side = _abilityIsRound[n] ? cell * ThemeTokens.GetFloat("share.hud.strip.round", 1.14f) : cell;
                c.Size(side, 0f, false);
                c.Place(StripMath.CellX(l.AbilityX, n, cell, i.Gap) - (side - cell) / 2f, l.CellsY - (side - cell) / 2f);
                c.Root.GetComponent<StripInput>().Ability = _abilityIndex[n];
            }
            Pool(_food, FoodCount, "Food", true);
            for (var n = 0; n < FoodCount; n++)
            {
                _food[n].Size(l.FoodCell, 0f, true);
                _food[n].Place(l.FoodX + n * l.FoodStep, l.FoodY);
            }
            Pool(_potions, PotionCount, "Potion", false);
            for (var n = 0; n < PotionCount; n++)
            {
                _potions[n].Size(l.PotionCell, l.PotionH, 0f, false);
                _potions[n].Place(l.PotionX + n * l.PotionStep, l.PotionY);
                _potions[n].Key(n < _quick.Count ? EaqsKey(_quick[n].Key) : null);
            }
        }

        private static string EaqsKey(string text) => string.IsNullOrEmpty(text) ? null : EpicLootBridge.Label(text);

        // Round sockets need their own cell objects: an ability cell is rebuilt when its shape changes.
        private StripCell AbilityCell(int n)
        {
            var round = _abilityIsRound[n];
            while (_abilityRound.Count < _abilities.Count) _abilityRound.Add(false);
            if (_abilityRound[n] != round)
            {
                Object.Destroy(_abilities[n].Root.gameObject);
                _abilities[n] = MakeAbility(n, round);
                _abilityRound[n] = round;
            }
            return _abilities[n];
        }

        private StripCell MakeAbility(int n, bool round)
        {
            var c = new StripCell(_art, _front, "Ability" + n, round);
            c.SetGhost(_art, "sprite.hud.strip.ghost.ability");
            c.Root.gameObject.AddComponent<StripInput>();
            return c;
        }

        private void Pool(List<StripCell> list, int n, string name, bool round)
        {
            while (list.Count < n)
            {
                var c = name == "Ability" ? MakeAbility(list.Count, false) : new StripCell(_art, _front, name + list.Count, round);
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
            for (var i = 0; i < AbilityCount; i++)
            {
                if (_abilityValid[i]) { AbilityFill(_abilities[i], _abilityState[i]); continue; }
                var c = _abilities[i]; // registered but empty: placeholder rune
                c.Empty(true);
                c.Icon(null, Color.white);
                c.Key(null);
                c.Sweep(0f);
                c.Ring(-1f, Color.white);
                c.Timer(-1f, false);
                c.Glow(false);
            }
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
                c.RarityAt = Time.unscaledTime + 1f;
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
                // Pulses when it can be eaten again (Hud.cs:1028-1031); ring = time left / burn time; timer under it.
                var tint = food.CanEatAgain() ? new Color(1f, 1f, 1f, 0.7f + Mathf.Sin(Time.time * 5f) * 0.3f) : Color.white;
                c.Icon(food.m_item.GetIcon(), tint);
                var burn = food.m_item.m_shared.m_foodBurnTime;
                c.Ring(burn > 0f ? Mathf.Clamp01(food.m_time / burn) : 0f, ring);
                c.Timer(food.m_time / Game.m_foodRate, true);
            }
        }

        private static void AbilityFill(StripCell c, in AbilitySlotState s)
        {
            c.Empty(false);
            var cooling = s.CooldownLeft > 0f;
            c.Icon(s.Icon, cooling ? ThemeTokens.GetColor("color.hud.gp.cooldown", new Color(1f, 1f, 1f, 0.45f)) : Color.white);
            c.Key(s.Key);
            c.Sweep(cooling && s.CooldownTotal > 0f ? Mathf.Clamp01(s.CooldownLeft / s.CooldownTotal) : 0f);
            var active = s.ActiveLeft > 0f;
            c.Ring(active ? (s.ActiveTotal > 0f ? Mathf.Clamp01(s.ActiveLeft / s.ActiveTotal) : 1f) : -1f, ThemeTokens.GetColor("color.hud.glyph", Color.cyan));
            c.Timer(cooling ? s.CooldownLeft : active ? s.ActiveLeft : -1f, false);
            c.Glow(false); // the active ring marks it; a halo on top read as a blur (Codex in-game r1)
            // Round slots (guardian power) wear their teal ring while ready too (mockup hud-3 F socket).
            if (c.IsRound && !active && !cooling) c.Ring(1f, ThemeTokens.GetColor("color.hud.glyph", Color.cyan));
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

        private static void Box(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
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
            Box(img.rectTransform, x - w * px, y, w, h);
        }

        // Knot blocks at both ends of the bar bed (bars rect in strip units: x, bottom, width, height of the bar block).
        public void BarKnots(Rect bars)
        {
            var pad = ThemeTokens.GetFloat("size.hud.strip.pad", 12f);
            var h = bars.height + 2f * pad;
            Native(_barKnotL, bars.x, bars.y - pad, h, 1f);
            Native(_barKnotR, bars.xMax, bars.y - pad, h, 0f);
        }
    }
}
