using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Goose2Client.InputBindings;
using Goose2Client.UI;

namespace Goose2Client;

internal static class KeyBindingsSelfTest
{
    private const string ProductionBindingPath = "user://input-bindings.json";
    private static readonly Vector2I Canvas = new(1280, 720);

    public static async System.Threading.Tasks.Task Run(GameManager gm)
    {
        await gm.ToSignal(gm.GetTree(), SceneTree.SignalName.ProcessFrame);
        bool failed = false;
        var leaseSlot = new LeaseSlot();
        var bindingPath = ProjectSettings.GlobalizePath(gm.InputBindingsPath);
        var character = $"key-bindings-selftest-{OS.GetProcessId()}";
        var characterPath = Path.Combine(ProjectSettings.GlobalizePath("user://"), character + "-settings.json");
        try
        {
            await SelfTestBody(gm, character, bindingPath, characterPath, leaseSlot);
            GD.Print("[key_bindings_selftest] PASS");
        }
        catch (System.Exception e)
        {
            failed = true;
            GD.PrintErr($"ERR_key_bindings_selftest: {e.Message}");
        }
        finally
        {
            var lease = leaseSlot.Value;
            if (lease is { IsRestored: false })
            {
                try
                {
                    lease.RequestRestore(InputReleaseGate.Immediate);
                    gm.InputBindings.ProcessSuppression(new GodotInputReleaseState());
                }
                catch (System.Exception restore)
                {
                    GD.PrintErr($"ERR_key_bindings_selftest: suppression restore failed: {restore.Message}");
                }
            }
            if (!DeleteQuietly(bindingPath))
                failed = true;
            if (!DeleteQuietly(characterPath))
                failed = true;
        }
        gm.GetTree().Quit(failed ? 1 : 0);
    }

    private sealed class LeaseSlot
    {
        public InputBindingSuppressionLease? Value;
    }

    private static int ChipCount(HBoxContainer chipsBox)
    {
        int count = 0;
        foreach (var child in chipsBox.GetChildren())
            if (child.Name.ToString().StartsWith("Chip_"))
                count++;
        return count;
    }

    private static bool DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            return !File.Exists(path);
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"ERR_key_bindings_selftest: could not delete {path}: {e.Message}");
            return false;
        }
    }

    private static async System.Threading.Tasks.Task SelfTestBody(GameManager gm, string character, string bindingPath, string characterPath, LeaseSlot leaseSlot)
    {
        var tree = gm.GetTree();
        var service = gm.InputBindings;
        var applier = UiScaleApplier.Instance!;
        var catalog = InputActionCatalog.Actions;

        async System.Threading.Tasks.Task Frame() => await gm.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        void Assert(bool cond, string msg)
        {
            if (!cond)
                throw new System.InvalidOperationException(msg);
        }

        var productionPath = ProjectSettings.GlobalizePath(ProductionBindingPath);
        Assert(gm.InputBindingsPath != ProductionBindingPath, "self-test must not use the production binding file");
        byte[]? productionBefore = File.Exists(productionPath) ? File.ReadAllBytes(productionPath) : null;

        tree.Root.Size = Canvas;
        var canvas = (Vector2I)tree.Root.GetVisibleRect().Size;
        Assert(canvas == Canvas, $"headless canvas {canvas} != 1280x720");
        gm.LoadSettings(character);
        await Frame();
        gm.EnsureHud();
        await Frame();

        Assert(catalog.Count == 48, $"catalog must define 48 actions, found {catalog.Count}");
        var factory = service.FactoryDefaults;
        foreach (var action in catalog)
        {
            Assert(InputMap.HasAction(action.Name), $"runtime input map missing action {action.Name}");
            var bindings = factory.GetBindings(action.Name);
            Assert(bindings.Count > 0, $"factory action {action.Name} has no bindings");
            Assert(InputMap.ActionGetEvents(action.Name).Count == bindings.Count,
                $"factory action {action.Name}: runtime events {InputMap.ActionGetEvents(action.Name).Count} != {bindings.Count}");
        }
        Assert(factory.GetBindings("MoveUp")[0] is InputBinding.Keyboard { PhysicalKey: Key.W, Ctrl: false, Shift: false, Alt: false, Meta: false },
            "MoveUp factory default must be the physical W key");
        Assert(factory.GetBindings("Attack")[0] is InputBinding.Keyboard { PhysicalKey: Key.Space },
            "Attack factory default must be the physical Space key");
        Assert(factory.GetBindings("Hotkey1")[0] is InputBinding.Keyboard { PhysicalKey: Key.Key1 },
            "Hotkey1 factory default must be the physical 1 key");
        Assert(factory.GetBindings("ToggleFullscreen")[0] is InputBinding.Keyboard { PhysicalKey: Key.Enter, Alt: true },
            "ToggleFullscreen factory default must be the Alt+Enter chord");
        GD.Print("[key_bindings_selftest] OK factory capture covers all 48 actions with physical-key defaults");

        var draft = new Dictionary<string, IReadOnlyList<InputBinding>>(catalog.Count);
        foreach (var action in catalog)
            draft[action.Name] = service.Active.GetBindings(action.Name);
        draft["Attack"] = [new InputBinding.Keyboard(Key.Space, true, false, false, false)];
        draft["PickUp"] = [new InputBinding.Mouse(MouseButton.Middle, false, false, false, false)];
        draft["CycleHotbarPage"] = [new InputBinding.Mouse(MouseButton.WheelUp, false, false, false, false)];
        draft["ToggleMount"] = [new InputBinding.JoypadButton(JoyButton.A)];
        draft["MoveLeft"] = [new InputBinding.JoypadAxis(JoyAxis.LeftX, -1), new InputBinding.JoypadAxis(JoyAxis.LeftX, 1)];
        var applied = service.Apply(draft);
        Assert(applied.Success, $"Apply failed: {applied.Error}");
        Assert(File.Exists(bindingPath), "Apply must write the temporary binding file");

        var attackEvents = InputMap.ActionGetEvents("Attack");
        Assert(attackEvents.Count == 1 && attackEvents[0] is InputEventKey { PhysicalKeycode: Key.Space, CtrlPressed: true, ShiftPressed: false, AltPressed: false, MetaPressed: false },
            "Attack runtime event must be the exact Ctrl+Space chord");
        var pickUpEvents = InputMap.ActionGetEvents("PickUp");
        Assert(pickUpEvents.Count == 1 && pickUpEvents[0] is InputEventMouseButton { ButtonIndex: MouseButton.Middle },
            "PickUp runtime event must be the middle mouse button");
        var wheelEvents = InputMap.ActionGetEvents("CycleHotbarPage");
        Assert(wheelEvents.Count == 1 && wheelEvents[0] is InputEventMouseButton { ButtonIndex: MouseButton.WheelUp },
            "CycleHotbarPage runtime event must be the wheel-up mouse button");
        var mountEvents = InputMap.ActionGetEvents("ToggleMount");
        Assert(mountEvents.Count == 1 && mountEvents[0] is InputEventJoypadButton { ButtonIndex: JoyButton.A, Device: -1 },
            "ToggleMount runtime event must be the generic (device -1) gamepad button");
        var leftEvents = InputMap.ActionGetEvents("MoveLeft");
        Assert(leftEvents.Count == 2 && leftEvents[0] is InputEventJoypadMotion { Axis: JoyAxis.LeftX, AxisValue: -1f }
            && leftEvents[1] is InputEventJoypadMotion { Axis: JoyAxis.LeftX, AxisValue: 1f },
            "MoveLeft runtime events must be the exact -1/+1 LeftX pair");
        GD.Print("[key_bindings_selftest] OK applied bindings produce exact InputMap events");

        // Headless dispatch does not update the live action-state machine, so matching is
        // asserted against the live InputMap via GetActionStrength.
        var chordPress = new InputEventKey { PhysicalKeycode = Key.Space, CtrlPressed = true, Pressed = true };
        Assert(chordPress.GetActionStrength("Attack") > 0f, "the Ctrl+Space chord must match the applied Attack mapping");
        var joypadPress = new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.A, Pressed = true };
        Assert(joypadPress.GetActionStrength("ToggleMount") > 0f, "a concrete-device joypad button must match the generic (device -1) mapping");
        var axisNegative = new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftX, AxisValue = -1f };
        Assert(axisNegative.GetActionStrength("MoveLeft") > 0f, "a concrete-device negative axis must match the generic mapping");
        var axisNeutral = new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftX, AxisValue = 0f };
        Assert(axisNeutral.GetActionStrength("MoveLeft") == 0f, "a neutral axis must not match");
        Input.ParseInputEvent(chordPress);
        Input.ParseInputEvent(joypadPress);
        Input.ParseInputEvent(axisNegative);
        GD.Print("[key_bindings_selftest] OK concrete-device joypad and axis events match the generic mapping");

        var activeBefore = service.Active;
        var lease = service.BeginSuppression();
        leaseSlot.Value = lease;
        Assert(service.CaptureGateHeld, "capture gate must be held during suppression");
        foreach (var action in catalog)
            Assert(InputMap.ActionGetEvents(action.Name).Count == 0, $"suppression must empty the {action.Name} event list");
        Assert(ReferenceEquals(service.Active, activeBefore), "suppression must not change Active");

        Input.ParseInputEvent((InputEvent)chordPress.Duplicate());
        Assert(chordPress.GetActionStrength("Attack") == 0f, "synthetic captured input must not match a gameplay action during suppression");
        Input.ParseInputEvent((InputEvent)joypadPress.Duplicate());
        Assert(joypadPress.GetActionStrength("ToggleMount") == 0f, "synthetic captured joypad input must not match during suppression");
        var chordRelease = new InputEventKey { PhysicalKeycode = Key.Space, CtrlPressed = true, Pressed = false };
        Input.ParseInputEvent(chordRelease);

        int restored = 0;
        bool gateReleased = false;
        service.SuppressionRestored += () => restored++;
        service.CaptureGateReleased += () => gateReleased = true;
        lease.RequestRestore(InputReleaseGate.ForJoypadAxis(0, JoyAxis.LeftX));
        Input.ParseInputEvent(axisNeutral);
        service.ProcessSuppression(new GodotInputReleaseState());
        Assert(restored == 1 && gateReleased, "an axis-neutral dispatch must restore the active map and release the gate");
        foreach (var action in catalog)
            Assert(InputMap.ActionGetEvents(action.Name).Count == service.Active.GetBindings(action.Name).Count,
                $"restored map must carry the exact active bindings for {action.Name}");
        service.ProcessSuppression(new GodotInputReleaseState());
        Assert(restored == 1 && !service.CaptureGateHeld, "restore must run exactly once");
        leaseSlot.Value = null;
        GD.Print("[key_bindings_selftest] OK suppression empties the map, blocks matching, and restores exactly once");

        var wheelLease = service.BeginSuppression();
        leaseSlot.Value = wheelLease;
        Assert(InputMap.ActionGetEvents("Attack").Count == 0, "the second suppression must empty the map again");
        wheelLease.RequestRestore(InputReleaseGate.NextFrame);
        Assert(InputMap.ActionGetEvents("Attack").Count == 0, "a wheel (NextFrame) restore must not run before the next process frame");
        await Frame();
        Assert(InputMap.ActionGetEvents("Attack").Count == 1 && !service.CaptureGateHeld,
            "a wheel (NextFrame) restore must complete on the following process frame");
        leaseSlot.Value = null;
        GD.Print("[key_bindings_selftest] OK wheel capture restores on the following process frame");

        var hud = gm.Hud!;
        int windowCount = 0;
        foreach (var child in hud.GetChildren())
            if (child is KeyBindingsWindow) windowCount++;
        Assert(windowCount == 1, $"exactly one key bindings window expected, found {windowCount}");
        var kb = hud.KeyBindings;
        Assert(!kb.Visible, "key bindings window must exist and start hidden");
        Assert(KeyBindingsLayout.DesignSize == new Vector2(760, 520) && KeyBindingsLayout.MinSize == new Vector2(520, 320),
            "layout design/minimum sizes");
        Assert(DefaultWindowLayout.IsDialog("KeyBindings"), "KeyBindings must be a centered dialog");

        var optionsButton = hud.Options.GetNode<Button>("Content/KeyBindingsButton");
        optionsButton.EmitSignal("pressed");
        Assert(kb.Visible, "the Options callback must open the key bindings window");
        Assert(kb.Size == KeyBindingsLayout.DesignSize, $"first size {kb.Size} must be the design size");
        Assert(kb.Position == WindowPlacement.Center(Canvas, KeyBindingsLayout.DesignSize),
            $"first placement {kb.Position} must be centered");
        Assert((string)kb.GetNode<Label>("TitleBar/TitleLabel").ThemeTypeVariation == "WindowTitleActive",
            "Open must activate the window");
        Assert(hud.GetChild(hud.GetChildCount() - 1) == kb, "Open must move the window to the front");
        GD.Print("[key_bindings_selftest] OK exactly one hidden window; Options opens, activates, and centers it");

        var rowsBox = kb.GetNode<VBoxContainer>("Content/RootBox/ScrollHost/RowsBox");
        var status = kb.GetNode<Label>("Content/RootBox/FooterRow/StatusLabel");
        var mountRow = kb.GetNode<HBoxContainer>("Content/RootBox/ScrollHost/RowsBox/Row_ToggleMount");
        var mountChips = mountRow.GetNode<HBoxContainer>("ChipsBox");
        Assert(ChipCount(mountChips) == 1, "ToggleMount must show its one applied chip");
        byte[] fileBefore = File.ReadAllBytes(bindingPath);
        // Reset Action deterministically produces a pending draft change without starting a capture session.
        mountRow.GetNode<Button>("ResetActionButton").EmitSignal("pressed");
        Assert(status.Text == "Unsaved changes.", $"status {status.Text} after a draft change");
        kb.GetNode<Button>("Content/RootBox/FooterRow/CancelButton").EmitSignal("pressed");
        Assert(!kb.Visible, "Cancel must close the window");
        Assert(service.Active.GetBindings("ToggleMount").Count == 1
            && service.Active.GetBindings("ToggleMount")[0] is InputBinding.JoypadButton { Button: JoyButton.A },
            "Cancel must not apply the draft");
        Assert(InputMap.ActionGetEvents("ToggleMount").Count == 1, "Cancel must leave the runtime map untouched");
        Assert(File.ReadAllBytes(bindingPath).AsSpan().SequenceEqual(fileBefore), "Cancel must not write the binding file");
        GD.Print("[key_bindings_selftest] OK Cancel closes without applying");

        optionsButton.EmitSignal("pressed");
        Assert(kb.Visible, "the Options callback must reopen the window");
        Assert(status.Text == "No unsaved changes.", $"status {status.Text} after reopen");
        Assert(ChipCount(mountChips) == 1, "reopen must restore the applied binding chip");

        gm.CharacterSettings.SetWindowSetting("KeyBindings", new Vector2(100, 100), new Vector2(100, 100), 1f, true, Canvas);
        kb.Relayout();
        Assert(kb.Size == KeyBindingsLayout.MinSize, $"shrunk size {kb.Size} must clamp to the minimum size");
        gm.CharacterSettings.ResetWindowSettings();
        kb.Relayout();
        Assert(kb.Size == KeyBindingsLayout.DesignSize, $"reset size {kb.Size} must restore the design size");
        GD.Print("[key_bindings_selftest] OK design and minimum sizes are honored");

        int expectedChips = 0;
        foreach (var action in catalog)
            expectedChips += service.Active.GetBindings(action.Name).Count;

        (int Rows, int Chips) CountRowsAndChips()
        {
            int rows = 0, chips = 0;
            foreach (var child in rowsBox.GetChildren())
            {
                if (!child.Name.ToString().StartsWith("Row_"))
                    continue;
                rows++;
                chips += ChipCount((HBoxContainer)child.GetNode("ChipsBox"));
            }
            return (rows, chips);
        }

        Assert(CountRowsAndChips() == (48, expectedChips), "all 48 rows and every applied chip must be present at 1x");
        applier.Apply(2f, ApplyReason.UserCommit);
        await Frame();
        Assert(kb.Size == new Vector2(Canvas.X, Canvas.Y), $"2x size {kb.Size} must clamp to the canvas");
        Assert(CountRowsAndChips() == (48, expectedChips), "rows and chips must survive 2x");
        Assert(kb.Position.X >= 0 && kb.Position.Y >= 0
            && kb.Position.X + kb.Size.X <= Canvas.X && kb.Position.Y + kb.Size.Y <= Canvas.Y,
            $"window rect {kb.Position}+{kb.Size} must stay within the root canvas at 2x");
        applier.Apply(1f, ApplyReason.UserCommit);
        await Frame();
        Assert(kb.Size == KeyBindingsLayout.DesignSize, $"1x size {kb.Size} must restore the design size");
        Assert(CountRowsAndChips() == (48, expectedChips), "rows and chips must survive the 1x/2x/1x round trip");
        kb.GetNode<Button>("Content/RootBox/FooterRow/CancelButton").EmitSignal("pressed");
        Assert(!kb.Visible, "Cancel must close the window after the scale round trip");
        GD.Print("[key_bindings_selftest] OK dynamic rows/chips survive 1x/2x/1x and the window stays in the canvas");

        optionsButton.EmitSignal("pressed");
        Assert(kb.Visible, "the Options callback must reopen the window for the chip callback check");
        var attackRow = kb.GetNode<HBoxContainer>("Content/RootBox/ScrollHost/RowsBox/Row_Attack");
        var attackChips = attackRow.GetNode<HBoxContainer>("ChipsBox");
        Assert(ChipCount(attackChips) == 1, "Attack must show its one applied chip");
        attackChips.GetNode<Button>("Chip_0/Remove_0").EmitSignal("pressed");
        Assert(ChipCount(attackChips) == 0, "pressing the chip remove button must remove the chip");
        Assert(status.Text == "Unsaved changes.", $"status {status.Text} after a chip removal");
        kb.GetNode<Button>("Content/RootBox/FooterRow/CancelButton").EmitSignal("pressed");
        Assert(!kb.Visible, "Cancel must close the window after the chip removal");
        Assert(service.Active.GetBindings("Attack").Count == 1
            && service.Active.GetBindings("Attack")[0] is InputBinding.Keyboard { PhysicalKey: Key.Space, Ctrl: true },
            "Cancel must not apply the chip removal");
        GD.Print("[key_bindings_selftest] OK chip remove button callback removes the binding");

        var chat = hud.Chat;
        chat.Visible = true;
        for (int i = 0; i < 200; i++)
            chat.AddChatLine($"self-test line {i}", ChatType.Server);
        await Frame();
        await Frame();
        var chatBar = chat.GetNode<RichTextLabel>("Content/ChatLog").GetVScrollBar();
        double bottom = chatBar.MaxValue - chatBar.Page;
        Assert(chatBar.Page > 0 && bottom > chatBar.Page, $"chat log must overflow for the scroll check (page {chatBar.Page}, max {chatBar.MaxValue})");
        Assert(chatBar.Value >= bottom - 1, $"chat log must start at the bottom ({chatBar.Value} of {bottom})");
        hud._UnhandledInput(new InputEventKey { PhysicalKeycode = Key.Pageup, Pressed = true });
        Assert(Math.Abs(chatBar.Value - (bottom - chatBar.Page)) < 1, $"Page Up must scroll the chat log one page up ({chatBar.Value})");
        hud._UnhandledInput(new InputEventKey { PhysicalKeycode = Key.Pagedown, Pressed = true });
        Assert(chatBar.Value >= bottom - 1, $"Page Down must scroll the chat log back to the bottom ({chatBar.Value})");
        GD.Print("[key_bindings_selftest] OK Page Up / Page Down page the chat log");

        byte[]? productionAfter = File.Exists(productionPath) ? File.ReadAllBytes(productionPath) : null;
        Assert(productionBefore == null
            ? productionAfter == null
            : productionAfter != null && productionBefore.AsSpan().SequenceEqual(productionAfter),
            "the production binding file must be untouched by the self-test");
        Assert(File.Exists(bindingPath), "the temporary binding file must exist until the finally cleanup");
        Assert(File.Exists(characterPath), "the temporary character settings file must exist until the finally cleanup");
    }
}
