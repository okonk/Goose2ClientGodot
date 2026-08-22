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
            UiScaleApplier.Instance = new UiScaleApplier();
            var startupCanvas = (Vector2I)GetTree().Root.GetVisibleRect().Size;
            UiScaleApplier.Instance.Apply(UiScale.AutoFactor(startupCanvas.Y), ApplyReason.Startup);

            // World sub-viewport display — must be added before UiLayer so the root Controls of
            // Login/LoadingMap scenes draw above the world texture (tree order).
            WorldViewport = new WorldViewport();
            WorldViewport.Name = "WorldViewport";
            AddChild(WorldViewport);

            // Between the world texture and the HUD in tree order — draws above the world, below the HUD.
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

            if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "+selftest=ui_scale") >= 0)
                _ = UiScaleSelfTestAsync();
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
                // Hide the bridge for the transition: it's a root CanvasLayer that draws ABOVE the loading
                // UI, and old-map characters (names/bubbles/battle text) stay alive until the new map's first frame.
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
                WorldTextBridge.Visible = true;
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
                w.RepositionFromSaved();
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

        private async System.Threading.Tasks.Task UiScaleSelfTestAsync()
        {
            // In-product gate behind a project arg; production with no arg never reaches this.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            bool failed = false;
            try
            {
                await UiScaleSelfTest();
                GD.Print("[ui_scale_selftest] PASS");
            }
            catch (System.Exception e)
            {
                failed = true;
                GD.PrintErr($"ERR_ui_scale_selftest: {e.Message}");
            }
            GetTree().Quit(failed ? 1 : 0);
        }

        private async System.Threading.Tasks.Task UiScaleSelfTest()
        {
            var tree = GetTree();
            var applier = UiScaleApplier.Instance;
            int fontChecked = 0;
            var authored1 = new Dictionary<Control, bool>();

            async System.Threading.Tasks.Task Frame() => await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            void Assert(bool cond, string msg)
            {
                if (!cond)
                    throw new System.InvalidOperationException(msg);
            }

            // Mirrors UiScaleLayout's walk: skip nodes are outside the snapshot model
            // (dynamic tooltips mutate their own size at runtime).
            void Walk(Control n, Dictionary<Control, Vector4> geo, Dictionary<Control, bool> authored)
            {
                if (n.HasMeta(UiScaleLayout.SkipMeta) && n.GetMeta(UiScaleLayout.SkipMeta).AsBool())
                    return;
                geo[n] = new Vector4(n.OffsetLeft, n.OffsetTop, n.OffsetRight, n.OffsetBottom);
                authored[n] = n.HasThemeConstantOverride("separation")
                    || n.HasThemeConstantOverride("h_separation")
                    || n.HasThemeConstantOverride("v_separation");
                foreach (var child in n.GetChildren())
                    if (child is Control c)
                        Walk(c, geo, authored);
            }

            void AuditFonts(Node n)
            {
                if (n is Control c)
                {
                    foreach (var prop in new[] { new StringName("font_size"), new StringName("normal_font_size") })
                    {
                        if (!c.HasThemeFontSizeOverride(prop))
                            continue;
                        Assert(applier.TryGetFontBase(c, prop, out float basePx),
                            $"font override outside registry: {c.GetPath()}");
                        Assert(c.GetThemeFontSize(prop) == applier.ScaleSize(basePx),
                            $"font {c.GetPath()}: {c.GetThemeFontSize(prop)} != {basePx} x 2");
                        fontChecked++;
                    }
                    if (authored1.TryGetValue(c, out bool hadAuthored) && !hadAuthored)
                        Assert(!c.HasThemeConstantOverride("separation")
                            && !c.HasThemeConstantOverride("h_separation")
                            && !c.HasThemeConstantOverride("v_separation"),
                            $"constants materialized on unauthored control: {c.GetPath()}");
                }
                foreach (var child in n.GetChildren())
                    AuditFonts(child);
            }

            bool ContainsRoot(Node w)
            {
                foreach (var r in applier.RegisteredWindows)
                    if (r.Window == w) return true;
                return false;
            }

            var canvas = (Vector2I)tree.Root.GetVisibleRect().Size;
            GD.Print($"[ui_scale_selftest] canvas={canvas.X}x{canvas.Y}");
            Assert(canvas.X > 0 && canvas.Y > 0, $"degenerate headless canvas {canvas}");

            // The selftest profile persists across runs; the explicit 1f apply keeps the
            // baseline independent of any persisted manual factor and of the canvas size.
            LoadSettings("ui-scale-selftest");
            applier.Apply(1f, ApplyReason.Startup);
            EnsureHud();
            await Frame();

            // Step 1: 1x no-op — Relayout at factor 1 must leave every registered root bit-identical.
            await Frame();
            var geo1 = new Dictionary<Control, Vector4>();
            var pos1 = new Dictionary<BaseWindow, Vector2>();
            foreach (var r in applier.RegisteredWindows)
            {
                Walk(r.ControlRef, geo1, authored1);
                if (r.Window is BaseWindow bw)
                    pos1[bw] = bw.Position;
            }
            // A BaseWindow snapshot owns the root's offsets, so Relayout alone reverts the
            // window's placed position — mirror applier.Apply's Relayout+RepositionFromSaved pair.
            foreach (var r in applier.RegisteredWindows)
            {
                r.Window.Relayout();
                if (r.Window is BaseWindow bw)
                    bw.RepositionFromSaved();
            }
            await Frame();
            var geo2 = new Dictionary<Control, Vector4>();
            var auth2 = new Dictionary<Control, bool>();
            foreach (var r in applier.RegisteredWindows)
                Walk(r.ControlRef, geo2, auth2);
            Assert(geo2.Count == geo1.Count, "1x no-op: control count changed");
            foreach (var kv in geo1)
                Assert(geo2.TryGetValue(kv.Key, out var v) && v == kv.Value,
                    $"1x no-op: {kv.Key.GetPath()} {kv.Value} -> {v}");
            GD.Print($"[ui_scale_selftest] OK 1x no-op: {applier.RegisteredWindows.Count} roots, {geo1.Count} controls");

            // Step 2: apply 2x and audit.
            applier.Apply(2f, ApplyReason.UserCommit);
            await Frame();

            Assert(applier.Theme.GetDefaultFontSize() == 20, $"theme default font {applier.Theme.GetDefaultFontSize()} != 20");
            AuditFonts(UiLayer);

            Assert(Hud.Vitals.Size == new Vector2(366, 110), $"vitals size {Hud.Vitals.Size} != (366, 110)");
            Assert(Hud.Vitals.Position == new Vector2(16, 16), $"vitals pos {Hud.Vitals.Position} != (16, 16)");
            var slot = (ItemSlot)Hud.Inventory.GetNode<GridContainer>("Content/SlotGrid").GetChild(0);
            Assert(slot.CustomMinimumSize == new Vector2(64, 64), $"item slot min {slot.CustomMinimumSize} != (64, 64)");
            Assert(Hud.Chat.OffsetTop == -426 && Hud.Chat.OffsetBottom == -10,
                $"chat offsets top={Hud.Chat.OffsetTop} bottom={Hud.Chat.OffsetBottom} != (-426, -10)");

            var vendor = Hud.Vendor;
            Assert(vendor.Position.X >= 0f && vendor.Position.X <= Mathf.Max(0f, canvas.X - vendor.Size.X),
                $"vendor x postcondition {vendor.Position}");
            Assert(vendor.Position.Y >= 0f && vendor.Position.Y <= Mathf.Max(0f, canvas.Y - applier.ScaleSize(24f)),
                $"vendor y postcondition {vendor.Position}");

            var memberList = Hud.Party.GetNode<VBoxContainer>("MemberList");
            Assert(memberList.GetChildCount() == 8, $"party tiles {memberList.GetChildCount()} != 8");
            var sep = memberList.GetThemeConstant("separation");
            Assert(sep == 2, $"party separation {sep} != 2");
            var tile = (PartyMember)memberList.GetChild(0);
            Assert(tile.CustomMinimumSize == new Vector2(174, 66), $"party tile min {tile.CustomMinimumSize} != (174, 66)");
            var nameOffset = tile.GetNode<Label>("Content/NameText").OffsetBottom;
            Assert(nameOffset == 22, $"party name offset {nameOffset} != 22");

            var stamp = GetNode<Label>("BuildStampOverlay/BuildIdLabel");
            var stampFont = stamp.GetThemeFontSize("font_size");
            Assert(stampFont == 10, $"build stamp font {stampFont} != 10");

            var tm = TooltipManager.Instance;
            Assert(tm != null, "tooltip manager missing");
            Assert(!tm.GetNode<ItemTooltipControl>("ItemTooltip").Visible
                && !tm.GetNode<SpellTooltipControl>("SpellTooltip").Visible
                && !tm.GetNode<TextTooltipControl>("TextTooltip").Visible
                && !tm.GetNode<MapItemTooltipControl>("MapItemTooltip").Visible, "tooltips not all hidden");

            tm.ShowSpellTooltip(new SpellInfo { Name = "Selftest" }, Hud);
            await Frame();
            await Frame();
            var spellTt = tm.GetNode<SpellTooltipControl>("SpellTooltip");
            var spellLabel = spellTt.GetNode<Label>("Label");
            var labelMin = spellLabel.GetCombinedMinimumSize();
            var expectedSpell = labelMin + new Vector2(applier.ScaleSize(8f), applier.ScaleSize(4f));
            Assert(spellTt.Visible, "spell tooltip not visible after show");
            Assert(spellTt.Size == expectedSpell, $"spell tooltip size {spellTt.Size} != {expectedSpell}");
            Assert(spellLabel.OffsetLeft == 8 && spellLabel.OffsetTop == 4
                && spellLabel.OffsetRight == -8 && spellLabel.OffsetBottom == -4,
                $"spell label 2x offsets {spellLabel.OffsetLeft},{spellLabel.OffsetTop},{spellLabel.OffsetRight},{spellLabel.OffsetBottom} != (8,4,-8,-4)");
            Assert(spellTt.Position.Y + spellTt.Size.Y <= canvas.Y, $"spell tooltip y-clamp {spellTt.Position.Y}+{spellTt.Size.Y} > {canvas.Y}");
            tm.HideSpellTooltip();
            await Frame();
            GD.Print($"[ui_scale_selftest] OK 2x audit: theme=20, {fontChecked} font overrides, spell tooltip {expectedSpell}");

            // Step 2b: runtime spawn at 2x. WindowSettings is a class mutated in place — the
            // record is deep-copied into locals, and restore runs in finally before anything that can throw.
            var cs = CharacterSettings;
            bool bHad = false; Vector2 bPos = default; bool bVis = false; Vector2I bCanvas = default; Vector2 bSize = default; float bFactor = 0f; bool bPlaced = false;
            {
                var ws = cs.GetWindowSettings("Bank");
                bHad = ws != null;
                if (ws != null)
                { bPos = ws.Position; bVis = ws.Visible; bCanvas = ws.CanvasSize; bSize = ws.Size; bFactor = ws.Factor; bPlaced = ws.Placed; }
            }
            BankWindow bank = null;
            try
            {
                cs.WindowSettings.Remove("Bank");
                cs.Save();
                bank = GD.Load<PackedScene>("res://Scenes/UI/BankWindow.tscn").Instantiate<BankWindow>();
                UiLayer.AddChild(bank);
                await Frame();
                var grid = bank.GetNode<GridContainer>("Content/SlotGrid");
                Assert(grid.OffsetLeft == 4 && grid.OffsetTop == 40 && grid.OffsetRight == 332 && grid.OffsetBottom == 434,
                    $"bank slot grid 2x offsets {grid.OffsetLeft},{grid.OffsetTop},{grid.OffsetRight},{grid.OffsetBottom} != (4,40,332,434)");
            }
            finally
            {
                if (bHad)
                {
                    var r = cs.WindowSettings.TryGetValue("Bank", out var cur) ? cur : cs.WindowSettings["Bank"] = new WindowSettings();
                    r.Position = bPos; r.Visible = bVis; r.CanvasSize = bCanvas; r.Size = bSize; r.Factor = bFactor; r.Placed = bPlaced;
                }
                else
                {
                    cs.WindowSettings.Remove("Bank");
                }
                cs.Save();
                if (bank != null)
                    bank.QueueFree();
            }
            if (bank != null)
            {
                await Frame();
                Assert(!ContainsRoot(bank), "bank registration not pruned after free");
            }
            GD.Print("[ui_scale_selftest] OK 2b runtime spawn at 2x");

            // Step 2c: factor-aware multi-window lines; the trailing 1x apply also proves float
            // identity at 1x (no rounding).
            var info = GD.Load<PackedScene>("res://Scenes/UI/InfoWindow.tscn").Instantiate<InfoWindow>();
            UiLayer.AddChild(info);
            await Frame();
            info.OnMakeWindow(new MakeWindowPacket
            {
                WindowId = 1001,
                WindowFrame = (WindowFrames)13,
                Title = "Welcome to my shop!",
                Buttons = new[] { false, true, false, false, false },
                NpcId = 1201,
                Unknown1 = 0,
                Unknown2 = 0,
            });
            info.OnWindowLine(new WindowLinePacket { WindowId = 1001, LineNumber = 0, Text = "line 0" });
            await Frame();
            var l0 = info.GetNode<Label>("Content/Line0");
            var l19 = info.GetNode<Label>("Content/Line19");
            Assert(l0.Position == MultiWindowMetrics.LinePosition(0, 2f), $"line 0 at 2x {l0.Position} != {MultiWindowMetrics.LinePosition(0, 2f)}");
            Assert(l19.Position == MultiWindowMetrics.LinePosition(19, 2f), $"line 19 at 2x {l19.Position} != {MultiWindowMetrics.LinePosition(19, 2f)}");
            var l0Font = l0.GetThemeFontSize("font_size");
            Assert(l0Font == 20, $"line 0 font {l0Font} != 20");
            applier.Apply(1f, ApplyReason.UserCommit);
            await Frame();
            Assert(l0.Position == new Vector2(6, 22), $"line 0 at 1x {l0.Position} != (6, 22)");
            Assert(l19.Position == new Vector2(6, 22 + 19 * 11.18f), $"line 19 at 1x {l19.Position} != (6, {22 + 19 * 11.18f})");
            info.QueueFree();
            await Frame();
            Assert(!ContainsRoot(info), "info registration not pruned after free");
            GD.Print("[ui_scale_selftest] OK 2c multi-window lines (2x + 1x identity)");

            // Step 2d: a saved (0,0) origin must survive; the persisted file is verified via a
            // throwaway instance because re-LoadSettings would swap the live object out from under the rest.
            var inv = Hud.Inventory;
            bool iHad = false; Vector2 iPos = default; bool iVis = false; Vector2I iCanvas = default; Vector2 iSize = default; float iFactor = 0f; bool iPlaced = false;
            {
                var ws = cs.GetWindowSettings("Inventory");
                iHad = ws != null;
                if (ws != null)
                { iPos = ws.Position; iVis = ws.Visible; iCanvas = ws.CanvasSize; iSize = ws.Size; iFactor = ws.Factor; iPlaced = ws.Placed; }
            }
            var invPos1 = inv.Position;
            try
            {
                cs.SetWindowSetting("Inventory", new Vector2(0, 0), inv.Size, 1f, null, canvas);
                inv.RepositionFromSaved();
                await Frame();
                Assert(inv.Position == new Vector2(0, 0), $"inventory saved origin {inv.Position} != (0, 0)");
            }
            finally
            {
                if (iHad)
                {
                    var r = cs.WindowSettings.TryGetValue("Inventory", out var cur) ? cur : cs.WindowSettings["Inventory"] = new WindowSettings();
                    r.Position = iPos; r.Visible = iVis; r.CanvasSize = iCanvas; r.Size = iSize; r.Factor = iFactor; r.Placed = iPlaced;
                }
                else
                {
                    cs.WindowSettings.Remove("Inventory");
                }
                cs.Save();
                inv.RepositionFromSaved();
                await Frame();
            }
            var verify = new CharacterSettings("ui-scale-selftest");
            var vr = verify.GetWindowSettings("Inventory");
            if (iHad)
                Assert(vr != null && vr.Position == iPos && vr.Visible == iVis && vr.CanvasSize == iCanvas
                    && vr.Size == iSize && vr.Factor == iFactor && vr.Placed == iPlaced,
                    "inventory record not restored on disk");
            else
                Assert(vr == null, "inventory record should be absent on disk");
            Assert(inv.Position == invPos1, $"inventory live pos {inv.Position} != {invPos1}");
            GD.Print("[ui_scale_selftest] OK 2d saved-origin round trip");

            // Step 2e: a visibility-only record must not demote a dialog to its default layout.
            bool eHad = false; Vector2 ePos = default; bool eVis = false; Vector2I eCanvas = default; Vector2 eSize = default; float eFactor = 0f; bool ePlaced = false;
            {
                var ws = cs.GetWindowSettings("Bank");
                eHad = ws != null;
                if (ws != null)
                { ePos = ws.Position; eVis = ws.Visible; eCanvas = ws.CanvasSize; eSize = ws.Size; eFactor = ws.Factor; ePlaced = ws.Placed; }
            }
            BankWindow dlg = null;
            try
            {
                cs.WindowSettings.Remove("Bank");
                cs.Save();
                dlg = GD.Load<PackedScene>("res://Scenes/UI/BankWindow.tscn").Instantiate<BankWindow>();
                UiLayer.AddChild(dlg);
                await Frame();
                Assert(dlg.Position == WindowPlacement.Center(canvas, dlg.Size),
                    $"bank no-record centering {dlg.Position} != {WindowPlacement.Center(canvas, dlg.Size)}");
                cs.SetWindowVisible("Bank", false);
                dlg.RepositionFromSaved();
                await Frame();
                Assert(dlg.Position == WindowPlacement.Center(canvas, dlg.Size),
                    $"bank visibility-only centering {dlg.Position} != {WindowPlacement.Center(canvas, dlg.Size)}");
            }
            finally
            {
                if (eHad)
                {
                    var r = cs.WindowSettings.TryGetValue("Bank", out var cur) ? cur : cs.WindowSettings["Bank"] = new WindowSettings();
                    r.Position = ePos; r.Visible = eVis; r.CanvasSize = eCanvas; r.Size = eSize; r.Factor = eFactor; r.Placed = ePlaced;
                }
                else
                {
                    cs.WindowSettings.Remove("Bank");
                }
                cs.Save();
                if (dlg != null)
                    dlg.QueueFree();
            }
            if (dlg != null)
            {
                await Frame();
                Assert(!ContainsRoot(dlg), "dialog registration not pruned after free");
            }
            GD.Print("[ui_scale_selftest] OK 2e visibility-only record keeps dialog centering");

            // Step 3: back to 1x — the apply is the early-return probe; geometry and positions
            // must be bit-identical to the step-1 baseline.
            applier.Apply(1f, ApplyReason.UserCommit);
            await Frame();
            var geo3 = new Dictionary<Control, Vector4>();
            var auth3 = new Dictionary<Control, bool>();
            foreach (var r in applier.RegisteredWindows)
                Walk(r.ControlRef, geo3, auth3);
            Assert(geo3.Count == geo1.Count, $"restore: control count {geo3.Count} != {geo1.Count}");
            foreach (var kv in geo1)
                Assert(geo3.TryGetValue(kv.Key, out var v) && v == kv.Value,
                    $"restore: {kv.Key.GetPath()} {kv.Value} != {v}");
            foreach (var kv in pos1)
                Assert(kv.Key.Position == kv.Value,
                    $"restore: {kv.Key.WindowName} pos {kv.Key.Position} != {kv.Value}");
            var tile1 = (PartyMember)Hud.Party.GetNode<VBoxContainer>("MemberList").GetChild(0);
            Assert(tile1.CustomMinimumSize == new Vector2(87, 33), $"party tile min {tile1.CustomMinimumSize} != (87, 33)");
            var sep1 = Hud.Party.GetNode<VBoxContainer>("MemberList").GetThemeConstant("separation");
            Assert(sep1 == 1, $"party separation {sep1} != 1");
            tm.ShowSpellTooltip(new SpellInfo { Name = "Selftest" }, Hud);
            await Frame();
            await Frame();
            var labelMin1 = spellLabel.GetCombinedMinimumSize();
            Assert(spellLabel.OffsetLeft == 4 && spellLabel.OffsetTop == 2
                && spellLabel.OffsetRight == -4 && spellLabel.OffsetBottom == -2,
                $"spell label 1x offsets {spellLabel.OffsetLeft},{spellLabel.OffsetTop},{spellLabel.OffsetRight},{spellLabel.OffsetBottom} != (4,2,-4,-2)");
            Assert(spellTt.Size == labelMin1 + new Vector2(8, 4), $"spell tooltip 1x size {spellTt.Size} != {labelMin1} + (8,4)");
            tm.HideSpellTooltip();
            GD.Print("[ui_scale_selftest] OK 1x restore (idempotence: geometry + positions)");
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
