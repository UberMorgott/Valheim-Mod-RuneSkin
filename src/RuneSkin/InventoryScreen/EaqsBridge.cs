using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using RuneSkin.Compat;

namespace RuneSkin.InventoryScreen
{
    // EquipmentAndQuickSlots 3.1.3 (third-party, soft): its equipment / quick / custom cells are extra rows of the
    // player inventory; slot i owns InventoryGrid.m_elements[Slots.InventorySizeVisible + i] (EquipmentPanel
    // UpdateInventorySlots). Read by reflection (Slots.slots, Slot.ID/Index/IsActive/IsQuickSlot/IsEquipmentSlot/
    // IsCustomSlot, Slots.VisibleRows / InventorySizeVisible); absent or changed members = no EAQS cells.
    public static class EaqsBridge
    {
        public const string PluginGuid = ModCompat.EaqsGuid;

        public enum Kind { Equipment, Quick, Custom }

        public struct SlotInfo
        {
            public string Id;
            public int Index;
            public int Element; // index into the player grid's m_elements
            public Kind Kind;
        }

        private static bool? _available;
        private static FieldInfo _slots;
        private static PropertyInfo _sizeVisible, _visibleRows, _id, _index, _isActive, _isQuick, _isEquipment, _isCustom;

        public static bool Available => _available ?? (_available = Chainloader.PluginInfos.ContainsKey(PluginGuid) && Resolve()).Value;

        private static bool Resolve()
        {
            var slotsType = AccessTools.TypeByName(ModCompat.EaqsSlotsType);
            _slots = slotsType == null ? null : AccessTools.Field(slotsType, "slots");
            var slotType = _slots?.FieldType.GetElementType();
            if (slotType == null) return Missing();
            _sizeVisible = AccessTools.Property(slotsType, "InventorySizeVisible");
            _visibleRows = AccessTools.Property(slotsType, "VisibleRows");
            _id = AccessTools.Property(slotType, "ID");
            _index = AccessTools.Property(slotType, "Index");
            _isActive = AccessTools.Property(slotType, "IsActive");
            _isQuick = AccessTools.Property(slotType, "IsQuickSlot");
            _isEquipment = AccessTools.Property(slotType, "IsEquipmentSlot");
            _isCustom = AccessTools.Property(slotType, "IsCustomSlot");
            foreach (var m in new[] { _sizeVisible, _visibleRows, _id, _index, _isActive, _isQuick, _isEquipment, _isCustom })
                if (m == null) return Missing();
            return true;
        }

        private static bool Missing()
        {
            Plugin.Log.LogWarning("[RuneSkin] EAQS found, but its slot members changed: doll shows no EAQS cells");
            return false;
        }

        // Rows of the player grid that are real inventory (EAQS hides its slot rows behind these).
        public static int VisibleRows(int fallback)
        {
            if (!Available) return fallback;
            try { return (int)_visibleRows.GetValue(null); }
            catch { return fallback; }
        }

        // Active EAQS cells in slot order. Allocates: call on change / every few frames only.
        public static void ReadSlots(int elementCount, List<SlotInfo> into)
        {
            into.Clear();
            if (!Available) return;
            try
            {
                var slots = (Array)_slots.GetValue(null);
                var first = (int)_sizeVisible.GetValue(null);
                var count = Math.Min(slots.Length, elementCount - first);
                for (var i = 0; i < count; i++)
                {
                    var slot = slots.GetValue(i);
                    if (!(bool)_isActive.GetValue(slot)) continue;
                    Kind kind;
                    if ((bool)_isEquipment.GetValue(slot)) kind = Kind.Equipment;
                    else if ((bool)_isQuick.GetValue(slot)) kind = Kind.Quick;
                    else if ((bool)_isCustom.GetValue(slot)) kind = Kind.Custom;
                    else continue;
                    into.Add(new SlotInfo { Id = (string)_id.GetValue(slot), Index = (int)_index.GetValue(slot), Element = first + i, Kind = kind });
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[RuneSkin] EAQS slot read failed: {e.Message}");
                into.Clear();
            }
        }

        // Quick cells (Quick1..QuickN, EAQS Slots.GetQuickSlots order) with their inventory grid position (Slot.GridPosition)
        // and key label (Slot.GetShortcutText). Allocates: call on a layout change / every few seconds.
        public struct QuickInfo
        {
            public Vector2i Pos;
            public string Key;
            public bool Active;
        }

        private static PropertyInfo _gridPos;
        private static MethodInfo _shortcutText;

        public static void ReadQuick(List<QuickInfo> into)
        {
            into.Clear();
            if (!Available) return;
            try
            {
                var slots = (Array)_slots.GetValue(null);
                var slotType = _slots.FieldType.GetElementType();
                if (_gridPos == null) _gridPos = AccessTools.Property(slotType, "GridPosition");
                if (_shortcutText == null) _shortcutText = AccessTools.Method(slotType, "GetShortcutText");
                if (_gridPos == null) return;
                foreach (var slot in slots)
                {
                    // Every quick cell (active or not): the potion count comes from MorgottTweaks live and EAQS turns
                    // the cells on a moment later.
                    if (slot == null || !(bool)_isQuick.GetValue(slot)) continue;
                    var key = _shortcutText != null ? _shortcutText.Invoke(slot, null) as string : null;
                    into.Add(new QuickInfo { Pos = (Vector2i)_gridPos.GetValue(slot), Key = key, Active = (bool)_isActive.GetValue(slot) });
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[RuneSkin] EAQS quick slot read failed: {e.Message}");
                into.Clear();
            }
        }

        // Doll placement of an EAQS cell: glyph type + order (layout A, inventory-layout.md section 9: 3x3 storage
        // helmet, back, light / chest, belt, necklace / legs, ring, ring; other custom cells append). The doll column / row
        // comes from InventoryWindowsMath.DollSide / DollRank (necklace: right column, top).
        public static void Describe(string id, out string glyph, out int order, out string tooltipKey)
        {
            switch (id)
            {
                case "Helmet": glyph = "head"; order = 0; tooltipKey = "$runeskin_slot_head"; return;
                case "Shoulder": glyph = "back"; order = 1; tooltipKey = "$runeskin_slot_back"; return;
                case "CustomLight": glyph = "light"; order = 2; tooltipKey = "$runeskin_slot_light"; return;
                case "Chest": glyph = "chest"; order = 3; tooltipKey = "$runeskin_slot_chest"; return;
                case "Utility": glyph = "belt"; order = 4; tooltipKey = "$runeskin_slot_belt"; return;
                case "Trinket": glyph = "neck"; order = 5; tooltipKey = "$runeskin_slot_neck"; return;
                case "Legs": glyph = "legs"; order = 6; tooltipKey = "$runeskin_slot_legs"; return;
                case "Utility2": glyph = "ring"; order = 7; tooltipKey = "$runeskin_slot_ring"; return;
                case "Utility3": glyph = "ring"; order = 8; tooltipKey = "$runeskin_slot_ring"; return;
                default: glyph = "generic"; order = 100; tooltipKey = id; return;
            }
        }
    }
}
