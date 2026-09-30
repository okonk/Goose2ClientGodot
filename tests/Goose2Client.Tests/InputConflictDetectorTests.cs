using System;
using System.Collections.Generic;
using System.Linq;
using Goose2Client.InputBindings;
using Godot;
using Xunit;

namespace Goose2Client.Tests;

public class InputConflictDetectorTests
{
    [Fact]
    public void MoveUp_SharingTargetUpBinding_ProducesNoConflict()
    {
        var binding = new InputBinding.Keyboard(Key.Up, false, false, false, false);
        var snapshot = Set(s =>
        {
            s["MoveUp"] = [binding];
            s["TargetUp"] = [binding];
        });

        Assert.Empty(InputConflictDetector.Detect(snapshot));
    }

    [Fact]
    public void MoveLeft_SharingTargetUpBinding_ProducesNoConflict()
    {
        var binding = new InputBinding.Keyboard(Key.Up, false, false, false, false);
        var snapshot = Set(s =>
        {
            s["MoveLeft"] = [binding];
            s["TargetUp"] = [binding];
        });

        Assert.Empty(InputConflictDetector.Detect(snapshot));
    }

    [Fact]
    public void MoveUp_SharingTargetDownBinding_ConflictsInTargeting()
    {
        var binding = new InputBinding.Keyboard(Key.Up, false, false, false, false);
        var snapshot = Set(s =>
        {
            s["MoveUp"] = [binding];
            s["TargetDown"] = [binding];
        });

        var conflict = Assert.Single(InputConflictDetector.Detect(snapshot));
        Assert.Equal("MoveUp", conflict.FirstAction);
        Assert.Equal("TargetDown", conflict.SecondAction);
        Assert.Equal(new[] { binding }, conflict.SharedBindings);
        Assert.Equal(new[] { BindingContext.Targeting }, conflict.OverlappingContexts);
    }

    [Fact]
    public void MoveUp_SharingTargetDownBindingOnTwoValues_ReportsEverySharedBinding()
    {
        var up = new InputBinding.Keyboard(Key.Up, false, false, false, false);
        var w = new InputBinding.Keyboard(Key.W, false, false, false, false);
        var snapshot = Set(s =>
        {
            s["MoveUp"] = [up, w];
            s["TargetDown"] = [w, up];
        });

        var conflict = Assert.Single(InputConflictDetector.Detect(snapshot));
        Assert.Equal(new[] { up, w }, conflict.SharedBindings);
    }

    [Fact]
    public void StartChat_SharingConfirmTargetBinding_ProducesNoConflict()
    {
        var binding = new InputBinding.Keyboard(Key.Enter, false, false, false, false);
        var snapshot = Set(s =>
        {
            s["StartChat"] = [binding];
            s["ConfirmTarget"] = [binding];
        });

        Assert.Empty(InputConflictDetector.Detect(snapshot));
    }

    [Fact]
    public void HotbarSlotsAndConfirmTarget_SharingOneBinding_ConflictOnlyBetweenSlotsInNormalGameplay()
    {
        var binding = new InputBinding.Keyboard(Key.Key1, false, false, false, false);
        var snapshot = Set(s =>
        {
            s["Hotkey1"] = [binding];
            s["Hotkey2"] = [binding];
            s["ConfirmTarget"] = [binding];
        });

        var conflict = Assert.Single(InputConflictDetector.Detect(snapshot));
        Assert.Equal("Hotkey1", conflict.FirstAction);
        Assert.Equal("Hotkey2", conflict.SecondAction);
        Assert.Equal(new[] { binding }, conflict.SharedBindings);
        Assert.Equal(new[] { BindingContext.Normal }, conflict.OverlappingContexts);
    }

    [Theory]
    [InlineData("Attack", BindingContext.Normal)]
    [InlineData("TargetUp", BindingContext.Targeting)]
    public void ToggleFullscreen_SharingBindingWithAnyCommand_Conflicts(string action, BindingContext context)
    {
        var binding = new InputBinding.Keyboard(Key.F11, false, false, false, false);
        var snapshot = Set(s =>
        {
            s[action] = [binding];
            s["ToggleFullscreen"] = [binding];
        });

        var conflict = Assert.Single(InputConflictDetector.Detect(snapshot));
        Assert.Equal(action, conflict.FirstAction);
        Assert.Equal("ToggleFullscreen", conflict.SecondAction);
        Assert.Equal(new[] { binding }, conflict.SharedBindings);
        Assert.Equal(new[] { context }, conflict.OverlappingContexts);
    }

    [Fact]
    public void SameKeyWithDifferentModifierSets_ProducesNoConflict()
    {
        var snapshot = Set(s =>
        {
            s["Attack"] = [new InputBinding.Keyboard(Key.A, false, false, false, false)];
            s["PickUp"] = [new InputBinding.Keyboard(Key.A, true, false, false, false)];
        });

        Assert.Empty(InputConflictDetector.Detect(snapshot));
    }

    [Fact]
    public void OppositeDirectionsOfSameJoypadAxis_ProduceNoConflict()
    {
        var snapshot = Set(s =>
        {
            s["MoveLeft"] = [new InputBinding.JoypadAxis(JoyAxis.LeftX, -1)];
            s["MoveRight"] = [new InputBinding.JoypadAxis(JoyAxis.LeftX, 1)];
        });

        Assert.Empty(InputConflictDetector.Detect(snapshot));
    }

    [Fact]
    public void Conflicts_AreStableInCatalogOrder_RegardlessOfDictionaryInsertionOrder()
    {
        var movement = new InputBinding.Keyboard(Key.Up, false, false, false, false);
        var fullscreen = new InputBinding.Mouse(MouseButton.Middle, false, false, false, false);
        var hotbar = new InputBinding.Keyboard(Key.Key1, false, false, false, false);

        var forward = CompleteSet();
        forward["MoveUp"] = [movement];
        forward["TargetDown"] = [movement];
        forward["ToggleFullscreen"] = [fullscreen];
        forward["Attack"] = [fullscreen];
        forward["Hotkey1"] = [hotbar];
        forward["Hotkey2"] = [hotbar];

        var reversed = CompleteSet();
        foreach (var pair in forward.Reverse())
            reversed[pair.Key] = pair.Value;

        var expected = new[]
        {
            new InputBindingConflict("MoveUp", "TargetDown", new[] { movement }, new[] { BindingContext.Targeting }),
            new InputBindingConflict("Attack", "ToggleFullscreen", new[] { fullscreen }, new[] { BindingContext.Normal }),
            new InputBindingConflict("Hotkey1", "Hotkey2", new[] { hotbar }, new[] { BindingContext.Normal })
        };

        Assert.Equal(expected, InputConflictDetector.Detect(new InputBindingSet(forward)));
        Assert.Equal(expected, InputConflictDetector.Detect(new InputBindingSet(reversed)));
    }

    [Fact]
    public void Detect_DoesNotMutateSnapshot()
    {
        var binding = new InputBinding.Keyboard(Key.Up, false, false, false, false);
        var snapshot = Set(s =>
        {
            s["MoveUp"] = [binding];
            s["TargetDown"] = [binding];
        });

        var before = snapshot.GetBindings("MoveUp");
        var first = InputConflictDetector.Detect(snapshot);
        var second = InputConflictDetector.Detect(snapshot);

        Assert.Equal(before, snapshot.GetBindings("MoveUp"));
        Assert.Equal(first, second);
    }

    private static InputBindingSet Set(Action<Dictionary<string, IReadOnlyList<InputBinding>>> configure)
    {
        var bindings = CompleteSet();
        configure(bindings);
        return new InputBindingSet(bindings);
    }

    private static Dictionary<string, IReadOnlyList<InputBinding>> CompleteSet()
    {
        var result = new Dictionary<string, IReadOnlyList<InputBinding>>();
        foreach (var action in InputActionCatalog.Actions)
            result[action.Name] = Array.Empty<InputBinding>();

        return result;
    }
}
