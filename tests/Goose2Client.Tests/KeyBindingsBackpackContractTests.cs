using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Goose2Client.Tests;

public class KeyBindingsBackpackContractTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.godot")))
            directory = directory.Parent;

        return directory!.FullName;
    }

    private static (string Name, List<int> PhysicalKeycodes)[] ParseInputActions()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "project.godot"));
        var actions = new List<(string, List<int>)>();
        var inInput = false;
        string? current = null;
        var currentKeys = new List<int>();

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("["))
            {
                if (current != null)
                    actions.Add((current, currentKeys));
                current = null;
                currentKeys = new List<int>();
                inInput = trimmed == "[input]";
                continue;
            }

            if (!inInput)
                continue;

            var header = Regex.Match(line, "^([A-Za-z_][A-Za-z0-9_]*)=");
            if (header.Success)
            {
                if (current != null)
                    actions.Add((current, currentKeys));
                current = header.Groups[1].Value;
                currentKeys = new List<int>();
            }

            foreach (Match key in Regex.Matches(line, @"""physical_keycode"":(\d+)"))
                currentKeys.Add(int.Parse(key.Groups[1].Value));
        }

        if (current != null)
            actions.Add((current, currentKeys));

        return actions.ToArray();
    }

    [Fact]
    public void ProjectGodot_DefinesToggleBackpack_DefaultingToPhysicalKeyN()
    {
        var actions = ParseInputActions();
        var backpack = actions.Where(a => a.Name == "ToggleBackpack").ToArray();

        Assert.Single(backpack);
        Assert.Contains(78, backpack[0].PhysicalKeycodes);
    }

    [Fact]
    public void NoOtherAction_UsesPhysicalKeyN()
    {
        var actions = ParseInputActions();
        var usersOfN = actions.Where(a => a.PhysicalKeycodes.Contains(78)).Select(a => a.Name).ToArray();

        Assert.Equal(new[] { "ToggleBackpack" }, usersOfN);
    }
}
