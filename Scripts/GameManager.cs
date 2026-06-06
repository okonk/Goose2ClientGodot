using System;
using System.Collections.Generic;
using Godot;
using Goose2Client.Network;
using Goose2Client.Network.Packets;

namespace Goose2Client
{
    public partial class GameManager : Node
    {
        private static GameManager instance;
        public static GameManager Instance => instance;

        public NetworkClient NetworkClient { get; private set; }
        public PacketManager PacketManager { get; private set; }

        private PausablePacketQueue _packetQueue;

        /// <summary>Persistent CanvasLayer that survives scene swaps. HUD windows attach here.</summary>
        public CanvasLayer UiLayer { get; private set; }

        /// <summary>Per-character settings (hotkeys, window positions, options).</summary>
        public CharacterSettings CharacterSettings { get; set; }

        /// <summary>Class ID → class name lookup, populated by ClassUpdatePacket.</summary>
        public Dictionary<int, string> Classes { get; } = new();

        public override void _EnterTree()
        {
            instance = this;

            PacketManager = new PacketManager();
            NetworkClient = new NetworkClient(this);
            _packetQueue = new PausablePacketQueue(() => NetworkClient.Pause, PacketManager.Handle);
        }

        public override void _Ready()
        {
            // Persistent UI CanvasLayer — lives in the autoload tree so it survives ChangeSceneToPacked.
            UiLayer = new CanvasLayer();
            UiLayer.Name = "UiLayer";
            AddChild(UiLayer);

            // Listen for class table updates for the lifetime of the app.
            PacketManager.Listen<ClassUpdatePacket>(OnClassUpdate);
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

        /// <summary>
        /// Full scene transition: Login → LoadingMap → Map.
        /// Pauses gameplay packets during the swap, then drains them on resume.
        /// </summary>
        public async void ChangeMap(string mapFile, string mapName)
        {
            SetPaused(true);   // buffer gameplay packets during the transition (drained on unpause)

            GetTree().ChangeSceneToPacked(GD.Load<PackedScene>("res://Scenes/LoadingMap.tscn"));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            if (GetTree().CurrentScene is LoadingMapScene loading)
                loading.SetMapName(mapName);

            // Step 5 hook: build the TileMapLayer world from `mapFile` here. Placeholder for now.

            GetTree().ChangeSceneToPacked(GD.Load<PackedScene>("res://Scenes/Map.tscn"));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            NetworkClient.DoneLoadingMap();   // "DLM" — tells the server we are in the world
            SetPaused(false);                 // drain queued gameplay packets in order
        }

        /// <summary>Load (or create default) settings for the given character.</summary>
        public void LoadSettings(string characterName)
        {
            CharacterSettings = new CharacterSettings(characterName);
        }

        private void OnClassUpdate(object packetObj)
        {
            var packet = (ClassUpdatePacket)packetObj;
            Classes[packet.ClassId] = packet.Name;
        }

        public override void _Notification(int what)
        {
            if (what == NotificationWMCloseRequest)
            {
                CharacterSettings?.Save();
                NetworkClient?.Disconnect();
            }
        }

        public override void _ExitTree()
        {
            PacketManager.Remove<ClassUpdatePacket>(OnClassUpdate);
            NetworkClient?.Disconnect();
        }
    }
}
