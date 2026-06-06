using Godot;
using Goose2Client.Network;

namespace Goose2Client
{
    public partial class GameManager : Node
    {
        private static GameManager instance;
        public static GameManager Instance => instance;

        public NetworkClient NetworkClient { get; private set; }
        public PacketManager PacketManager { get; private set; }

        public override void _EnterTree()
        {
            instance = this;

            PacketManager = new PacketManager();
            NetworkClient = new NetworkClient(this);
        }

        // Main-thread entry point. The NetworkClient receive thread marshals each complete
        // packet here via CallDeferred("HandlePacket", packet). The Pause flag is honored
        // HERE (on the main thread), not on the receive thread.
        public void HandlePacket(string packet)
        {
            if (NetworkClient.Pause) return;

            PacketManager.Handle(packet);
        }

        public override void _Notification(int what)
        {
            if (what == NotificationWMCloseRequest)
                NetworkClient?.Disconnect();
        }

        public override void _ExitTree()
        {
            NetworkClient?.Disconnect();
        }
    }
}
