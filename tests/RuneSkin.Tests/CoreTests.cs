using System;
using System.Collections.Generic;
using System.Linq;
using RuneSkin.API;
using RuneSkin.Theme;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace RuneSkin.Tests
{
    public class HexColorTests
    {
        [Theory]
        [InlineData("#FF0000", 1f, 0f, 0f, 1f)]
        [InlineData("#00FF0080", 0f, 1f, 0f, 128f / 255f)]
        public void Parses(string hex, float r, float g, float b, float a)
        {
            Assert.True(HexColor.TryParse(hex, out var c));
            Assert.Equal(r, c.r, 3);
            Assert.Equal(g, c.g, 3);
            Assert.Equal(b, c.b, 3);
            Assert.Equal(a, c.a, 3);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("FF0000")]
        [InlineData("#FF00")]
        [InlineData("#GG0000")]
        public void Rejects(string hex) => Assert.False(HexColor.TryParse(hex, out _));
    }

    public class ThemeValidationTests
    {
        private static ThemeFile Minimal() => new ThemeFile
        {
            schema = 1,
            name = "t",
            colors = new[] { new ColorEntry { token = "color.a", value = "#FFFFFF" } },
            sprites = new[] { new AssetEntry { token = "sprite.a", asset = "rs_a" } },
            fonts = new AssetEntry[0],
            floats = new FloatEntry[0],
            roles = new[]
            {
                new RoleRule { role = "panel", sprite = "sprite.a", color = "color.a", imageType = "Sliced" },
                new RoleRule { role = "content" },
                new RoleRule { role = "skip" }
            },
            spriteRoles = new[] { new SpriteRoleEntry { match = "woodpanel_*", role = "panel" } },
            families = new[] { new FamilyEntry { family = "Menu", requires = new[] { "sprite.a" } } }
        };

        [Fact]
        public void MinimalThemeIsValid() => Assert.Empty(ThemeSchema.Validate(Minimal()));

        // Fonts: empty source = our bundle, "loaded" = a TMP asset the game / another plugin loads (rune face).
        [Theory]
        [InlineData(null, true)]
        [InlineData("loaded", true)]
        [InlineData("disk", false)]
        public void FontSourceIsBundleOrLoaded(string source, bool valid)
        {
            var f = Minimal();
            f.fonts = new[] { new AssetEntry { token = "font.rune", asset = "Valheim-Norsebold", source = source } };
            Assert.Equal(valid, !ThemeSchema.Validate(f).Exists(e => e.Contains("source")));
        }

        // The shipped minimap (0.9.3, user ask): ring 320 ref (was 220) capped at 26 % of the safe height (was 18 %).
        [Fact]
        public void DefaultThemeMinimapIsLarge()
        {
            using var s = typeof(ThemeSchema).Assembly.GetManifestResourceStream("RuneSkin.default-theme.json");
            using var r = new System.IO.StreamReader(s!);
            var f = ThemeJson.Parse(r.ReadToEnd());
            Assert.Equal(320f, System.Array.Find(f.floats, x => x.token == "size.minimap.ring").value);
            Assert.Equal(0.26f, System.Array.Find(f.floats, x => x.token == "share.minimap.ring.h").value, 3);
            // 1080p: the cap (0.26 x 1080 = 280.8 px at scale 1) wins over the 320 ref size.
            Assert.Equal(280.8f, RuneSkin.MapScreen.MinimapMath.RingSize(320f, 1080f, 1f, 0.26f), 1);
        }

        // The shipped theme: two faces (user 2026-10-22), the rune face as display bound late from a loaded asset (menu
        // entries, headers), PT Sans for everything else (buttons, rows, tooltips, item names) with its real bold cut.
        [Fact]
        public void DefaultThemeHasTwoFaces()
        {
            using var s = typeof(ThemeSchema).Assembly.GetManifestResourceStream("RuneSkin.default-theme.json");
            using var r = new System.IO.StreamReader(s!);
            var f = ThemeJson.Parse(r.ReadToEnd());
            Assert.Equal(new[] { "font.display", "font.body", "font.body.bold" }, System.Array.ConvertAll(f.fonts, x => x.token));
            Assert.Equal("loaded", System.Array.Find(f.fonts, x => x.token == "font.display").source);
            Assert.Equal("Valheim-Norsebold", System.Array.Find(f.fonts, x => x.token == "font.display").asset);
            foreach (var role in new[] { "menu.entry", "button.label.menu", "text.header", "info.title", "text.display" })
                Assert.Equal("font.display", System.Array.Find(f.roles, x => x.role == role).font);
            foreach (var role in new[] { "button.label", "row.label" })
                Assert.Equal("font.body", System.Array.Find(f.roles, x => x.role == role).font);
            foreach (var role in new[] { "tooltip.topic", "craft.title", "dialog.subject", "button.label.primary" })
                Assert.Equal("font.body.bold", System.Array.Find(f.roles, x => x.role == role).font);
            Assert.All(f.roles, x => Assert.True(x.font == null || x.font == "font.body" || x.font == "font.body.bold" || x.font == "font.display", x.role + ": " + x.font));
        }

        [Fact]
        public void UndefinedTokenReferenceIsAnError()
        {
            var f = Minimal();
            f.roles[0].color = "color.missing";
            Assert.Contains(ThemeSchema.Validate(f), e => e.Contains("color.missing"));
        }

        [Fact]
        public void DuplicateTokenBadColourAndTypeAreErrors()
        {
            var f = Minimal();
            f.colors = new[] { new ColorEntry { token = "color.a", value = "#FFFFFF" }, new ColorEntry { token = "color.a", value = "red" } };
            f.roles[0].imageType = "Wobbly";
            var errors = ThemeSchema.Validate(f);
            Assert.Contains(errors, e => e.Contains("duplicate token"));
            Assert.Contains(errors, e => e.Contains("bad value"));
            Assert.Contains(errors, e => e.Contains("imageType"));
        }

        [Fact]
        public void ClassifierRolesAreRequired()
        {
            var f = Minimal();
            f.roles = new[] { f.roles[0] };
            Assert.Contains(ThemeSchema.Validate(f), e => e.Contains("'content' missing"));
        }

        [Fact]
        public void EmbeddedDefaultThemeIsValid()
        {
            using var stream = typeof(ThemeSchema).Assembly.GetManifestResourceStream("RuneSkin.default-theme.json");
            Assert.NotNull(stream);
            using var reader = new System.IO.StreamReader(stream);
            var theme = ThemeJson.Parse(reader.ReadToEnd());
            Assert.Empty(ThemeSchema.Validate(theme));
            var roles = theme.roles.Select(r => r.role).ToList();
            foreach (var role in new[] { "panel", "button", "menu.entry", "tooltip.box", "tooltip.text", "tooltip.topic", "content", "skip" })
                Assert.Contains(role, roles);
            Assert.Equal("button_hover", theme.sprites.Single(s => s.token == theme.roles.Single(r => r.role == "button").spriteHover).asset);
        }

        [Theory]
        [InlineData("{\"schema\": 1,}")]
        [InlineData("{\"schema\": 1} x")]
        [InlineData("{\"a\": 1, \"a\": 2}")]
        [InlineData("[1]")]
        public void ParserRejectsBadJson(string json) => Assert.Throws<FormatException>(() => ThemeJson.Parse(json));
    }

    // Skin keeps static state: one ordered test covers pending, cancel, replay and live registration.
    public class ApiReplayTests
    {
        private sealed class FakeHost : IHost
        {
            public readonly List<string> Calls = new List<string>();
            public bool IsOwned(string family) => family == "Menu";
            public bool Supports(string capability) => capability == "root.restyle";
            public IDisposable RegisterRoot(string owner, Transform root, string screenId) => Rec("root");
            public IDisposable Bind(string owner, Graphic graphic, string role) => Rec("bind");
            public void Invalidate(Transform root) => Calls.Add("invalidate");
            public IDisposable RegisterPanel(string owner, RectTransform rect, string region) => Rec("panel");
            public IDisposable RegisterSlot(string owner, SlotDescriptor d) => Rec("slot:" + d.Id);
            public IDisposable RegisterStatusEffect(string owner, StatusEffectDescriptor d) => Rec("effect:" + d.Hash);
            public IDisposable RegisterResourceProvider(string owner, string pool, Func<Player, ResourceState> p) => Rec("resource:" + pool);
            public IDisposable RegisterCapacityProvider(string owner, string list, Func<Player, CapacityState> p) => Rec("capacity:" + list);
            public IDisposable RegisterPanelAction(string owner, PanelActionDescriptor d) => Rec("action:" + d.Id);
            public IDisposable RegisterAbilitySlot(string owner, AbilitySlotDescriptor d) => Rec("ability:" + d.Id);
            public IDisposable SetSlotOverlay(string owner, Inventory inventory, int x, int y, string glyphToken) => Rec($"overlay:{x},{y}");
            public IDisposable RegisterHoldPrompt(string owner, string id, Func<Player, HoldPromptState> p) => Rec("hold:" + id);
            public bool TryGetColor(string token, out Color color) { color = Color.white; return true; }
            public bool TryGetSprite(string token, out Sprite sprite) { sprite = null; return false; }
            public bool TryGetFloat(string token, out float value) { value = 1f; return true; }

            private IDisposable Rec(string what)
            {
                Calls.Add(what);
                return new Undo(() => Calls.Add("dispose:" + what));
            }
        }

        private sealed class Undo : IDisposable
        {
            private readonly Action _a;
            public Undo(Action a) => _a = a;
            public void Dispose() => _a();
        }

        [Fact]
        public void EarlyRegistrationsReplayOnAttachAndCancelOnDispose()
        {
            Assert.False(Skin.IsAvailable);
            Assert.False(Skin.Supports("root.restyle"));
            Assert.False(Skin.IsOwned("Menu"));

            var descriptor = new SlotDescriptor { Id = "ring" };
            var kept = Skin.RegisterSlot("test.owner", descriptor);
            descriptor.Id = "mutated"; // copied on registration
            var cancelled = Skin.RegisterPanelAction("test.owner", new PanelActionDescriptor { Id = "sort" });
            cancelled.Dispose();
            Skin.RegisterResourceProvider("test.owner", "es", _ => default);

            var host = new FakeHost();
            Skin.Attach(host);
            Assert.Equal(new[] { "slot:ring", "resource:es" }, host.Calls);
            Assert.True(Skin.IsAvailable);
            Assert.True(Skin.Supports("root.restyle"));
            Assert.False(Skin.Supports("slot.render"));
            Assert.True(Skin.IsOwned("Menu"));

            var live = Skin.RegisterStatusEffect("test.owner", new StatusEffectDescriptor { Hash = 7 });
            Assert.Equal("effect:7", host.Calls[host.Calls.Count - 1]);
            live.Dispose();
            live.Dispose(); // idempotent
            kept.Dispose();
            Assert.Equal(new[] { "slot:ring", "resource:es", "effect:7", "dispose:effect:7", "dispose:slot:ring" }, host.Calls);

            var ability = Skin.RegisterAbilitySlot("test.owner", new AbilitySlotDescriptor { Id = "dash", Order = 5, GetState = _ => default });
            Assert.Equal("ability:dash", host.Calls[host.Calls.Count - 1]);
            ability.Dispose();
            Assert.Equal("dispose:ability:dash", host.Calls[host.Calls.Count - 1]);
            Assert.Throws<ArgumentNullException>(() => Skin.RegisterAbilitySlot("test.owner", new AbilitySlotDescriptor { Id = "x" }));
            Assert.Equal(10, Skin.ApiVersion);

            Assert.Throws<ArgumentNullException>(() => Skin.RegisterSlot("", new SlotDescriptor { Id = "x" }));
            Assert.Throws<InvalidOperationException>(() => Skin.Attach(host));
        }
    }
}
