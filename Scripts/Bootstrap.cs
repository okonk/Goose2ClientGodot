using System;
using Godot;
using Goose2Client.Network.Packets;

namespace Goose2Client
{
    public partial class Bootstrap : Node
    {
        public override void _Ready()
        {
            var gm = GameManager.Instance;

            // 1. Register temporary packet listeners
            gm.PacketManager.Listen<PingPacket>(OnPing);
            gm.PacketManager.Listen<ServerMessagePacket>(OnServerMessage);
            gm.PacketManager.Listen<LoginFailPacket>(OnLoginFail);
            gm.PacketManager.Listen<LoginSuccessPacket>(OnLoginSuccess);

            // 2. Subscribe to NetworkClient events
            gm.NetworkClient.Connected += OnConnected;
            gm.NetworkClient.ConnectionError += OnConnectionError;
            gm.NetworkClient.SocketError += OnSocketError;

            // 3. Read connection target from environment
            string host = OS.GetEnvironment("GOOSE_HOST");
            if (string.IsNullOrEmpty(host)) host = "game.illutia.net";

            string portStr = OS.GetEnvironment("GOOSE_PORT");
            int port = int.TryParse(portStr, out int p) ? p : 2006;

            GD.Print($"[Bootstrap] Connecting to {host}:{port}");
            gm.NetworkClient.Connect(host, port);

            // 6. Auto-quit after 15 seconds for clean headless termination
            GetTree().CreateTimer(15).Timeout += () =>
            {
                GD.Print("[Bootstrap] timer quit");
                GetTree().Quit();
            };
        }

        // --- Event handlers ---

        private void OnConnected()
        {
            GD.Print("[Bootstrap] Connected to server");

            // 4. Read credentials and login
            string user = OS.GetEnvironment("GOOSE_USER");
            if (string.IsNullOrEmpty(user)) user = "goosetest";

            string pass = OS.GetEnvironment("GOOSE_PASS");
            if (string.IsNullOrEmpty(pass)) pass = "goosetest";

            GD.Print($"[Bootstrap] Logging in as {user}");
            GameManager.Instance.NetworkClient.Login(user, pass);
        }

        private void OnConnectionError(Exception e)
        {
            GD.Print($"[Bootstrap] ConnectionError: {e.Message}");
        }

        private void OnSocketError(Exception e)
        {
            GD.Print($"[Bootstrap] SocketError: {e.Message}");
        }

        // --- Packet handlers ---

        // 5. Ping — respond with PONG
        private void OnPing(object o)
        {
            GD.Print("[Bootstrap] PingPacket -> Pong");
            GameManager.Instance.NetworkClient.Pong();
        }

        private void OnServerMessage(object o)
        {
            var p = (ServerMessagePacket)o;
            GD.Print($"[Bootstrap] ServerMessagePacket -> ChatType={p.ChatType}, Message=\"{p.Message}\"");
        }

        private void OnLoginFail(object o)
        {
            var p = (LoginFailPacket)o;
            GD.Print($"[Bootstrap] LoginFailPacket -> Message=\"{p.Message}\"");
        }

        private void OnLoginSuccess(object o)
        {
            var p = (LoginSuccessPacket)o;
            GD.Print($"[Bootstrap] LoginSuccessPacket -> RealmName=\"{p.RealmName}\"");
        }
    }
}
