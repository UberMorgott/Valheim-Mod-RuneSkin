using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using RuneSkin.Ownership;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.GameScreens
{
    // Compendium "Active effects" page (family Compendium; AugaSkin 2.2.21 yields its own page to RuneSkin >= 0.9.1).
    // Vanilla writes one text blob of names and tooltips (TextsDialog.AddActiveEffects, TextsDialog.cs:230-253); here it
    // is a list in the reading pane: one row per active effect (row_recipe_* plate, icon in a slot_cell_recess socket,
    // name display serif, time left muted, effect text body sans). Sources, all live (same contract as AugaSkin's page):
    // - foods (Player.GetFoods): item icon and name, current health / stamina / eitr of the food (Food.m_health ...,
    //   decayed by Player.UpdateFood), regen per tick (SharedData.m_foodRegen), m_time left;
    // - status effects (SEMan.GetStatusEffects): an SE with a public `IEnumerable MtActiveEffectRowsV1()` (soft contract,
    //   MorgottTweaks ActiveEffectRows: object[] {id, Sprite, name, text, seconds left (<0 none), active}) is drawn from
    //   its rows, hidden or not; any other SE as vanilla lists it (SEMan.GetHUDStatusEffects): icon, Localize(m_name),
    //   Localize(GetTooltipString()), time left when m_ttl > 0;
    // - the selected guardian power (Player.GetGuardianPowerHUD) with its cooldown or "ready".
    // Vanilla's TextInfo stays (AddActiveEffects still fills it); the one it inserted at 0 is remembered by reference and
    // on ShowText of it the text area's ScrollRect is swapped for the list (after Epic Loot's ShowText postfix, whose
    // MagicPages.Reset turns the text area back on). Refreshed every second while shown; rows reused by position.
    public static class ActiveEffectsPage
    {
        public const string ScreenId = CompendiumSkin.ScreenId + ".ActiveEffects";
        public const string RootName = "RuneSkinActiveEffects";
        private const string ProviderMethod = "MtActiveEffectRowsV1";

        private static TextsDialog.TextInfo _info;
        private static ActiveEffectsList _list;

        public static bool IsPage(TextsDialog.TextInfo text) => _info != null && ReferenceEquals(text, _info);

        // AddActiveEffects returns early without a local player (TextsDialog.cs:232-235) and inserts at 0 (:252).
        public static void Added(TextsDialog d) => _info = Player.m_localPlayer && d.m_texts.Count > 0 ? d.m_texts[0] : null;

        public static void Shown(TextsDialog d, TextsDialog.TextInfo text)
        {
            var list = List(d);
            if (list) list.Show(IsPage(text));
        }

        public static void Closed()
        {
            if (_list) _list.Show(false);
        }

        private static ActiveEffectsList List(TextsDialog d)
        {
            if (_list && _list.Dialog == d) return _list;
            var area = d.m_textArea ? d.m_textArea.GetComponentInParent<ScrollRect>(true) : null;
            if (!area)
            {
                Plugin.Log.LogWarning("[RuneSkin] active effects: no ScrollRect above TextsDialog.m_textArea (vanilla text page stays)");
                return null;
            }
            _list = ActiveEffectsList.Create(d, (RectTransform)area.transform);
            return _list;
        }

        // Roles of the list (own registration, ScreenId): row plates, sockets, icons, texts; the hit area is layout.
        public static void Roles(Classify.ScreenContext ctx, Transform root)
        {
            if (root.TryGetComponent<Image>(out var hit)) ctx.Role(hit, "skip", "active effects wheel hit area (transparent)");
            foreach (Transform row in root.GetComponentsInChildren<Transform>(true))
            {
                if (!row.name.StartsWith("Row", StringComparison.Ordinal) || !row.TryGetComponent<Image>(out var plate)) continue;
                ctx.Role(plate, "craft.row", "active effect row plate (row_recipe_normal)");
                if (row.Find("Socket") is Transform s && s.TryGetComponent<Image>(out var socket))
                {
                    ctx.Role(socket, "slot.recess", "active effect icon socket (slot_cell_recess)");
                    if (s.Find("Icon") is Transform i && i.TryGetComponent<Image>(out var icon)) ctx.Role(icon, "content", "active effect icon");
                }
                Text(ctx, row, "Name", "text.display", "active effect name (display serif)");
                Text(ctx, row, "Time", "text.muted", "active effect time left");
                Text(ctx, row, "Text", "text.body", "active effect text (body sans)");
            }
        }

        private static void Text(Classify.ScreenContext ctx, Transform row, string name, string role, string reason)
        {
            if (row.Find(name) is Transform t && t.TryGetComponent<TMP_Text>(out var tmp)) ctx.Role(tmp, role, reason);
        }

        // ---- rows

        public struct Row
        {
            public Sprite Icon;
            public string Name, Text;
            public float Seconds;
            public bool Active;
        }

        private static readonly Dictionary<Type, MethodInfo> Providers = new Dictionary<Type, MethodInfo>();
        private static readonly HashSet<Type> Failed = new HashSet<Type>();
        private static readonly List<StatusEffect> HudEffects = new List<StatusEffect>();

        private static string Hex(string token, Color fallback) => "#" + ColorUtility.ToHtmlStringRGBA(ThemeTokens.GetColor(token, fallback));

        public static void Collect(Player player, List<Row> rows)
        {
            rows.Clear();
            var loc = Localization.instance;
            var health = Hex("color.hud.edge.health", new Color(1f, 0.5f, 0.5f));
            var stamina = Hex("color.hud.edge.stamina", new Color(1f, 1f, 0.5f));
            var eitr = Hex("color.hud.edge.eitr", new Color(0.56f, 0.56f, 1f));
            var regen = Hex("color.text.ember", new Color(1f, 0.55f, 0.2f));
            foreach (var food in player.GetFoods())
            {
                var s = food.m_item?.m_shared;
                if (s == null) continue;
                var text = new List<string>(4);
                if (food.m_health > 0f) text.Add($"$item_food_health: <color={health}>{food.m_health:0}</color>");
                if (food.m_stamina > 0f) text.Add($"$item_food_stamina: <color={stamina}>{food.m_stamina:0}</color>");
                if (food.m_eitr > 0f) text.Add($"$item_food_eitr: <color={eitr}>{food.m_eitr:0}</color>");
                if (s.m_foodRegen > 0f) text.Add($"$item_food_regen: <color={regen}>{s.m_foodRegen.ToString("0.#", CultureInfo.InvariantCulture)} hp/tick</color>");
                rows.Add(new Row { Icon = food.m_item.GetIcon(), Name = loc.Localize(s.m_name), Text = loc.Localize(string.Join("   ", text)), Seconds = food.m_time, Active = true });
            }

            var seman = player.GetSEMan();
            HudEffects.Clear();
            seman.GetHUDStatusEffects(HudEffects);
            foreach (var se in seman.GetStatusEffects())
            {
                if (!se) continue;
                var provider = Provider(se.GetType());
                if (provider != null)
                {
                    AddProvided(se, provider, rows);
                    continue;
                }
                if (!HudEffects.Contains(se)) continue;
                rows.Add(new Row
                {
                    Icon = se.m_icon,
                    Name = loc.Localize(se.m_name),
                    Text = GameScreenMath.Tidy(loc.Localize(se.GetTooltipString())),
                    Seconds = se.m_ttl > 0f ? se.GetRemaningTime() : -1f,
                    Active = true
                });
            }

            player.GetGuardianPowerHUD(out var gp, out var cooldown);
            if (gp)
            {
                var state = cooldown > 0f ? StatusEffect.GetTimeString(cooldown) : loc.Localize("$hud_ready");
                rows.Add(new Row
                {
                    Icon = gp.m_icon,
                    Name = loc.Localize("$inventory_selectedgp") + ": " + loc.Localize(gp.m_name),
                    Text = GameScreenMath.Tidy(loc.Localize(gp.GetTooltipString())) + $"\n<color={Hex("color.text.active", new Color(0.91f, 0.71f, 0.29f))}>{state}</color>",
                    Seconds = -1f,
                    Active = cooldown <= 0f
                });
            }
        }

        private static MethodInfo Provider(Type type)
        {
            if (Providers.TryGetValue(type, out var m)) return m;
            m = type.GetMethod(ProviderMethod, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (m != null && !typeof(IEnumerable).IsAssignableFrom(m.ReturnType)) m = null;
            Providers[type] = m;
            return m;
        }

        // A provider decides what its SE shows; on failure it shows nothing new and the error is logged once per type.
        private static void AddProvided(StatusEffect se, MethodInfo provider, List<Row> rows)
        {
            try
            {
                if (!(provider.Invoke(se, null) is IEnumerable list)) return;
                foreach (var o in list)
                {
                    if (!(o is object[] r) || r.Length < 6) continue;
                    rows.Add(new Row
                    {
                        Icon = r[1] as Sprite,
                        Name = r[2] as string ?? "",
                        Text = GameScreenMath.Tidy(r[3] as string ?? ""),
                        Seconds = r[4] is float f ? f : -1f,
                        Active = !(r[5] is bool b) || b
                    });
                }
            }
            catch (Exception e)
            {
                if (Failed.Add(se.GetType()))
                    Plugin.Log.LogWarning($"[RuneSkin] active effects: {se.GetType().Name}.{ProviderMethod} failed: {e.GetBaseException().Message}");
            }
        }
    }

    // The list view: a sibling of the text area's ScrollRect with the same rect, its own ScrollRect, viewport mask and a
    // copy of the dialog's right scrollbar (styled by the role pass). Own screen registration (ActiveEffectsPage.ScreenId),
    // excluded from the Compendium root; restyled when rows are added.
    public sealed class ActiveEffectsList : MonoBehaviour
    {
        public TextsDialog Dialog;
        private GameObject _textScroll;
        private ScrollRect _scroll;
        private RectTransform _content;
        private readonly List<RowView> _views = new List<RowView>();
        private readonly List<ActiveEffectsPage.Row> _rows = new List<ActiveEffectsPage.Row>();
        private float _next;

        private sealed class RowView
        {
            public RectTransform Root;
            public Image Icon;
            public TMP_Text Name, Time, Text;
        }

        private static float Size(string token, float fallback) => ThemeTokens.GetFloat(token, fallback);

        public static ActiveEffectsList Create(TextsDialog dialog, RectTransform textScroll)
        {
            var go = new GameObject(ActiveEffectsPage.RootName, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(textScroll.parent, false);
            rt.anchorMin = textScroll.anchorMin;
            rt.anchorMax = textScroll.anchorMax;
            rt.pivot = textScroll.pivot;
            rt.anchoredPosition = textScroll.anchoredPosition;
            rt.sizeDelta = textScroll.sizeDelta;
            rt.SetSiblingIndex(textScroll.GetSiblingIndex() + 1);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            viewport.SetParent(rt, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = new Vector2(-20f, 0f); // room for the scrollbar
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;

            var list = go.AddComponent<ActiveEffectsList>();
            list.Dialog = dialog;
            list._textScroll = textScroll.gameObject;
            list._content = content;
            var scroll = go.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            scroll.viewport = viewport;
            scroll.content = content;
            // A raycast target over the whole view so the wheel scrolls anywhere on it.
            go.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            if (dialog.m_rightScrollbar)
            {
                var bar = Instantiate(dialog.m_rightScrollbar.gameObject, rt, false).GetComponent<Scrollbar>();
                bar.name = "Scrollbar";
                bar.onValueChanged = new Scrollbar.ScrollEvent();
                var barRt = (RectTransform)bar.transform;
                var w = ((RectTransform)dialog.m_rightScrollbar.transform).rect.width;
                barRt.anchorMin = new Vector2(1f, 0f);
                barRt.anchorMax = Vector2.one;
                barRt.pivot = new Vector2(1f, 0.5f);
                barRt.anchoredPosition = Vector2.zero;
                barRt.sizeDelta = new Vector2(w > 0f ? w : 12f, 0f);
                bar.direction = Scrollbar.Direction.BottomToTop;
                scroll.verticalScrollbar = bar;
                scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            }
            list._scroll = scroll;
            go.SetActive(false);
            GameScreenKit.Form(rt, ActiveEffectsPage.ScreenId, OwnershipRegistry.Compendium, ActiveEffectsPage.Roles, panels: false);
            return list;
        }

        public void Show(bool show)
        {
            if (show)
            {
                _textScroll.SetActive(false);
                gameObject.SetActive(true);
                _next = 0f;
                Refresh();
                _scroll.verticalNormalizedPosition = 1f;
            }
            else if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
                _textScroll.SetActive(true);
            }
        }

        private void Update()
        {
            if (ZInput.IsGamepadActive())
            {
                var y = ZInput.GetJoyRightStickY();
                if (Mathf.Abs(y) > 0.1f && _content.rect.height > 0f)
                    _scroll.verticalNormalizedPosition = Mathf.Clamp01(_scroll.verticalNormalizedPosition -
                        y * 1000f * UnityEngine.Time.unscaledDeltaTime / Mathf.Max(1f, _content.rect.height));
            }
            if (UnityEngine.Time.unscaledTime >= _next) Refresh();
        }

        private void Refresh()
        {
            _next = UnityEngine.Time.unscaledTime + 1f;
            var player = Player.m_localPlayer;
            if (!player) return;
            ActiveEffectsPage.Collect(player, _rows);
            // New rows take the look before they are filled (the role pass sets colours; the alpha below dims them).
            if (_rows.Count > _views.Count)
            {
                while (_views.Count < _rows.Count) _views.Add(MakeRow(_views.Count));
                GameScreenKit.Restyle(transform);
            }
            var width = ((RectTransform)_scroll.viewport).rect.width;
            var pad = Size("size.effects.pad", 12f);
            var icon = Size("size.effects.icon", 48f);
            var nameSize = Size("size.effects.name", 22f);
            var textX = pad * 3f + icon;
            var textWidth = Mathf.Max(100f, width - textX - pad);
            var y = 0f;
            for (var i = 0; i < _rows.Count; i++)
            {
                var v = _views[i];
                var r = _rows[i];
                v.Root.gameObject.SetActive(true);
                v.Icon.sprite = r.Icon;
                v.Icon.enabled = r.Icon;
                var alpha = r.Active ? 1f : 0.45f;
                v.Icon.color = new Color(1f, 1f, 1f, alpha);
                v.Name.text = r.Name;
                v.Name.alpha = alpha;
                v.Time.text = r.Seconds >= 0f ? StatusEffect.GetTimeString(r.Seconds) : "";
                v.Text.text = r.Text;
                v.Text.alpha = alpha;
                var nameHeight = nameSize + 6f;
                var textHeight = string.IsNullOrEmpty(r.Text) ? 0f : v.Text.GetPreferredValues(r.Text, textWidth, 0f).y;
                Place(v.Name.rectTransform, textX, pad, textWidth, nameHeight);
                Place(v.Time.rectTransform, textX, pad, textWidth, nameHeight);
                Place(v.Text.rectTransform, textX, pad + nameHeight, textWidth, textHeight);
                var height = Mathf.Max(icon, nameHeight + textHeight) + pad * 2f;
                v.Root.anchoredPosition = new Vector2(0f, -y);
                v.Root.sizeDelta = new Vector2(0f, height);
                y += height + Size("size.effects.gap", 6f);
            }
            for (var i = _rows.Count; i < _views.Count; i++) _views[i].Root.gameObject.SetActive(false);
            _content.sizeDelta = new Vector2(0f, y);
        }

        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private RowView MakeRow(int index)
        {
            var root = new GameObject("Row" + index, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
            root.SetParent(_content, false);
            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = Vector2.one;
            root.pivot = new Vector2(0.5f, 1f);
            root.GetComponent<Image>().raycastTarget = false;
            var pad = Size("size.effects.pad", 12f);
            var side = Size("size.effects.icon", 48f);
            var socket = new GameObject("Socket", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            var srt = socket.rectTransform;
            srt.SetParent(root, false);
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0f, 1f);
            srt.anchoredPosition = new Vector2(pad, -pad);
            srt.sizeDelta = new Vector2(side + pad, side + pad);
            socket.raycastTarget = false;
            var icon = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            var irt = icon.rectTransform;
            irt.SetParent(srt, false);
            irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 0.5f);
            irt.anchoredPosition = Vector2.zero;
            irt.sizeDelta = new Vector2(side, side);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var body = Size("size.effects.body", 18f);
            var title = Text(root, "Name", Size("size.effects.name", 22f), TextAlignmentOptions.TopLeft);
            var time = Text(root, "Time", body, TextAlignmentOptions.TopRight);
            var text = Text(root, "Text", body, TextAlignmentOptions.TopLeft);
            text.textWrappingMode = TextWrappingModes.Normal;
            return new RowView { Root = root, Icon = icon, Name = title, Time = time, Text = text };
        }

        private TMP_Text Text(RectTransform parent, string name, float size, TextAlignmentOptions align)
        {
            var t = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            var rt = t.rectTransform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            if (Dialog && Dialog.m_textArea) t.font = Dialog.m_textArea.font; // the body face until the role pass runs
            t.enableAutoSizing = false;
            t.fontSize = size;
            t.alignment = align;
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            t.text = "";
            return t;
        }
    }
}
