using System;
using System.IO;
using Xunit;

namespace Goose2Client.Tests
{
    public class PartyMemberNameOutlineTests
    {
        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.godot")))
                directory = directory.Parent;

            return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find project.godot");
        }

        private static string Read(string path) => File.ReadAllText(Path.Combine(RepositoryRoot(), path));

        private static string NameLabelNode()
        {
            var scene = Read("Scenes/UI/PartyMember.tscn");
            var node = scene.Substring(scene.IndexOf("node name=\"NameText\""));
            return node.Substring(0, node.IndexOf("[node name=", StringComparison.Ordinal));
        }

        [Fact]
        public void Scene_NameLabel_HasBlackOutline()
        {
            var name = NameLabelNode();

            Assert.Contains("theme_override_colors/font_outline_color = Color(0, 0, 0, 0.9)", name);
            Assert.Contains("theme_override_constants/outline_size = 4", name);
        }

        [Fact]
        public void Script_ScalesTheAuthoredOutline()
        {
            var script = Read("Scripts/UI/PartyMember.cs");

            Assert.Contains("_nameText.GetThemeConstant(\"outline_size\")", script);
            Assert.Contains("UiScale.ScaleSize(_nameOutlineBase, factor)", script);
        }
    }
}
