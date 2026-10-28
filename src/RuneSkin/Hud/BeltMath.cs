using System;

namespace RuneSkin.PlayerHud
{
    // Pure rules of the belt row (MorgottTweaks Belt API v1, MorgottTweaks docs\belt-api.md): which source feeds the
    // food / potion wings, the strip row order of the belt cells (food index 0 nearest the centre growing LEFT, potion
    // index 0 nearest the centre growing RIGHT) and the gamepad focus walk over that row. No Unity: unit tested.
    public static class BeltMath
    {
        public const int SupportedApi = 1;     // Belt.ApiVersion; additive changes keep 1
        public const int LayoutMax = 5;        // Belt.LayoutMax: most cells per kind
        public const int Food = 0, Potion = 1; // Belt.BeltKind values

        // Contract v1 only: a higher major (other meaning) is not read.
        public static bool ApiUsable(int version) => version == SupportedApi;

        public enum Source { Vanilla, Belt, EaqsQuick }

        // Food / potion wing source: the belt when MorgottTweaks' belt is on (EAQS quick slots are off then: Codex
        // decision 2026-10-29, belt REPLACES them); else EAQS quick cells for potions only while EAQS has quick slots
        // on; food without the belt = eaten foods (vanilla).
        public static Source PotionSource(bool beltOn, bool eaqsQuickOn) =>
            beltOn ? Source.Belt : eaqsQuickOn ? Source.EaqsQuick : Source.Vanilla;

        public static Source FoodSource(bool beltOn) => beltOn ? Source.Belt : Source.Vanilla;

        public static int Clamp(int cells) => Math.Max(0, Math.Min(LayoutMax, cells));

        // Draining ring fraction of a running effect; -1 = no ring (nothing running / unknown length).
        public static float Fraction(float remaining, float total) =>
            remaining > 0f && total > 0f ? Math.Min(1f, remaining / total) : -1f;

        // Cooldown wedge fraction (part of the lock still to run); 0 = none.
        public static float Cooldown(float remaining, float total) =>
            remaining > 0f && total > 0f ? Math.Min(1f, remaining / total) : 0f;

        // ---- row order (left to right): food cells food-1 .. 0, then potion cells 0 .. potion-1 ----

        public static int RowLength(int food, int potion) => Clamp(food) + Clamp(potion);

        public static bool RowCell(int pos, int food, int potion, out int kind, out int index)
        {
            food = Clamp(food);
            potion = Clamp(potion);
            kind = Food;
            index = -1;
            if (pos < 0 || pos >= food + potion) return false;
            if (pos < food)
            {
                index = food - 1 - pos;
                return true;
            }
            kind = Potion;
            index = pos - food;
            return true;
        }

        public static int RowPos(int kind, int index, int food, int potion)
        {
            food = Clamp(food);
            potion = Clamp(potion);
            if (kind == Food) return index >= 0 && index < food ? food - 1 - index : -1;
            return index >= 0 && index < potion ? food + index : -1;
        }

        // One step left (-1) / right (+1), clamped to the row (no wrap: the grid edges do not wrap either,
        // InventoryGrid.UpdateGamepad).
        public static int Step(int pos, int dir, int food, int potion)
        {
            var n = RowLength(food, potion);
            if (n == 0) return -1;
            return Math.Max(0, Math.Min(n - 1, pos + Math.Sign(dir)));
        }

        // Entering the row from a grid column: the same relative place (grid column centre -> nearest row cell).
        public static int EntryPos(int gridX, int gridWidth, int food, int potion)
        {
            var n = RowLength(food, potion);
            if (n == 0) return -1;
            if (gridWidth <= 1) return n / 2;
            var t = Math.Max(0, Math.Min(gridWidth - 1, gridX)) / (float)(gridWidth - 1);
            return (int)Math.Round(t * (n - 1), MidpointRounding.AwayFromZero);
        }

        // Leaving the row upward: the grid column at the same relative place.
        public static int GridColumn(int pos, int gridWidth, int food, int potion)
        {
            var n = RowLength(food, potion);
            if (gridWidth <= 1 || n <= 1) return Math.Max(0, gridWidth / 2);
            var t = Math.Max(0, Math.Min(n - 1, pos)) / (float)(n - 1);
            return (int)Math.Round(t * (gridWidth - 1), MidpointRounding.AwayFromZero);
        }

        // ---- gamepad presses on a focused belt cell (mirrors InventoryGrid.UpdateGamepad: A = select / drop, X = the
        // right-click action, LTrigger+X = Move) ----

        public enum PadAction { None, Select, ToggleAuto, MoveOut, UseNow }

        public static PadAction PadPress(bool a, bool x, bool leftTrigger, bool rightTrigger)
        {
            if (a) return PadAction.Select;                 // pick the stack up / drop the carried stack (vanilla drag)
            if (!x) return PadAction.None;
            if (leftTrigger) return PadAction.MoveOut;      // vanilla LTrigger+X = Modifier.Move
            if (rightTrigger) return PadAction.UseNow;      // mouse Alt+RMB
            return PadAction.ToggleAuto;                    // mouse RMB
        }
    }
}
