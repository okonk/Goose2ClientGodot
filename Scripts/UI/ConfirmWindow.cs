using System;
using Godot;

namespace Goose2Client.UI;

public partial class ConfirmWindow : BaseWindow
{
    protected override bool DefaultVisible => false;

    private Label _prompt = null!;
    private Button _okButton = null!;
    private Action? _onConfirm;

    public override void _Ready()
    {
        base._Ready();

        Visible = false;

        _prompt = GetNode<Label>("Content/Prompt");
        _okButton = GetNode<Button>("Content/OkButton");
        _okButton.Pressed += Confirm;
        GetNode<Button>("Content/CancelButton").Pressed += Cancel;

        UiScaleApplier.Instance!.ApplyFontSize(GetNode<Label>("TitleBar/TitleLabel"), 9);
        ScaleRegister();
    }

    public void Open(string title, string prompt, string okText, Action onConfirm)
    {
        Title = title;
        _prompt.Text = prompt;
        _okButton.Text = okText;
        _onConfirm = onConfirm;
        Visible = true;
        Activate();
        _okButton.GrabFocus();
    }

    public void Cancel()
    {
        _onConfirm = null;
        Hide();
    }

    private void Confirm()
    {
        var action = _onConfirm;
        _onConfirm = null;
        Hide();
        action?.Invoke();
    }

    protected override void OnClosePressed()
    {
        Cancel();
    }
}
