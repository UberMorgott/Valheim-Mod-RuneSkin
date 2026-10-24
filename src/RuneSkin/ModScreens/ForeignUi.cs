using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RuneSkin.Api;
using RuneSkin.Classify;
using RuneSkin.Diagnostics;
using RuneSkin.GameScreens;
using RuneSkin.Ownership;
using RuneSkin.PreWorld;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.ModScreens
{
    // Universal fallback restyler (family ModUi, M7): every uGUI / TMP window that no RuneSkin family owns gets the
    // theme through the same role pass as the vanilla forms (GameScreenKit.Form: vanilla panel art -> panel stack,
    // controls by their component wiring, theme fonts, button labels fitted, TextInset / CellFit layout rules), with no
    // per-mod code. Visual properties only: sizes and positions of the mod's own rects stay.
    // What is foreign (ForeignMath.Step, cached per transform): the topmost object below the vanilla boundary (the
    // vanilla UI singletons and every family root, with their ancestors) that carries another plugin's MonoBehaviour and
    // none of the game's, or that Jotunn's GUIManager built (JotunnHook anchors). Inside a family root only anchors and
    // the precise adapters' types (ForeignRoles.Hints: Epic Loot enchanting table / board / tempering, mod HUD parts)
    // start a unit; the family stays responsible for the rest. Opt-out (config Ownership.ModUiOptOut): object or
    // assembly names; RuneSkin's own objects (RuneSkin*) and world-space canvases are never touched.
    // Event driven: a postfix on Graphic.OnEnable / TextMeshProUGUI.OnEnable queues the graphic; LateUpdate classifies
    // each new graphic once (instance id), marks its unit dirty and restyles dirty units (a unit gets a RootMark, so
    // the passes of the vanilla family above skip it). Nothing runs in a frame without new graphics.
    public sealed class ForeignUi : MonoBehaviour
    {
        public const string ScreenPrefix = "ModUi";
        private static ForeignUi _instance;
        private static bool _on, _generic;
        private static HashSet<string> _optOut = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<Graphic> Pending = new HashSet<Graphic>();
        private static readonly Dictionary<Graphic, int> Retry = new Dictionary<Graphic, int>();
        private static readonly List<Graphic> RetryWork = new List<Graphic>();
        private static readonly List<Graphic> Work = new List<Graphic>(256);
        // Graphics already looked at / transforms already judged. Every damage number, tooltip and mod popup passes
        // through here: destroyed keys leave whenever a table doubled since its last prune (perf audit B1; the old
        // instance-id set only cleared itself at 200000 entries).
        private static readonly HashSet<Graphic> Decided = new HashSet<Graphic>();
        private static int _decidedPrune = 1024, _cachePrune = 1024, _unitsPrune = Bounded.MinPrune, _anchorsPrune = Bounded.MinPrune;
        private static readonly Predicate<Transform> TransformDead = t => !t;
        private static readonly List<Transform> DeadTransforms = new List<Transform>();
        private static readonly Func<Transform, bool> TransformAlive = t => t;
        private static readonly Predicate<Graphic> GraphicDead = g => !g;
        private static readonly Dictionary<Transform, Entry> Cache = new Dictionary<Transform, Entry>();
        private static readonly HashSet<Transform> Boundary = new HashSet<Transform>();
        private static readonly HashSet<Transform> Anchors = new HashSet<Transform>();
        private static readonly List<Transform> NewAnchors = new List<Transform>();
        private static readonly Dictionary<Transform, string> Units = new Dictionary<Transform, string>();
        private static readonly HashSet<Transform> Dirty = new HashSet<Transform>();
        private static readonly List<Transform> DirtyWork = new List<Transform>();
        private static readonly List<Transform> Chain = new List<Transform>(32);
        private static readonly List<MonoBehaviour> Behaviours = new List<MonoBehaviour>(8);
        private static readonly Dictionary<Assembly, KeyValuePair<ForeignMath.AssemblyKind, string>> Kinds = new Dictionary<Assembly, KeyValuePair<ForeignMath.AssemblyKind, string>>();
        private static int _version = -1;
        private static bool _refresh = true;

        private struct Entry
        {
            public ForeignMath.Verdict V;
            public Transform Unit;
        }

        public static bool Active => _on;
        public static int UnitCount => Units.Count;
        public static IEnumerable<KeyValuePair<Transform, string>> AllUnits => Units;

        // Plugin.Awake: on when the generic family or any precise mod family is RuneSkin's.
        public static void Init(GameObject host, Harmony harmony, string optOut)
        {
            _generic = OwnershipRegistry.IsOwned(OwnershipRegistry.ModUi);
            _on = _generic || ForeignRoles.AnyPreciseOwned();
            if (!_on) return;
            _optOut = ForeignMath.ParseOptOut(optOut);
            _instance = host.AddComponent<ForeignUi>();
            var tmpEnable = AccessTools.DeclaredMethod(typeof(TextMeshProUGUI), "OnEnable");
            if (tmpEnable != null) harmony.Patch(tmpEnable, postfix: new HarmonyMethod(typeof(ForeignUiPatches), nameof(ForeignUiPatches.Enable_Postfix)));
            Plugin.Log.LogInfo($"[RuneSkin] fallback restyler on (generic={_generic}, opt-out [{string.Join(",", _optOut)}])");
        }

        public static void Notice(Graphic g)
        {
            if (_on && g) Pending.Add(g);
        }

        // Jotunn-built roots (GUIManager CustomGUIFront / Back and Create* results): foreign even without a component.
        public static void AddAnchor(Transform t)
        {
            if (!_on || !t || !Anchors.Add(t)) return;
            NewAnchors.Add(t);
            _refresh = true;
        }

        private void LateUpdate()
        {
            if (Retry.Count > 0) RequeueRetries();
            if (Pending.Count == 0 && NewAnchors.Count == 0 && Dirty.Count == 0) return;
            try { Process(); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] fallback restyler: {e}"); Pending.Clear(); Dirty.Clear(); }
        }

        private static void RequeueRetries()
        {
            RetryWork.Clear();
            foreach (var p in Retry) RetryWork.Add(p.Key);
            foreach (var g in RetryWork)
            {
                if (!g || Time.frameCount > Retry[g]) { Retry.Remove(g); continue; }
                Pending.Add(g);
            }
            RetryWork.Clear();
        }

        private static void Process()
        {
            if (_refresh || _version != ScreenRegistry.Version)
            {
                RebuildBoundary();
                Cache.Clear();
                _version = ScreenRegistry.Version;
                _refresh = false;
            }
            Work.Clear();
            foreach (var g in Pending) Work.Add(g);
            Pending.Clear();
            if (Decided.Count >= _decidedPrune)
            {
                Decided.RemoveWhere(GraphicDead);
                _decidedPrune = Math.Max(1024, Decided.Count * 2);
            }
            Bounded.PruneWhenDoubled(Cache, TransformAlive, ref _cachePrune, DeadTransforms);
            // Units and Jotunn anchors of a previous world (logout / re-enter) and of rebuilt mod windows: destroyed ones
            // leave when the table doubled (run 20261022-194855: Units 36 -> 69, Anchors 64 -> 76 over 4 relogs).
            Bounded.PruneWhenDoubled(Units, TransformAlive, ref _unitsPrune, DeadTransforms);
            if (Anchors.Count >= _anchorsPrune)
            {
                Anchors.RemoveWhere(TransformDead);
                _anchorsPrune = Math.Max(Bounded.MinPrune, Anchors.Count * 2);
            }
            for (var i = 0; i < Work.Count; i++)
            {
                var g = Work[i];
                if (!g || !Decided.Add(g)) continue;
                // Screen UI only: in-world canvases (signs, name plates, mod bars over creatures) stay theirs.
                var canvas = g.canvas;
                if (!canvas)
                {
                    // Built off-canvas and parented after its OnEnable (new GameObject + AddComponent, then SetParent):
                    // looked at again for a second.
                    Decided.Remove(g);
                    if (!Retry.ContainsKey(g)) Retry[g] = Time.frameCount + 60;
                    continue;
                }
                Retry.Remove(g);
                if (canvas.rootCanvas.renderMode == RenderMode.WorldSpace) continue;
                var e = Resolve(g.transform);
                if (e.Unit) Dirty.Add(e.Unit);
            }
            Work.Clear();
            for (var i = 0; i < NewAnchors.Count; i++)
            {
                var a = NewAnchors[i];
                if (!a) continue;
                var e = Resolve(a);
                if (e.Unit) Dirty.Add(e.Unit);
            }
            NewAnchors.Clear();
            if (Dirty.Count == 0) return;
            DirtyWork.Clear();
            DirtyWork.AddRange(Dirty);
            Dirty.Clear();
            foreach (var unit in DirtyWork)
            {
                if (!unit) continue;
                var family = Cache.TryGetValue(unit, out var unitEntry) ? unitEntry.V.Family : null;
                try { Restyle(unit, family ?? OwnershipRegistry.ModUi); }
                catch (Exception ex) { Plugin.Log.LogError($"[RuneSkin] fallback restyle of {unit.name} failed (its own look stays): {ex}"); }
            }
            DirtyWork.Clear();
        }

        // The vanilla UI singletons and every family root, with all their ancestors: never a unit themselves.
        private static void RebuildBoundary()
        {
            Boundary.Clear();
            void Mark(Component c)
            {
                for (var p = c ? c.transform : null; p; p = p.parent)
                    if (!Boundary.Add(p)) break;
            }
            Mark(Hud.instance);
            if (Hud.instance && Hud.instance.m_rootObject) Mark(Hud.instance.m_rootObject.transform);
            Mark(InventoryGui.instance);
            Mark(Menu.instance);
            Mark(Minimap.instance);
            Mark(MessageHud.instance);
            Mark(EnemyHud.instance);
            Mark(Chat.instance);
            Mark(Console.instance);
            Mark(StoreGui.instance);
            Mark(TextInput.instance);
            Mark(TextViewer.instance);
            Mark(FejdStartup.instance);
            Mark(ConnectPanel.instance);
            Mark(DamageText.instance);
            Mark(Settings.instance);
            Mark(KeyHints.instance);
            foreach (var root in ScreenRegistry.All)
                if (root && !Units.ContainsKey(root)) Mark(root);
        }

        private static Entry Resolve(Transform t)
        {
            Chain.Clear();
            var above = default(Entry);
            for (var p = t; p; p = p.parent)
            {
                if (Cache.TryGetValue(p, out above)) break;
                above = default;
                Chain.Add(p);
            }
            for (var i = Chain.Count - 1; i >= 0; i--)
            {
                var p = Chain[i];
                Entry e;
                if (above.V.Blocked || above.V.HasUnit) e = above;
                else
                {
                    var v = ForeignMath.Step(above.V, Facts(p), 0, _generic);
                    e = new Entry { V = v, Unit = v.HasUnit ? p : null };
                    // Foreign parts under the HUD root are the mod HUD family.
                    if (v.HasUnit && v.Family == OwnershipRegistry.ModUi && UnderHud(p))
                    {
                        if (OwnershipRegistry.IsOwned(OwnershipRegistry.ModHud)) e.V.Family = OwnershipRegistry.ModHud;
                        else e = new Entry { V = new ForeignMath.Verdict { Blocked = true } };
                    }
                }
                Cache[p] = e;
                above = e;
            }
            Chain.Clear();
            return above;
        }

        private static bool UnderHud(Transform t)
        {
            var hud = Hud.instance && Hud.instance.m_rootObject ? Hud.instance.m_rootObject.transform : null;
            return hud && t.IsChildOf(hud);
        }

        private static ForeignMath.Node Facts(Transform p)
        {
            var n = new ForeignMath.Node
            {
                Boundary = Boundary.Contains(p),
                FamilyRoot = ScreenRegistry.IsRoot(p) && !Units.ContainsKey(p),
                Anchor = Anchors.Contains(p),
                Own = NameCache.Of(p).StartsWith("RuneSkin", StringComparison.Ordinal),
                OptedOut = _optOut.Contains(NameCache.Of(p)),
                WorldCanvas = p.TryGetComponent<Canvas>(out var canvas) && canvas.renderMode == RenderMode.WorldSpace
            };
            Behaviours.Clear();
            p.GetComponents(Behaviours);
            foreach (var b in Behaviours)
            {
                if (!b) continue; // missing script
                var type = b.GetType();
                var hint = ForeignRoles.HintFor(type);
                if (hint != null)
                {
                    n.Hint = hint;
                    n.HintOwned = OwnershipRegistry.IsOwned(hint);
                }
                var kind = KindOf(type.Assembly, out var asm);
                if (kind == ForeignMath.AssemblyKind.Game) n.Game = true;
                else if (kind == ForeignMath.AssemblyKind.Foreign)
                {
                    n.Foreign = true;
                    if (_optOut.Contains(asm)) n.OptedOutPlugin = true;
                }
            }
            Behaviours.Clear();
            return n;
        }

        private static ForeignMath.AssemblyKind KindOf(Assembly a, out string name)
        {
            if (!Kinds.TryGetValue(a, out var k))
            {
                var n = a.GetName().Name;
                k = new KeyValuePair<ForeignMath.AssemblyKind, string>(ForeignMath.Classify(n), n);
                Kinds[a] = k;
            }
            name = k.Value;
            return k.Key;
        }

        // ---------------------------------------------------------------- unit restyle

        private static void Restyle(Transform unit, string family)
        {
            if (Units.ContainsKey(unit))
            {
                ScreenRegistry.Restyle(unit);
                ForeignRoles.After(unit, family);
                return;
            }
            var id = $"{ScreenPrefix}.{family}.{Clean(unit.name)}";
            Units[unit] = id;
            if (!unit.TryGetComponent<RootMark>(out var mark)) mark = unit.gameObject.AddComponent<RootMark>();
            mark.ScreenId = id;
            mark.Owner = "RuneSkin.ModUi";
            var hud = family == OwnershipRegistry.ModHud;
            if (!hud) ForeignRoles.Before(unit, family);
            GameScreenKit.Form(unit, id, family, (ctx, r) => Roles(ctx, r, family), panels: !hud);
            ForeignRoles.After(unit, family);
            if (!hud) ForeignRoles.Movable(unit, id);
            SkinDiagnostics_Note(id, unit);
        }

        private static void SkinDiagnostics_Note(string id, Transform unit) =>
            Plugin.Log.LogInfo($"[RuneSkin] fallback unit {id} ({Diagnostics.SkinDiagnostics.PathOf(unit, null)})");

        private static string Clean(string n) => n.Replace("(Clone)", "").Trim();

        // Exclusions (nested family roots, opted-out / RuneSkin / world-space subtrees), the precise adapter's roles,
        // then the generic foreign rules (window art, legacy texts, icon buttons).
        private static void Roles(ScreenContext ctx, Transform root, string family)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                if (ScreenRegistry.IsRoot(t)) ctx.Exclude(t, Units.ContainsKey(t) ? "nested fallback unit (own pass)" : "nested family root (own pass)");
                else if (_optOut.Contains(t.name)) ctx.Exclude(t, "opted out (Ownership.ModUiOptOut)");
                else if (t.TryGetComponent<Canvas>(out var c) && c.renderMode == RenderMode.WorldSpace) ctx.Exclude(t, "world-space canvas");
            }
            ForeignRoles.Precise(ctx, root, family);
            ForeignRoles.Generic(ctx, root);
        }
    }

    [HarmonyPatch]
    internal static class ForeignUiPatches
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly), batched by FastStartup).
        private static bool Prepare() => ForeignUi.Active;

        [HarmonyPatch(typeof(Graphic), "OnEnable")]
        [HarmonyPostfix]
        internal static void Enable_Postfix(Graphic __instance) => ForeignUi.Notice(__instance);
    }
}
