using System;
using System.IO;
using Godot;
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests
{
    public class PartyEffectPresentationTests
    {
        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.godot")))
                directory = directory.Parent;

            return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find project.godot");
        }

        private static string Read(string path) => File.ReadAllText(Path.Combine(RepositoryRoot(), path));

        [Fact]
        public void Tooltip_ShowsNameAndFormattedRemaining()
        {
            Assert.Equal("Name (1m 23s remaining)", PartyEffect.BuildTooltip("Name", 83.0));
        }

        [Fact]
        public void Tooltip_ZeroDuration_ShowsNameOnly()
        {
            Assert.Equal("Name", PartyEffect.BuildTooltip("Name", 0.0));
        }

        [Fact]
        public void Tooltip_NegativeRemaining_ClampsToNameOnly()
        {
            Assert.Equal("Name", PartyEffect.BuildTooltip("Name", -5.0));
        }

        [Fact]
        public void SweepColor_NormalBeforeFinalTenSeconds()
        {
            var color = BuffSweepBar.SelectSweepColor(10.1, 10.0);
            Assert.Equal(new Color(0, 0, 0, 0.7f), color);
        }

        [Theory]
        [InlineData(10.0)]
        [InlineData(5.0)]
        [InlineData(0.0)]
        [InlineData(-3.0)]
        public void SweepColor_RedAtTenSecondsAndBelow(double remaining)
        {
            var color = BuffSweepBar.SelectSweepColor(remaining, 10.0);
            Assert.Equal(new Color(0.8f, 0.1f, 0.1f, 0.7f), color);
        }

        [Fact]
        public void SweepColor_NoDangerThreshold_NeverRed()
        {
            Assert.Equal(new Color(0, 0, 0, 0.7f), BuffSweepBar.SelectSweepColor(0.0, 0.0));
        }

        [Fact]
        public void Scene_IsCompactIconWithSweepAndNoCountdown()
        {
            var scene = Read("Scenes/UI/PartyEffect.tscn");

            Assert.Contains("res://Scripts/UI/PartyEffect.cs", scene);
            Assert.Contains("res://Scripts/UI/BuffSweepBar.cs", scene);
            Assert.Contains("custom_minimum_size = Vector2(16, 16)", scene);
            Assert.Contains("node name=\"Icon\" type=\"TextureRect\"", scene);
            Assert.Contains("node name=\"Sweep\" type=\"Control\"", scene);
            Assert.DoesNotContain("Countdown", scene);
        }

        [Fact]
        public void Scene_IconAndSweep_IgnoreMouse_RootReceivesIt()
        {
            var scene = Read("Scenes/UI/PartyEffect.tscn");

            var icon = scene.Substring(scene.IndexOf("node name=\"Icon\""));
            icon = icon.Substring(0, icon.IndexOf("[node name=\"Sweep\""));
            Assert.Contains("mouse_filter = 2", icon);

            var root = scene.Substring(scene.IndexOf("node name=\"PartyEffect\""), scene.IndexOf("node name=\"Icon\""));
            Assert.Contains("mouse_filter = 0", root);
        }

        [Fact]
        public void Script_HasNoInputOrDoubleClickPath()
        {
            var script = Read("Scripts/UI/PartyEffect.cs");

            Assert.DoesNotContain("_GuiInput", script);
            Assert.DoesNotContain("DoubleClick", script);
            Assert.DoesNotContain("OnDoubleClick", script);
            Assert.DoesNotContain("Blink", script);
        }
    }
}
