using System;
using System.IO;
using Xunit;

namespace Goose2Client.Tests;

public class InputMapContractTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.godot")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find project.godot");
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepositoryRoot(), relative));

    private static int Count(string text, string token)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(token, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    [Fact]
    public void KeyboardMappings_WritePhysicalKeycodeAndModifiersOnly()
    {
        string source = Read("Scripts/InputBindings/GodotInputMapSurface.cs");

        Assert.Contains("PhysicalKeycode = d.PhysicalKey", source);
        Assert.Equal(Count(source, "PhysicalKeycode = "), Count(source, "Keycode = "));
        Assert.DoesNotContain("Unicode = ", source);
        Assert.Contains("CtrlPressed = d.Ctrl", source);
        Assert.Contains("ShiftPressed = d.Shift", source);
        Assert.Contains("AltPressed = d.Alt", source);
        Assert.Contains("MetaPressed = d.Meta", source);
    }

    [Fact]
    public void MouseMappings_WriteButtonAndModifiers()
    {
        string source = Read("Scripts/InputBindings/GodotInputMapSurface.cs");

        Assert.Contains("ButtonIndex = d.MouseButton", source);
    }

    [Fact]
    public void JoypadMappings_AreDeviceAgnostic()
    {
        string source = Read("Scripts/InputBindings/GodotInputMapSurface.cs");

        Assert.Equal(2, Count(source, "Device = -1"));
        Assert.Contains("ButtonIndex = d.JoyButton", source);
        Assert.Contains("Axis = d.JoyAxis", source);
        Assert.Contains("AxisValue = d.AxisValue", source);
    }

    [Fact]
    public void Replacement_OnlyErasesAndAddsEvents()
    {
        string source = Read("Scripts/InputBindings/GodotInputMapSurface.cs");

        Assert.Contains("InputMap.ActionEraseEvents(", source);
        Assert.Contains("InputMap.ActionAddEvent(", source);
        Assert.DoesNotContain("InputMap.EraseAction(", source);
        Assert.DoesNotContain("InputMap.AddAction(", source);
        Assert.DoesNotContain("InputMap.ActionSetDeadzone(", source);
    }

    [Fact]
    public void Surface_ReadsThroughInputMapQueriesOnly()
    {
        string source = Read("Scripts/InputBindings/GodotInputMapSurface.cs");

        Assert.Contains("InputMap.HasAction(", source);
        Assert.Contains("InputMap.ActionGetEvents(", source);
        Assert.Contains("InputMap.ActionGetDeadzone(", source);
    }

    [Fact]
    public void Surface_RejectsUnsupportedEventClasses()
    {
        string source = Read("Scripts/InputBindings/GodotInputMapSurface.cs");

        Assert.Contains("InputMapConfigurationException", source);
    }

    [Fact]
    public void Adapter_OnlyTouchesInputMapThroughTheSurface()
    {
        string adapter = Read("Scripts/InputBindings/GodotInputMapAdapter.cs");

        Assert.DoesNotContain("InputMap.", adapter);
        Assert.Contains("IInputMapSurface", adapter);
    }
}
