using System;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuneSkin.InventoryScreen
{
    // A doll cell RuneSkin builds itself: hand / ammo mirrors and API-registered slots. No visible label: an empty cell
    // shows its carved type glyph, the name is in the tooltip. Input: left click (with the vanilla drag item held =
    // drop onto the cell, without = OnClick), drop at the end of a drag (IDropHandler), right click = OnSecondary.
    public sealed class DollCell : MonoBehaviour, IPointerClickHandler, IDropHandler
    {
        public Image Background, Icon, Glyph, Link, Badge;
        private Sprite _glyph, _occupied;
        public TMP_Text Amount;
        public UITooltip Tooltip;
        public string Id;

        public Action<DollCell> OnPrimary, OnSecondary, OnDropped;

        private ItemDrop.ItemData _shown;
        private bool _linked;
        private float _iconAlpha = -1f;

        public static DollCell Create(Transform parent, string id, string backgroundRole, string glyphRole, GameObject tooltipPrefab)
        {
            var go = new GameObject("RuneSkinDoll_" + id, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.pivot = new Vector2(0f, 1f);
            var cell = go.AddComponent<DollCell>();
            cell.Id = id;
            cell.Background = Layer(rt, "Bkg", backgroundRole, true, 0f);
            cell.Link = Layer(rt, "Link", "slot.link", false, -3f);
            cell.Link.enabled = false;
            cell.Glyph = Layer(rt, "Glyph", glyphRole, false, 8f);
            // Engraving, not an icon: the empty-cell glyph reads smaller than an item (Codex M2 review).
            var gs = ThemeTokens.GetFloat("share.inv.glyph.scale", 0.82f);
            cell.Glyph.rectTransform.localScale = new Vector3(gs, gs, 1f);
            var icon = new GameObject("Icon", typeof(RectTransform)).AddComponent<Image>();
            Stretch(icon.rectTransform, rt, 6f);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.enabled = false;
            Mark(icon, "content", "doll cell item icon");
            cell.Icon = icon;
            var amount = new GameObject("Amount", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            Stretch(amount.rectTransform, rt, 4f);
            amount.alignment = TextAlignmentOptions.BottomRight;
            amount.fontSize = 16f;
            amount.raycastTarget = false;
            var font = ThemeTokens.GetFont("font.body");
            if (font) amount.font = font;
            amount.text = "";
            Mark(amount, "content", "doll cell amount (live value)");
            cell.Amount = amount;
            // Mirror badge (chain links: "equipped from the grid") + occupied-hand glyph for two-handed weapons.
            if (backgroundRole == "slot.mirror")
            {
                var badge = new GameObject("Badge", typeof(RectTransform)).AddComponent<Image>();
                var b = badge.rectTransform;
                b.SetParent(rt, false);
                b.anchorMin = b.anchorMax = b.pivot = new Vector2(1f, 1f);
                b.anchoredPosition = new Vector2(-2f, -2f);
                b.sizeDelta = new Vector2(18f, 18f);
                badge.raycastTarget = false;
                badge.preserveAspect = true;
                var bm = Mark(badge, "glyph.link", "mirror link badge");
                bm.Outcome = Restyler.Apply(badge, bm, ThemeTokens.Role("glyph.link")) ? Outcome.Styled : Outcome.Degraded;
                badge.enabled = false;
                cell.Badge = badge;
                cell._occupied = ThemeTokens.GetSprite("sprite.glyph.hand.occupied");
            }
            cell._glyph = cell.Glyph.sprite;
            if (tooltipPrefab)
            {
                cell.Tooltip = go.AddComponent<UITooltip>();
                cell.Tooltip.m_tooltipPrefab = tooltipPrefab;
            }
            return cell;
        }

        private static Image Layer(RectTransform parent, string name, string role, bool raycast, float inset)
        {
            var image = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            Stretch(image.rectTransform, parent, inset);
            image.raycastTarget = raycast;
            var mark = Mark(image, role, "RuneSkin doll cell layer");
            mark.Outcome = Restyler.Apply(image, mark, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
            // Missing art draws nothing (never a white box), except the hit area of the background.
            if (mark.Outcome != Outcome.Styled)
            {
                if (raycast) image.color = new Color(0f, 0f, 0f, 0.35f);
                else image.enabled = false;
            }
            return image;
        }

        public static SkinMark Mark(Graphic g, string role, string reason)
        {
            var mark = g.gameObject.AddComponent<SkinMark>();
            mark.Internal = true;
            mark.Role = role;
            mark.Reason = reason;
            mark.BoundInstance = g.gameObject.GetInstanceID();
            mark.hideFlags = HideFlags.HideInInspector;
            if (role == "content") { mark.Outcome = Outcome.Content; mark.ThemeRevision = ThemeTokens.Revision; }
            return mark;
        }

        private static void Stretch(RectTransform rt, RectTransform parent, float inset)
        {
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        // Shows an item (null = empty: glyph visible). linked = the item is the other hand's two-handed weapon.
        // linked = this hand is taken by the other hand's two-handed weapon: occupied glyph, no icon, link rim.
        public void Show(ItemDrop.ItemData item, bool linked, int amount)
        {
            if (!ReferenceEquals(item, _shown) || linked != _linked)
            {
                _shown = item;
                _linked = linked;
                var occupied = linked && _occupied;
                Icon.enabled = item != null && !occupied;
                if (item != null) Icon.sprite = item.GetIcon();
                Glyph.sprite = occupied ? _occupied : _glyph;
                Glyph.enabled = Glyph.sprite && (item == null || occupied);
                if (Link.sprite) Link.enabled = linked;
                if (Badge && Badge.sprite) Badge.enabled = item != null && !occupied;
            }
            var alpha = linked ? 0.45f : 1f;
            if (!Mathf.Approximately(alpha, _iconAlpha))
            {
                _iconAlpha = alpha;
                Icon.color = new Color(1f, 1f, 1f, alpha);
            }
            var text = amount > 1 ? amount.ToString() : "";
            if (Amount.text != text) Amount.text = text;
        }

        public void SetTooltip(string topic, string text)
        {
            if (!Tooltip) return;
            if (Tooltip.m_topic != topic) Tooltip.m_topic = topic;
            if (Tooltip.m_text != text) Tooltip.m_text = text;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right) OnSecondary?.Invoke(this);
            else if (eventData.button == PointerEventData.InputButton.Left) OnPrimary?.Invoke(this);
        }

        public void OnDrop(PointerEventData eventData) => OnDropped?.Invoke(this);
    }
}
