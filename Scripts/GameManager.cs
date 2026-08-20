using System.Collections.Generic;
using Godot;
using Goose2Client.Map;
using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Goose2Client.UI;

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

        /// <summary>The parsed map for the scene currently being entered. Set in ChangeMap, read by MapManager._Ready.</summary>
        public MapFile CurrentMap { get; set; }

        /// <summary>Shared UI/icon sprite cache used by HUD windows.</summary>
        public SpriteCache Sprites { get; private set; }

        /// <summary>Tracks per-slot spell cooldown timers.</summary>
        public SpellCooldownManager SpellCooldownManager { get; } = new();

        /// <summary>Manages on-screen spell targeting (stub until step 8).</summary>
        public SpellTargetManager SpellTargetManager { get; private set; }

        /// <summary>Whether the player is currently in spell-targeting mode.</summary>
        public bool IsTargeting => SpellTargetManager?.IsTargeting ?? false;

        /// <summary>The active MapManager node, set/cleared by MapManager itself.</summary>
        public MapManager CurrentMapManager { get; set; }

        /// <summary>Owns the world sub-viewport and its display texture; map scenes attach here.</summary>
        public WorldViewport WorldViewport { get; private set; }

        /// <summary>CanvasLayer hosting world-anchored text (names, bubbles, battle text) at native resolution.</summary>
        public WorldTextBridge WorldTextBridge { get; private set; }

        /// <summary>The persistent HUD root, instantiated once under UiLayer.</summary>
        public GameHud Hud { get; private set; }

        public event System.Action<Character.Character> CharacterUpdated;
        public void OnCharacterUpdated(Character.Character c) => CharacterUpdated?.Invoke(c);

        // User signal (not a [Signal] delegate — the tests project compiles this file without
        // the Godot source generator): one-shot winner of the ChangeMap render race, emitted
        // when the new map's first render pass has completed (frame_post_draw) — or one frame
        // elapses in a headless run, where frame_post_draw is never emitted.
        private const string RenderRaceSignal = "world_transition_render_done";

        // ChangeMap re-entrancy guard (async void): two SendCurrentMap packets drained in the
        // same flush would otherwise start two concurrent transitions, and the first one's map
        // would be superseded by the second Attach while no transition is left to free it.
        private bool _changingMap;

        public override void _EnterTree()
        {
            instance = this;

            PacketManager = new PacketManager();
            NetworkClient = new NetworkClient(this);
            _packetQueue = new PausablePacketQueue(() => NetworkClient.Pause, PacketManager.Handle);
        }

        public override void _Ready()
        {
            // World sub-viewport display — must be added before UiLayer so the root Controls of
            // Login/LoadingMap scenes draw above the world texture (tree order).
            WorldViewport = new WorldViewport();
            WorldViewport.Name = "WorldViewport";
            AddChild(WorldViewport);

            // World-anchored text at native resolution — between the world texture and the HUD
            // in tree order, so it draws above the world and below the HUD.
            WorldTextBridge = new WorldTextBridge();
            WorldTextBridge.Name = "WorldTextBridge";
            AddChild(WorldTextBridge);
            WorldTextBridge.Attach(WorldViewport);

            // Persistent UI CanvasLayer — lives in the autoload tree so it survives ChangeSceneToPacked.
            UiLayer = new CanvasLayer();
            UiLayer.Name = "UiLayer";
            AddChild(UiLayer);

            // Re-anchor HUD windows onto the (new) window edges when the OS window resizes
            // (e.g. windowed → fullscreen), so a live resize yields the same layout as a
            // restart at the new size. Coexists on the same signal with WorldViewport's own
            // SizeChanged handler, which only re-lays out the world.
            var window = GetWindow();
            if (window != null)
                window.SizeChanged += OnWindowResized;

            // Always-on-top build stamp. Its own CanvasLayer at 128 so HUD windows on
            // UiLayer can never draw over it. Added first on purpose: anything below can
            // throw (asset loading did), and a stamp that vanishes exactly when startup
            // breaks is useless for identifying which build broke.
            AddChild(new UI.BuildStampOverlay());

            // Listen for class table updates for the lifetime of the app.
            PacketManager.Listen<ClassUpdatePacket>(OnClassUpdate);
            PacketManager.Listen<PingPacket>(OnPing);
            // GameManager persists across scene swaps and owns ChangeMap.
            // SendCurrentMapPacket drives warp / door / death-recall map transitions
            // that arrive after login — login scene is freed and would drop them.
            PacketManager.Listen<SendCurrentMapPacket>(OnSendCurrentMap);

            Sprites = new SpriteCache();
            SpellTargetManager = new SpellTargetManager();
            AddChild(SpellTargetManager);
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
            // Re-entrancy guard (blocking): a second transition started inside the first would
            // attach a second map whose Attach supersedes the first's pending present handler,
            // orphaning the first map (never freed, rendering forever in UpdateMode.Always,
            // running a live-but-invisible MapManager). Clear in the finally on EVERY exit
            // path — including the missing-map early return and exceptions.
            if (_changingMap) return;
            _changingMap = true;

            // Loading overlay: added to root directly, NOT set as a current scene — freed manually.
            LoadingMapScene loading = null;
            try
            {
                // Unity parity: clear and unfocus chat input on every map change
                if (Hud != null && GodotObject.IsInstanceValid(Hud))
                    Hud.Chat?.ClearAndUnfocus();

                SetPaused(true);   // buffer gameplay packets during the transition (drained on unpause)

                // Previous world, tracked explicitly (I7) — scene reassignment never frees it:
                //  - previousMap: the currently attached map (later entries). CurrentScene never
                //    points at the map: set_current_scene requires a direct root child (Godot 4.7
                //    scene_tree.cpp:1665), and the map lives under WorldViewport.
                //  - previousScene: the previous current scene — Login on first entry (main scene),
                //    null on later entries (the engine nulls it when the freed scene leaves the tree).
                var previousMap = WorldViewport.Current;
                var previousScene = GetTree().CurrentScene;

                loading = GD.Load<PackedScene>("res://Scenes/LoadingMap.tscn").Instantiate<LoadingMapScene>();
                GetTree().Root.AddChild(loading);
                // Hide the text bridge for the whole transition. The bridge is a root CanvasLayer
                // (layer 1) that draws ABOVE the root-canvas LoadingMap control, while old-map
                // characters (and thus their names/bubbles/battle text) stay alive until the new
                // map's first presented frame. Without this, old-map text would overlay the loading
                // UI — Stage 1 text lived inside the world texture and drew BELOW the overlay.
                WorldTextBridge.Visible = false;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                loading.SetMapName(mapName);

                CurrentMap = LoadMap(mapFile);
                if (CurrentMap == null) return;
                // finally: frees loading, unpauses; old world stays live, no DoneLoadingMap sent

                // The Map scene IS its own SubViewport; attaching it to WorldViewport puts it in
                // the tree, forces its first render, and sizes it (RefreshFromSettings). The
                // display-texture swap is DEFERRED inside Attach to the completion of the next
                // render pass (RenderingServer.frame_post_draw — this engine's successor to the
                // old Viewport 'rendered' signal): a fresh sub-viewport's buffer is undefined
                // before its first render (garbage flash), and WorldTexture still shows the
                // previous map until the swap (no black flash). MapManager._Ready (fired inside
                // Attach's AddChild) has already registered CurrentMapManager by the time
                // DoneLoadingMap drains packets below.
                var mapScene = GD.Load<PackedScene>("res://Scenes/Map.tscn").Instantiate<SubViewport>();
                WorldViewport.Attach(mapScene);

                // Present the new map's first clean frame before removing the old world:
                // WorldTexture still displays the previous map (or black pre-first-map) until
                // the next render pass completes; freeing the old world earlier would yank the
                // texture source out from under the display (black flash), and the fresh
                // sub-viewport's buffer is undefined before its first render (garbage flash).
                // ProcessFrame is the headless fallback — frame_post_draw is not emitted in
                // a headless build, so that leg never fires and the frame must carry the
                // transition (in a real run frame_post_draw fires first: rendering completes
                // later in the same frame, before the next process_frame). This GodotSharp
                // has no multi-signal ToSignal, so the race is wired by hand: the first leg
                // to fire emits the RenderRaceSignal user signal, which is awaited below.
                // LOAD-BEARING TIMING INVARIANT: the loading-overlay await above forces this
                // Attach to land in the NEXT mid-frame flush — i.e. AFTER the attach frame's
                // own process_frame emission. The process_frame race leg therefore cannot win
                // on the attach frame; if it did, the old world would be freed+destroyed
                // before that frame's render, bringing the flash back. DO NOT remove/hoist
                // the loading await without re-deriving this ordering.
                var tree = GetTree();   // captured so FinishRace can detach without touching `this`
                bool raceDone = false;
                System.Action onPostDraw = null;
                System.Action onFrame = null;
                void FinishRace()
                {
                    // A freed GameManager must not throw inside the FramePostDraw callback
                    // (and leave a no-op delegate firing every frame): detach both legs via
                    // the captured tree (no `this` access beyond this validity check) and stop.
                    if (!GodotObject.IsInstanceValid(this))
                    {
                        raceDone = true;
                        if (onPostDraw != null) { RenderingServer.FramePostDraw -= onPostDraw; onPostDraw = null; }
                        if (onFrame != null) { tree.ProcessFrame -= onFrame; onFrame = null; }
                        return;
                    }
                    if (raceDone) return;
                    raceDone = true;
                    RenderingServer.FramePostDraw -= onPostDraw;
                    tree.ProcessFrame -= onFrame;
                    EmitSignal(RenderRaceSignal);
                }
                if (!HasUserSignal(RenderRaceSignal))
                    AddUserSignal(RenderRaceSignal);
                onPostDraw = FinishRace;
                onFrame = FinishRace;
                RenderingServer.FramePostDraw += onPostDraw;
                tree.ProcessFrame += onFrame;
                await ToSignal(this, RenderRaceSignal);

                // Explicit lifecycle ownership: free the previous world only after the new map
                // has rendered its first (now-presented) frame; failure before the await keeps
                // the old world live.
                if (previousScene != null && previousScene != mapScene && GodotObject.IsInstanceValid(previousScene))
                    previousScene.QueueFree();
                if (previousMap != null && previousMap != mapScene && GodotObject.IsInstanceValid(previousMap))
                    previousMap.QueueFree();

                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);   // let the frees run

                NetworkClient.DoneLoadingMap();   // "DLM" — tells the server we are in the world
            }
            finally
            {
                _changingMap = false;   // release on every exit path (early return, throw, normal)
                if (loading != null && GodotObject.IsInstanceValid(loading))
                    loading.QueueFree();   // no leaked full-window Control
                WorldTextBridge.Visible = true;   // restore: new-map elements project on their first frame
                SetPaused(false);   // always drain queued gameplay packets, even if the transition throws
            }
        }

        /// <summary>Load (or create default) settings for the given character.</summary>
        public void LoadSettings(string characterName)
        {
            CharacterSettings = new CharacterSettings(characterName);
        }

        private void OnPing(object packetObj) => NetworkClient.Pong();

        private void OnSendCurrentMap(object packetObj)
        {
            var p = (SendCurrentMapPacket)packetObj;
            ChangeMap(p.MapFileName, p.MapName);
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
                NetworkClient?.Quit();         // notify the server with a graceful QUIT (mirrors Unity OnApplicationQuit)
                NetworkClient?.Disconnect();   // then tear down the socket + join the receive thread
            }
        }

        private MapFile LoadMap(string mapFile)
        {
            // The server's MapFileName carries the original ".map" extension (e.g. "Map2.map");
            // the converter emits "{basename}.bytes" (e.g. "Map2.bytes"). Normalize to the basename.
            var name = System.IO.Path.GetFileNameWithoutExtension(mapFile);
            var path = $"res://Assets/Maps/{name}.bytes";
            using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            if (f == null)
            {
                GD.PushError($"LoadMap: cannot open {path} (err {Godot.FileAccess.GetOpenError()})");
                return null;
            }
            return new MapFile(f.GetBuffer((long)f.GetLength()));
        }

        /// <summary>Quit the game (used by Toolbar Exit button).</summary>
        public void Quit() => GetTree().Quit();

        /// <summary>Instantiate the persistent HUD under the UI layer once; survives map swaps.</summary>
        public void EnsureHud()
        {
            if (Hud != null && GodotObject.IsInstanceValid(Hud)) return;
            Hud = GD.Load<PackedScene>("res://Scenes/UI/GameHud.tscn").Instantiate<GameHud>();
            UiLayer.AddChild(Hud);
        }

        /// <summary>
        /// OS-window resize handler: re-runs each HUD window's placement against the new canvas
        /// so edge-stuck windows (e.g. the hotbar) stay stuck to the window edges. Returns
        /// silently pre-HUD (UiLayer null) or before the root canvas has a real size. Windows
        /// created lazily later (quest/info dialogs) are picked up on the next resize; they also
        /// get the correct canvas at creation via BaseWindow._Ready.
        /// </summary>
        private void OnWindowResized()
        {
            if (UiLayer == null)
                return;
            var tree = GetTree();
            if (tree == null)
                return;
            var canvas = (Vector2I)tree.Root.GetVisibleRect().Size;
            if (canvas.X < 2 || canvas.Y < 2)
                return;
            foreach (var w in CollectBaseWindows(UiLayer))
                w.RepositionForCurrentCanvas();
        }

        /// <summary>Recursive depth-first walk of <paramref name="root"/>'s children collecting every
        /// BaseWindow. The subtree is small (few dozen nodes) and resize events are cheap, so a
        /// plain walk beats a registry; lazily created windows are found on the next walk.</summary>
        private static IEnumerable<BaseWindow> CollectBaseWindows(Node root)
        {
            foreach (var child in root.GetChildren())
            {
                if (child is BaseWindow w)
                    yield return w;
                foreach (var d in CollectBaseWindows(child))
                    yield return d;
            }
        }

        public override void _ExitTree()
        {
            // Unsubscribe the resize handler (mirrors WorldViewport._ExitTree).
            var window = GetWindow();
            if (window != null)
                window.SizeChanged -= OnWindowResized;
            PacketManager.Remove<ClassUpdatePacket>(OnClassUpdate);
            PacketManager.Remove<PingPacket>(OnPing);
            PacketManager.Remove<SendCurrentMapPacket>(OnSendCurrentMap);
            NetworkClient?.Disconnect();
        }
    }
}
