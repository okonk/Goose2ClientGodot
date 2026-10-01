using System;
using System.Collections.Generic;
using Godot;
using Goose2Client;
using Goose2Client.InputBindings;

namespace Goose2Client.UI;

public partial class KeyBindingsWindow : BaseWindow
{
    protected override bool DefaultVisible => false;
    protected override bool Resizable => true;
    protected override Vector2 MinResizeSize => KeyBindingsLayout.MinSize;

    private static readonly Color UnboundColor = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Color HeaderColor = new(0.81f, 0.69f, 0.45f, 1f);
    private static readonly Color ConflictColor = new(1f, 0.55f, 0.5f, 1f);

    private KeyBindingEditorState? _editor;
    private readonly IInputBindingLabelProvider _labels = new GodotInputBindingLabelProvider();
    private LineEdit _search = null!;
    private VBoxContainer _rowsBox = null!;
    private Label _status = null!;
    private Button _resetAll = null!;
    private Button _apply = null!;
    private Button _cancel = null!;
    private Panel _captureOverlay = null!;
    private Label _capturePrompt = null!;
    private Button _cancelCapture = null!;
    private readonly Dictionary<string, RowShell> _rows = new();
    private readonly Dictionary<string, Label> _categoryHeaders = new();
    private readonly Dictionary<string, List<string>> _categoryActions = new();
    private bool _pendingOpen;

    public override void _Ready()
    {
        base._Ready();

        Visible = false;

        _search = GetNode<LineEdit>("Content/RootBox/SearchRow/SearchField");
        _rowsBox = GetNode<VBoxContainer>("Content/RootBox/ScrollHost/RowsBox");
        _status = GetNode<Label>("Content/RootBox/FooterRow/StatusLabel");
        _resetAll = GetNode<Button>("Content/RootBox/FooterRow/ResetAllButton");
        _apply = GetNode<Button>("Content/RootBox/FooterRow/ApplyButton");
        _cancel = GetNode<Button>("Content/RootBox/FooterRow/CancelButton");
        _captureOverlay = GetNode<Panel>("CaptureOverlay");
        _capturePrompt = GetNode<Label>("CaptureOverlay/CaptureCenter/CaptureBox/CapturePrompt");
        _cancelCapture = GetNode<Button>("CaptureOverlay/CaptureCenter/CaptureBox/CancelCaptureButton");

        var service = GameManager.Instance.InputBindings;
        _editor = new KeyBindingEditorState(service, service.StartupWarning);

        BuildRows();

        _search.TextChanged += OnSearchChanged;
        _resetAll.Pressed += OnResetAllPressed;
        _apply.Pressed += OnApplyPressed;
        _cancel.Pressed += OnCancelPressed;
        _cancelCapture.Pressed += OnCancelCapturePressed;
        service.CaptureGateReleased += OnCaptureGateReleased;

        Rerender();

        ScaleRegister();
    }

    public override void _ExitTree()
    {
        var service = GameManager.Instance?.InputBindings;
        if (service != null)
            service.CaptureGateReleased -= OnCaptureGateReleased;
    }

    public void Open()
    {
        if (Visible)
        {
            Activate();
            return;
        }

        var service = GameManager.Instance?.InputBindings;
        if (service is { CaptureGateHeld: true })
        {
            _pendingOpen = true;
            return;
        }

        _pendingOpen = false;
        _editor!.Open();
        Rerender();
        Visible = true;
        Activate();
    }

    private void OnCaptureGateReleased()
    {
        if (!_pendingOpen)
            return;
        _pendingOpen = false;
        _editor!.Open();
        Rerender();
        Visible = true;
        Activate();
    }

    public override void Relayout()
    {
        base.Relayout();
        var applier = UiScaleApplier.Instance;
        if (applier is null)
            return;
        var chip = new Vector2(applier.ScaleSize(KeyBindingsLayout.ChipWidth), applier.ScaleSize(KeyBindingsLayout.RowHeight));
        var remove = new Vector2(applier.ScaleSize(KeyBindingsLayout.RemoveWidth), applier.ScaleSize(KeyBindingsLayout.RowHeight));
        foreach (var shell in _rows.Values)
        {
            foreach (var chipButton in shell.Chips)
                chipButton.CustomMinimumSize = chip;
            foreach (var removeButton in shell.Removes)
                removeButton.CustomMinimumSize = remove;
        }
    }

    protected override void OnClosePressed()
    {
        _editor?.Cancel();
        Rerender();
        base.OnClosePressed();
    }

    private void OnCancelPressed()
    {
        OnClosePressed();
    }

    private void OnSearchChanged(string text)
    {
        _editor!.Search = text;
        Rerender();
    }

    private void OnResetAllPressed()
    {
        _editor!.ResetAll();
        Rerender();
    }

    private void OnApplyPressed()
    {
        _editor!.Apply();
        Rerender();
    }

    private void OnCancelCapturePressed()
    {
        _captureOverlay.Visible = false;
    }

    private void OpenCapturePrompt()
    {
        _capturePrompt.Text = "Press a key…";
        _captureOverlay.Visible = true;
    }

    private void BuildRows()
    {
        foreach (var action in InputActionCatalog.Actions)
        {
            if (!_categoryActions.ContainsKey(action.Category))
            {
                var header = new Label
                {
                    Name = $"Header_{_categoryHeaders.Count}",
                    Text = action.Category,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                header.AddThemeColorOverride("font_color", HeaderColor);
                _categoryHeaders.Add(action.Category, header);
                _categoryActions.Add(action.Category, new List<string>());
                _rowsBox.AddChild(header);
            }
            _categoryActions[action.Category].Add(action.Name);

            var root = new HBoxContainer
            {
                Name = $"Row_{action.Name}",
                CustomMinimumSize = new Vector2(0f, KeyBindingsLayout.RowHeight)
            };
            root.AddThemeConstantOverride("separation", (int)KeyBindingsLayout.RowSeparation);

            var label = new Label
            {
                Name = "ActionLabel",
                Text = action.Label,
                MouseFilter = MouseFilterEnum.Ignore,
                VerticalAlignment = VerticalAlignment.Center,
                SizeFlagsHorizontal = SizeFlags.Fill | SizeFlags.Expand,
                CustomMinimumSize = new Vector2(KeyBindingsLayout.ActionLabelWidth, 0f)
            };
            label.ClipText = true;

            var chips = new HBoxContainer
            {
                Name = "ChipsBox",
                Alignment = BoxContainer.AlignmentMode.End
            };
            chips.AddThemeConstantOverride("separation", (int)KeyBindingsLayout.ChipSeparation);

            var unbound = new Label
            {
                Name = "UnboundLabel",
                Text = "Unbound",
                MouseFilter = MouseFilterEnum.Ignore,
                VerticalAlignment = VerticalAlignment.Center
            };
            unbound.AddThemeColorOverride("font_color", UnboundColor);

            var add = new Button
            {
                Name = "AddBindingButton",
                Text = "Add Binding",
                CustomMinimumSize = new Vector2(KeyBindingsLayout.ButtonWidth, KeyBindingsLayout.RowHeight)
            };
            add.Pressed += OpenCapturePrompt;

            var reset = new Button
            {
                Name = "ResetActionButton",
                Text = "Reset Action",
                CustomMinimumSize = new Vector2(KeyBindingsLayout.ButtonWidth, KeyBindingsLayout.RowHeight)
            };
            reset.Pressed += () =>
            {
                _editor!.ResetAction(action.Name);
                Rerender();
            };

            root.AddChild(label);
            root.AddChild(chips);
            root.AddChild(unbound);
            root.AddChild(add);
            root.AddChild(reset);
            _rowsBox.AddChild(root);

            _rows.Add(action.Name, new RowShell
            {
                Root = root,
                ChipsBox = chips,
                UnboundLabel = unbound
            });
        }
    }

    private void Rerender()
    {
        if (_editor is null)
            return;

        var rowsByAction = new Dictionary<string, List<KeyBindingEditorRow>>(InputActionCatalog.Actions.Count);
        foreach (var group in _editor.Groups)
            foreach (var row in group.Rows)
            {
                if (!rowsByAction.TryGetValue(row.Action.Name, out var list))
                    rowsByAction[row.Action.Name] = list = new List<KeyBindingEditorRow>();
                list.Add(row);
            }

        foreach (var (actionName, shell) in _rows)
        {
            if (!rowsByAction.TryGetValue(actionName, out var rows))
            {
                shell.Root.Visible = false;
                continue;
            }
            shell.Root.Visible = true;
            shell.UnboundLabel.Visible = rows.Count == 0;
            RebuildChips(shell, actionName, rows);
        }

        foreach (var (category, header) in _categoryHeaders)
        {
            bool any = false;
            foreach (var actionName in _categoryActions[category])
            {
                if (rowsByAction.ContainsKey(actionName))
                {
                    any = true;
                    break;
                }
            }
            header.Visible = any;
        }

        if (_search.Text != _editor.Search)
            _search.Text = _editor.Search;
        _status.Text = _editor.StatusText;
    }

    private void RebuildChips(RowShell shell, string actionName, List<KeyBindingEditorRow> rows)
    {
        foreach (var child in shell.ChipsBox.GetChildren())
        {
            shell.ChipsBox.RemoveChild(child);
            child.QueueFree();
        }
        shell.Chips.Clear();
        shell.Removes.Clear();

        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Binding is not { } binding)
                continue;

            var chip = new Button
            {
                Name = $"Chip_{i}",
                Text = InputBindingDisplay.Format(binding, _labels),
                CustomMinimumSize = new Vector2(Px(KeyBindingsLayout.ChipWidth), Px(KeyBindingsLayout.RowHeight))
            };
            if (rows[i].Conflicts.Count > 0)
            {
                chip.TooltipText = ConflictTooltip(actionName, rows[i].Conflicts);
                chip.AddThemeColorOverride("font_color", ConflictColor);
                chip.AddThemeColorOverride("font_hover_color", ConflictColor);
            }
            chip.Pressed += OpenCapturePrompt;
            shell.ChipsBox.AddChild(chip);
            shell.Chips.Add(chip);

            var remove = new Button
            {
                Name = $"Remove_{i}",
                Text = "×",
                CustomMinimumSize = new Vector2(Px(KeyBindingsLayout.RemoveWidth), Px(KeyBindingsLayout.RowHeight))
            };
            remove.Pressed += () =>
            {
                _editor!.Remove(actionName, i);
                Rerender();
            };
            shell.ChipsBox.AddChild(remove);
            shell.Removes.Add(remove);
        }
    }

    private static int Px(float basePx) => UiScaleApplier.Instance?.ScaleSize(basePx) ?? (int)basePx;

    private static string ConflictTooltip(string actionName, IReadOnlyList<InputBindingConflict> conflicts)
    {
        var others = new List<string>();
        foreach (var conflict in conflicts)
        {
            var other = conflict.FirstAction == actionName ? conflict.SecondAction : conflict.FirstAction;
            if (!others.Contains(other))
                others.Add(other);
        }
        var labels = new List<string>(others.Count);
        foreach (var other in others)
            labels.Add(CatalogLabel(other));
        return "Conflicts with " + string.Join(", ", labels);
    }

    private static string CatalogLabel(string actionName)
    {
        foreach (var action in InputActionCatalog.Actions)
            if (action.Name == actionName)
                return action.Label;
        return actionName;
    }

    private sealed class RowShell
    {
        public HBoxContainer Root = null!;
        public HBoxContainer ChipsBox = null!;
        public Label UnboundLabel = null!;
        public readonly List<Button> Chips = new();
        public readonly List<Button> Removes = new();
    }
}
