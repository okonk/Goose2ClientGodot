using System;
using Godot;

namespace Goose2Client;

public static class ScreenOrientationSetting
{
    private const string Path = "user://device.cfg";

    public static readonly (string Label, DisplayServer.ScreenOrientation Orientation)[] Choices =
    [
        ("Landscape", DisplayServer.ScreenOrientation.Landscape),
        ("Flipped", DisplayServer.ScreenOrientation.ReverseLandscape),
        ("Auto-rotate", DisplayServer.ScreenOrientation.SensorLandscape),
        ("Portrait", DisplayServer.ScreenOrientation.Portrait),
    ];

    public static int IndexOf(DisplayServer.ScreenOrientation orientation)
        => Math.Max(0, Array.FindIndex(Choices, c => c.Orientation == orientation));

    public static DisplayServer.ScreenOrientation Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok)
            return Choices[0].Orientation;
        return Choices[IndexOf((DisplayServer.ScreenOrientation)(int)cfg.GetValue("display", "orientation", 0))].Orientation;
    }

    public static void Save(DisplayServer.ScreenOrientation orientation)
    {
        var cfg = new ConfigFile();
        cfg.Load(Path);
        cfg.SetValue("display", "orientation", (int)orientation);
        cfg.Save(Path);
    }

    public static void Apply(DisplayServer.ScreenOrientation orientation)
    {
        if (OS.HasFeature("mobile"))
            DisplayServer.ScreenSetOrientation(orientation);
    }
}
