using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Goose2Client;
using Goose2Client.InputBindings;

namespace Goose2Client.UI;

public partial class KeyBindingsWindow : BaseWindow
{
    protected override bool DefaultVisible => false;
    protected override bool Resizable => true;
    protected override Vector2 MinResizeSize => KeyBindingsLayout.MinSize;

    private static readonly Texture2D ResetIcon = GD.Load<Texture2D>("res://Assets/UI/reset-arrow.svg");
    private static readonly string[] SlotTitles = ["Primary", "Secondary", "Alternate"];

    private static readonly Color UnboundColor = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Color HeaderColor = new(0.81f, 0.69f, 0.45f, 1f);
    private static readonly Color ConflictColor = new(1f, 0.55f, 0.5f, 1f);
    private static readonly Color StripeColor = new(1f, 1f, 1f, 0.03f);
    private static readonly Color KeyTextColor = new(0.93f, 0.94f, 0.97f, 1f);
    private static readonly Color KeyBg = new(0.14f, 0.17f, 0.25f, 1f);
    private static readonly Color KeyBgHover = new(0.19f, 0.23f, 0.34f, 1f);
    private static readonly Color KeyBgPressed = new(0.04f, 0.24f, 0.43f, 1f);
    private static readonly Color KeyBorder = new(0.34f, 0.39f, 0.52f, 1f);
    private static readonly Color KeyBorderHover = new(0.62f, 0.68f, 0.82f, 1f);
    private static readonly Color ModifiedBorder = new(0.32f, 0.77f, 1f, 1f);
    private static readonly Color ConflictBorder = new(0.85f, 0.4f, 0.36f, 1f);
    private static readonly Color EmptyBg = new(1f, 1f, 1f, 0.02f);
    private static readonly Color EmptyBorder = new(0.26f, 0.3f, 0.4f, 0.7f);
    private static readonly Color AddBorderHover = new(0.93f, 0.73f, 0.34f, 1f);
    private static readonly Color RemoveColor = new(0.6f, 0.63f, 0.72f, 1f);
    private static readonly Color RemoveBgHover = new(0.64f, 0.18f, 0.18f, 0.6f);

    private KeyBindingEditorState? _editor;
    private readonly IInputBindingLabelProvider _labels = new GodotInputBindingLabelProvider();
    private LineEdit _search = null!;
    private TextureRect _searchIcon = null!;
    private VBoxContainer _rowsBox = null!;
    private Label _status = null!;
    private Button _resetAll = null!;
    private Button _apply = null!;
    private Button _cancel = null!;
    private Panel _captureOverlay = null!;
    private Label _capturePrompt = null!;
    private Button _cancelCapture = null!;
    private readonly Dictionary<string, RowShell> _rows = new();
    private readonly Dictionary<string, CategoryHeader> _categoryHeaders = new();
    private readonly Dictionary<string, List<string>> _categoryActions = new();
    private bool _pendingOpen;
    private KeyBindingCaptureState? _capture;
    private InputBindingSuppressionLease? _lease;
    private string? _capturingAction;
    private int _capturingIndex = -1;
    private bool _candidateSaved;
    private bool _pendingApply;
    private bool _footerLocked;
    private float _stylesFactor = -1f;
    private readonly Dictionary<string, StyleBox> _styles = new();

    public override void _Ready()
    {
        base._Ready();

        Visible = false;

        _search = GetNode<LineEdit>("Content/RootBox/SearchRow/SearchField");
        _searchIcon = GetNode<TextureRect>("Content/RootBox/SearchRow/SearchField/SearchIcon");
        _rowsBox = GetNode<VBoxContainer>("Content/RootBox/ScrollHost/RowsBox");
        _status = GetNode<Label>("Content/RootBox/FooterRow/StatusLabel");
        _resetAll = GetNode<Button>("Content/RootBox/FooterRow/ResetAllButton");
        _apply = GetNode<Button>("Content/RootBox/FooterRow/ApplyButton");
        _cancel = GetNode<Button>("Content/RootBox/FooterRow/CancelButton");
        _captureOverlay = GetNode<Panel>("CaptureOverlay");
        _capturePrompt = GetNode<Label>("CaptureOverlay/CaptureCenter/CaptureCard/CaptureBox/CapturePrompt");
        _cancelCapture = GetNode<Button>("CaptureOverlay/CaptureCenter/CaptureCard/CaptureBox/CancelCaptureButton");

        var service = GameManager.Instance.InputBindings;
        _editor = new KeyBindingEditorState(service, service.StartupWarning);

        BuildRows();

        _search.TextChanged += OnSearchChanged;
        _resetAll.Pressed += OnResetAllPressed;
        _apply.Pressed += OnApplyPressed;
        _cancel.Pressed += OnCancelPressed;
        _cancelCapture.Pressed += OnCancelCapturePressed;
        service.CaptureGateReleased += OnCaptureGateReleased;
        service.SuppressionRestored += OnSuppressionRestored;
        VisibilityChanged += OnVisibilityChanged;

        Rerender();

        ScaleRegister();
    }

    public override void _ExitTree()
    {
        RequestSafeRestore();
        VisibilityChanged -= OnVisibilityChanged;
        var service = GameManager.Instance?.InputBindings;
        if (service != null)
        {
            service.CaptureGateReleased -= OnCaptureGateReleased;
            service.SuppressionRestored -= OnSuppressionRestored;
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_capture is { WaitingInitiating: true })
        {
            _capture.Tick();
            SetCapturePrompt();
        }
    }

    private void OnVisibilityChanged()
    {
        if (!Visible)
            RequestSafeRestore();
    }

    public override void _Input(InputEvent @event)
    {
        var capture = _capture;
        if (capture is null || !capture.IsActive)
        {
            base._Input(@event);
            return;
        }

        switch (@event)
        {
            case InputEventKey key:
                capture.KeyEvent(
                    key.PhysicalKeycode,
                    key.Pressed,
                    key.Echo,
                    key.CtrlPressed,
                    key.ShiftPressed,
                    key.AltPressed,
                    key.MetaPressed);
                break;
            case InputEventMouseButton mouse:
                if (mouse.ButtonIndex == MouseButton.Left && IsOverCancelCapture(mouse.Position))
                {
                    base._Input(@event);
                    return;
                }
                capture.MouseButtonEvent(
                    mouse.ButtonIndex,
                    mouse.Pressed,
                    mouse.CtrlPressed,
                    mouse.ShiftPressed,
                    mouse.AltPressed,
                    mouse.MetaPressed);
                break;
            case InputEventJoypadButton joypadButton:
                capture.JoypadButtonEvent(joypadButton.Device, joypadButton.ButtonIndex, joypadButton.Pressed);
                break;
            case InputEventJoypadMotion joypadMotion:
                capture.JoypadAxisEvent(joypadMotion.Device, joypadMotion.Axis, joypadMotion.AxisValue);
                break;
            default:
                base._Input(@event);
                return;
        }

        // SetInputAsHandled stops GUI propagation only; the service-owned empty
        // InputMap lease is what keeps capture input from firing mapped actions.
        GetViewport().SetInputAsHandled();
        AfterCaptureTransition();
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
        Rerender();
    }

    protected override void ApplyHoverOpacity(float alpha)
    {
    }

    protected override void OnClosePressed()
    {
        RequestSafeRestore();
        _editor?.Cancel();
        Rerender();
        _captureOverlay.Visible = false;
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
        if (_capture is { IsActive: true })
        {
            _pendingApply = true;
            RequestSafeRestore();
            return;
        }

        _editor!.Apply();
        Rerender();
    }

    private void OnCancelCapturePressed()
    {
        RequestSafeRestore();
    }

    private void OnSuppressionRestored()
    {
        EndCaptureSession();
        _lease = null;
        if (_pendingApply)
        {
            _pendingApply = false;
            _editor!.Apply();
            Rerender();
        }
    }

    private void RequestSafeRestore()
    {
        var lease = _lease;
        if (lease is null)
            return;
        var gate = _capture is { IsActive: true } ? _capture.Cancel() : InputReleaseGate.Immediate;
        lease.RequestRestore(gate);
    }

    private void OpenCapturePrompt(string action, int index = -1)
    {
        if (_capture is { IsActive: true })
            return;

        var service = GameManager.Instance.InputBindings;
        if (service.CaptureGateHeld)
            return;

        var capture = new KeyBindingCaptureState();
        capture.Begin();
        var lease = service.BeginSuppression();

        _capture = capture;
        _lease = lease;
        _capturingAction = action;
        _capturingIndex = index;
        _candidateSaved = false;

        foreach (var device in Input.GetConnectedJoypads())
            for (var axis = 1; axis < (int)JoyAxis.Max; axis++)
                capture.SampleAxis(device, (JoyAxis)axis, Input.GetJoyAxis(device, (JoyAxis)axis));

        SetFooterEnabled(false);
        SetCapturePrompt();
        _captureOverlay.Visible = true;
    }

    private void AfterCaptureTransition()
    {
        var capture = _capture!;
        if (!capture.IsActive)
        {
            EndCaptureSession();
            return;
        }

        if (capture.Candidate is { } candidate && !_candidateSaved)
        {
            _candidateSaved = true;
            if (_capturingIndex < 0)
                _editor!.Add(_capturingAction!, candidate);
            else
                _editor!.Replace(_capturingAction!, _capturingIndex, candidate);
            Rerender();
            _lease!.RequestRestore(capture.CandidateGate);
        }

        SetCapturePrompt();
    }

    private void EndCaptureSession()
    {
        _capture = null;
        _capturingAction = null;
        _capturingIndex = -1;
        _candidateSaved = false;
        _captureOverlay.Visible = false;
        SetFooterEnabled(true);
    }

    private void SetCapturePrompt()
    {
        if (_capture is { } capture)
            _capturePrompt.Text = capture.Prompt;
    }

    private void SetFooterEnabled(bool enabled)
    {
        _footerLocked = !enabled;
        _cancel.Disabled = !enabled;
        UpdateFooterButtons();
    }

    private void UpdateFooterButtons()
    {
        if (_editor is null)
            return;
        _apply.Disabled = _footerLocked || !_editor.IsDirty;
        _resetAll.Disabled = _footerLocked || InputActionCatalog.Actions.All(a => _editor.IsFactoryDefault(a.Name));
    }

    private bool IsOverCancelCapture(Vector2 position) =>
        _cancelCapture.Visible && _cancelCapture.GetGlobalRect().HasPoint(position);

    private void BuildRows()
    {
        BuildColumnHeader();

        foreach (var action in InputActionCatalog.Actions)
        {
            if (!_categoryActions.ContainsKey(action.Category))
            {
                var header = BuildCategoryHeader(action.Category);
                _categoryHeaders.Add(action.Category, header);
                _categoryActions.Add(action.Category, new List<string>());
                _rowsBox.AddChild(header.Root);
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
                ClipText = true,
                CustomMinimumSize = new Vector2(KeyBindingsLayout.ActionLabelWidth, 0f)
            };

            var chips = new HBoxContainer
            {
                Name = "ChipsBox",
                SizeFlagsVertical = SizeFlags.ShrinkCenter
            };
            chips.AddThemeConstantOverride("separation", (int)KeyBindingsLayout.ChipSeparation);

            var reset = new Button
            {
                Name = "ResetActionButton",
                ThemeTypeVariation = "IconButton",
                Icon = ResetIcon,
                ExpandIcon = true,
                TextureFilter = TextureFilterEnum.LinearWithMipmaps,
                TooltipText = "Reset to default",
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                CustomMinimumSize = new Vector2(KeyBindingsLayout.ResetWidth, KeyBindingsLayout.ResetWidth)
            };
            reset.Pressed += () =>
            {
                _editor!.ResetAction(action.Name);
                Rerender();
            };

            root.AddChild(Spacer(KeyBindingsLayout.RowPadding));
            root.AddChild(label);
            root.AddChild(chips);
            root.AddChild(new Control { Name = "Fill", SizeFlagsHorizontal = SizeFlags.Expand | SizeFlags.Fill, MouseFilter = MouseFilterEnum.Ignore });
            root.AddChild(reset);
            root.AddChild(Spacer(KeyBindingsLayout.RowPadding));
            _rowsBox.AddChild(root);

            var shell = new RowShell
            {
                Root = root,
                ActionLabel = label,
                ChipsBox = chips,
                ResetButton = reset
            };
            root.Draw += () =>
            {
                if (shell.Striped)
                    root.DrawRect(new Rect2(Vector2.Zero, root.Size), StripeColor);
            };
            _rows.Add(action.Name, shell);
        }
    }

    private void BuildColumnHeader()
    {
        var header = GetNode<HBoxContainer>("Content/RootBox/ColumnHeader");
        header.AddThemeConstantOverride("separation", (int)KeyBindingsLayout.RowSeparation);
        header.AddChild(Spacer(KeyBindingsLayout.RowPadding));
        header.AddChild(new Label
        {
            Text = "Action",
            ThemeTypeVariation = "SectionHeader",
            CustomMinimumSize = new Vector2(KeyBindingsLayout.ActionLabelWidth, 0f)
        });

        var slots = new HBoxContainer();
        slots.AddThemeConstantOverride("separation", (int)KeyBindingsLayout.ChipSeparation);
        foreach (var title in SlotTitles)
            slots.AddChild(new Label
            {
                Text = title,
                ThemeTypeVariation = "SectionHeader",
                HorizontalAlignment = HorizontalAlignment.Center,
                CustomMinimumSize = new Vector2(KeyBindingsLayout.ChipWidth, 0f)
            });
        header.AddChild(slots);
    }

    private CategoryHeader BuildCategoryHeader(string category)
    {
        var root = new VBoxContainer
        {
            Name = $"Header_{_categoryHeaders.Count}",
            MouseFilter = MouseFilterEnum.Ignore
        };
        root.AddThemeConstantOverride("separation", 2);

        var gap = new Control
        {
            Name = "Gap",
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(0f, KeyBindingsLayout.HeaderGap)
        };

        var line = new HBoxContainer { Name = "Line", MouseFilter = MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", (int)KeyBindingsLayout.RowSeparation);

        var title = new Label
        {
            Name = "Title",
            Text = category.ToUpperInvariant(),
            MouseFilter = MouseFilterEnum.Ignore
        };
        title.AddThemeColorOverride("font_color", HeaderColor);

        var rule = new HSeparator
        {
            SizeFlagsHorizontal = SizeFlags.Expand | SizeFlags.Fill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore
        };

        line.AddChild(Spacer(KeyBindingsLayout.RowPadding));
        line.AddChild(title);
        line.AddChild(rule);
        root.AddChild(gap);
        root.AddChild(line);
        root.AddChild(new Control { MouseFilter = MouseFilterEnum.Ignore });
        return new CategoryHeader { Root = root, Gap = gap };
    }

    private static Control Spacer(float width) => new()
    {
        MouseFilter = MouseFilterEnum.Ignore,
        CustomMinimumSize = new Vector2(width, 0f)
    };

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

        var firstVisibleHeader = true;
        foreach (var (category, header) in _categoryHeaders)
        {
            var striped = false;
            var any = false;
            foreach (var actionName in _categoryActions[category])
            {
                var shell = _rows[actionName];
                if (!rowsByAction.TryGetValue(actionName, out var rows))
                {
                    shell.Root.Visible = false;
                    continue;
                }

                any = true;
                shell.Root.Visible = true;
                SetStriped(shell, striped);
                striped = !striped;

                var unbound = rows.All(r => r.Binding is null);
                if (unbound)
                    shell.ActionLabel.AddThemeColorOverride("font_color", UnboundColor);
                else
                    shell.ActionLabel.RemoveThemeColorOverride("font_color");
                shell.ResetButton.Visible = !_editor.IsFactoryDefault(actionName);
                RebuildChips(shell, actionName, rows);
            }

            header.Root.Visible = any;
            header.Gap.Visible = any && !firstVisibleHeader;
            if (any)
                firstVisibleHeader = false;
        }

        if (_search.Text != _editor.Search)
            _search.Text = _editor.Search;
        _searchIcon.Visible = _search.Text.Length == 0;
        _status.Text = _editor.StatusText;
        if (_editor.Error is not null)
            _status.AddThemeColorOverride("font_color", ConflictColor);
        else
            _status.RemoveThemeColorOverride("font_color");
        UpdateFooterButtons();
    }

    private static void SetStriped(RowShell shell, bool striped)
    {
        if (shell.Striped == striped)
            return;
        shell.Striped = striped;
        shell.Root.QueueRedraw();
    }

    private void RebuildChips(RowShell shell, string actionName, List<KeyBindingEditorRow> rows)
    {
        foreach (var child in shell.ChipsBox.GetChildren())
        {
            shell.ChipsBox.RemoveChild(child);
            child.QueueFree();
        }

        var slotSize = new Vector2(Px(KeyBindingsLayout.ChipWidth), Px(KeyBindingsLayout.ChipHeight));
        var bound = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Binding is not { } binding)
                continue;

            var index = i;
            var text = InputBindingDisplay.Format(binding, _labels);
            var conflicted = rows[i].Conflicts.Count > 0;
            var kind = conflicted ? "conflict" : _editor!.IsFactoryBinding(actionName, binding) ? "key" : "modified";

            var chip = new Button
            {
                Name = $"Chip_{i}",
                Text = text,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                CustomMinimumSize = slotSize,
                TooltipText = conflicted
                    ? $"{text}\n{ConflictTooltip(actionName, rows[i].Conflicts)}"
                    : $"{text}\nClick to rebind, right-click to remove"
            };
            ApplyChipStyles(chip, kind, conflicted ? ConflictColor : KeyTextColor);
            chip.Pressed += () => OpenCapturePrompt(actionName, index);
            chip.GuiInput += @event =>
            {
                if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
                    RemoveBinding(actionName, index);
            };

            var remove = new Button
            {
                Name = $"Remove_{i}",
                Text = "×",
                FocusMode = FocusModeEnum.None,
                TooltipText = "Remove binding"
            };
            remove.SetAnchorsPreset(LayoutPreset.RightWide);
            remove.OffsetLeft = -Px(KeyBindingsLayout.RemoveWidth);
            remove.OffsetRight = 0f;
            remove.OffsetTop = 0f;
            remove.OffsetBottom = 0f;
            var clear = Style("clear");
            remove.AddThemeStyleboxOverride("normal", clear);
            remove.AddThemeStyleboxOverride("pressed", clear);
            remove.AddThemeStyleboxOverride("focus", clear);
            remove.AddThemeStyleboxOverride("hover", Style("remove_hover"));
            remove.AddThemeColorOverride("font_color", RemoveColor);
            remove.AddThemeColorOverride("font_hover_color", Colors.White);
            remove.AddThemeColorOverride("font_pressed_color", Colors.White);
            remove.Pressed += () => RemoveBinding(actionName, index);
            chip.AddChild(remove);

            shell.ChipsBox.AddChild(chip);
            bound++;
        }

        var slots = Math.Max(KeyBindingsLayout.SlotColumns, bound + 1);
        for (var slot = bound; slot < slots; slot++)
        {
            if (slot == bound)
            {
                var add = new Button
                {
                    Name = "AddBindingButton",
                    Text = "+",
                    CustomMinimumSize = slotSize,
                    TooltipText = "Add binding"
                };
                add.AddThemeStyleboxOverride("normal", Style("empty"));
                add.AddThemeStyleboxOverride("hover", Style("add_hover"));
                add.AddThemeStyleboxOverride("pressed", Style("key_pressed"));
                add.AddThemeColorOverride("font_color", RemoveColor);
                add.AddThemeColorOverride("font_hover_color", AddBorderHover);
                add.Pressed += () => OpenCapturePrompt(actionName, -1);
                shell.ChipsBox.AddChild(add);
                continue;
            }

            var empty = new Panel
            {
                Name = $"EmptySlot_{slot}",
                MouseFilter = MouseFilterEnum.Ignore,
                CustomMinimumSize = slotSize
            };
            empty.AddThemeStyleboxOverride("panel", Style("empty"));
            shell.ChipsBox.AddChild(empty);
        }
    }

    private void RemoveBinding(string actionName, int index)
    {
        _editor!.Remove(actionName, index);
        Rerender();
    }

    private void ApplyChipStyles(Button chip, string kind, Color fontColor)
    {
        chip.AddThemeStyleboxOverride("normal", Style(kind));
        chip.AddThemeStyleboxOverride("hover", Style(kind + "_hover"));
        chip.AddThemeStyleboxOverride("pressed", Style("key_pressed"));
        chip.AddThemeColorOverride("font_color", fontColor);
        chip.AddThemeColorOverride("font_hover_color", fontColor);
        chip.AddThemeColorOverride("font_focus_color", fontColor);
    }

    private StyleBox Style(string name)
    {
        var factor = UiScaleApplier.Instance?.Factor ?? 1f;
        if (_stylesFactor != factor)
        {
            _styles.Clear();
            _stylesFactor = factor;
        }
        if (_styles.TryGetValue(name, out var cached))
            return cached;

        var inset = Px(KeyBindingsLayout.RemoveWidth);
        StyleBox style = name switch
        {
            "key" => Keycap(KeyBg, KeyBorder, inset),
            "key_hover" => Keycap(KeyBgHover, KeyBorderHover, inset),
            "modified" => Keycap(KeyBg, ModifiedBorder, inset),
            "modified_hover" => Keycap(KeyBgHover, ModifiedBorder, inset),
            "conflict" => Keycap(KeyBg, ConflictBorder, inset),
            "conflict_hover" => Keycap(KeyBgHover, ConflictBorder, inset),
            "key_pressed" => Keycap(KeyBgPressed, ModifiedBorder, inset),
            "empty" => Keycap(EmptyBg, EmptyBorder, 0, bottom: 1),
            "add_hover" => Keycap(KeyBg, AddBorderHover, 0, bottom: 1),
            "remove_hover" => new StyleBoxFlat
            {
                BgColor = RemoveBgHover,
                CornerRadiusTopRight = 3,
                CornerRadiusBottomRight = 3
            },
            _ => new StyleBoxEmpty()
        };
        _styles[name] = style;
        return style;
    }

    private static StyleBoxFlat Keycap(Color bg, Color border, int inset, int bottom = 2)
    {
        var style = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = bottom,
            ContentMarginLeft = inset,
            ContentMarginRight = inset
        };
        style.SetCornerRadiusAll(3);
        return style;
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
        public Label ActionLabel = null!;
        public HBoxContainer ChipsBox = null!;
        public Button ResetButton = null!;
        public bool Striped;
    }

    private sealed class CategoryHeader
    {
        public VBoxContainer Root = null!;
        public Control Gap = null!;
    }
}
