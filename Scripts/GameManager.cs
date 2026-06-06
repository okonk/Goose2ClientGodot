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

        private PausablePacketQueue _packetQueue;

        public override void _EnterTree()
        {
            instance = this;

            PacketManager = new PacketManager();
            NetworkClient = new NetworkClient(this);
            _packetQueue = new PausablePacketQueue(() => NetworkClient.Pause, PacketManager.Handle);
        }

        /// <summary>
        /// Main-thread entry point. The NetworkClient receive thread marshals each complete
        /// packet here via CallDeferred("HandlePacket", packet). The Pause flag is honored
        /// HERE (on the main thread), not on the receive thread.
        /// While paused, packets are buffered in a FIFO queue; on unpause they are drained
        /// in order before any newly-arriving packet is handled.
        /// </summary>
        public void HandlePacket(string packet)
        {
            _packetQueue.Handle(packet);
        }

        /// <summary>
        /// Sets the pause flag. When transitioning to unpaused, drains any buffered packets
        /// in FIFO order. All callers that change pause state MUST use this method
        /// (not <c>NetworkClient.Pause = value</c>) so the drain always fires.
        /// </summary>
        public void SetPaused(bool paused)
        {
            NetworkClient.Pause = paused;
            if (!paused)
                _packetQueue.Drain();
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
