using Godot;
using Goose2Client;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI;

public partial class TextInputWindow : BaseWindow, IWindow
{
    protected override bool DefaultVisible => false;

    public int WindowId { get; private set; }
    public WindowFrames WindowFrame => WindowFrames.TextInput;
    public int NpcId { get; private set; }

    private Label _prompt = null!;
    private LineEdit _field = null!;
    private Button _confirmButton = null!;
    private Button _cancelButton = null!;
    private bool _listenersRegistered;
    private float _promptGrowth;

    public override void _Ready()
    {
        base._Ready();

        Visible = false;

        _prompt = GetNode<Label>("Content/Prompt");
        _field = GetNode<LineEdit>("Content/TextField");
        _confirmButton = GetNode<Button>("Content/ConfirmButton");
        _cancelButton = GetNode<Button>("Content/CancelButton");

        _field.TextSubmitted += _ => Submit();
        _confirmButton.Pressed += Submit;
        _cancelButton.Pressed += Cancel;

        GameManager.Instance.PacketManager.Listen<MakeWindowPacket>(OnMakeWindow);
        GameManager.Instance.PacketManager.Listen<EndWindowPacket>(OnEndWindow);
        GameManager.Instance.PacketManager.Listen<CloseWindowPacket>(OnCloseWindow);
        GameManager.Instance.PacketManager.Listen<TextInputInfoPacket>(OnTextInputInfo);
        _listenersRegistered = true;

        UiScaleApplier.Instance!.ApplyFontSize(GetNode<Label>("TitleBar/TitleLabel"), 9);
        ScaleRegister();
    }

    public override void Relayout()
    {
        base.Relayout();
        // base.Relayout restores the scene's one-line geometry, discarding any earlier growth.
        _promptGrowth = 0;
        FitPrompt();
    }

    private void FitPrompt()
    {
        if (_prompt == null) return;

        int lines = Mathf.Max(1, _prompt.GetLineCount());
        float textHeight = lines * _prompt.GetLineHeight()
            + (lines - 1) * _prompt.GetThemeConstant("line_spacing");
        float baseHeight = _prompt.OffsetBottom - _promptGrowth - _prompt.OffsetTop;
        float growth = Mathf.Max(0f, Mathf.Ceil(textHeight - baseHeight));
        float shift = growth - _promptGrowth;
        if (shift == 0) return;

        _prompt.OffsetBottom += shift;
        foreach (var c in new Control[] { _field, _confirmButton, _cancelButton })
        {
            c.OffsetTop += shift;
            c.OffsetBottom += shift;
        }
        Size = Size with { Y = Size.Y + shift };
        _promptGrowth = growth;
    }

    public override void _ExitTree()
    {
        if (!_listenersRegistered) return;
        GameManager.Instance.PacketManager.Remove<MakeWindowPacket>(OnMakeWindow);
        GameManager.Instance.PacketManager.Remove<EndWindowPacket>(OnEndWindow);
        GameManager.Instance.PacketManager.Remove<CloseWindowPacket>(OnCloseWindow);
        GameManager.Instance.PacketManager.Remove<TextInputInfoPacket>(OnTextInputInfo);
    }

    private void OnMakeWindow(object o)
    {
        var p = (MakeWindowPacket)o;
        if (p.WindowFrame != WindowFrames.TextInput) return;
        // HUD survives map swaps and the server discards windows without a CLW,
        // so a new WindowId means a fresh session that must not inherit state.
        if (p.WindowId != WindowId)
            ResetState();
        WindowId = p.WindowId;
        NpcId = p.NpcId;
        Title = p.Title;
    }

    private void ResetState()
    {
        _field.Text = "";
    }

    private void OnTextInputInfo(object o)
    {
        var p = (TextInputInfoPacket)o;
        if (p.WindowId != WindowId) return;
        _prompt.Text = p.Prompt;
        FitPrompt();
        _field.MaxLength = p.MaxLength;
        _field.Text = p.DefaultText;
        _confirmButton.Text = p.ButtonText;
    }

    private void OnEndWindow(object o)
    {
        var p = (EndWindowPacket)o;
        if (p.WindowId == WindowId)
        {
            Visible = true;
            _field.GrabFocus();
            _field.SelectAll();
        }
    }

    private void OnCloseWindow(object o)
    {
        var p = (CloseWindowPacket)o;
        if (p.WindowId != WindowId) return;
        Visible = false;
    }

    private void Submit()
    {
        var text = _field.Text.Trim();
        if (text.Length == 0) return;
        GameManager.Instance.NetworkClient.SubmitText(WindowId, text);
        // The server closes the window with CLW on success; on invalid input it
        // stays open with the server's notice.
    }

    private void Cancel()
    {
        GameManager.Instance.NetworkClient.WindowButtonClick(WindowButtons.Close, WindowId, NpcId);
        Hide();
    }

    protected override void OnClosePressed()
    {
        Cancel();
    }
}
