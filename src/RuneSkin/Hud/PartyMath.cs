using System;
using System.Collections.Generic;

namespace RuneSkin.PlayerHud
{
    // Party frame (PoE style, user pick v3 column B, docs\mockups\party-frame\v3-*.png), the managed part (unit-tested):
    // one unit per summon slot / pet, grouped into one icon per (creature prefab, pet, state class) with a count;
    // aggregated health / energy shield, the respawn sweep and the grid layout (top-left, wraps after N columns).
    public struct PartyUnit
    {
        public int Id;            // slot id (summons) or instance id (pets / loaded summons)
        public int Prefab;        // prefab name hash (ZDO prefab / mt_sum_ui)
        public bool Pet;
        public SummonState State;
        public byte Flags;        // mt_sum_ui flags: bit 0 stay, bit 1 Defensive, bit 2 Aggressive
        public float Hp;          // health fraction 0..1
        public float Es;          // energy shield fraction 0..1 (0 = none)
        public float HpCur, HpMax; // absolute health (estimated from the prefab when the body is not loaded)
        public float EsCur;       // absolute energy shield (0 when unknown)
        public double CdUntil;    // respawn time (ZNet.GetTimeSeconds) while Dead
    }

    public sealed class PartyGroup
    {
        public int Prefab;
        public bool Pet;
        public SummonState State; // state class (Spawning shares Waiting's stack)
        public readonly List<PartyUnit> Units = new List<PartyUnit>();

        public int Count => Units.Count;
        public float Hp, Es;      // stack fills: sum(cur) / sum(max) and the mean shield fraction
        public float HpCur, HpMax, EsCur;
        public float Weakest = -1f; // lowest current health of a unit with a body (-1 = none)
        public bool Stay;
        public int Mode;          // 0 normal, 1 Defensive, 2 Aggressive (first unit)
        public double NextCd;     // soonest respawn time of the stack (Dead)
    }

    public static class PartyMath
    {
        public const byte FlagStay = 1, FlagDefensive = 2, FlagAggressive = 4;

        // Spawning and Waiting (respawning) look the same (teal ring + refresh glyph) and share one stack.
        public static SummonState StateClass(SummonState s) => s == SummonState.Spawning ? SummonState.Waiting : s;

        // Order inside one creature type: fighting first, then coming back, then not available, dead last.
        public static int StateRank(SummonState s)
        {
            switch (s)
            {
                case SummonState.Alive: return 0;
                case SummonState.Waiting: return 1;
                case SummonState.Spawning: return 2;
                case SummonState.NoReserve: return 3;
                case SummonState.Off: return 4;
                case SummonState.Dead: return 5;
                default: return 6;
            }
        }

        public static int Mode(byte flags) => (flags & FlagAggressive) != 0 ? 2 : (flags & FlagDefensive) != 0 ? 1 : 0;

        // Has a body in the world (bars filled, full-strength icon).
        public static bool HasBody(SummonState s) => s == SummonState.Alive;

        // Stacks in display order: creature types in the order they first appear (summons before pets), inside a type
        // by StateRank. Units of one stack keep their input order.
        public static List<PartyGroup> Group(IList<PartyUnit> units)
        {
            var typeOrder = new Dictionary<long, int>();
            var stacks = new Dictionary<long, PartyGroup>();
            var list = new List<PartyGroup>();
            foreach (var u in units)
            {
                var type = TypeKey(u.Prefab, u.Pet);
                if (!typeOrder.ContainsKey(type)) typeOrder[type] = typeOrder.Count + (u.Pet ? 1 << 20 : 0);
                var cls = StateClass(u.State);
                var key = type * 16 + (long)cls;
                if (!stacks.TryGetValue(key, out var s))
                {
                    s = new PartyGroup { Prefab = u.Prefab, Pet = u.Pet, State = cls };
                    stacks[key] = s;
                    list.Add(s);
                }
                s.Units.Add(u);
            }
            foreach (var s in list) Aggregate(s);
            var order = new List<KeyValuePair<PartyGroup, int>>();
            for (var i = 0; i < list.Count; i++) order.Add(new KeyValuePair<PartyGroup, int>(list[i], i));
            order.Sort((a, b) =>
            {
                var ta = typeOrder[TypeKey(a.Key.Prefab, a.Key.Pet)];
                var tb = typeOrder[TypeKey(b.Key.Prefab, b.Key.Pet)];
                if (ta != tb) return ta.CompareTo(tb);
                var ra = StateRank(a.Key.State);
                var rb = StateRank(b.Key.State);
                return ra != rb ? ra.CompareTo(rb) : a.Value.CompareTo(b.Value);
            });
            return order.ConvertAll(p => p.Key);
        }

        private static long TypeKey(int prefab, bool pet) => ((long)(uint)prefab << 1) | (pet ? 1L : 0L);

        public static void Aggregate(PartyGroup s)
        {
            float cur = 0f, max = 0f, esCur = 0f, esSum = 0f;
            var bodies = 0;
            var weakest = float.MaxValue;
            var next = double.MaxValue;
            s.Stay = false;
            s.Mode = s.Units.Count > 0 ? Mode(s.Units[0].Flags) : 0;
            foreach (var u in s.Units)
            {
                if ((u.Flags & FlagStay) != 0) s.Stay = true;
                if (u.State == SummonState.Dead && u.CdUntil < next) next = u.CdUntil;
                if (!HasBody(u.State)) continue;
                bodies++;
                cur += u.HpCur;
                max += u.HpMax;
                esCur += u.EsCur;
                esSum += Clamp01(u.Es);
                if (u.HpCur < weakest) weakest = u.HpCur;
            }
            s.HpCur = cur;
            s.HpMax = max;
            s.EsCur = esCur;
            s.Hp = max > 0f ? Clamp01(cur / max) : 0f;
            s.Es = bodies > 0 ? Clamp01(esSum / bodies) : 0f;
            s.Weakest = bodies > 0 ? weakest : -1f;
            s.NextCd = next == double.MaxValue ? 0d : next;
        }

        // Dark clockwise wedge from 12 o'clock over the dead icon: the share of the respawn time still left.
        public static float SweepFraction(double remaining, double total) =>
            total > 0d && remaining > 0d ? (float)Math.Min(1d, remaining / total) : 0f;

        // The full respawn time of a death: the longest remaining time seen for it, at least the configured respawn
        // (a death first seen mid-way, e.g. after a relog, still sweeps from the right angle).
        public static double SweepTotal(double firstSeenRemaining, double configured) => Math.Max(firstSeenRemaining, configured);

        // "0:45" (minutes:seconds, seconds rounded up; 0:00 at the end).
        public static string Timer(double remaining)
        {
            var s = Math.Max(0, (int)Math.Ceiling(remaining));
            return $"{s / 60}:{s % 60:00}";
        }

        // Height of one icon block: tile, 2 px, ES bar, 1 px, life bar (mockup PH = 44 + 2 + 3 + 1 + 4 = 54).
        public static float BlockHeight(float tile, float esH, float lifeH) => tile + 2f + esH + 1f + lifeH;

        // Top-left of stack i inside the grid (x right, y down, reference px).
        public static void Cell(int index, int columns, float tile, float gap, float blockH, float rowGap, out float x, out float y)
        {
            columns = Math.Max(1, columns);
            x = index % columns * (tile + gap);
            y = index / columns * (blockH + rowGap);
        }

        public static int Rows(int count, int columns) => count <= 0 ? 0 : (count + Math.Max(1, columns) - 1) / Math.Max(1, columns);

        // Grid size for `count` stacks (0 x 0 when empty).
        public static void GridSize(int count, int columns, float tile, float gap, float blockH, float rowGap, out float w, out float h)
        {
            var rows = Rows(count, columns);
            var cols = Math.Min(count, Math.Max(1, columns));
            w = cols <= 0 ? 0f : cols * tile + (cols - 1) * gap;
            h = rows <= 0 ? 0f : rows * blockH + (rows - 1) * rowGap;
        }

        // Grid origin (its top-left corner) in the parent's local units, parent anchor top-left, y up: the inset (x, y)
        // reference px of a 1080p screen (scaled by `scale` = local units per reference px) measured from the safe area's
        // top-left corner (safeLeft / safeTop screen px from the screen's left / top edge; `perPx` = local units per
        // screen px). User 2026-10-26: the icon goes in the very top-left corner of the screen.
        public static void Origin(float x, float y, float scale, float safeLeft, float safeTop, float perPx, out float ox, out float oy)
        {
            ox = Math.Max(0f, safeLeft) * perPx + x * scale;
            oy = -(Math.Max(0f, safeTop) * perPx + y * scale);
        }

        // How far the top-left notification moves down (local units, >= 0) so its plate's top edge (where it sits with no
        // shift, y up) stays `gap` below the grid's bottom edge (same space); 0 with no grid shown.
        public static float NotifyShift(bool gridShown, float plateTop, float gridBottom, float gap) =>
            gridShown && !float.IsNaN(plateTop) && !float.IsNaN(gridBottom) ? Math.Max(0f, plateTop - (gridBottom - gap)) : 0f;

        private static float Clamp01(float v) => float.IsNaN(v) ? 0f : Math.Max(0f, Math.Min(1f, v));
    }
}
