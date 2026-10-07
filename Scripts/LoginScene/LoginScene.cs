using System;
using System.Collections.Generic;
using Godot;
using Goose2Client.Network.Packets;

namespace Goose2Client;

/// <summary>
/// Faithful Godot port of Unity's LoginScene/LoginButton.cs.
/// Provides the interactive login form: username/password input, connect + login flow.
/// </summary>
public partial class LoginScene : Control, IScalableWindow
{
    private List<UiScaleLayout.GeomRecord> _geom = null!;

    // Cached UI nodes
    private LineEdit _nameInput = default!;
    private LineEdit _passwordInput = default!;
    private Button _loginButton = default!;
    private Label _statusLabel = default!;
    private Label _footer = default!;
    private Control _serverModal = default!;
    private LineEdit _addressInput = default!;
    private LineEdit _portInput = default!;
    private Label _modalError = default!;
    private Color _footerColor;

    private const string ModalVb = "ServerModal/Center/Card/CardPadding/VBox/";

    public override void _Ready()
    {
        // 1. Cache UI nodes
        _nameInput = GetNode<LineEdit>("LoginLayout/Center/LoginCard/CardPadding/VBox/NameInput");
        _passwordInput = GetNode<LineEdit>("LoginLayout/Center/LoginCard/CardPadding/VBox/PasswordInput");
        _loginButton = GetNode<Button>("LoginLayout/Center/LoginCard/CardPadding/VBox/LoginButton");
        _statusLabel = GetNode<Label>("LoginLayout/Center/LoginCard/CardPadding/VBox/StatusLabel");
        _footer = GetNode<Label>("LoginLayout/Center/LoginCard/CardPadding/VBox/Footer");
        _serverModal = GetNode<Control>("ServerModal");
        _addressInput = GetNode<LineEdit>(ModalVb + "AddressInput");
        _portInput = GetNode<LineEdit>(ModalVb + "PortInput");
        _modalError = GetNode<Label>(ModalVb + "ModalError");

        // Register the card's font overrides so they scale with the UI factor like every
        // other window; the tscn values are the 1x bases.
        var applier = UiScaleApplier.Instance!;
        const string vb = "LoginLayout/Center/LoginCard/CardPadding/VBox/";
        applier.ApplyFontSize(GetNode<Label>(vb + "GameTitle"), 17);
        applier.ApplyFontSize(GetNode<Label>(vb + "Subtitle"), 11);
        applier.ApplyFontSize(GetNode<Label>(vb + "NameLabel"), 11);
        applier.ApplyFontSize(_nameInput, 12);
        applier.ApplyFontSize(GetNode<Label>(vb + "PasswordLabel"), 11);
        applier.ApplyFontSize(_passwordInput, 12);
        applier.ApplyFontSize(_loginButton, 12);
        applier.ApplyFontSize(_statusLabel, 11);
        applier.ApplyFontSize(GetNode<Label>(vb + "Footer"), 10);
        applier.ApplyFontSize(GetNode<Label>(ModalVb + "Title"), 12);
        applier.ApplyFontSize(GetNode<Label>(ModalVb + "AddressLabel"), 11);
        applier.ApplyFontSize(_addressInput, 12);
        applier.ApplyFontSize(GetNode<Label>(ModalVb + "PortLabel"), 11);
        applier.ApplyFontSize(_portInput, 12);
        applier.ApplyFontSize(_modalError, 10);
        applier.ApplyFontSize(GetNode<Button>(ModalVb + "Buttons/CancelButton"), 12);
        applier.ApplyFontSize(GetNode<Button>(ModalVb + "Buttons/SaveButton"), 12);

        // 2. Autofill from credential store
        var (name, password) = LoginCredentialStore.Load();
        _nameInput.Text = name;
        _passwordInput.Text = password;
        UpdateServerLabel();

        // 3. Register packet listeners on the autoload PacketManager
        var gm = GameManager.Instance;
        gm.PacketManager.Listen<LoginSuccessPacket>(OnLoginSuccess);
        gm.PacketManager.Listen<LoginFailPacket>(OnLoginFail);

        // 4. Subscribe to NetworkClient events
        gm.NetworkClient.Connected += OnConnected;
        gm.NetworkClient.ConnectionError += OnError;
        gm.NetworkClient.SocketError += OnError;

        // 5. Wire UI signals
        _loginButton.Pressed += OnLoginClicked;
        _passwordInput.TextSubmitted += _ => OnLoginClicked();

        _footerColor = _footer.GetThemeColor("font_color");
        _footer.MouseDefaultCursorShape = CursorShape.PointingHand;
        _footer.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                OpenServerModal();
        };
        _footer.MouseEntered += () => _footer.AddThemeColorOverride("font_color", new Color(1f, 0.807843f, 0.4f));
        _footer.MouseExited += () => _footer.AddThemeColorOverride("font_color", _footerColor);
        GetNode<ColorRect>("ServerModal/Dim").GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true })
                CloseServerModal();
        };
        GetNode<Button>(ModalVb + "Buttons/CancelButton").Pressed += CloseServerModal;
        GetNode<Button>(ModalVb + "Buttons/SaveButton").Pressed += OnServerSaveClicked;
        _addressInput.TextSubmitted += _ => OnServerSaveClicked();
        _portInput.TextSubmitted += _ => OnServerSaveClicked();

        // Clear status label initially
        _statusLabel.Text = "";

        _geom = UiScaleLayout.Snapshot(this);
        applier.RegisterWindow(this);
        Relayout();
        TreeExited += () => applier.UnregisterWindow(this);
    }

    public void Relayout()
    {
        UiScaleLayout.Apply(_geom, UiScaleApplier.Instance!.Factor);
        // Godot quirk: a Label's min size is not refreshed when the theme default font
        // size changes, so the stale min over-allocates the VBox after a scale-down.
        _statusLabel.UpdateMinimumSize();
        _modalError.UpdateMinimumSize();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (_serverModal.Visible && @event.IsActionPressed("ui_cancel"))
        {
            CloseServerModal();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree()
    {
        // CRITICAL: ChangeMap frees this scene. Remove all autoload-owned subscriptions
        // to prevent dangling callbacks that would crash on the next packet/connect.
        var gm = GameManager.Instance;
        gm.NetworkClient.Connected -= OnConnected;
        gm.NetworkClient.ConnectionError -= OnError;
        gm.NetworkClient.SocketError -= OnError;
        gm.PacketManager.Remove<LoginSuccessPacket>(OnLoginSuccess);
        gm.PacketManager.Remove<LoginFailPacket>(OnLoginFail);
    }

    // --- UI handlers ---

    private void OnLoginClicked()
    {
        if (_serverModal.Visible)
            return;

        string name = _nameInput.Text;
        string password = _passwordInput.Text;

        if (name.Length <= 2 || password.Length <= 3)
            return;

        _statusLabel.Text = "Connecting...";
        _loginButton.Disabled = true;

        var (host, port) = ServerConfig.Load();
        GameManager.Instance.NetworkClient.Connect(host, port);
    }

    private void OpenServerModal()
    {
        var (host, port) = ServerConfig.Load();
        _addressInput.Text = host;
        _portInput.Text = port.ToString();
        _modalError.Text = "";
        _serverModal.Visible = true;
        _addressInput.GrabFocus();
    }

    private void CloseServerModal()
    {
        _serverModal.Visible = false;
    }

    private void OnServerSaveClicked()
    {
        if (!ServerSettings.TryValidate(_addressInput.Text, _portInput.Text,
                out string host, out int port, out string? error))
        {
            _modalError.Text = error ?? "";
            return;
        }

        ServerConfig.Save(host, port);
        UpdateServerLabel();
        CloseServerModal();
    }

    private void UpdateServerLabel()
    {
        var (host, port) = ServerConfig.Load();
        _footer.Text = ServerSettings.FormatLabel(host, port);
    }

    // --- NetworkClient event handlers ---

    private void OnConnected()
    {
        GameManager.Instance.NetworkClient.Login(_nameInput.Text, _passwordInput.Text);
    }

    private void OnError(Exception e)
    {
        _statusLabel.Text = e.Message;
        _loginButton.Disabled = false;
    }

    // --- Packet handlers ---

    private void OnLoginSuccess(object packet)
    {
        _statusLabel.Text = "Connected!";

        var name = _nameInput.Text;
        LoginCredentialStore.Save(name, _passwordInput.Text);
        GameManager.Instance.LoadSettings(name);
        GameManager.Instance.NetworkClient.LoginContinued();
    }

    private void OnLoginFail(object packet)
    {
        var p = (LoginFailPacket)packet;
        _statusLabel.Text = p.Message;
        _loginButton.Disabled = false;
    }

}
