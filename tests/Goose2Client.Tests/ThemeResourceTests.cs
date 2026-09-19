using System;
using System.IO;
using Xunit;

namespace Goose2Client.Tests;

public class ThemeResourceTests
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
    public void LoginScene_UsesBackgroundArtworkAndFocusedLoginCard()
    {
        var scene = Read("Scenes/Login.tscn");

        Assert.Contains("res://Assets/UI/goose-login.png", scene);
        Assert.Contains("node name=\"Background\" type=\"TextureRect\"", scene);
        Assert.Contains("node name=\"LoginCard\" type=\"PanelContainer\"", scene);
        Assert.Contains("node name=\"GameTitle\" type=\"Label\"", scene);
        Assert.DoesNotContain("LiberationSans-Bold.ttf", scene);
        Assert.Contains("text = \"CHARACTER NAME\"", scene);
        Assert.Contains("placeholder_text = \"Character name\"", scene);
        Assert.Contains("placeholder_text = \"Password\"", scene);
    }

    [Fact]
    public void DefaultFont_KeepsClearRenderingSettings()
    {
        var fontImport = Read("Assets/UI/Fonts/LiberationSans.ttf.import");

        Assert.Contains("antialiasing=1", fontImport);
        Assert.Contains("subpixel_positioning=4", fontImport);
    }

    [Fact]
    public void GameTheme_DefinesReadableInteractiveStates()
    {
        var theme = Read("Assets/UI/GameTheme.tres");

        Assert.Contains("Button/styles/hover", theme);
        Assert.Contains("Button/styles/focus", theme);
        Assert.Contains("LineEdit/styles/focus", theme);
        Assert.Contains("CheckBox/icons/checked", theme);
        Assert.Contains("HSlider/styles/slider", theme);
        Assert.Contains("TooltipPanel/styles/panel", theme);
    }

    [Fact]
    public void GameTheme_UsesClearLabelsAndLighterSlotPanels()
    {
        var theme = Read("Assets/UI/GameTheme.tres");

        Assert.DoesNotContain("\nLabel/colors/font_shadow_color", theme);
        Assert.Contains("SlotPanel/styles/panel", theme);

        foreach (var scenePath in new[]
                 {
                     "Scenes/UI/ItemSlot.tscn",
                     "Scenes/UI/HotbarSlot.tscn",
                     "Scenes/UI/SpellSlot.tscn"
                 })
            Assert.Contains("theme_type_variation = &\"SlotPanel\"", Read(scenePath));
    }

    [Fact]
    public void ChatLog_LeavesTheWindowBackgroundVisible()
    {
        var scene = Read("Scenes/UI/ChatWindow.tscn");

        Assert.Contains("sub_resource type=\"StyleBoxEmpty\" id=\"chat_log_transparent\"", scene);
        Assert.Contains("theme_override_styles/normal = SubResource(\"chat_log_transparent\")", scene);
    }

    [Fact]
    public void LoadingScene_UsesThemedTravelPanel()
    {
        var scene = Read("Scenes/LoadingMap.tscn");

        Assert.Contains("node name=\"Backdrop\" type=\"ColorRect\"", scene);
        Assert.Contains("node name=\"LoadingPanel\" type=\"PanelContainer\"", scene);
        Assert.Contains("text = \"TRAVELLING\"", scene);
    }


    [Fact]
    public void GameTheme_GivesThePrimaryActionAndIconButtonsTheirOwnTreatment()
    {
        var theme = Read("Assets/UI/GameTheme.tres");

        Assert.Contains("PrimaryButton/base_type = &\"Button\"", theme);
        Assert.Contains("PrimaryButton/styles/normal", theme);
        Assert.Contains("PrimaryButton/styles/hover", theme);
        Assert.Contains("IconButton/base_type = &\"Button\"", theme);
        Assert.Contains("IconButton/styles/hover", theme);
        Assert.Contains("IconButton/colors/icon_hover_color", theme);
    }

    [Fact]
    public void LoginButton_IsStyledAsThePrimaryAction()
    {
        var scene = Read("Scenes/Login.tscn");

        Assert.Contains("theme_type_variation = &\"PrimaryButton\"", scene);
    }

    [Fact]
    public void ToolbarButtons_UseTheThemedToolbarStyleSoTheyRespondToHover()
    {
        var scene = Read("Scenes/UI/Toolbar.tscn");

        Assert.Equal(4, Occurrences(scene, "theme_type_variation = &\"ToolbarButton\""));
        Assert.DoesNotContain("flat = true", scene);
    }

    [Fact]
    public void LineEditClearButton_IsMutedRatherThanStarkWhite()
    {
        var theme = Read("Assets/UI/GameTheme.tres");

        Assert.Contains("LineEdit/colors/clear_button_color", theme);
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;

        return count;
    }


    [Fact]
    public void FpsReadout_SitsInTheCornerAwayFromTheVitalsCluster()
    {
        var debug = Read("Scenes/UI/DebugWindow.tscn");
        var vitals = Read("Scenes/UI/VitalsWindow.tscn");

        Assert.Contains("anchor_left = 1.0", debug);
        Assert.Contains("anchor_right = 1.0", debug);
        Assert.Contains("grow_horizontal = 0", debug);
        Assert.Contains("horizontal_alignment = 2", debug);
        Assert.Contains("offset_left = 8.0", vitals);
    }

    [Fact]
    public void FpsReadout_DoesNotOverlapTheBuildStamp()
    {
        var overlay = Read("Scripts/UI/BuildStampOverlay.cs");
        var margin = int.Parse(System.Text.RegularExpressions.Regex
            .Match(overlay, @"Margin\s*=\s*(\d+)").Groups[1].Value);
        var stampHeight = int.Parse(System.Text.RegularExpressions.Regex
            .Match(overlay, @"OffsetBottom\s*=\s*Margin\s*\+\s*(\d+)").Groups[1].Value);
        var stampBottom = margin + stampHeight;

        Assert.Contains("LayoutPreset.TopRight", overlay);

        var debug = Read("Scenes/UI/DebugWindow.tscn");
        var debugTop = float.Parse(System.Text.RegularExpressions.Regex
            .Match(debug, @"offset_top = ([\d.]+)").Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(debugTop >= stampBottom,
            $"DebugWindow starts at y={debugTop} but the always-on-top build stamp occupies " +
            $"y=0..{stampBottom} in the same top-right corner; the fps readout would sit under it.");
    }

    [Fact]
    public void LevelBadge_UsesTheThemePaletteRatherThanFlatGrey()
    {
        var png = ReadBytes("Assets/UI/vitals-level-circle.png");

        var colourType = png[25];
        Assert.True(colourType == 2 || colourType == 6,
            $"vitals-level-circle.png should be a colour image (got colour type {colourType}); " +
            "a greyscale badge reads as off-palette next to the gold HUD chrome.");
    }

    private static byte[] ReadBytes(string path) => File.ReadAllBytes(Path.Combine(RepositoryRoot(), path));

    [Fact]
    public void ChatFrame_KeepsTransparentTitleBarGutter()
    {
        var png = ReadBytes("Assets/UI/chat.png");

        // PNG layout: 8-byte signature, then the IHDR chunk (4 length + 4 type + 4 width
        // + 4 height + 1 bit depth), so the colour-type byte sits at offset 25.
        var colourType = png[25];
        var hasAlphaChannel = colourType == 4 || colourType == 6;
        var hasPaletteTransparency = colourType == 3 && IndexOf(png, "tRNS") >= 0;

        Assert.True(hasAlphaChannel || hasPaletteTransparency,
            $"chat.png must keep transparency (colour type {colourType}); the title-bar gutter " +
            "beside the Chat tab would otherwise render as an opaque black bar over the world.");
    }

    private static int IndexOf(byte[] haystack, string needle)
    {
        var probe = System.Text.Encoding.ASCII.GetBytes(needle);
        for (var i = 0; i <= haystack.Length - probe.Length; i++)
        {
            var match = true;
            for (var j = 0; j < probe.Length; j++)
                if (haystack[i + j] != probe[j]) { match = false; break; }
            if (match) return i;
        }

        return -1;
    }
}
