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

        public override void _Ready()
        {
            WorldTexture.Name = "WorldTexture";
            WorldTexture.MouseFilter = Control.MouseFilterEnum.Ignore;
            AddChild(WorldTexture);
            GetWindow().SizeChanged += OnWindowResized;
        }

        public override void _ExitTree()
        {
            if (GetWindow() != null)
                GetWindow().SizeChanged -= OnWindowResized;
        }

        /// <summary>
        /// Attaches a map scene as the current sub-viewport and applies the mode from settings.
        /// Single mode-application point: the map scene itself never applies a mode. The texture
        /// is assigned before any previous scene is freed (entry-sequence call order guarantees
        /// this), so the display never shows a freed texture.
        /// </summary>
        public void Attach(SubViewport mapScene)
        {
            Current = mapScene;
            AddChild(mapScene);
            WorldTexture.Texture = mapScene.GetTexture();
            RefreshFromSettings();
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

        private void OnWindowResized()
        {
            var rootSize = (Vector2I)GetTree().Root.GetVisibleRect().Size;
            if (rootSize.X < 2 || rootSize.Y < 2)
                return; // ignore while the root has no usable size yet
            ApplyMode(Mode);
        }
    }
}
