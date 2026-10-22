using System.Runtime.CompilerServices;
using HarmonyLib;
using RuneSkin.PlayerHud;
using RuneSkin.Theme;
using UnityEngine;
using UnityEngine.UI;
using RuneSkin.Ownership;

namespace RuneSkin.WorldHud
{
    // Other players' shields under their overhead health bar (family PlayerShields, batch P: was an AugaSkin feature).
    // Vanilla shows another player's health only on the EnemyHud player HUD (m_baseHudPlayer, ShowHud EnemyHud.cs:126-157,
    // shown in range and not crouching :101-124, health set every frame :213-224); the bar is its "Health" child. Thin
    // rune rails go under it, each filled current / max, from the bar outwards:
    // - energy shield (MorgottTweaks [EnergyShield], player ZDO floats mt_es / mt_es_max, written by the owner);
    // - absorb shield (vanilla SE_Shield, e.g. Staff of Protection: its remaining absorb lives only in the owner's SEMan,
    //   so MorgottTweaks [Multiplayer] ShareShieldAbsorb publishes player ZDO mt_shield / mt_shield_max).
    // A rail hides while its max is 0 and the rest close up; the rails are children of the HUD object (its visibility).
    // The two floats of a pair arrive independently: the fill is clamped (SummonMath.Ratio).
    [HarmonyPatch]
    public static class PlayerShields
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly), batched by FastStartup).
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.PlayerShields);

        public const string EsName = "RuneSkin_EnergyShield";
        public const string ShieldName = "RuneSkin_Shield";
        private static readonly int EsKey = "mt_es".GetStableHashCode(), EsMaxKey = "mt_es_max".GetStableHashCode();
        private static readonly int ShieldKey = "mt_shield".GetStableHashCode(), ShieldMaxKey = "mt_shield_max".GetStableHashCode();

        private sealed class Rail
        {
            public RectTransform Rect, Fill;
            public int Slot = -1;
            public float Ratio = -1f;
        }

        private sealed class Rails
        {
            public Rail Es, Shield;
        }

        private static readonly ConditionalWeakTable<EnemyHud.HudData, Rails> Huds = new ConditionalWeakTable<EnemyHud.HudData, Rails>();

        [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.UpdateHuds))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void UpdateHuds_Postfix(EnemyHud __instance)
        {
            foreach (var pair in __instance.m_huds)
            {
                var c = pair.Key;
                var hud = pair.Value;
                if (!c || hud?.m_gui == null || !hud.m_gui.activeSelf || !c.IsPlayer()) continue;
                var zdo = c.m_nview ? c.m_nview.GetZDO() : null;
                if (zdo == null) continue;
                var rails = Huds.GetValue(hud, _ => new Rails());
                var slot = 0;
                if (Show(ref rails.Es, hud.m_gui.transform, EsName, "color.hud.es", zdo.GetFloat(EsKey), zdo.GetFloat(EsMaxKey), slot)) slot++;
                Show(ref rails.Shield, hud.m_gui.transform, ShieldName, "color.hud.shield", zdo.GetFloat(ShieldKey), zdo.GetFloat(ShieldMaxKey), slot);
            }
        }

        private static bool Show(ref Rail rail, Transform gui, string name, string color, float value, float max, int slot)
        {
            if (max <= 0f)
            {
                if (rail != null && rail.Rect && rail.Rect.gameObject.activeSelf) rail.Rect.gameObject.SetActive(false);
                return false;
            }
            if (rail == null || !rail.Rect) rail = Make(gui, name, color);
            if (rail == null) return false;
            if (!rail.Rect.gameObject.activeSelf) rail.Rect.gameObject.SetActive(true);
            if (rail.Slot != slot)
            {
                rail.Slot = slot;
                Place(rail.Rect, gui, slot);
            }
            var r = SummonMath.Ratio(value, max);
            if (rail.Ratio != r)
            {
                rail.Ratio = r;
                rail.Fill.anchorMax = new Vector2(r, 1f);
            }
            return true;
        }

        private static float Height => ThemeTokens.GetFloat("size.enemy.shield.rail", 5f);
        private static float Gap => ThemeTokens.GetFloat("size.enemy.shield.gap", 2f);

        // Top edge Gap under the health bar's bottom edge, each further slot one rail lower.
        private static void Place(RectTransform rail, Transform gui, int slot)
        {
            if (!(gui.Find("Health") is RectTransform health)) return;
            var below = health.pivot.y * health.rect.height + Gap + slot * (Height + Gap);
            rail.anchoredPosition = health.anchoredPosition - new Vector2(0f, below);
        }

        private static Rail Make(Transform gui, string name, string color)
        {
            if (!(gui.Find("Health") is RectTransform health)) return null;
            var rect = gui.Find(name) as RectTransform; // a rail left by an earlier build of this HUD object is reused
            if (!rect)
            {
                rect = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
                rect.SetParent(gui, false);
                new GameObject("fill", typeof(RectTransform), typeof(Image)).transform.SetParent(rect, false);
            }
            rect.anchorMin = health.anchorMin;
            rect.anchorMax = health.anchorMax;
            rect.pivot = new Vector2(health.pivot.x, 1f);
            rect.sizeDelta = new Vector2(health.rect.width, Height);
            var trough = rect.GetComponent<Image>();
            trough.sprite = ThemeTokens.GetSprite("sprite.hud.rail.trough");
            trough.type = trough.sprite ? Image.Type.Sliced : Image.Type.Simple;
            trough.color = trough.sprite ? Color.white : ThemeTokens.GetColor("color.hud.trough", Color.black);
            trough.raycastTarget = false;
            var fill = (RectTransform)rect.GetChild(0);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.sizeDelta = Vector2.zero;
            var img = fill.GetComponent<Image>();
            img.sprite = ThemeTokens.GetSprite("sprite.hud.rail.fill");
            img.type = img.sprite ? Image.Type.Sliced : Image.Type.Simple;
            img.color = ThemeTokens.GetColor(color, Color.cyan);
            img.raycastTarget = false;
            return new Rail { Rect = rect, Fill = fill };
        }
    }
}
