using RuneSkin.PreWorld;
using Xunit;

namespace RuneSkin.Tests
{
    // M5 pre-world helpers.
    public class PreWorldTests
    {
        [Theory]
        [InlineData("woodpanel_settings", true)]
        [InlineData("woodpanel_trophys", true)]
        [InlineData("panel_bkg_128", true)]
        [InlineData("panel_interior_bkg_128", true)]
        [InlineData("button", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void PanelSprites(string sprite, bool expected) => Assert.Equal(expected, PreWorldMath.IsPanelSprite(sprite));

        // Rune spinner glow chase (0.10.2): head at full, the tail fades over 3 steps, the next rune rises during the
        // last step, the rest stay at the base level.
        [Theory]
        [InlineData(2, 2f, 1f)]
        [InlineData(1, 2f, 0.7978f)]
        [InlineData(0, 2f, 0.4222f)]
        [InlineData(7, 2f, 0.22f)]
        [InlineData(3, 2f, 0.22f)]
        [InlineData(3, 2.5f, 0.61f)]
        [InlineData(2, 10f, 1f)]
        [InlineData(2, -6f, 1f)]
        public void RuneGlowChasesClockwise(int rune, float phase, float want) =>
            Assert.Equal(want, PreWorldMath.RuneGlow(rune, 8, phase, 3f, 0.22f), 3);

        [Fact]
        public void RuneGlowHeadMovesWithThePhase()
        {
            for (var p = 0f; p < 16f; p += 0.25f)
            {
                var head = PreWorldMath.RuneHead(8, p);
                for (var i = 0; i < 8; i++)
                    Assert.True(PreWorldMath.RuneGlow(head, 8, p, 3f, 0.22f) >= PreWorldMath.RuneGlow(i, 8, p, 3f, 0.22f) - 1e-4f);
            }
            Assert.Equal(1, PreWorldMath.RuneHead(8, 1.5f));
            Assert.Equal(0, PreWorldMath.RuneHead(8, 8.2f));
            Assert.Equal(7, PreWorldMath.RuneHead(8, -0.5f));
        }

        [Theory]
        [InlineData("morgott", "M")]
        [InlineData("  эйрик", "Э")]
        [InlineData("", "?")]
        [InlineData("   ", "?")]
        [InlineData(null, "?")]
        public void Initials(string name, string expected) => Assert.Equal(expected, PreWorldMath.Initial(name));

        // M5 polish roles: lit menu entries, primary (Start / Ok) plates with gold labels, danger-red confirm label.
        [Theory]
        [InlineData("button.menu", "sprite.button.hover", null)]
        [InlineData("button.primary", "sprite.button.selected", null)]
        [InlineData("button.label.primary", null, "color.text.item.title")]
        [InlineData("button.label.danger", null, "color.text.missing")]
        public void PolishRoles(string role, string sprite, string textColor)
        {
            using var stream = typeof(RuneSkin.Theme.ThemeSchema).Assembly.GetManifestResourceStream("RuneSkin.default-theme.json");
            Assert.NotNull(stream);
            using var reader = new System.IO.StreamReader(stream);
            var theme = RuneSkin.Theme.ThemeJson.Parse(reader.ReadToEnd());
            var rule = System.Array.Find(theme.roles, r => r.role == role);
            Assert.NotNull(rule);
            if (sprite != null) Assert.Equal(sprite, rule.sprite);
            if (textColor != null) Assert.Equal(textColor, rule.textColor);
        }

        [Fact]
        public void TabColumnSteps()
        {
            Assert.Equal(0f, PreWorldMath.TabOffset(0, 52f, 8f));
            Assert.Equal(120f, PreWorldMath.TabOffset(2, 52f, 8f));
        }

        // Character framing: feet / head project to the wanted viewport rows, the centre to the wanted column.
        [Theory]
        [InlineData(0f, 0.55f, 0.26f, 0.93f, 16f / 9f)]
        [InlineData(0.3f, 0.6f, 0.2f, 0.9f, 21f / 9f)]
        public void FrameMarkerProjects(float yawSin, float vx, float bottom, float top, float aspect)
        {
            var yawCos = (float)System.Math.Sqrt(1f - yawSin * yawSin);
            var rot = new UnityEngine.Quaternion(0f, yawSin, 0f, yawCos);
            var foot = new UnityEngine.Vector3(3f, 1f, 20f);
            var far = foot - rot * UnityEngine.Vector3.forward * 40f;
            var p = PreWorldMath.FrameMarker(rot, foot, 1.9f, 60f, aspect, vx, bottom, top, far);
            float Vy(UnityEngine.Vector3 w) => Project(rot, p, w, aspect).y;
            Assert.Equal(bottom, Vy(foot), 2);
            Assert.Equal(top, Vy(foot + UnityEngine.Vector3.up * 1.9f), 2);
            Assert.Equal(vx, Project(rot, p, foot + UnityEngine.Vector3.up * 0.95f, aspect).x, 2);
            // Already closer than wanted: stays.
            var near = foot - rot * UnityEngine.Vector3.forward * 0.5f;
            Assert.Equal(near, PreWorldMath.FrameMarker(rot, foot, 1.9f, 60f, aspect, vx, bottom, top, near));
        }

        private static UnityEngine.Vector2 Project(UnityEngine.Quaternion rot, UnityEngine.Vector3 cam, UnityEngine.Vector3 w, float aspect)
        {
            var d = w - cam;
            var z = UnityEngine.Vector3.Dot(d, rot * UnityEngine.Vector3.forward);
            var x = UnityEngine.Vector3.Dot(d, rot * UnityEngine.Vector3.right);
            var y = UnityEngine.Vector3.Dot(d, rot * UnityEngine.Vector3.up);
            var tan = (float)System.Math.Tan(30.0 * System.Math.PI / 180.0);
            return new UnityEngine.Vector2(0.5f + x / (2f * z * tan * aspect), 0.5f + y / (2f * z * tan));
        }
    }
}
