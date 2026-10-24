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
        private readonly Image _wingL, _wingR, _bars, _block, _divider, _railL, _railR, _knotL, _knotR, _endL, _endR, _iconHp, _iconSt, _iconEi, _knot2L, _knot2R;
        private readonly Image _field, _stubL, _stubR, _capL, _capR, _tipL, _tipR;

        // Target panel layout (mode.hud.strip.target, user screenshot 2026-10-23, strip-target/spec.md).
        public static bool Target => ThemeTokens.GetFloat("mode.hud.strip.target", 0f) > 0.5f;

        // Frame tint: the target pieces are exact 4x crops of the reference, so no tint (run 20261023-191817: the shared
        // #C9CBD0 frame tint darkened every piece to ~0.79, medallion / block / bars dE 16-19 vs ~3 offline).
        public static string FrameTint => Target ? "color.hud.strip7.frame" : "color.hud.strip.frame";

        // Right-side art of its own ("<token>.r"); without it the left piece is mirrored.
        private static bool HasOwn(string token) => ThemeTokens.GetSprite(token + ".r");
        private static string Own(string token) => HasOwn(token) ? token + ".r" : token;

        // Pool socket (whole round ring + glyph, own sprite): a circle of size.hud.strip7.socket.d centred on the target's
        // icon box (icon.<name>.x / y / w / h). User crops 0.20.0: the old icon crops cut the ring and the field / frame
        // art carried ring fragments (an eitr ring splitting the stamina row with no eitr).
        private void Icon(Image img, string name, float barsX)
        {
            if (!img || !img.sprite) return;
            var d = T7("socket.d", 37f);
            var cx = barsX + T7(name + ".x", 0f) + T7(name + ".w", 0f) / 2f;
            var cy = T7(name + ".y", 0f) + T7(name + ".h", 0f) / 2f;
            Box(img.rectTransform, cx - d / 2f, cy - d / 2f, d, d, false);
        }

        // The eitr socket between stamina and eitr shows with the eitr channel (HudCluster.RowPlace).
        public void EitrIcon(bool on)
        {
            on = on && Target && _iconEi.sprite;
            if (_iconEi.gameObject.activeSelf != on) _iconEi.gameObject.SetActive(on);
        }
        private Image _medL, _medR, _glyphL, _glyphR;
        private readonly Image[] _rowDiv = new Image[MaxRowDividers];
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

        // Extra scale of the strip4 frame pieces (1: the cuts are 2x the mockup's px and ship at PPU 200 = 2 px per unit).
        private static float ArtScale => ThemeTokens.GetFloat("share.hud.strip4.art", 1f);

        public HudStrip(HudArt art, RectTransform root)
        {
            _art = art;
            _root = root;
            _back = HudArt.Rect("StripBack", root);
            // Draw order: wings (tucked under the centre) -> bar block -> hotbar block -> divider; the bars follow (HudCluster),
            // then the medallions and the cells (BuildFront).
            // Target panel extras (user screenshot 2026-10-23): rail runs and small diamond knots between the wings and the
            // bar block, the wings themselves (fixed pieces, the right one its own art), end medallions over the wing ends.
            // block-tip rail caps: first = under the inner diamonds (Knot2) and the bar block's tips (composite gate of the pieces)
            // rail extensions under the bar block tips (StripMath.TargetFrame CapL / CapR): the rail itself, tiled
            _capL = Piece("RailCapLeft", _back, "sprite.hud.strip7.rail", Image.Type.Tiled);
            _capR = Piece("RailCapRight", _back, Own("sprite.hud.strip7.rail"), Image.Type.Tiled);
            _stubL = Piece("RailStubLeft", _back, "sprite.hud.strip7.rail", Image.Type.Tiled);
            _stubR = Piece("RailStubRight", _back, Own("sprite.hud.strip7.rail"), Image.Type.Tiled);
            _railL = Piece("RailLeft", _back, "sprite.hud.strip7.rail", Image.Type.Sliced);
            _railR = Piece("RailRight", _back, Own("sprite.hud.strip7.rail"), Image.Type.Sliced);
            // block-tip caps (target crops: rail glint + bead) over the rail ends, under the inner diamonds and the block tips
            _tipL = Piece("TipCapLeft", _back, "sprite.hud.strip7.tipcap", Image.Type.Simple);
            _tipR = Piece("TipCapRight", _back, Own("sprite.hud.strip7.tipcap"), Image.Type.Simple);
            _wingL = Piece("WingLeft", _back, "sprite.hud.strip4.wing", Image.Type.Sliced);
            _wingR = Piece("WingRight", _back, ThemeTokens.GetSprite("sprite.hud.strip4.wing.r") ? "sprite.hud.strip4.wing.r" : "sprite.hud.strip4.wing", Image.Type.Sliced);
            _endL = Piece("EndLeft", _back, "sprite.hud.strip7.end", Image.Type.Simple);
            _endR = Piece("EndRight", _back, Own("sprite.hud.strip7.end"), Image.Type.Simple);
            // the wing-tip diamonds over the rail run and the wing's tip stub (target: the diamond sits on top)
            _knotL = Piece("KnotLeft", _back, "sprite.hud.strip7.knot", Image.Type.Simple);
            _knotR = Piece("KnotRight", _back, Own("sprite.hud.strip7.knot"), Image.Type.Simple);
            // small diamonds at the bar-block end of each rail run (target: diamonds at both rail ends)
            _knot2L = Piece("Knot2Left", _back, "sprite.hud.strip7.knot2", Image.Type.Simple);
            _knot2R = Piece("Knot2Right", _back, Own("sprite.hud.strip7.knot2"), Image.Type.Simple);
            foreach (var img in new[] { _capL, _capR, _stubL, _stubR, _railL, _railR, _tipL, _tipR, _knotL, _knotR, _endL, _endR, _knot2L, _knot2R }) img.gameObject.SetActive(Target);
            // always built like the other target pieces (run 20261023-191817: created only when the sprite resolved at
            // construction -> never built in game, the scene showed between the HP and stamina channels)
            // own tint token (color.hud.strip7.field): the 0.20.0 field art read ~27 % lighter than the target's metal (mean
            // grey 31.2 vs 24.5, run 20261023-204838); the 0.20.1 plain field (no baked channels / sockets) is drawn at the
            // target tone (mean ~24), tint white
            _field = _art.Image("BarField", _back, "sprite.hud.strip7.field", Target ? "color.hud.strip7.field" : FrameTint, Image.Type.Simple);
            _art.Track(_field, ArtScale);
            _field.gameObject.SetActive(Target);
            _bars = Piece("BarBlock", _back, "sprite.hud.strip4.bars", Image.Type.Sliced);
            _iconHp = Piece("IconHealth", _back, "sprite.hud.strip7.icon.hp", Image.Type.Simple);
            _iconSt = Piece("IconStamina", _back, "sprite.hud.strip7.icon.st", Image.Type.Simple);
            _iconEi = Piece("IconEitr", _back, "sprite.hud.strip7.icon.ei", Image.Type.Simple);
            foreach (var img in new[] { _iconHp, _iconSt, _iconEi }) img.gameObject.SetActive(Target && img.sprite);
            _block = Piece("HotbarBlock", _back, "sprite.hud.strip4.block", Image.Type.Sliced);
            _divider = Piece("Divider", _back, "sprite.hud.strip4.divider", Image.Type.Simple);
        }

        private Image Piece(string name, Transform parent, string token, Image.Type type)
        {
            var img = _art.Image(name, parent, token, FrameTint, type);
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
            _medR = Piece("MedallionRight", _front, Own("sprite.hud.strip4.medallion"), Image.Type.Simple);
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
            var cell = ThemeTokens.GetFloat("size.hud.strip.cell", 85f);
            return new StripMath.Input
            {
                Width = width,
                // Every slot cell is the same square (user call 2026-10-23): the inventory slot (HudCluster maps
                // size.hud.strip.cell onto it), food / potion / ability included; wings spread gaps, never cells.
                Cell = cell,
                CellH = cell,
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
                WingInnerY = WingInner(ThemeTokens.GetFloat("size.hud.strip.wing.h", 130f)),
                // the food disc sits above the timer band (StripCell.Size): half the band above the cell centre
                FoodLift = cell * Mathf.Clamp(ThemeTokens.GetFloat("share.hud.strip.timer.band", 0.27f), 0f, 0.5f) / 2f,
                BarsExtra = ThemeTokens.GetFloat("size.hud.strip.bars.extra", 0f),
                ConnW = ThemeTokens.GetFloat("size.hud.strip.conn", 0f),
                WingGap = ThemeTokens.GetFloat("size.hud.strip.wing.gap", 0f),
                PotionStart = ThemeTokens.GetFloat("size.hud.strip.potion.start", 0f),
                PotionGap = ThemeTokens.GetFloat("size.hud.strip.potion.gap", 0f),
                CenterDX = ThemeTokens.GetFloat("size.hud.strip.center.dx", 0f),
                MedRW = ThemeTokens.GetFloat("size.hud.strip.med.r.w", 0f),
                MedRTuck = ThemeTokens.GetFloat("size.hud.strip.med.r.tuck", 0f),
                FoodSize = ThemeTokens.GetFloat("size.hud.strip.food.size", 0f),
                CellsDX = ThemeTokens.GetFloat("size.hud.strip.cells.dx", 0f),
                AbilityCell = ThemeTokens.GetFloat("size.hud.strip.ability.cell", 0f),
                WingMax = (int)ThemeTokens.GetFloat("count.hud.strip.wing.max", 0f),
                PotionSize = ThemeTokens.GetFloat("size.hud.strip.potion.size", 0f),
                GrowFromEnds = ThemeTokens.GetFloat("mode.hud.strip.wing.grow", 0f) > 0.5f,
                FoodCell = cell,
                PotionCell = cell,
                PotionH = cell,
                TimerH = ThemeTokens.GetFloat("size.hud.strip.timer", 24f),
                MinCell = ThemeTokens.GetFloat("size.hud.strip.wing.min", 52f),
                SlotPad = ThemeTokens.GetFloat("size.hud.strip.slot.pad", 8f),
                Hotbar = HotbarCells,
                Abilities = AbilityCount,
                Food = FoodCount,
                Potions = PotionCount
            };
        }

        // Centre of the wing plate's inner area (between the top and bottom rails = the 9-slice top / bottom borders of
        // the wing sprite), in strip units above the strip bottom; 0 = no sprite (StripMath uses the middle).
        private static float WingInner(float wingH)
        {
            var s = ThemeTokens.GetSprite("sprite.hud.strip4.wing");
            if (!s || s.rect.height <= 0f) return 0f;
            var b = s.border; // (left, bottom, right, top) in sprite px
            return wingH * (b.y + (s.rect.height - b.w)) / 2f / s.rect.height;
        }

        public void Apply(in StripMath.Layout l, in StripMath.Input i)
        {
            L = l;
            if (Target) TargetFrame(l);
            else
            {
                Box(_wingL.rectTransform, 0f, 0f, l.LeftW, l.WingH, false);
                Box(_wingR.rectTransform, l.RightX, 0f, l.RightW, l.WingH, true);
            }
            var lap = ThemeTokens.GetFloat("size.hud.strip.block.overlap", 6f);
            Box(_block.rectTransform, l.CenterX, l.BlockY - lap, l.CenterW, l.BlockH + lap, false);
            var divOn = AbilityCount > 0 && _divider.sprite;
            if (_divider.gameObject.activeSelf != divOn) _divider.gameObject.SetActive(divOn);
            if (divOn) Native(_divider, l.DividerX, l.CellsY - i.DurH, i.CellH + i.DurH, 0.5f);
            Box(_medL.rectTransform, l.MedLX, l.MedY, i.MedW, i.MedH, false);
            Box(_medR.rectTransform, l.MedRX, l.MedY, i.MedRW > 0f ? i.MedRW : i.MedW, i.MedH, !HasOwn("sprite.hud.strip4.medallion"));
            Glyph(_glyphL, i.MedW, i.MedH);
            Glyph(_glyphR, i.MedW, i.MedH);
            if (_glyphL.gameObject.activeSelf == Target) { _glyphL.gameObject.SetActive(!Target); _glyphR.gameObject.SetActive(!Target); } // target medallion piece carries its glyph
            // Target panel: no medallions at the hotbar block ends (user 2026-10-24) - the block's own crest ends close it.
            // Only these two go: the end medallions the wing slots grow from are EndLeft / EndRight. MedLX / MedRX stay in
            // the layout (status rows end there), no cell moves.
            Show(_medL, !Target);
            Show(_medR, !Target);
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
                var ac = i.AbilityCell > 0f ? i.AbilityCell : i.Cell;
                c.Size(ac, ac, 0f, false);
                c.Place(StripMath.CellX(l.AbilityX, n, ac, i.Gap), l.CellsY - (ac - i.CellH) / 2f); // centred on the hotbar row
                c.Root.GetComponent<StripInput>().Ability = _abilityIndex[n];
            }
            Pool(_food, l.FoodShown, "Food", CellKind.Food);
            for (var n = 0; n < l.FoodShown; n++)
            {
                _food[n].Size(l.FoodCell, 0f, true);
                _food[n].Place(l.FoodX + n * l.FoodStep, l.FoodY);
            }
            Pool(_potions, l.PotionShown, "Potion", CellKind.Potion);
            for (var n = 0; n < l.PotionShown; n++)
            {
                _potions[n].Size(l.PotionCell, l.PotionH, 0f, false);
                _potions[n].Place(l.PotionX + n * l.PotionStep, l.PotionY);
                _potions[n].Key(n < _quick.Count ? EaqsKey(_quick[n].Key) : null);
            }
        }

        // Target panel frame (strip-target/spec.md, units = target px x 85 / 59): fixed wing pieces from the outer ends,
        // end medallions over them, a small diamond knot and a stretched rail run from each wing to the bar block.
        private static float T7(string name, float fallback) => ThemeTokens.GetFloat("size.hud.strip7." + name, fallback);

        // Lowest frame edge in strip units (<= 0: the end medallion tips hang below the wings); HudCluster places the
        // strip so this edge sits on the screen bottom.
        public float FrameBottom { get; private set; }

        private static readonly System.Func<string, float, float> T7Fn = T7;

        private void TargetFrame(in StripMath.Layout l)
        {
            var f = StripMath.TargetFrame(l, l.RightX + l.RightW, T7Fn);
            FrameBottom = f.Bottom;
            Place(_endL, f.EndL, false);
            // the right end medallion (run 20261023-175632: never placed, it sat at the strip origin over the left one)
            Place(_endR, f.EndR, !HasOwn("sprite.hud.strip7.end"));
            Place(_wingL, f.WingL, false);
            Place(_wingR, f.WingR, false);
            Place(_knotL, f.KnotL, false);
            Place(_knotR, f.KnotR, !HasOwn("sprite.hud.strip7.knot"));
            Place(_railL, f.RailL, false);
            Place(_railR, f.RailR, !HasOwn("sprite.hud.strip7.rail"));
            // tile phase: every stub / cap starts its tiling at the joint with its run (StripMath.TargetFrame); a tiled image
            // tiles from its local left, so the pieces left of a run (StubL, CapR) are drawn mirrored
            Place(_stubL, f.StubL, true);
            Place(_stubR, f.StubR, false);
            Place(_capL, f.CapL, false);
            Place(_capR, f.CapR, true);
            foreach (var (img, h) in new[] { (_stubL, f.StubL.H), (_stubR, f.StubR.H), (_capL, f.CapL.H), (_capR, f.CapR.H) }) FitTile(img, h);
            Place(_tipL, f.TipL, false);
            Place(_tipR, f.TipR, !HasOwn("sprite.hud.strip7.tipcap"));
            Show(_tipL, f.TipL.W > 0f);
            Show(_tipR, f.TipR.W > 0f);
            Place(_knot2L, f.Knot2L, false);
            Place(_knot2R, f.Knot2R, !HasOwn("sprite.hud.strip7.knot2"));
            // a wing slot grown onto the rail run hides the wing-tip diamond under it (the round socket would leave its
            // corners peeking out)
            Show(_knotL, !(l.FoodShown > 0 && StripMath.Covers(l.FoodX + (l.FoodShown - 1) * l.FoodStep, l.FoodCell, f.KnotL)));
            Show(_knotR, !(l.PotionShown > 0 && StripMath.Covers(l.PotionX, l.PotionCell, f.KnotR)));
            Field(l);
            // pool icons in the bar block (heart / lightning / eitr socket), placed from its left edge
            Icon(_iconHp, "icon.hp", l.BarsX);
            Icon(_iconSt, "icon.st", l.BarsX);
            Icon(_iconEi, "icon.ei", l.BarsX);
        }

        // Tiled rail piece: one tile row fills the box height exactly (the band fit draws the rail taller than its sprite;
        // tiles are sprite px x the tracked scale), so no second, cut tile row shows at the top.
        private void FitTile(Image img, float h)
        {
            var s = img ? img.sprite : null;
            if (!s || s.rect.height <= 0f || s.pixelsPerUnit <= 0f || h <= 0f) return;
            _art.Track(img, h / (s.rect.height * 100f / s.pixelsPerUnit));
        }

        private static void Place(Image img, in StripMath.Box b, bool mirrored) => Box(img.rectTransform, b.X, b.Y, b.W, b.H, mirrored);

        private static void Show(Image img, bool on)
        {
            on = on && img.sprite;
            if (img.gameObject.activeSelf != on) img.gameObject.SetActive(on);
        }

        // The bar block's metal field under the channels (the bar-frame piece is only the outer frame; its interior is
        // open): stretched over the block between its end knots, under the frame piece.
        private void Field(in StripMath.Layout l)
        {
            if (!_field) return;
            // the target's field band (x 522..1100, y 142..199 of the target) on its own rect inside the block
            float fl = T7("field.l", 121f), fr = T7("field.r", 129.7f), fb = T7("field.b", 5.76f), ft = T7("field.t", 4.34f);
            Box(_field.rectTransform, l.BarsX + fl, l.BarsY + fb, l.BarsW - fl - fr, l.BarsH - fb - ft, false);
        }


        // The bar block frame: its top is fixed under the hotbar block, its height follows the bar rows (no resource
        // row: the frame closes right under the HP row and the space below shows the scene, mockup hud-4M3-nostam).
        public void BarFrame(float height)
        {
            var top = L.BarsY + L.BarsH;
            if (Target) height = L.BarsH; // the target's bar block is one fixed piece
            Box(_bars.rectTransform, L.BarsX, top - height, L.BarsW, height, false);
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
            bottomLeft = new Vector2(L.PotionX + i * L.PotionStep, L.PotionY);
            side = Mathf.Min(L.PotionCell, L.PotionH);
            return true;
        }

        // Visible potion cell i (shown cells only): the EAQS quick cell of the open inventory is placed exactly over it.
        public RectTransform PotionRect(int i) => i >= 0 && i < L.PotionShown && i < _potions.Count && _potions[i] != null && _potions[i].Root.gameObject.activeInHierarchy ? _potions[i].Root : null;

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
