using System;
using Godot;
using Goose2Client.Network.Packets;

namespace Goose2Client;

/// <summary>
/// Faithful Godot port of Unity's LoginScene/LoginButton.cs.
/// Provides the interactive login form: username/password input, connect + login flow.
/// </summary>
public partial class LoginScene : Control
{
    // Cached UI nodes
    private LineEdit _nameInput = default!;
    private LineEdit _passwordInput = default!;
    private Button _loginButton = default!;
    private Label _statusLabel = default!;

    // Server config — defaults match Unity LoginButton:98, overridable via env vars for headless testing
    private static string Host => OS.GetEnvironment("GOOSE_HOST") is { Length: > 0 } h ? h : "game.illutia.net";
    private static int Port => int.TryParse(OS.GetEnvironment("GOOSE_PORT"), out int p) ? p : 2006;

    public override void _Ready()
    {
        // 1. Cache UI nodes
        _nameInput = GetNode<LineEdit>("MarginContainer/VBox/NameInput");
        _passwordInput = GetNode<LineEdit>("MarginContainer/VBox/PasswordInput");
        _loginButton = GetNode<Button>("MarginContainer/VBox/LoginButton");
        _statusLabel = GetNode<Label>("MarginContainer/VBox/StatusLabel");

        // 2. Autofill from credential store
        var (name, password) = LoginCredentialStore.Load();
        _nameInput.Text = name;
        _passwordInput.Text = password;

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

        // Clear status label initially
        _statusLabel.Text = "";
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
        string name = _nameInput.Text;
        string password = _passwordInput.Text;

        if (name.Length <= 2 || password.Length <= 3)
            return;

        _statusLabel.Text = "Connecting...";
        _loginButton.Disabled = true;

        GameManager.Instance.NetworkClient.Connect(Host, Port);
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
