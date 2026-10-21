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
        public Image Background, Icon, Glyph, Link;
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
        public void Show(ItemDrop.ItemData item, bool linked, int amount)
        {
            if (!ReferenceEquals(item, _shown) || linked != _linked)
            {
                _shown = item;
                _linked = linked;
                Icon.enabled = item != null;
                if (item != null) Icon.sprite = item.GetIcon();
                if (Glyph.sprite) Glyph.enabled = item == null;
                if (Link.sprite) Link.enabled = linked;
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
