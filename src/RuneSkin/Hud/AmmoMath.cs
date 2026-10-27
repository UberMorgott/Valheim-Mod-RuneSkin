using System;
using System.Collections.Generic;
using System.Globalization;

namespace RuneSkin.PlayerHud
{
    // Ammo plate (top-left HUD) + crossbow magazine dots (item icons), the managed part (unit-tested). User decision
    // 2026-10-27 (mockups docs\mockups\ammo-hud\f1-varA-plate-*.png, f2-varA-bottom-row-*.png): "1A (plate with the name)
    // + 2A (a row of dots up to 7, beyond that one dot + n/N), empty dots brighter".
    public struct AmmoCandidate
    {
        public int ItemType;      // ItemDrop.ItemData.ItemType as int
        public string AmmoType;   // m_shared.m_ammoType
        public string Prefab;     // m_dropPrefab name (null: m_shared.m_name)
        public int GridX, GridY;  // m_gridPos
        public int Stack;         // m_stack
    }

    public enum MagazineMode { None, Dots, Compact }

    // One dot row in a cell / plate: `Count` dots of diameter `Dot` every `Pitch` from (X, Y) (top-left, y down).
    // Compact: one dot at (X, Y) and the "n/N" text box at (TextX, Y) of TextW x Dot.
    public struct DotRow
    {
        public MagazineMode Mode;
        public int Count, Filled;
        public float X, Y, Dot, Pitch;
        public float TextX, TextW;
    }

    public static class AmmoMath
    {
        // Vanilla ItemDrop.ItemData.ItemType values (ItemDrop.cs): the three Inventory.GetAmmoItem accepts.
        public const int Consumable = 2, Ammo = 9, AmmoNonEquipable = 23;

        // Most dots a row shows; a larger magazine is one dot + "n/N" (user decision 2A).
        public const int MaxDots = 7;

        // Vanilla's ammo choice (Attack.FindAmmo, Attack.cs:675-690, the same test Attack.UseAmmo fires with,
        // Attack.cs UseAmmo): the equipped ammo (Humanoid.GetAmmoItem = m_ammoItem) when the inventory still holds it and
        // its m_ammoType is the weapon's, else Inventory.GetAmmoItem(ammoType) (Inventory.cs:515-533): among Ammo /
        // AmmoNonEquipable / Consumable items of that m_ammoType the first in grid order (y * width + x; ties keep the
        // earlier list entry). Returns the index into `items`, -1 = none. `equipped` = index of m_ammoItem in `items`
        // (-1 when it is not in the inventory).
        public static int SelectAmmo(string weaponAmmoType, IList<AmmoCandidate> items, int equipped, int width)
        {
            if (string.IsNullOrEmpty(weaponAmmoType) || items == null) return -1;
            if (equipped >= 0 && equipped < items.Count && items[equipped].AmmoType == weaponAmmoType) return equipped;
            var best = -1;
            var bestPos = 0;
            for (var i = 0; i < items.Count; i++)
            {
                var c = items[i];
                if (c.ItemType != Ammo && c.ItemType != AmmoNonEquipable && c.ItemType != Consumable) continue;
                if (c.AmmoType != weaponAmmoType) continue;
                var pos = c.GridY * width + c.GridX;
                if (best < 0 || pos < bestPos)
                {
                    best = i;
                    bestPos = pos;
                }
            }
            return best;
        }

        // Shots left of the chosen ammo: every stack of the same prefab (and ammo type) the player's inventory holds.
        // EquipmentAndQuickSlots quick slots are rows of that same inventory, so they count; Attack.UseAmmo draws from
        // the player's inventory only (no backpack: AdventureBackpacks does not touch ammo).
        public static int Total(IList<AmmoCandidate> items, int selected)
        {
            if (items == null || selected < 0 || selected >= items.Count) return 0;
            var s = items[selected];
            var total = 0;
            foreach (var c in items)
                if (c.Prefab == s.Prefab && c.AmmoType == s.AmmoType) total += Math.Max(0, c.Stack);
            return total;
        }

        public static MagazineMode Mode(int capacity) =>
            capacity <= 0 ? MagazineMode.None : capacity <= MaxDots ? MagazineMode.Dots : MagazineMode.Compact;

        public static string CountText(int total) => "×" + Math.Max(0, total).ToString(CultureInfo.InvariantCulture);

        // The real loaded count: MorgottTweaks keeps bolts above a lowered capacity (MagazineSize cut, affix lost) until fired.
        public static string MagazineText(int loaded, int capacity) =>
            Math.Max(0, loaded).ToString(CultureInfo.InvariantCulture) + "/" + Math.Max(0, capacity).ToString(CultureInfo.InvariantCulture);

        // Dots on an item icon cell (cell-local units, origin top-left, y down): a row just above the durability pill
        // (`durX`, `durY` = its left / top edge), left-aligned with it, at least `minGapPx` screen px above it
        // (px = screen px per unit). Dot = `dotShare` x side up to a pitch of `pitchShare` x side, shrunk so the row
        // fits from durX to the cell's right inset; compact = one dot + "n/N" in the same row.
        public static DotRow CellRow(float w, float h, float durX, float durY, int loaded, int capacity, float px,
            float dotShare = 0.12f, float pitchShare = 0.144f, float insetShare = 0.07f, float minGapPx = 3f, float gapShare = 0.04f, float textShare = 1.45f)
        {
            var r = new DotRow { Mode = Mode(capacity) };
            var side = Math.Min(w, h);
            if (r.Mode == MagazineMode.None || !(side > 0f)) return r;
            r.Filled = Clamp(loaded, 0, capacity);
            var right = w - side * insetShare;
            var avail = Math.Max(0f, right - durX);
            var ratio = dotShare / Math.Max(1e-4f, pitchShare);
            r.X = durX;
            if (r.Mode == MagazineMode.Dots)
            {
                r.Count = capacity;
                r.Pitch = Math.Min(side * pitchShare, avail / capacity);
                r.Dot = r.Pitch * ratio;
            }
            else
            {
                r.Count = 1;
                r.Pitch = side * pitchShare;
                r.Dot = side * dotShare;
                r.TextX = durX + r.Pitch;
                r.TextW = Math.Max(0f, right - r.TextX);
            }
            // The row's height: the dot, or in compact mode the taller "n/N" box centred on it (MagazinePips sizes it
            // textShare x Dot): its bottom keeps the gap too.
            var gap = Math.Max(side * gapShare, px > 0f ? minGapPx / px : 0f);
            var rowH = r.Mode == MagazineMode.Compact ? r.Dot * Math.Max(1f, textShare) : r.Dot;
            r.Y = durY - gap - (rowH + r.Dot) / 2f;
            return r;
        }

        // Plate readout (second line of the ammo plate, plate-local units from the readout's left edge `x`, vertical
        // centre `cy`): dots of `dot` every dot + `gap`, then the "n/N" text; compact = one dot + text.
        public static DotRow PlateRow(float x, float cy, int loaded, int capacity, float dot, float gap, float textGap)
        {
            var r = new DotRow { Mode = Mode(capacity), Filled = Clamp(loaded, 0, capacity), Dot = dot, Pitch = dot + gap, X = x, Y = cy - dot / 2f };
            if (r.Mode == MagazineMode.None) return r;
            r.Count = r.Mode == MagazineMode.Dots ? capacity : 1;
            r.TextX = x + r.Count * r.Pitch - gap + textGap;
            return r;
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
