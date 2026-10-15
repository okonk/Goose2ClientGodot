using System;
using System.Globalization;
using System.Linq;
using Godot;
using Goose2Client;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI;

public partial class AmountWindow : BaseWindow, IWindow
{
    protected override bool DefaultVisible => false;

    public int WindowId { get; private set; }
    public WindowFrames WindowFrame => WindowFrames.Amount;
    public int NpcId { get; private set; }

    private TextureRect _icon = null!;
    private Label _prompt = null!;
    private LineEdit _field = null!;
    private Button _maxButton = null!;
    private Button _donateButton = null!;
    private Button _cancelButton = null!;
    private bool _listenersRegistered;

    private long _min;
    private long _max;

    public override void _Ready()
    {
        base._Ready();

        Visible = false;

        _icon = GetNode<TextureRect>("Content/Icon");
        _prompt = GetNode<Label>("Content/Prompt");
        _field = GetNode<LineEdit>("Content/AmountField");
        _maxButton = GetNode<Button>("Content/MaxButton");
        _donateButton = GetNode<Button>("Content/DonateButton");
        _cancelButton = GetNode<Button>("Content/CancelButton");

        _field.TextChanged += OnFieldTextChanged;
        _field.TextSubmitted += _ => Submit();
        _maxButton.Pressed += () => _field.Text = _max.ToString(CultureInfo.InvariantCulture);
        _donateButton.Pressed += Submit;
        _cancelButton.Pressed += Cancel;

        GameManager.Instance.PacketManager.Listen<MakeWindowPacket>(OnMakeWindow);
        GameManager.Instance.PacketManager.Listen<EndWindowPacket>(OnEndWindow);
        GameManager.Instance.PacketManager.Listen<CloseWindowPacket>(OnCloseWindow);
        GameManager.Instance.PacketManager.Listen<AmountInfoPacket>(OnAmountInfo);
        _listenersRegistered = true;

        UiScaleApplier.Instance!.ApplyFontSize(GetNode<Label>("TitleBar/TitleLabel"), 9);
        ScaleRegister();
    }

    public override void _ExitTree()
    {
        if (!_listenersRegistered) return;
        GameManager.Instance.PacketManager.Remove<MakeWindowPacket>(OnMakeWindow);
        GameManager.Instance.PacketManager.Remove<EndWindowPacket>(OnEndWindow);
        GameManager.Instance.PacketManager.Remove<CloseWindowPacket>(OnCloseWindow);
        GameManager.Instance.PacketManager.Remove<AmountInfoPacket>(OnAmountInfo);
    }

    private void OnMakeWindow(object o)
    {
        var p = (MakeWindowPacket)o;
        if (p.WindowFrame != WindowFrames.Amount) return;
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
        _min = 0;
        _max = 0;
    }

    private void OnAmountInfo(object o)
    {
        var p = (AmountInfoPacket)o;
        if (p.WindowId != WindowId) return;
        Icon.Apply(_icon, p.Sheet, p.Graphic, p.R, p.G, p.B, p.A);
        _prompt.Text = p.Prompt;
        _min = p.Min;
        _max = p.Max;
        _field.Text = p.Default.ToString(CultureInfo.InvariantCulture);
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

    private void OnFieldTextChanged(string text)
    {
        var digits = new string(text.Where(c => c >= '0' && c <= '9').ToArray());
        if (digits != text)
        {
            int caret = _field.CaretColumn;
            _field.Text = digits;
            _field.CaretColumn = caret;
        }
    }

    private void Submit()
    {
        var amount = AmountInput.Parse(_field.Text, _min, _max);
        if (amount == null) return;
        GameManager.Instance.NetworkClient.SubmitAmount(WindowId, amount.Value);
        Hide();
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
