using System;
using System.IO;
using Xunit;

namespace Goose2Client.Tests;

public class KeyBindingsWindowContractTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.godot")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find project.godot");
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepositoryRoot(), relative));

    private static string WindowSource() => Read("Scripts/UI/KeyBindingsWindow.cs");

    private static string OptionsSource() => Read("Scripts/UI/OptionsWindow.cs");

    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"signature not found: {signature}");
        int brace = source.IndexOf('{', start);
        Assert.True(brace > start, $"no brace after signature: {signature}");
        int depth = 0;
        for (int i = brace; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}' && --depth == 0)
                return source.Substring(start, i - start + 1);
        }
        throw new InvalidOperationException($"unbalanced braces after: {signature}");
    }

    [Fact]
    public void Window_OverridesResizableDefaults()
    {
        var src = WindowSource();
        Assert.Contains("protected override bool DefaultVisible => false;", src, StringComparison.Ordinal);
        Assert.Contains("protected override bool Resizable => true;", src, StringComparison.Ordinal);
        Assert.Contains("protected override Vector2 MinResizeSize => KeyBindingsLayout.MinSize;", src, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutConstants_Define760x520DesignAnd520x320Minimum()
    {
        var src = Read("Scripts/UI/KeyBindingsLayout.cs");
        Assert.Contains("new(760f, 520f)", src, StringComparison.Ordinal);
        Assert.Contains("new(520f, 320f)", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Ready_BuildsControlsEditorAndRowsBeforeScaleRegister()
    {
        var body = MethodBody(WindowSource(), "public override void _Ready()");
        int scale = body.IndexOf("ScaleRegister()", StringComparison.Ordinal);
        Assert.True(scale >= 0, "ScaleRegister() not called in _Ready");

        int search = body.IndexOf("GetNode<LineEdit>", StringComparison.Ordinal);
        Assert.True(search >= 0 && search < scale, "search control must be wired before ScaleRegister");

        int editor = body.IndexOf("new KeyBindingEditorState(", StringComparison.Ordinal);
        Assert.True(editor >= 0 && editor < scale, "editor must be created before ScaleRegister");

        int rows = body.IndexOf("BuildRows()", StringComparison.Ordinal);
        Assert.True(rows >= 0 && rows < scale, "stable catalog rows must be built before ScaleRegister");
    }

    [Fact]
    public void Ready_HidesWindowAfterSavedVisibilityLoads()
    {
        var body = MethodBody(WindowSource(), "public override void _Ready()");
        int baseReady = body.IndexOf("base._Ready()", StringComparison.Ordinal);
        int hide = body.IndexOf("Visible = false", StringComparison.Ordinal);
        Assert.True(baseReady >= 0 && hide > baseReady, "Visible = false must follow base._Ready()");
    }

    [Fact]
    public void Open_ActivatesVisibleWindowWithoutDiscardingDraft()
    {
        var body = MethodBody(WindowSource(), "public void Open()");
        int visible = body.IndexOf("if (Visible)", StringComparison.Ordinal);
        int activate = body.IndexOf("Activate();", StringComparison.Ordinal);
        int clone = body.IndexOf(".Open();", StringComparison.Ordinal);
        Assert.True(visible >= 0 && visible < activate, "visible guard must precede Activate");
        Assert.True(activate < clone, "staged edits must not be re-cloned while visible");
        Assert.DoesNotContain("Cancel", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_DefersWhileServiceCaptureGateHeld()
    {
        var body = MethodBody(WindowSource(), "public void Open()");
        Assert.Contains("CaptureGateHeld", body, StringComparison.Ordinal);
        Assert.Contains("_pendingOpen = true", body, StringComparison.Ordinal);

        var src = WindowSource();
        Assert.Contains("CaptureGateReleased += OnCaptureGateReleased", src, StringComparison.Ordinal);
        Assert.Contains("CaptureGateReleased -= OnCaptureGateReleased", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_ExposesMinimalCaptureGateSeam()
    {
        var src = Read("Scripts/InputBindings/InputBindingService.cs");
        Assert.Contains("public bool CaptureGateHeld", src, StringComparison.Ordinal);
        Assert.Contains("public event Action? CaptureGateReleased", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Mutations_GoThroughEditorOnlyNeverServiceOrInputMap()
    {
        var src = WindowSource();
        Assert.DoesNotContain("new InputBindingService(", src, StringComparison.Ordinal);
        Assert.DoesNotContain("InputBindings.Apply(", src, StringComparison.Ordinal);
        Assert.DoesNotContain("InputMap.", src, StringComparison.Ordinal);
        Assert.Contains(".Apply();", src, StringComparison.Ordinal);
        Assert.Contains(".Cancel();", src, StringComparison.Ordinal);
        Assert.Contains(".Search = ", src, StringComparison.Ordinal);
        Assert.Contains(".ResetAll();", src, StringComparison.Ordinal);
        Assert.Contains(".ResetAction(", src, StringComparison.Ordinal);
        Assert.Contains(".Remove(", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_LeavesWindowOpenAndCancel_DiscardsAndHides()
    {
        var apply = MethodBody(WindowSource(), "private void OnApplyPressed()");
        Assert.DoesNotContain("Hide(", apply, StringComparison.Ordinal);

        var cancel = MethodBody(WindowSource(), "private void OnCancelPressed()");
        Assert.Contains("OnClosePressed()", cancel, StringComparison.Ordinal);
    }

    [Fact]
    public void Close_DiscardsDraftWithoutWriting()
    {
        var close = MethodBody(WindowSource(), "protected override void OnClosePressed()");
        Assert.Contains(".Cancel();", close, StringComparison.Ordinal);
        Assert.Contains("base.OnClosePressed()", close, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_SafeRestoreRequestedOnCloseTeardownAndHide()
    {
        var close = MethodBody(WindowSource(), "protected override void OnClosePressed()");
        Assert.Contains("RequestSafeRestore()", close, StringComparison.Ordinal);

        var exit = MethodBody(WindowSource(), "public override void _ExitTree()");
        Assert.Contains("RequestSafeRestore()", exit, StringComparison.Ordinal);

        var src = WindowSource();
        Assert.Contains("VisibilityChanged += OnVisibilityChanged", src, StringComparison.Ordinal);
        var handler = MethodBody(src, "private void OnVisibilityChanged()");
        Assert.Contains("!Visible", handler, StringComparison.Ordinal);
        Assert.Contains("RequestSafeRestore()", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("public override void _Notification", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Input_MarksCaptureEventsHandledAndExemptsCancelCaptureButton()
    {
        var input = MethodBody(WindowSource(), "public override void _Input(InputEvent @event)");
        Assert.Contains("SetInputAsHandled()", input, StringComparison.Ordinal);
        Assert.Contains("IsOverCancelCapture(mouse.Position)", input, StringComparison.Ordinal);

        var exemption = MethodBody(WindowSource(), "private bool IsOverCancelCapture(Vector2 position)");
        Assert.Contains("_cancelCapture.Visible", exemption, StringComparison.Ordinal);
        Assert.Contains("_cancelCapture.GetGlobalRect().HasPoint(position)", exemption, StringComparison.Ordinal);
    }

    [Fact]
    public void ExitTree_UnsubscribesServiceAndVisibilityHandlers()
    {
        var exit = MethodBody(WindowSource(), "public override void _ExitTree()");
        Assert.Contains("CaptureGateReleased -= OnCaptureGateReleased", exit, StringComparison.Ordinal);
        Assert.Contains("SuppressionRestored -= OnSuppressionRestored", exit, StringComparison.Ordinal);
        Assert.Contains("VisibilityChanged -= OnVisibilityChanged", exit, StringComparison.Ordinal);
    }

    [Fact]
    public void Rerender_UnboundLabelTestsForNullBindingsNotRowCount()
    {
        var body = MethodBody(WindowSource(), "private void Rerender()");
        Assert.Contains("Binding is null", body, StringComparison.Ordinal);
        Assert.DoesNotContain("rows.Count == 0", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Close_HidesStaleCaptureOverlay()
    {
        var close = MethodBody(WindowSource(), "protected override void OnClosePressed()");
        Assert.Contains("_captureOverlay.Visible = false", close, StringComparison.Ordinal);
    }

    [Fact]
    public void Relayout_ReappliesScaledSizesToDynamicChips()
    {
        var body = MethodBody(WindowSource(), "public override void Relayout()");
        Assert.Contains("Rerender()", body, StringComparison.Ordinal);
        Assert.Contains("ScaleSize", MethodBody(WindowSource(), "private static int Px(float basePx)"), StringComparison.Ordinal);
    }

    [Fact]
    public void Options_OwnsOnlyAnEntryButtonNotASecondWindow()
    {
        var src = OptionsSource();
        Assert.DoesNotContain("KeyBindingsWindow", src, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyBindingsWindow.tscn", src, StringComparison.Ordinal);
        Assert.DoesNotContain("Instantiate", src, StringComparison.Ordinal);
        Assert.Contains("public Action? OpenKeyBindings", src, StringComparison.Ordinal);
        Assert.Contains("GetNode<Button>(\"Content/KeyBindingsButton\")", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_Ready_WiresButtonBeforeScaleRegisterAndWithoutHudLookup()
    {
        var body = MethodBody(OptionsSource(), "public override void _Ready()");
        int button = body.IndexOf("KeyBindingsButton", StringComparison.Ordinal);
        int scale = body.IndexOf("ScaleRegister()", StringComparison.Ordinal);
        Assert.True(button >= 0 && button < scale, "button must be wired before ScaleRegister");

        var handler = MethodBody(OptionsSource(), "private void OnKeyBindingsPressed()");
        Assert.Contains("OpenKeyBindings", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("Hud", handler, StringComparison.Ordinal);
    }
}
