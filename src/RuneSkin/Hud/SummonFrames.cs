using System.Collections.Generic;
using System.Text;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RuneSkin.Ownership;

namespace RuneSkin.PlayerHud
{
    // Party frame (family SummonFrames; RuneSkin 0.20.3 rework, PoE style = user pick v3 column B,
    // docs\mockups\party-frame\v3-poe-{3,12,40}.png / v3-zoom-states.png): a top-left grid of 44 px icons, one per
    // creature type + state with the unit count in the corner, under each a thin light-blue energy-shield bar over a
    // thin life bar. Dead = darkened icon + clockwise radial sweep + "0:45"; no reserve = violet eitr glyph; off = grey +
    // minus; spawning / respawning = teal ring + refresh glyph; badges: stay (gold, top-left), Defensive / Aggressive
    // (top-right); pets = green frame + green life bar. With the inventory open, hovering an icon shows a tooltip
    // (name x N, total health + shield, weakest unit). Vanilla has no such element.
    // Sources:
    // - MorgottTweaks permanent summons: player ZDO mt_sum_ui (written by the summoner's client on every change:
    //   int version 1, int count, per slot int id, int prefab hash, byte state (SummonState), byte weight, byte flags
    //   (bit 0 stay, bit 1 Defensive, bit 2 Aggressive), float hp fraction, float ES fraction, double respawn time
    //   (ZNet.GetTimeSeconds), int generation, ZDOID body; MorgottTweaks SummonSlotsModule.MirrorUi). A loaded body of the
    //   slot's generation (body ZDO mt_sum_gen) gives live health and its ZDO ES (mt_sum_es / mt_sum_esmax).
    // - Pets: loaded creatures whose ZDO long mt_pet_owner = the local player id (not in the slot list).
    // - Without mt_sum_ui: the loaded creatures that are this player's summons (vanilla's own test,
    //   Tameable.UnsummonMaxInstances, Tameable.cs:641-701: tamed, following this player by name; a summon prefab =
    //   unsummon distance or logout timer, Tameable.cs:30-32; a mt_sum_owner stamp of another player excludes it).
    // Placement: child of Hud.m_rootObject (hidden with the HUD; Hud.SetVisible moves that root off screen), in the
    // screen's top-left corner (user 2026-10-26): size.hud.party.x / .y reference px of a 1080p screen from the safe
    // area's top-left, the whole grid scaled by scale.hud.party (2: the 44 px mockup tile read too small live) and with
    // the screen height; left of the minimap, above the chat; the top-left notification moves below it (MessageSkin
    // ClearPartyFrame); 9 per row. Shown only while a hand holds a summoning weapon (HoldsSummoner; user 2026-10-26:
    // no summon weapon, no icon) and the large map is closed. Data refreshed 4x a second, sweep / timers / hover every
    // frame.
    public sealed class SummonFrames : MonoBehaviour
    {
        public const string RootName = "RuneSkin_SummonFrames";
        private const float Refresh = 0.25f;
        public const int MaxStacks = 45;

        private static readonly int SlotsKey = "mt_sum_ui".GetStableHashCode();
        private static readonly int GenKey = "mt_sum_gen".GetStableHashCode();
        private static readonly int EsKey = "mt_sum_es".GetStableHashCode();
        private static readonly int EsMaxKey = "mt_sum_esmax".GetStableHashCode();
        private static readonly int OwnerKey = "mt_sum_owner".GetStableHashCode();
        private static readonly int PetOwnerKey = "mt_pet_owner".GetStableHashCode();

        private sealed class Tile
        {
            public RectTransform Root, Icon, EsBar, EsFill, LifeFill;
            public Image Socket, IconImg, Dim, Sweep, Glyph, Stay, Mode, LifeFillImg;
            public Image[] Ring;
            public TMP_Text Letter, Timer, Count;
            public int Prefab = int.MinValue;
            public bool Pet;
        }

        private readonly HudArt _art = new HudArt("SummonFrames");
        private readonly List<PartyUnit> _units = new List<PartyUnit>();
        private List<PartyGroup> _stacks = new List<PartyGroup>();
        private readonly List<Tile> _tiles = new List<Tile>();
        private readonly Dictionary<long, double> _deathTotals = new Dictionary<long, double>();
        private static readonly Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();
        private static Sprite _white;
        private Hud _hud;
        private RectTransform _rt, _grid, _tip;
        private TMP_Text _tipText;
        private float _timer;
        private string _last;
        private int _hover = -1;
        private bool _armed;
        private readonly Vector3[] _corners = new Vector3[4];

        public static SummonFrames Instance { get; private set; }
        public static string Describe { get; private set; } = "";

        public static void Attach(Hud hud)
        {
            var parent = hud && hud.m_rootObject ? hud.m_rootObject.transform : null;
            if (!parent || parent.Find(RootName)) return;
            var rt = new GameObject(RootName, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            var f = rt.gameObject.AddComponent<SummonFrames>();
            f._hud = hud;
            f._rt = rt;
            f._grid = HudArt.Rect("Grid", rt);
            f._grid.anchorMin = f._grid.anchorMax = f._grid.pivot = new Vector2(0f, 1f);
            f.MakeTooltip();
            Instance = f;
            Plugin.Log.LogInfo("[RuneSkin] party frame attached under the HUD root");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private static float F(string token, float fallback) => ThemeTokens.GetFloat(token, fallback);
        private float TileSize => F("size.hud.party.tile", 44f);
        private float Gap => F("size.hud.party.gap", 6f);
        private float RowGap => F("size.hud.party.row.gap", 6f);
        private float EsH => F("size.hud.party.es", 3f);
        private float LifeH => F("size.hud.party.life", 4f);
        private int Columns => Mathf.Max(1, Mathf.RoundToInt(F("count.hud.party.columns", 9f)));
        private float BlockH => PartyMath.BlockHeight(TileSize, EsH, LifeH);

        private void Update()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas) _art.SyncPpu(canvas.rootCanvas.referencePixelsPerUnit);
            var mapOpen = Minimap.instance && Minimap.instance.m_mode == Minimap.MapMode.Large;
            _armed = HoldsSummoner(Player.m_localPlayer);
            var show = _armed && !mapOpen;
            if (_grid.gameObject.activeSelf != show) _grid.gameObject.SetActive(show);
            _timer -= Time.unscaledDeltaTime;
            if (_timer <= 0f)
            {
                _timer = Refresh;
                Collect();
                for (var i = 0; i < _stacks.Count; i++)
                {
                    if (i == _tiles.Count) _tiles.Add(MakeTile(i));
                    Fill(_tiles[i], _stacks[i]);
                }
                for (var i = _stacks.Count; i < _tiles.Count; i++)
                    if (_tiles[i].Root.gameObject.activeSelf) _tiles[i].Root.gameObject.SetActive(false);
                Log();
            }
            Place();
            var now = ZNet.instance ? ZNet.instance.GetTimeSeconds() : 0d;
            var pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * F("rate.hud.party.pulse", 1f));
            for (var i = 0; i < _stacks.Count && i < _tiles.Count; i++) Animate(_tiles[i], _stacks[i], now, pulse);
            Hover(canvas, !show);
        }

        // ---- visibility

        // A hand item whose attack summons a creature (Humanoid.RightItem / LeftItem, Humanoid.cs:149-151). Cached per
        // SharedData (item definitions are shared and fixed after ObjectDB setup).
        private static readonly Dictionary<ItemDrop.ItemData.SharedData, bool> SummonerCache = new Dictionary<ItemDrop.ItemData.SharedData, bool>();

        public static bool HoldsSummoner(Humanoid h) => h && (IsSummoner(h.RightItem) || IsSummoner(h.LeftItem));

        public static bool IsSummoner(ItemDrop.ItemData item)
        {
            var s = item?.m_shared;
            if (s == null) return false;
            if (SummonerCache.TryGetValue(s, out var known)) return known;
            var result = SummonsCreature(s.m_spawnOnHit) || AttackSummons(s.m_attack) || AttackSummons(s.m_secondaryAttack);
            SummonerCache[s] = result;
            return result;
        }

        // Every object an attack instantiates and sets up as an IProjectile: the attack projectile (Attack.cs:887,
        // FireProjectileBurst), its Projectile.m_spawnOnHit (Projectile.cs:109 / 804-808), the spawn-on-trigger
        // (Attack.cs:135, instantiated :1091-1093 / :1502-1504) and the chance spawn-on-hit (Attack.cs:999-1001 /
        // :1581-1583). Same test as MorgottTweaks EpicLootItemRequirements.AttackSummons (summon affix gating).
        private static bool AttackSummons(Attack a)
        {
            if (a == null) return false;
            var p = a.m_attackProjectile;
            if (SummonsCreature(p)) return true;
            var proj = p ? p.GetComponent<Projectile>() : null;
            if (proj && SummonsCreature(proj.m_spawnOnHit)) return true;
            if (SummonsCreature(a.m_spawnOnTrigger)) return true;
            return a.m_spawnOnHitChance > 0f && SummonsCreature(a.m_spawnOnHit);
        }

        // A SpawnAbility (on the object or a child, Attack.cs:1583 GetComponentInChildren; SpawnAbility.cs:30
        // m_spawnPrefab) spawning a tameable Character (Dead Raiser, Troll staff, mod summon staffs). A ward (Staff of
        // Protection) spawns no Character.
        private static bool SummonsCreature(GameObject go)
        {
            if (!go) return false;
            var ability = go.GetComponentInChildren<SpawnAbility>(true);
            if (!ability || ability.m_spawnPrefab == null) return false;
            foreach (var prefab in ability.m_spawnPrefab)
                if (prefab && prefab.GetComponent<Character>() && prefab.GetComponent<Tameable>())
                    return true;
            return false;
        }

        // Bottom edge of the shown grid in screen px (bottom-left origin) for MessageSkin; false when nothing is shown.
        public static bool GridScreenBottom(out float y)
        {
            y = 0f;
            var f = Instance;
            if (!f || !f._grid || !f._grid.gameObject.activeInHierarchy || f._stacks.Count == 0) return false;
            var canvas = f.GetComponentInParent<Canvas>();
            var cam = canvas && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            f._grid.GetWorldCorners(f._corners);
            y = Mathf.Min(RectTransformUtility.WorldToScreenPoint(cam, f._corners[0]).y, RectTransformUtility.WorldToScreenPoint(cam, f._corners[2]).y);
            return true;
        }

        // ---- data

        private void Collect()
        {
            _units.Clear();
            var player = Player.m_localPlayer;
            if (player && !player.IsDead())
            {
                if (!ReadSlots(player)) ReadLoaded(player);
                ReadPets(player);
            }
            _stacks = PartyMath.Group(_units);
            if (_stacks.Count > MaxStacks) _stacks.RemoveRange(MaxStacks, _stacks.Count - MaxStacks);
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
                var u = new PartyUnit { Id = pkg.ReadInt(), Prefab = pkg.ReadInt(), State = (SummonState)pkg.ReadByte() };
                pkg.ReadByte(); // weight
                u.Flags = pkg.ReadByte();
                u.Hp = SummonMath.Ratio(pkg.ReadSingle(), 1f);
                u.Es = SummonMath.Ratio(pkg.ReadSingle(), 1f);
                u.CdUntil = pkg.ReadDouble();
                var gen = pkg.ReadInt();
                var body = Body(pkg.ReadZDOID(), gen);
                var baseHp = BaseHealth(u.Prefab);
                u.HpMax = baseHp;
                u.HpCur = u.Hp * baseHp;
                if (body) Live(body, ref u);
                _units.Add(u);
            }
            return true;
        }

        private static float BaseHealth(int prefab)
        {
            var go = ZNetScene.instance ? ZNetScene.instance.GetPrefab(prefab) : null;
            return go && go.TryGetComponent<Character>(out var c) ? c.m_health : 0f;
        }

        private static Character Body(ZDOID id, int generation)
        {
            if (id.IsNone() || !ZNetScene.instance) return null;
            var go = ZNetScene.instance.FindInstance(id);
            if (!go || !go.TryGetComponent<Character>(out var c) || c.IsDead() || !c.m_nview || c.m_nview.GetZDO() == null) return null;
            return c.m_nview.GetZDO().GetInt(GenKey) == generation ? c : null;
        }

        private static void Live(Character c, ref PartyUnit u)
        {
            var zdo = c.m_nview.GetZDO();
            u.HpCur = c.GetHealth();
            u.HpMax = c.GetMaxHealth();
            u.Hp = SummonMath.Ratio(u.HpCur, u.HpMax);
            var max = zdo.GetFloat(EsMaxKey);
            u.EsCur = max > 0f ? Mathf.Max(0f, zdo.GetFloat(EsKey)) : 0f;
            u.Es = SummonMath.Ratio(u.EsCur, max);
        }

        private void ReadLoaded(Player player)
        {
            var id = player.GetPlayerID();
            var playerName = player.GetPlayerName();
            foreach (var c in Character.GetAllCharacters())
            {
                if (!c || c.IsPlayer() || c.IsDead() || !c.m_nview || c.m_nview.GetZDO() == null || !IsSummonOf(c, player, id, playerName)) continue;
                if (c.m_nview.GetZDO().GetLong(PetOwnerKey) != 0L) continue;
                var u = new PartyUnit { Id = c.GetInstanceID(), Prefab = c.m_nview.GetZDO().GetPrefab(), State = SummonState.Alive };
                Live(c, ref u);
                _units.Add(u);
            }
        }

        private void ReadPets(Player player)
        {
            var id = player.GetPlayerID();
            foreach (var c in Character.GetAllCharacters())
            {
                if (!c || c.IsPlayer() || c.IsDead() || !c.m_nview || c.m_nview.GetZDO() == null) continue;
                var zdo = c.m_nview.GetZDO();
                if (zdo.GetLong(PetOwnerKey) != id) continue;
                var u = new PartyUnit { Id = c.GetInstanceID(), Prefab = zdo.GetPrefab(), Pet = true, State = SummonState.Alive };
                Live(c, ref u);
                _units.Add(u);
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

        // ---- layout

        // Top-left at (x, y) reference px of a 1080p screen from the safe area's top-left, scaled by screen height / 1080
        // (the root's local units are screen px / its lossy scale on an overlay canvas); tiles x scale.hud.party.
        private void Place()
        {
            var parent = _rt.parent as RectTransform;
            if (!parent) return;
            var px = Screen.height / 1080f;
            var ls = Mathf.Abs(parent.lossyScale.x);
            var canvas = parent.GetComponentInParent<Canvas>();
            if (canvas && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay && canvas.rootCanvas.scaleFactor > 0f)
                ls = canvas.rootCanvas.scaleFactor * Mathf.Abs(parent.lossyScale.x) / Mathf.Max(1e-6f, Mathf.Abs(canvas.rootCanvas.transform.lossyScale.x));
            if (!(ls > 0f)) return;
            var k = px / ls;
            var safe = Screen.safeArea;
            PartyMath.Origin(F("size.hud.party.x", 16f), F("size.hud.party.y", 16f), k, safe.xMin, Screen.height - safe.yMax, 1f / ls, out var ox, out var oy);
            var want = new Vector2(ox, oy);
            if ((_rt.anchoredPosition - want).sqrMagnitude > 0.01f) _rt.anchoredPosition = want;
            var g = Mathf.Max(0.1f, F("scale.hud.party", 2f));
            var scale = new Vector3(k * g, k * g, 1f);
            if (_rt.localScale != scale) _rt.localScale = scale;
            // The tooltip keeps its own reference size (a child of the scaled root).
            var tipScale = new Vector3(1f / g, 1f / g, 1f);
            if (_tip.localScale != tipScale) _tip.localScale = tipScale;
            PartyMath.GridSize(_stacks.Count, Columns, TileSize, Gap, BlockH, RowGap, out var w, out var h);
            var size = new Vector2(w, h);
            if (_rt.sizeDelta != size) { _rt.sizeDelta = size; _grid.sizeDelta = size; }
        }

        private Tile MakeTile(int index)
        {
            var size = TileSize;
            var t = new Tile();
            t.Root = HudArt.Rect("Party" + index, _grid);
            t.Root.anchorMin = t.Root.anchorMax = t.Root.pivot = new Vector2(0f, 1f);
            t.Root.sizeDelta = new Vector2(size, BlockH);

            t.Socket = _art.Image("Socket", t.Root, "sprite.hud.socket.hotbar", "color.frame", Image.Type.Simple);
            TopLeft(t.Socket.rectTransform, 0f, 0f, size, size);
            var inset = Mathf.Round(size * F("share.hud.party.icon.inset", 0.16f));
            var inner = size - inset * 2f;
            t.IconImg = _art.Image("Icon", t.Root, null, "color.frame", Image.Type.Simple);
            t.Icon = t.IconImg.rectTransform;
            TopLeft(t.Icon, inset, inset, inner, inner);
            t.IconImg.preserveAspect = true;
            t.Letter = _art.Text("Letter", t.Icon, "font.body.bold", "color.hud.text", F("size.hud.party.letter", 20f), TextAlignmentOptions.Center);
            Stretch(t.Letter.rectTransform);
            t.Dim = _art.Image("Dim", t.Root, null, "color.hud.clear", Image.Type.Simple);
            t.Dim.color = Color.black;
            TopLeft(t.Dim.rectTransform, inset, inset, inner, inner);

            // Respawn sweep over the whole socket (mockup: sweep(ir.Inset(-7))), dark wedge clockwise from 12 o'clock.
            t.Sweep = _art.Image("Sweep", t.Root, null, "color.hud.clear", Image.Type.Simple);
            t.Sweep.sprite = White();
            t.Sweep.type = Image.Type.Filled;
            t.Sweep.fillMethod = Image.FillMethod.Radial360;
            t.Sweep.fillOrigin = (int)Image.Origin360.Top;
            t.Sweep.fillClockwise = true;
            t.Sweep.color = new Color(0f, 0f, 0f, F("alpha.hud.party.sweep", 0.7f));
            TopLeft(t.Sweep.rectTransform, inset - 7f, inset - 7f, inner + 14f, inner + 14f);

            // Teal ring 3 px outside the portrait (spawning / respawning), pulsing.
            var ring = F("size.hud.party.ring", 3f);
            t.Ring = new Image[4];
            for (var i = 0; i < 4; i++)
            {
                t.Ring[i] = _art.Image("Ring" + i, t.Root, null, "color.text.accent", Image.Type.Simple);
                t.Ring[i].sprite = White();
            }
            var o = inset - ring;
            var span = inner + ring * 2f;
            TopLeft(t.Ring[0].rectTransform, o, o, span, ring);
            TopLeft(t.Ring[1].rectTransform, o, o + span - ring, span, ring);
            TopLeft(t.Ring[2].rectTransform, o, o, ring, span);
            TopLeft(t.Ring[3].rectTransform, o + span - ring, o, ring, span);

            t.Glyph = _art.Image("Glyph", t.Root, null, "color.text.accent", Image.Type.Simple);
            t.Glyph.preserveAspect = true;
            t.Timer = _art.Text("Timer", t.Root, "font.body.bold", "color.hud.text", F("size.hud.party.timer", 16f), TextAlignmentOptions.Center);
            TopLeft(t.Timer.rectTransform, inset, inset - 3f, inner, inner);
            Outline(t.Timer);

            var badge = F("size.hud.party.badge", 12f);
            t.Stay = _art.Image("Stay", t.Root, "sprite.glyph.stop", "color.text.active", Image.Type.Simple);
            t.Stay.preserveAspect = true;
            TopLeft(t.Stay.rectTransform, inset - 1f, inset - 1f, badge - 1f, badge - 1f);
            t.Mode = _art.Image("Mode", t.Root, "sprite.glyph.sub.shield", "color.text.accent", Image.Type.Simple);
            t.Mode.preserveAspect = true;
            TopLeft(t.Mode.rectTransform, inset + inner - badge + 1f, inset - 1f, badge, badge);

            t.Count = _art.Text("Count", t.Root, "font.body.bold", "color.hud.text", F("size.hud.party.count", 16f), TextAlignmentOptions.BottomRight);
            TopLeft(t.Count.rectTransform, inset, inset, inner + 3f, inner + 6f);
            Outline(t.Count);

            var barW = size - 4f;
            var esTrough = _art.Image("Es", t.Root, null, "color.hud.trough", Image.Type.Simple);
            t.EsBar = esTrough.rectTransform;
            TopLeft(t.EsBar, 2f, size + 2f, barW, EsH);
            t.EsFill = _art.Image("fill", t.EsBar, null, "color.hud.es", Image.Type.Simple).rectTransform;
            FillRect(t.EsFill);
            var life = _art.Image("Life", t.Root, null, "color.hud.trough", Image.Type.Simple).rectTransform;
            TopLeft(life, 2f, size + 2f + EsH + 1f, barW, LifeH);
            t.LifeFillImg = _art.Image("fill", life, null, "color.hud.health", Image.Type.Simple);
            t.LifeFill = t.LifeFillImg.rectTransform;
            FillRect(t.LifeFill);

            PartyMath.Cell(index, Columns, size, Gap, BlockH, RowGap, out var x, out var y);
            t.Root.anchoredPosition = new Vector2(x, -y);
            return t;
        }

        private void Fill(Tile t, PartyGroup s)
        {
            if (!t.Root.gameObject.activeSelf) t.Root.gameObject.SetActive(true);
            if (t.Prefab != s.Prefab || t.Pet != s.Pet)
            {
                t.Prefab = s.Prefab;
                t.Pet = s.Pet;
                var prefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(s.Prefab) : null;
                var icon = prefab ? IconFor(prefab) : null;
                if (!icon) icon = ThemeTokens.GetSprite("sprite.glyph.boss"); // generic creature
                t.IconImg.sprite = icon;
                t.IconImg.enabled = icon;
                t.Letter.gameObject.SetActive(!icon);
                if (!icon)
                {
                    var n = prefab && prefab.TryGetComponent<Character>(out var c) ? Localization.instance.Localize(c.m_name) : null;
                    t.Letter.text = string.IsNullOrEmpty(n) ? "?" : n.Substring(0, 1).ToUpperInvariant();
                }
                var ally = ThemeTokens.GetColor("color.hud.ally", Color.green);
                t.Socket.color = s.Pet ? Color.Lerp(Color.white, ally, F("share.hud.party.pet.tint", 0.55f)) : ThemeTokens.GetColor("color.frame", Color.white);
                t.LifeFillImg.color = s.Pet ? ally : ThemeTokens.GetColor("color.hud.health", Color.red);
            }
            var state = s.State;
            var body = PartyMath.HasBody(state);
            var inertTint = ThemeTokens.GetColor("color.hud.inert", Color.gray);
            t.IconImg.color = state == SummonState.Off ? inertTint : Color.white;
            // Darkening per state (mockup stateInside: dim alpha 0.35 dead, 0.4 no reserve, 0.6 off, 0.7 coming back).
            var dim = state == SummonState.Dead ? F("alpha.hud.party.dim.dead", 0.3f)
                : state == SummonState.NoReserve ? F("alpha.hud.party.dim.noreserve", 0.45f)
                : state == SummonState.Off ? F("alpha.hud.party.dim.off", 0.6f)
                : state == SummonState.Waiting ? F("alpha.hud.party.dim.waiting", 0.5f) : 0f;
            Show(t.Dim, dim > 0f);
            t.Dim.color = new Color(0f, 0f, 0f, dim);
            Show(t.Sweep, state == SummonState.Dead);
            Show(t.Timer, state == SummonState.Dead);
            foreach (var r in t.Ring) Show(r, state == SummonState.Waiting);
            string glyph = null;
            var glyphColor = "color.text.accent";
            var glyphSize = 20f;
            switch (state)
            {
                case SummonState.NoReserve: glyph = "sprite.hud.glyph.eitr"; glyphColor = "color.hud.eitr"; glyphSize = 24f; break;
                case SummonState.Off: glyph = "sprite.glyph.minus"; glyphColor = "color.hud.inert"; glyphSize = 22f; break;
                case SummonState.Waiting: glyph = "sprite.glyph.refresh"; break;
            }
            var sprite = glyph != null ? ThemeTokens.GetSprite(glyph) : null;
            Show(t.Glyph, sprite);
            if (sprite)
            {
                t.Glyph.sprite = sprite;
                t.Glyph.color = ThemeTokens.GetColor(glyphColor, Color.white);
                var size = TileSize;
                TopLeft(t.Glyph.rectTransform, (size - glyphSize) / 2f, (size - glyphSize) / 2f, glyphSize, glyphSize);
            }
            Show(t.Stay, s.Stay && t.Stay.sprite);
            var modeSprite = s.Mode == 1 ? ThemeTokens.GetSprite("sprite.glyph.sub.shield") : s.Mode == 2 ? ThemeTokens.GetSprite("sprite.glyph.sub.swords") : null;
            Show(t.Mode, modeSprite);
            if (modeSprite)
            {
                t.Mode.sprite = modeSprite;
                t.Mode.color = ThemeTokens.GetColor(s.Mode == 1 ? "color.text.accent" : "color.text.missing", Color.white);
            }
            var count = s.Count > 1 ? s.Count.ToString() : "";
            if (t.Count.text != count) t.Count.text = count;
            Show(t.Count, s.Count > 1);
            var es = body && s.Es > 0f;
            if (t.EsBar.gameObject.activeSelf != es) t.EsBar.gameObject.SetActive(es);
            t.EsFill.anchorMax = new Vector2(s.Es, 1f);
            Show(t.LifeFillImg, body);
            t.LifeFill.anchorMax = new Vector2(body ? s.Hp : 0f, 1f);
        }

        // Every frame: sweep + timer of dead stacks (soonest respawn of the stack), ring pulse.
        private void Animate(Tile t, PartyGroup s, double now, float pulse)
        {
            if (s.State == SummonState.Dead)
            {
                var remaining = s.NextCd - now;
                var key = DeathKey(s);
                if (!_deathTotals.TryGetValue(key, out var total) || remaining > total)
                    _deathTotals[key] = total = PartyMath.SweepTotal(remaining, F("time.hud.party.respawn", 30f));
                t.Sweep.fillAmount = PartyMath.SweepFraction(remaining, total);
                var text = PartyMath.Timer(remaining);
                if (t.Timer.text != text) t.Timer.text = text;
            }
            else if (s.State == SummonState.Waiting)
            {
                var a = ThemeTokens.GetColor("color.text.accent", Color.cyan);
                a.a *= pulse;
                foreach (var r in t.Ring)
                    if (r.color != a) r.color = a;
            }
            if (_deathTotals.Count > 64) _deathTotals.Clear();
        }

        // A death = (first dead unit of the stack, its respawn time).
        private static long DeathKey(PartyGroup s)
        {
            foreach (var u in s.Units)
                if (u.State == SummonState.Dead && u.CdUntil <= s.NextCd)
                    return ((long)u.Id << 32) ^ (long)(s.NextCd * 10d);
            return 0L;
        }

        // ---- tooltip (inventory open)

        private void MakeTooltip()
        {
            var plate = _art.Image("Tooltip", _rt, "sprite.hud.frame.small", "color.frame", Image.Type.Sliced);
            _tip = plate.rectTransform;
            _tip.anchorMin = _tip.anchorMax = _tip.pivot = new Vector2(0f, 1f);
            _tipText = _art.Text("Text", _tip, "font.body", "color.hud.text", F("size.hud.party.tooltip.font", 16f), TextAlignmentOptions.TopLeft);
            _tipText.richText = true;
            _tipText.rectTransform.anchorMin = Vector2.zero;
            _tipText.rectTransform.anchorMax = Vector2.one;
            var pad = F("size.hud.party.tooltip.pad", 16f);
            _tipText.rectTransform.offsetMin = new Vector2(pad, 7f);
            _tipText.rectTransform.offsetMax = new Vector2(-pad, -7f);
            _tip.gameObject.SetActive(false);
        }

        private void Hover(Canvas canvas, bool mapOpen)
        {
            var hover = -1;
            if (!mapOpen && InventoryGui.IsVisible())
            {
                var cam = canvas && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
                Vector2 mouse = ZInput.pointerPosition;
                for (var i = 0; i < _stacks.Count && i < _tiles.Count; i++)
                    if (RectTransformUtility.RectangleContainsScreenPoint(_tiles[i].Root, mouse, cam)) { hover = i; break; }
            }
            if (hover < 0)
            {
                if (_tip.gameObject.activeSelf) _tip.gameObject.SetActive(false);
                _hover = -1;
                return;
            }
            var s = _stacks[hover];
            var text = TooltipText(s);
            if (_tipText.text != text) _tipText.text = text;
            var pad = F("size.hud.party.tooltip.pad", 16f);
            var lines = text.Split('\n').Length;
            var size = new Vector2(_tipText.GetPreferredValues(text).x + pad * 2f, 14f + F("size.hud.party.tooltip.line", 22f) * lines);
            if (_tip.sizeDelta != size) _tip.sizeDelta = size;
            PartyMath.Cell(hover, Columns, TileSize, Gap, BlockH, RowGap, out var x, out var y);
            _tip.anchoredPosition = new Vector2(x + TileSize + 6f, -(y + 6f));
            _tip.SetAsLastSibling();
            if (!_tip.gameObject.activeSelf) _tip.gameObject.SetActive(true);
            _hover = hover;
        }

        private static string Hex(string token, Color fallback) => ColorUtility.ToHtmlStringRGBA(ThemeTokens.GetColor(token, fallback));

        public static string TooltipText(PartyGroup s)
        {
            var prefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(s.Prefab) : null;
            var title = prefab && prefab.TryGetComponent<Character>(out var c) ? Localization.instance.Localize(c.m_name) : prefab ? prefab.name : "?";
            var sb = new StringBuilder();
            sb.Append($"<color=#{Hex("color.text.active", Color.yellow)}>{title}{(s.Count > 1 ? " ×" + s.Count : "")}</color>");
            if (PartyMath.HasBody(s.State))
            {
                sb.Append($"\n{Mathf.CeilToInt(s.HpCur)}/{Mathf.CeilToInt(s.HpMax)}");
                if (s.EsCur > 0f) sb.Append($"<color=#{Hex("color.text.accent", Color.cyan)}> +{Mathf.FloorToInt(s.EsCur)}</color>");
                if (s.Count > 1 && s.Weakest >= 0f) sb.Append("\n" + string.Format(Localization.instance.Localize("$runeskin_party_weakest"), Mathf.CeilToInt(s.Weakest)));
            }
            var now = ZNet.instance ? ZNet.instance.GetTimeSeconds() : 0d;
            var key = SummonMath.StatusKey(s.State, s.Stay, s.NextCd, now, out var secs);
            var line = key == null ? "" : Localization.instance.Localize("$" + key);
            if (secs >= 0) line = string.Format(line, secs);
            if (s.Mode != 0) line = (line.Length > 0 ? line + ", " : "") + Localization.instance.Localize(s.Mode == 1 ? "$runeskin_party_defensive" : "$runeskin_party_aggressive");
            if (line.Length > 0) sb.Append("\n" + line);
            return sb.ToString();
        }

        // ---- diagnostics

        private void Log()
        {
            var d = string.Join(" ", _stacks.ConvertAll(s => $"[{PrefabName(s.Prefab)}{(s.Pet ? " pet" : "")} {s.State} x{s.Count} hp={s.Hp:0.00} es={s.Es:0.00}]"));
            if (d == _last) return;
            _last = d;
            Describe = $"{_stacks.Count} stack(s) {_units.Count} unit(s) {d}";
            Plugin.Log.LogInfo($"[RuneSkin] party frame: {Describe}");
        }

        private static string PrefabName(int hash)
        {
            var go = ZNetScene.instance ? ZNetScene.instance.GetPrefab(hash) : null;
            return go ? go.name : hash.ToString();
        }

        // Header: stacks, units, armed (a summoning weapon in hand), grid shown, hover, tooltip. One line per icon
        // (harness): index, prefab, pet, state, count, fills, shown parts and the icon's screen rect (x,y,w,h bottom-left origin, socket + bars). Machine-read: numbers in the invariant culture ("0.50", not the
        // game culture's "0,50" - run 20261024-192847, ru-RU: every fill parsed as NaN by the harness).
        public static string DebugState()
        {
            var thread = System.Threading.Thread.CurrentThread;
            var culture = thread.CurrentCulture;
            thread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            try { return DebugStateText(); }
            finally { thread.CurrentCulture = culture; }
        }

        private static string DebugStateText()
        {
            var f = Instance;
            if (!f) return "no party frame";
            var sb = new StringBuilder();
            var canvas = f.GetComponentInParent<Canvas>();
            var cam = canvas && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            var c = new Vector3[4];
            sb.Append($"stacks={f._stacks.Count} units={f._units.Count} armed={(f._armed ? 1 : 0)} grid={f._grid.gameObject.activeInHierarchy} hover={f._hover} tip='{(f._tip.gameObject.activeInHierarchy ? f._tipText.text.Replace('\n', '|').Replace('\'', '`') : "-")}'");
            for (var i = 0; i < f._stacks.Count && i < f._tiles.Count; i++)
            {
                var s = f._stacks[i];
                var t = f._tiles[i];
                t.Root.GetWorldCorners(c);
                Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, c[0]), b = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
                bool On(Component g) => g && g.gameObject.activeSelf && (!(g is Behaviour be) || be.enabled);
                sb.Append($"\n{i} prefab={PrefabName(s.Prefab)} pet={(s.Pet ? 1 : 0)} state={s.State} n={s.Count} countText='{(On(t.Count) ? t.Count.text : "")}'" +
                    $" hp={s.Hp:0.00} es={s.Es:0.00} life={(On(t.LifeFillImg) ? t.LifeFill.anchorMax.x : -1f):0.00} esBar={(t.EsBar.gameObject.activeSelf ? t.EsFill.anchorMax.x : -1f):0.00}" +
                    $" icon={(On(t.IconImg) && t.IconImg.sprite ? t.IconImg.sprite.name : "-")} glyph={(On(t.Glyph) && t.Glyph.sprite ? t.Glyph.sprite.name : "-")}" +
                    $" sweep={(On(t.Sweep) ? t.Sweep.fillAmount : -1f):0.00} timer='{(On(t.Timer) ? t.Timer.text : "")}' ring={(On(t.Ring[0]) ? 1 : 0)}" +
                    $" stay={(On(t.Stay) ? 1 : 0)} mode={(On(t.Mode) && t.Mode.sprite ? t.Mode.sprite.name : "-")} socket={ColorUtility.ToHtmlStringRGB(t.Socket.color)}" +
                    $" rect={a.x:0},{a.y:0},{b.x - a.x:0},{b.y - a.y:0}");
            }
            return sb.ToString();
        }

        // ---- icons

        // The creature's trophy (the only per-creature art: trophies panel, Compendium): its own CharacterDrop trophy,
        // else ObjectDB "Trophy<Name>" / "Trophy<Name before _>", else the trophy of the creature whose m_name is the
        // longest prefix of its own ("$enemy_skeleton" of "$enemy_skeleton_summoned": summon variants drop nothing).
        // None: a generic glyph (Fill).
        public static Sprite IconFor(GameObject creature)
        {
            var key = Utils.GetPrefabName(creature);
            if (Icons.TryGetValue(key, out var icon)) return icon;
            icon = Trophy(creature) ?? DbTrophy("Trophy" + key);
            var cut = key.IndexOf('_');
            if (!icon && cut > 0) icon = DbTrophy("Trophy" + key.Substring(0, cut));
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

        private static Sprite DbTrophy(string name)
        {
            var item = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(name) : null;
            var shared = item && item.TryGetComponent<ItemDrop>(out var d) ? d.m_itemData.m_shared : null;
            return shared != null && shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy && shared.m_icons.Length > 0 ? shared.m_icons[0] : null;
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

        // ---- helpers

        private static Sprite White()
        {
            if (_white) return _white;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "RuneSkin_PartyWhite" };
            var px = new Color32[16];
            for (var i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            _white = Sprite.Create(tex, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 100f);
            _white.name = "RuneSkin_PartyWhite";
            return _white;
        }

        private static void Show(Behaviour g, bool on)
        {
            if (g && g.enabled != on) g.enabled = on;
        }

        private static void Outline(TMP_Text t)
        {
            t.outlineWidth = 0.25f;
            t.outlineColor = ThemeTokens.GetColor("color.text.outline", Color.black);
        }

        // x right / y down from the parent's top-left, reference px.
        private static void TopLeft(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static void FillRect(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }

    // Installed only when RuneSkin owns SummonFrames (Plugin).
    [HarmonyLib.HarmonyPatch]
    public static class SummonFramesAdapter
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly), batched by FastStartup).
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.SummonFrames);

        [HarmonyLib.HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
        [HarmonyLib.HarmonyPostfix]
        private static void Awake_Postfix(Hud __instance) => SummonFrames.Attach(__instance);
    }
}
