using System.Collections.Generic;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.PlayerHud
{
    // Summon frames (family SummonFrames, batch P: was an AugaSkin feature): one small socket per summon of the local
    // player in a row next to the hotbar: creature icon (its trophy), a health rail with the summon energy shield over its
    // upper half, a status line. Vanilla has no such element.
    // Sources:
    // - MorgottTweaks permanent summons: player ZDO mt_sum_ui (written by the summoner's client on every change:
    //   int version 1, int count, per slot int id, int prefab hash, byte state (SummonState), byte weight, byte flags
    //   (bit 0 = stay), float hp fraction, float ES fraction, double respawn time, int generation, ZDOID body;
    //   MorgottTweaks SummonSlotsModule.MirrorUi). Dead / off / suppressed slots without a body show too. A loaded body of
    //   the slot's generation (body ZDO mt_sum_gen) gives live health and its ZDO ES (mt_sum_es / mt_sum_esmax).
    // - Without that key: the loaded creatures that are this player's summons (vanilla's own test,
    //   Tameable.UnsummonMaxInstances, Tameable.cs:641-701: tamed, following this player by name; a summon prefab =
    //   unsummon distance or logout timer, Tameable.cs:30-32; a mt_sum_owner stamp of another player excludes it).
    // The row is a child of the HotkeyBar (hidden with it: large map, HUD fade) placed below or above its slots
    // (SummonMath.Below), refreshed 4x a second.
    public sealed class SummonFrames : MonoBehaviour
    {
        public const string RootName = "RuneSkin_SummonFrames";
        private const float Refresh = 0.25f;

        private static readonly int SlotsKey = "mt_sum_ui".GetStableHashCode();
        private static readonly int GenKey = "mt_sum_gen".GetStableHashCode();
        private static readonly int EsKey = "mt_sum_es".GetStableHashCode();
        private static readonly int EsMaxKey = "mt_sum_esmax".GetStableHashCode();
        private static readonly int OwnerKey = "mt_sum_owner".GetStableHashCode();

        private struct Entry
        {
            public int Key;
            public GameObject Prefab;
            public SummonState State;
            public bool Stay, HasShield;
            public float Health, Shield;
            public double CdUntil;
        }

        private sealed class Frame
        {
            public RectTransform Root, Fill, ShieldFill;
            public Image Icon;
            public TMP_Text Letter, Status;
            public GameObject Prefab;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<Frame> _frames = new List<Frame>();
        private static readonly Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();
        private HotkeyBar _bar;
        private RectTransform _rt;
        private float _timer;
        private string _last;

        public static string Describe { get; private set; } = "";

        public static void Attach(Hud hud)
        {
            var bar = hud ? hud.GetComponentInChildren<HotkeyBar>(true) : null;
            if (!bar || bar.transform.Find(RootName)) return;
            var rt = new GameObject(RootName, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(bar.transform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            var f = rt.gameObject.AddComponent<SummonFrames>();
            f._bar = bar;
            f._rt = rt;
            Plugin.Log.LogInfo("[RuneSkin] summon frames attached under the hotbar");
        }

        private float Size => ThemeTokens.GetFloat("size.hud.summon.frame", 44f);
        private float Gap => ThemeTokens.GetFloat("size.hud.summon.gap", 6f);
        private float BarH => ThemeTokens.GetFloat("size.hud.summon.bar", 7f);

        private void Update()
        {
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = Refresh;
            _entries.Clear();
            var player = Player.m_localPlayer;
            if (player && !player.IsDead() && !ReadSlots(player)) ReadLoaded(player);
            for (var i = 0; i < _entries.Count; i++)
            {
                if (i == _frames.Count) _frames.Add(MakeFrame(i));
                Fill(_frames[i], _entries[i]);
            }
            for (var i = _entries.Count; i < _frames.Count; i++)
                if (_frames[i].Root.gameObject.activeSelf) _frames[i].Root.gameObject.SetActive(false);
            Place();
            var d = string.Join(" ", _entries.ConvertAll(e => $"[{(e.Prefab ? e.Prefab.name : "?")} {e.State} hp={e.Health:0.00} es={(e.HasShield ? e.Shield.ToString("0.00") : "-")}]"));
            if (d != _last)
            {
                _last = d;
                Describe = $"{_entries.Count} frame(s) {d}";
                Plugin.Log.LogInfo($"[RuneSkin] summon frames: {Describe}");
            }
        }

        private bool ReadSlots(Player player)
        {
            var zdo = player.m_nview ? player.m_nview.GetZDO() : null;
            var bytes = zdo?.GetByteArray(SlotsKey);
            if (bytes == null || bytes.Length < 8) return false;
            var pkg = new ZPackage(bytes);
            if (pkg.ReadInt() != SummonMath.SlotsVersion) return false;
            var count = pkg.ReadInt();
            for (var i = 0; i < count; i++)
            {
                var id = pkg.ReadInt();
                var hash = pkg.ReadInt();
                var state = (SummonState)pkg.ReadByte();
                pkg.ReadByte(); // weight
                var flags = pkg.ReadByte();
                var hp = pkg.ReadSingle();
                var es = pkg.ReadSingle();
                var cd = pkg.ReadDouble();
                var gen = pkg.ReadInt();
                var body = pkg.ReadZDOID();
                if (_entries.Count >= SummonMath.MaxFrames) continue;
                var e = new Entry
                {
                    Key = id,
                    State = state,
                    Stay = (flags & 1) != 0,
                    CdUntil = cd,
                    Prefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(hash) : null,
                    Health = SummonMath.Ratio(hp, 1f),
                    Shield = SummonMath.Ratio(es, 1f),
                    HasShield = es > 0f,
                };
                var c = Body(body, gen);
                if (c) Live(c, ref e);
                _entries.Add(e);
            }
            return true;
        }

        private static Character Body(ZDOID id, int generation)
        {
            if (id.IsNone() || !ZNetScene.instance) return null;
            var go = ZNetScene.instance.FindInstance(id);
            if (!go || !go.TryGetComponent<Character>(out var c) || c.IsDead() || !c.m_nview || c.m_nview.GetZDO() == null) return null;
            return c.m_nview.GetZDO().GetInt(GenKey) == generation ? c : null;
        }

        private static void Live(Character c, ref Entry e)
        {
            var zdo = c.m_nview.GetZDO();
            e.Health = SummonMath.Ratio(c.GetHealth(), c.GetMaxHealth());
            var max = zdo.GetFloat(EsMaxKey);
            e.HasShield = max > 0f;
            e.Shield = SummonMath.Ratio(zdo.GetFloat(EsKey), max);
        }

        private void ReadLoaded(Player player)
        {
            var id = player.GetPlayerID();
            var playerName = player.GetPlayerName();
            foreach (var c in Character.GetAllCharacters())
            {
                if (_entries.Count >= SummonMath.MaxFrames) break;
                if (!c || c.IsPlayer() || c.IsDead() || !c.m_nview || c.m_nview.GetZDO() == null || !IsSummonOf(c, player, id, playerName)) continue;
                var e = new Entry { Key = c.GetInstanceID(), Prefab = c.gameObject, State = SummonState.Alive };
                Live(c, ref e);
                _entries.Add(e);
            }
        }

        public static bool IsSummonOf(Character c, Player player, long playerId, string playerName)
        {
            if (!c.IsTamed() || !c.TryGetComponent<Tameable>(out var t)) return false;
            if (t.m_unsummonDistance <= 0f && t.m_unsummonOnOwnerLogoutSeconds <= 0f) return false;
            var zdo = c.m_nview.GetZDO();
            var stamp = zdo.GetLong(OwnerKey);
            if (stamp != 0L && stamp != playerId) return false;
            if (string.IsNullOrEmpty(playerName) || zdo.GetString(ZDOVars.s_follow) != playerName) return false;
            // The ZDO owner knows the live follow target (MonsterAI, set only there): another target = not following us.
            if (c.m_nview.IsOwner() && c.GetBaseAI() is MonsterAI ai && ai.GetFollowTarget() is GameObject target && target != player.gameObject) return false;
            return t.m_unsummonDistance <= 0f || Vector3.Distance(c.transform.position, player.transform.position) <= t.m_unsummonDistance;
        }

        // Below the hotbar's slot row (vanilla top-left) or above it (bottom strip): the slots are laid out from the bar's
        // origin (HotkeyBar.cs:110, element (i * m_elementSpace, 0), top-left pivot) at the element prefab's height.
        private void Place()
        {
            if (!_bar || !_rt) return;
            var size = Size;
            var h = size + BarH + 3f;
            _rt.sizeDelta = new Vector2(SummonMath.FrameX(SummonMath.MaxFrames, size, Gap), h);
            var elemH = _bar.m_elementPrefab && _bar.m_elementPrefab.transform is RectTransform e ? e.rect.height : 64f;
            var canvas = _bar.GetComponentInParent<Canvas>();
            var cam = canvas && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            var screenY = RectTransformUtility.WorldToScreenPoint(cam, _bar.transform.position).y;
            var gap = ThemeTokens.GetFloat("size.hud.gap", 5f) * 2f;
            var y = SummonMath.Below(screenY, Screen.height) ? -(elemH + gap) : h + gap;
            var want = new Vector2(0f, y);
            if ((_rt.anchoredPosition - want).sqrMagnitude > 0.25f) _rt.anchoredPosition = want;
        }

        private Frame MakeFrame(int index)
        {
            var size = Size;
            var f = new Frame();
            f.Root = Rect(transform, "Summon", new Vector2(0f, 1f), new Vector2(0f, 1f));
            f.Root.pivot = new Vector2(0f, 1f);
            f.Root.sizeDelta = new Vector2(size, size + BarH + 3f);
            f.Root.anchoredPosition = new Vector2(SummonMath.FrameX(index, size, Gap), 0f);

            var slot = Rect(f.Root, "Slot", new Vector2(0f, 1f), new Vector2(1f, 1f));
            slot.pivot = new Vector2(0.5f, 1f);
            slot.sizeDelta = new Vector2(0f, size);
            Img(slot, ThemeTokens.GetSprite("sprite.hud.socket.hotbar"), Color.white, Image.Type.Simple);
            var icon = Rect(slot, "Icon", Vector2.zero, Vector2.one);
            var inset = size * ThemeTokens.GetFloat("share.hud.summon.icon.inset", 0.18f);
            icon.offsetMin = new Vector2(inset, inset);
            icon.offsetMax = new Vector2(-inset, -inset);
            f.Icon = Img(icon, null, Color.white, Image.Type.Simple);
            f.Icon.preserveAspect = true;
            f.Letter = Text(slot, "Letter", ThemeTokens.GetFloat("size.hud.summon.letter", 20f), TextAlignmentOptions.Center);
            f.Status = Text(slot, "Status", ThemeTokens.GetFloat("size.hud.summon.status", 11f), TextAlignmentOptions.Bottom);
            f.Status.rectTransform.offsetMin = new Vector2(2f, 3f);
            f.Status.rectTransform.offsetMax = new Vector2(-2f, 0f);
            f.Status.enableAutoSizing = true;
            f.Status.fontSizeMin = ThemeTokens.GetFloat("floor.text.timer.px", 9f);
            f.Status.fontSizeMax = f.Status.fontSize;
            f.Status.outlineWidth = 0.25f;
            f.Status.outlineColor = Color.black;

            var bar = Rect(f.Root, "Health", Vector2.zero, new Vector2(1f, 0f));
            bar.pivot = new Vector2(0.5f, 0f);
            bar.sizeDelta = new Vector2(0f, BarH);
            Img(bar, ThemeTokens.GetSprite("sprite.hud.rail.trough"), Color.white, Image.Type.Sliced);
            f.Fill = Rect(bar, "fill", Vector2.zero, Vector2.one);
            Img(f.Fill, ThemeTokens.GetSprite("sprite.hud.rail.fill"), ThemeTokens.GetColor("color.hud.ally", Color.green), Image.Type.Sliced);
            f.ShieldFill = Rect(bar, "shield", new Vector2(0f, 0.5f), Vector2.one);
            Img(f.ShieldFill, ThemeTokens.GetSprite("sprite.hud.rail.fill"), ThemeTokens.GetColor("color.hud.es", Color.cyan), Image.Type.Sliced);
            return f;
        }

        private static void Fill(Frame f, Entry e)
        {
            if (!f.Root.gameObject.activeSelf) f.Root.gameObject.SetActive(true);
            if (f.Prefab != e.Prefab)
            {
                f.Prefab = e.Prefab;
                var icon = e.Prefab ? IconFor(e.Prefab) : null;
                f.Icon.sprite = icon;
                f.Icon.enabled = icon;
                f.Letter.gameObject.SetActive(!icon);
                if (!icon)
                {
                    var n = e.Prefab && e.Prefab.TryGetComponent<Character>(out var c) ? Localization.instance.Localize(c.m_name) : null;
                    f.Letter.text = string.IsNullOrEmpty(n) ? "?" : n.Substring(0, 1).ToUpperInvariant();
                }
            }
            var active = SummonMath.Active(e.State);
            var tint = active ? Color.white : ThemeTokens.GetColor("color.hud.inert", Color.gray);
            f.Icon.color = tint;
            f.Letter.color = tint;
            var key = SummonMath.StatusKey(e.State, e.Stay, e.CdUntil, ZNet.instance ? ZNet.instance.GetTimeSeconds() : 0d, out var secs);
            var text = key == null ? "" : Localization.instance.Localize("$" + key);
            if (secs >= 0) text = string.Format(text, secs);
            if (f.Status.text != text) f.Status.text = text;
            f.Fill.anchorMax = new Vector2(e.Health, 1f);
            var shield = e.HasShield && e.Shield > 0f;
            if (f.ShieldFill.gameObject.activeSelf != shield) f.ShieldFill.gameObject.SetActive(shield);
            f.ShieldFill.anchorMax = new Vector2(e.Shield, 1f);
        }

        // The creature's trophy (the only per-creature art: trophies panel, Compendium). Summon variants drop nothing
        // (Skeleton_Friendly, Troll_Summoned): the trophy of the creature whose m_name is the longest prefix of theirs
        // ("$enemy_skeleton" of "$enemy_skeleton_summoned"). None: the frame shows the name's first letter.
        public static Sprite IconFor(GameObject creature)
        {
            var key = Utils.GetPrefabName(creature);
            if (Icons.TryGetValue(key, out var icon)) return icon;
            icon = Trophy(creature);
            var mName = creature.TryGetComponent<Character>(out var c) ? c.m_name : null;
            if (!icon && ZNetScene.instance && !string.IsNullOrEmpty(mName))
            {
                var best = 0;
                foreach (var p in ZNetScene.instance.m_prefabs)
                {
                    if (!p || !p.TryGetComponent<Character>(out var o) || string.IsNullOrEmpty(o.m_name)) continue;
                    var len = o.m_name.Length;
                    if (len <= best || len >= mName.Length || !mName.StartsWith(o.m_name, System.StringComparison.Ordinal)) continue;
                    var t = Trophy(p);
                    if (!t) continue;
                    icon = t;
                    best = len;
                }
            }
            Icons[key] = icon;
            return icon;
        }

        private static Sprite Trophy(GameObject creature)
        {
            if (!creature.TryGetComponent<CharacterDrop>(out var drops)) return null;
            foreach (var d in drops.m_drops)
            {
                var item = d.m_prefab ? d.m_prefab.GetComponent<ItemDrop>() : null;
                var shared = item ? item.m_itemData.m_shared : null;
                if (shared != null && shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy && shared.m_icons.Length > 0) return shared.m_icons[0];
            }
            return null;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.sizeDelta = Vector2.zero;
            return rt;
        }

        private static Image Img(RectTransform rt, Sprite sprite, Color color, Image.Type type)
        {
            var i = rt.gameObject.AddComponent<Image>();
            i.sprite = sprite;
            i.type = sprite ? type : Image.Type.Simple;
            i.color = color;
            i.raycastTarget = false;
            return i;
        }

        private static TMP_Text Text(RectTransform parent, string name, float size, TextAlignmentOptions align)
        {
            var rt = Rect(parent, name, Vector2.zero, Vector2.one);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            var font = ThemeTokens.GetFont("font.body");
            if (font) t.font = font;
            t.fontSize = size;
            t.alignment = align;
            t.color = ThemeTokens.GetColor("color.hud.text", Color.white);
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }
    }

    // Installed only when RuneSkin owns SummonFrames (Plugin).
    [HarmonyLib.HarmonyPatch]
    public static class SummonFramesAdapter
    {
        [HarmonyLib.HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
        [HarmonyLib.HarmonyPostfix]
        private static void Awake_Postfix(Hud __instance) => SummonFrames.Attach(__instance);
    }
}
