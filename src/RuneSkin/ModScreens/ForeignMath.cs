using System;
using System.Collections.Generic;

namespace RuneSkin.ModScreens
{
    // Pure rules of the fallback restyler (ForeignUi), managed-only so the tests compile them in.
    public static class ForeignMath
    {
        public enum AssemblyKind
        {
            Game,    // the game, Unity, the runtime: vanilla objects
            Own,     // RuneSkin itself: objects RuneSkin draws
            Foreign  // any other plugin
        }

        // Assembly of a MonoBehaviour -> who built the object.
        public static AssemblyKind Classify(string assembly)
        {
            if (string.IsNullOrEmpty(assembly)) return AssemblyKind.Game;
            if (assembly == "RuneSkin" || assembly == "RuneSkin.API") return AssemblyKind.Own;
            if (assembly.StartsWith("assembly_", StringComparison.Ordinal) || assembly == "gui_framework" ||
                assembly.StartsWith("UnityEngine", StringComparison.Ordinal) || assembly.StartsWith("Unity.", StringComparison.Ordinal) ||
                assembly == "Assembly-CSharp" || assembly == "Assembly-CSharp-firstpass" || assembly == "mscorlib" ||
                assembly == "netstandard" || assembly.StartsWith("System", StringComparison.Ordinal) ||
                assembly.StartsWith("com.rlabrecque.steamworks", StringComparison.Ordinal) || assembly == "PlayFab" ||
                assembly.StartsWith("PlayFabParty", StringComparison.Ordinal))
                return AssemblyKind.Game;
            return AssemblyKind.Foreign;
        }

        // Config list "A, B,C" -> set (case-insensitive, blanks dropped).
        public static HashSet<string> ParseList(string csv)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(csv)) return set;
            foreach (var part in csv.Split(','))
            {
                var s = part.Trim();
                if (s.Length > 0) set.Add(s);
            }
            return set;
        }

        // What one transform contributes to the decision (gathered by ForeignUi from the live object).
        public struct Node
        {
            public bool Boundary;        // the object or a descendant is a vanilla UI singleton: never a unit itself
            public bool FamilyRoot;      // a root another RuneSkin family registered
            public bool Anchor;          // Jotunn GUIManager-built (CustomGUIFront / Back, Create* results)
            public string Hint;          // family of a precise adapter whose component sits on the object, or null
            public bool HintOwned;       // that family is RuneSkin's (otherwise the other skin keeps the subtree)
            public bool Foreign;         // a MonoBehaviour of another plugin on the object
            public bool Game;            // a MonoBehaviour of the game on the object (vanilla object or vanilla clone)
            public bool Own;             // a RuneSkin component on the object or a RuneSkin-named object
            public bool OptedOut;        // object name in the opt-out list
            // A MonoBehaviour of an opted-out plugin on the object. Blocks only a would-be unit: on the vanilla boundary
            // (BossAwakening's WorldBoss sits on _GameMain, Game.instance) it must not take every screen below with it.
            public bool OptedOutPlugin;
            public bool WorldCanvas;     // world-space canvas (in-world signs, name plates): never restyled
        }

        public struct Verdict
        {
            public bool Blocked;     // nothing at or below is the fallback's
            public bool UnderRoot;   // inside a family root: only anchors and precise adapters may start a unit
            public bool HasUnit;     // the topmost unit at or above is known
            public int UnitDepth;    // depth of that unit's root (0 = scene root)
            public string Family;    // family of that unit
        }

        // One step down the hierarchy: the verdict of a node from its parent's. A unit is the topmost foreign object
        // below the vanilla boundary; everything below it belongs to it (its context excludes nested family roots).
        public static Verdict Step(Verdict parent, Node n, int depth, bool genericOwned)
        {
            if (parent.Blocked) return parent;
            if (parent.HasUnit) return parent;
            if (n.OptedOut || n.Own || n.WorldCanvas || (n.OptedOutPlugin && !n.Boundary && !n.FamilyRoot)) return new Verdict { Blocked = true };
            var underRoot = parent.UnderRoot || n.FamilyRoot;
            if (!n.Boundary && !n.FamilyRoot)
            {
                if (n.Hint != null)
                    return n.HintOwned
                        ? new Verdict { HasUnit = true, UnitDepth = depth, Family = n.Hint, UnderRoot = underRoot }
                        : new Verdict { Blocked = true };
                if (genericOwned && (n.Anchor || (!underRoot && n.Foreign && !n.Game)))
                    return new Verdict { HasUnit = true, UnitDepth = depth, Family = RuneSkin.API.SkinFamilies.ModUi, UnderRoot = underRoot };
            }
            return new Verdict { UnderRoot = underRoot };
        }

        // A foreign window background (own art the theme does not map) becomes the panel stack only when it is the
        // window itself: the unit root's image or a child image covering most of the unit (rows and cells inside a
        // list stay what they are). Size floor as the vanilla panels (size.preworld.panel.min, height half of it).
        public static bool IsWindowPanel(int depthBelowUnit, float coverShare, float width, float height, float min, bool slicedOrTiled) =>
            slicedOrTiled && width >= min && height >= min * 0.5f && (depthBelowUnit == 0 || (depthBelowUnit == 1 && coverShare >= 0.6f));

        // Epic Loot enchanting table tab -> glyph role (art m7 ench-a: glyph left of the label).
        public static string EnchantGlyph(string tab)
        {
            if (string.IsNullOrEmpty(tab)) return null;
            var t = tab.ToLowerInvariant();
            if (t.Contains("sacrifice")) return "glyph.ench.sacrifice";
            if (t.Contains("convert")) return "glyph.ench.convert";
            if (t.Contains("disenchant")) return "glyph.ench.disenchant";
            if (t.Contains("augment")) return "glyph.ench.augment";
            if (t.Contains("enchant")) return "glyph.ench.enchant";
            if (t.Contains("upgrade")) return "glyph.ench.upgrade";
            return null;
        }

        // Epic Loot adventure board list / tab -> glyph role (art m7 board-a).
        public static string BoardGlyph(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var t = name.ToLowerInvariant();
            if (t.Contains("stash") || t.Contains("secret")) return "glyph.board.stash";
            if (t.Contains("gamble")) return "glyph.board.gamble";
            if (t.Contains("treasure")) return "glyph.board.treasure";
            if (t.Contains("bounty") || t.Contains("bounties")) return "glyph.board.bounty";
            return null;
        }
    }
}
