using System.Collections.Generic;
using RuneSkin.Api;
using RuneSkin.API;
using RuneSkin.InventoryScreen;
using RuneSkin.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.PlayerHud
{
    // The bottom HUD panel, mockup bottom-hud-center-v2 (user approved 2026-10-26). The frame is the mockup's own
    // food 3 / potion 4 row (Codex clean, art\src\kit\panel_row.png) cut into full-height column slices and laid edge to
    // edge by StripMath: centre = left end | hotbar run | one ability slice per ability cell | right end; each wing =
    // inner cap | one segment per slot (growing outward) | round end medallion. Hotbar = player inventory row 0 in 8
    // cells, abilities 0..2 from the RuneSkin.API ability registry (guardian power, Epic Loot set skill), food = the
    // eaten foods (vanilla Hud.UpdateFood), potions 0..N (capacity list "potions" = MorgottTweaks PotionSlotCount, else
    // the active EAQS quick cells). Counts are polled every frame (pull providers, cheap) and the geometry is recomputed
    // only when one of them or the screen / config changed (LayoutKey); every cell write is change-only, cells are
    // pooled, nothing allocates per frame.
    public sealed class HudStrip
    {
        public const int HotbarCells = 8;

        private readonly HudArt _art;
        private readonly RectTransform _root, _back;
        private RectTransform _front;
        // frame pieces: StripBack/WingLeft {Seg*, Inner, End}, StripBack/WingRight {...}, StripBack/Centre {BlockLeft,
        // Hotbar, Ability*, BlockRight}
        private readonly RectTransform _wingL, _wingR, _centre;
        private readonly Image _innerL, _innerR, _endL, _endR, _blockL, _hotbarArt, _blockR;
        private readonly List<Image> _segL = new List<Image>(4), _segR = new List<Image>(4), _abilityArt = new List<Image>(2);

        // Frame tint: the pieces are the mockup's own pixels (white = untinted).
        public const string FrameTint = "color.hud.panel.frame";

        private readonly StripCell[] _hotbar = new StripCell[HotbarCells];
        private readonly List<StripCell> _food = new List<StripCell>(5), _potions = new List<StripCell>(6), _abilities = new List<StripCell>(2);
        private readonly List<EaqsBridge.QuickInfo> _quick = new List<EaqsBridge.QuickInfo>(6);
        private StripInput[] _potionInput = new StripInput[6];
        private float _quickReadAt;
        private int _quickRevision;

        // Valid ability slots this frame: provider index + state.
        private readonly int[] _abilityIndex = new int[4];
        private readonly AbilitySlotState[] _abilityState = new AbilitySlotState[4];

        public int FoodCount { get; private set; }
        public int PotionCount { get; private set; }
        public int AbilityCount { get; private set; }
        public StripMath.Layout L;

        public HudStrip(HudArt art, RectTransform root)
        {
            _art = art;
            _root = root;
            _back = HudArt.Rect("StripBack", root);
            // Draw order: wings, then the centre; the bars follow (HudCluster), then the cells (BuildFront). The slices
            // butt edge to edge, so the order only matters at their anti-aliased silhouettes.
            _wingL = HudArt.Rect("WingLeft", _back);
            _wingR = HudArt.Rect("WingRight", _back);
            _innerL = Piece("Inner", _wingL, "sprite.hud.panel.food.in");
            _endL = Piece("End", _wingL, "sprite.hud.panel.food.end");
            _innerR = Piece("Inner", _wingR, "sprite.hud.panel.potion.in");
            _endR = Piece("End", _wingR, "sprite.hud.panel.potion.end");
            _centre = HudArt.Rect("Centre", _back);
            _blockL = Piece("BlockLeft", _centre, "sprite.hud.panel.block.l");
            _hotbarArt = Piece("Hotbar", _centre, "sprite.hud.panel.hotbar");
            _blockR = Piece("BlockRight", _centre, "sprite.hud.panel.block.r");
        }

        private Image Piece(string name, Transform parent, string token)
        {
            var img = _art.Image(name, parent, token, FrameTint, Image.Type.Simple);
            return img;
        }

        // Cells over the bars and panels: created after the bar views (sibling order = draw order).
        public void BuildFront()
        {
            _front = HudArt.Rect("StripCells", _root);
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
            // One food cell per eaten food, like vanilla Hud.UpdateFood (a bar per Player.GetFoods() entry, the rest hidden):
            // the food wing grows from the bar block outward as foods are eaten (user 2026-10-26).
            FoodCount = p.GetFoods().Count;
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

        // Panel art geometry (theme size.hud.panel.*, art px = mockup 1080p px of the row; measured on panel_row.png).
        private static float P(string name, float fallback) => ThemeTokens.GetFloat("size.hud.panel." + name, fallback);

        private static StripMath.WingArt WingArt(string side, int arts) => new StripMath.WingArt
        {
            InnerW = P(side + ".in.w", 0f),
            SegW = P(side + ".seg.w", 0f),
            EndW = P(side + ".end.w", 0f),
            SlotY = P(side + ".slot.y", 0f),
            Arts = arts
        };

        public StripMath.Input Input(float width)
        {
            var cell = ThemeTokens.GetFloat("size.hud.strip.cell", 85f);
            return new StripMath.Input
            {
                Width = width,
                Unit = P("unit", 85f / 70f),
                CenterDX = ThemeTokens.GetFloat("size.hud.strip.center.dx", 0f),
                H = P("h", 162f),
                EndLW = P("block.l.w", 0f),
                HotbarW = P("hotbar.w", 0f),
                AbilityW = P("ability.w", 0f),
                EndRW = P("block.r.w", 0f),
                // every hotbar cell is the inventory slot (HudCluster maps size.hud.strip.cell onto it)
                Cell = cell,
                CellH = cell,
                AbilityCell = ThemeTokens.GetFloat("size.hud.strip.ability.cell", cell),
                HotbarC0 = P("hotbar.c0", 0f),
                HotbarPitch = P("hotbar.pitch", 0f),
                AbilityC = P("ability.c", 0f),
                CellsCY = P("cells.cy", 0f),
                BarsL = P("bars.l", 0f),
                BarsR = P("bars.r", 0f),
                BarsB = P("bars.b", 0f),
                BarsT = P("bars.t", 0f),
                WingTop = P("wing.top", 0f),
                FoodArt = WingArt("food", (int)ThemeTokens.GetFloat("count.hud.panel.food.arts", 1f)),
                PotionArt = WingArt("potion", (int)ThemeTokens.GetFloat("count.hud.panel.potion.arts", 1f)),
                FoodCell = ThemeTokens.GetFloat("size.hud.strip.food.size", cell),
                PotionCell = ThemeTokens.GetFloat("size.hud.strip.potion.size", cell),
                // the food disc sits above the timer band (StripCell.Size): half the band above the cell centre
                FoodLift = ThemeTokens.GetFloat("size.hud.strip.food.size", cell) * Mathf.Clamp(ThemeTokens.GetFloat("share.hud.strip.timer.band", 0.27f), 0f, 0.5f) / 2f,
                WingMax = (int)ThemeTokens.GetFloat("count.hud.strip.wing.max", 0f),
                Hotbar = HotbarCells,
                Abilities = AbilityCount,
                Food = FoodCount,
                Potions = PotionCount
            };
        }

        public void Apply(in StripMath.Layout l, in StripMath.Input i)
        {
            L = l;
            Frame(l, i);
            for (var n = 0; n < HotbarCells; n++)
            {
                var c = _hotbar[n];
                c.Size(i.Cell, i.CellH, ThemeTokens.GetFloat("size.hud.strip.dur", 10f), false);
                c.Place(StripMath.CellX(l.HotbarX, n, i.Cell, l.Gap), l.CellsY);
            }
            Pool(_abilities, AbilityCount, "Ability", CellKind.Hotbar);
            for (var n = 0; n < AbilityCount; n++)
            {
                var c = _abilities[n];
                var ac = i.AbilityCell;
                c.Size(ac, ac, 0f, false);
                c.Place(l.AbilityLeft(n, ac), l.CellsY - (ac - i.CellH) / 2f); // centred on the hotbar row
                c.Root.GetComponent<StripInput>().Ability = _abilityIndex[n];
            }
            Pool(_food, l.FoodShown, "Food", CellKind.Food);
            for (var n = 0; n < l.FoodShown; n++)
            {
                _food[n].Size(l.FoodCell, 0f, true);
                _food[n].Place(l.FoodLeft(n), l.FoodY);
            }
            Pool(_potions, l.PotionShown, "Potion", CellKind.Potion);
            for (var n = 0; n < l.PotionShown; n++)
            {
                _potions[n].Size(l.PotionCell, l.PotionH, 0f, false);
                _potions[n].Place(l.PotionLeft(n), l.PotionY);
                _potions[n].Key(n < _quick.Count ? EaqsKey(_quick[n].Key) : null);
            }
        }

        // Frame slices on their StripMath boxes; a wing without slots is not drawn at all.
        private void Frame(in StripMath.Layout l, in StripMath.Input i)
        {
            Box(_centre, l.CenterX, 0f, l.CenterW, l.Height);
            Place(_blockL, Local(l.EndL, l.CenterX));
            Place(_hotbarArt, Local(l.Hotbar, l.CenterX));
            Place(_blockR, Local(l.EndR, l.CenterX));
            while (_abilityArt.Count < AbilityCount)
            {
                var a = Piece("Ability" + _abilityArt.Count, _centre, "sprite.hud.panel.ability");
                a.transform.SetSiblingIndex(_blockR.transform.GetSiblingIndex());
                _abilityArt.Add(a);
            }
            for (var n = 0; n < _abilityArt.Count; n++)
            {
                Show(_abilityArt[n], n < AbilityCount);
                if (n < AbilityCount) Place(_abilityArt[n], new StripMath.Box(l.AbilityX0 + n * l.AbilitySliceW - l.CenterX, 0f, l.AbilitySliceW, l.Height));
            }
            WingPieces(l.WingL, _wingL, _innerL, _endL, _segL, "sprite.hud.panel.food.seg", i.FoodArt.Arts);
            WingPieces(l.WingR, _wingR, _innerR, _endR, _segR, "sprite.hud.panel.potion.seg", i.PotionArt.Arts);
        }

        // Composed wing: inner cap, segments (pooled, each with its row slice StripMath.SegArt), end medallion.
        private void WingPieces(in StripMath.WingRun r, RectTransform box, Image inner, Image end, List<Image> segs, string segToken, int arts)
        {
            var on = r.Slots > 0;
            if (box.gameObject.activeSelf != on) box.gameObject.SetActive(on);
            if (!on) return;
            Box(box, r.X, 0f, r.Right - r.X, r.Inner.H);
            Place(inner, Local(r.Inner, r.X));
            Place(end, Local(r.End, r.X));
            while (segs.Count < r.Slots)
            {
                var s = Piece("Seg" + segs.Count, box, segToken + "0");
                s.transform.SetSiblingIndex(0); // under the caps
                segs.Add(s);
            }
            for (var k = 0; k < segs.Count; k++)
            {
                Show(segs[k], k < r.Slots);
                if (k >= r.Slots) continue;
                var token = segToken + StripMath.SegArt(k, r.Slots, arts);
                var sprite = ThemeTokens.GetSprite(token);
                if (sprite && segs[k].sprite != sprite) segs[k].sprite = sprite;
                Place(segs[k], Local(r.Seg(k), r.X));
            }
        }

        private static StripMath.Box Local(in StripMath.Box b, float x0) => new StripMath.Box(b.X - x0, b.Y, b.W, b.H);

        private static void Place(Image img, in StripMath.Box b) => Box(img.rectTransform, b.X, b.Y, b.W, b.H);

        private static void Show(Image img, bool on)
        {
            on = on && img.sprite;
            if (img.gameObject.activeSelf != on) img.gameObject.SetActive(on);
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
                else if (name == "Potion")
                {
                    c.SetGhost(_art, "sprite.hud.strip.ghost.potion");
                    // Press / drop on a potion cell = on its EAQS quick cell (StripInput; cell set by Fill).
                    c.Root.gameObject.AddComponent<StripInput>().Potion = list.Count;
                }
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
            for (var i = 0; i < L.PotionShown && i < _potions.Count; i++)
            {
                var item = i < _quick.Count ? inv.GetItemAt(_quick[i].Pos.x, _quick[i].Pos.y) : null;
                if (_potionInput.Length < _potions.Count) System.Array.Resize(ref _potionInput, _potions.Count);
                var input = _potionInput[i] ? _potionInput[i] : _potionInput[i] = _potions[i].Root.GetComponent<StripInput>();
                if (input)
                {
                    input.HasCell = i < _quick.Count;
                    if (input.HasCell) input.Cell = _quick[i].Pos;
                }
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
            for (var i = 0; i < L.FoodShown && i < _food.Count; i++)
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
            if (i < 0 || i >= L.PotionShown) return false;
            bottomLeft = new Vector2(L.PotionLeft(i), L.PotionY);
            side = Mathf.Min(L.PotionCell, L.PotionH);
            return true;
        }

        // Visible potion cell i (shown cells only): the EAQS quick cell of the open inventory is placed exactly over it.
        public RectTransform PotionRect(int i) => i >= 0 && i < L.PotionShown && i < _potions.Count && _potions[i] != null && _potions[i].Root.gameObject.activeInHierarchy ? _potions[i].Root : null;

        public RectTransform HotbarRect(int i) => i >= 0 && i < HotbarCells && _hotbar[i] != null ? _hotbar[i].Root : null;

        // Box with its bottom-left at (x, y) in the parent's units.
        private static void Box(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            if (rt.pivot != Vector2.zero) rt.pivot = Vector2.zero;
            var p = new Vector2(x, y);
            var s = new Vector2(Mathf.Max(0f, w), Mathf.Max(0f, h));
            if (rt.anchoredPosition != p) rt.anchoredPosition = p;
            if (rt.sizeDelta != s) rt.sizeDelta = s;
        }
    }
}
