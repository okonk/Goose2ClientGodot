using Godot;

namespace Goose2Client
{
    /// <summary>
    /// Owns the world sub-viewport and its display <see cref="TextureRect"/> in the root window.
    /// The root window runs at native resolution (project stretch disabled); the world scene is
    /// rendered into a capped sub-viewport and blitted through <see cref="WorldTexture"/> at the
    /// layout's integer scale. Gutters around the display rect show the root background (black).
    /// </summary>
    public partial class WorldViewport : Node
    {
        /// <summary>
        /// Displays the attached sub-viewport texture. Free placement (no anchors);
        /// Position/Size track the layout's display rect so the TextureRect's default
        /// stretch fills exactly its (integer-sized) rect at on-screen scale == layout.Scale.
        /// Never intercepts the mouse.
        /// </summary>
        public TextureRect WorldTexture { get; } = new();

        /// <summary>The attached map scene, or null before the first map.</summary>
        public SubViewport Current { get; private set; }

        /// <summary>Current layout, from the last <see cref="ApplyMode"/> with a map attached.</summary>
        public WorldViewportLayout Layout { get; private set; }

        /// <summary>Stored even with no map attached (applied on next <see cref="Attach"/>).</summary>
        public WorldRenderMode Mode { get; private set; } = WorldRenderMode.Integer2x;

        // One-shot first-frame presentation deferred from <see cref="Attach"/> (null = none pending).
        private System.Action _pendingPresent;

        public override void _Ready()
        {
            WorldTexture.Name = "WorldTexture";
            WorldTexture.MouseFilter = Control.MouseFilterEnum.Ignore;
            // Explicit Nearest (do not rely on the project default): the sub-viewport is
            // upscaled here at layout.Scale, and bilinear would soften the whole world.
            WorldTexture.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
            AddChild(WorldTexture);
            GetWindow().SizeChanged += OnWindowResized;
        }

        public override void _ExitTree()
        {
            if (GetWindow() != null)
                GetWindow().SizeChanged -= OnWindowResized;
            // Detach an un-fired first-frame presentation: it is connected to the
            // RenderingServer singleton (which outlives us), so without this the delegate
            // would keep this node and its map referenced until the next render pass.
            if (_pendingPresent != null)
                RenderingServer.FramePostDraw -= _pendingPresent;
            _pendingPresent = null;
        }

        /// <summary>
        /// Attaches a map scene as the current sub-viewport and applies the mode from settings
        /// (single mode-application point: the map scene itself never applies a mode). The
        /// display texture is presented only after the new map's first render pass completes
        /// (the next <see cref="RenderingServer.SignalName.FramePostDraw"/> after attach — this
        /// engine's successor to the old Viewport 'rendered' signal), not now: a freshly
        /// created SubViewport's ViewportTexture contains undefined/stale GPU memory until its
        /// first render pass (blitting it immediately flashes garbage for one frame), and until
        /// that swap <see cref="WorldTexture"/> still shows the previously displayed map (no
        /// black flash). The caller (GameManager.ChangeMap) must therefore free the previous
        /// world only after the same frame-post-draw signal fires.
        /// </summary>
        public void Attach(SubViewport mapScene)
        {
            Current = mapScene;
            // Force the sub-viewport to render its first frame even though its texture is not
            // displayed yet: the default UpdateMode (WhenVisible) would skip rendering while
            // WorldTexture still shows the previous map — exactly the frame we wait for —
            // and the deferred presentation would never fire. Restored to WhenVisible once the
            // texture is presented (it is visible on WorldTexture then, so rendering resumes).
            mapScene.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
            AddChild(mapScene);
            // Size the sub-viewport BEFORE its first render so the first frame is at the
            // right size (no resize pop on frame 2).
            RefreshFromSettings();

            // A second transition can start before this map's first render pass: detach the
            // superseded handler (it is connected to the RenderingServer singleton, not to
            // the map, so it must be dropped explicitly — otherwise it would fire on the next
            // frame and clear the NEW handler's state). The guard below is a second line of
            // defense: a superseded or freed map can never be re-presented over the newer one.
            if (_pendingPresent != null)
                RenderingServer.FramePostDraw -= _pendingPresent;

            System.Action present = () =>
            {
                // One-shot: detach from the singleton first so the delegate (and this node
                // and the map it captures) are not kept alive by the connection. A handler
                // can never run after being superseded (Attach detaches it explicitly), so
                // the field still references it when it fires.
                if (_pendingPresent != null)
                    RenderingServer.FramePostDraw -= _pendingPresent;
                _pendingPresent = null;
                // Stale-handler guard: if a newer map was attached (Current changed) or this
                // map was freed before its first render pass, do not present it.
                if (Current != mapScene || !GodotObject.IsInstanceValid(mapScene)) return;
                WorldTexture.Texture = mapScene.GetTexture();
                mapScene.RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible;   // restore default steady state
            };
            _pendingPresent = present;
            RenderingServer.FramePostDraw += present;
        }

        /// <summary>
        /// Stores the mode and, if a map is attached, recomputes the layout from the current
        /// root window size and applies it to the sub-viewport and display rect. Sole mutator of
        /// <see cref="Current"/>.Size / <see cref="Layout"/> / the display rect. No-op (mode
        /// stored) when no map is attached or the root size is not usable yet.
        /// </summary>
        public void ApplyMode(WorldRenderMode mode)
        {
            Mode = mode;
            if (Current == null)
                return;

            var rootSize = (Vector2I)GetTree().Root.GetVisibleRect().Size;
            if (rootSize.X < 2 || rootSize.Y < 2)
                return;

            Layout = WorldViewportScale.Compute(mode, rootSize);
            Current.Size = Layout.SubViewportSize;
            WorldTexture.Position = Layout.DisplayOrigin;
            WorldTexture.Size = Layout.DisplaySize;
        }

        /// <summary>
        /// Applies the render mode from character settings (null-safe: pre-login → Integer2x,
        /// the node default). Used only by <see cref="Attach"/>.
        /// </summary>
        public void RefreshFromSettings()
        {
            bool native = GameManager.Instance?.CharacterSettings != null
                && GameManager.Instance.CharacterSettings.GetOption<bool>(Options.RenderMode, false) == true;
            ApplyMode(native ? WorldRenderMode.Native1x : WorldRenderMode.Integer2x);
        }

        /// <summary>
        /// Maps a root-window position into world (map) pixels — the space
        /// <c>MapCoords.WorldToTile</c> consumes. Preconditions: <see cref="Current"/> attached
        /// with an active camera; <see cref="Layout"/> current.
        /// </summary>
        public Vector2 WindowToWorld(Vector2 windowPos)
        {
            var vp = (windowPos - Layout.DisplayOrigin) / (float)Layout.Scale;
            // GetCanvasTransform() maps world→viewport; the affine inverse is required to go
            // back (using it forward would displace clicks by ~2x the camera offset).
            return Current.GetCanvasTransform().AffineInverse() * vp;
        }

        /// <summary>
        /// Window mouse clicks → world clicks. Sub-viewport nodes never receive window input
        /// (handle_input_locally=false), so the map's own _UnhandledInput is dead; convert
        /// explicitly at the root and dispatch to the MapManager. mb.Position is root-window
        /// coordinates (root viewport is 1:1 with the window). The display-rect gate is
        /// mandatory, not cosmetic: with the camera centered inside a large map, a click 1px
        /// into a gutter converts to a valid tile at the camera-view edge and would otherwise
        /// be sent to the server. HUD windows are Controls and consume their own clicks before
        /// unhandled input reaches this node.
        /// </summary>
        public override void _UnhandledInput(InputEvent e)
        {
            if (e is not InputEventMouseButton mb || !mb.Pressed) return;
            if (mb.ButtonIndex != MouseButton.Left && mb.ButtonIndex != MouseButton.Right) return;
            if (Current == null) return;
            if (!WorldViewportScale.IsInsideDisplay(Layout, (Vector2I)mb.Position)) return;   // gutters are not the world
            var mm = GameManager.Instance.CurrentMapManager;
            if (mm == null || !GodotObject.IsInstanceValid(mm)) return;
            mm.HandleWorldClick(mb.ButtonIndex, WindowToWorld(mb.Position));
        }

        private void OnWindowResized()
        {
            var rootSize = (Vector2I)GetTree().Root.GetVisibleRect().Size;
            if (rootSize.X < 2 || rootSize.Y < 2)
                return; // ignore while the root has no usable size yet
            ApplyMode(Mode);
        }
    }
}
