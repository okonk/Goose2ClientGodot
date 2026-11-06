using Godot;

namespace Goose2Client
{
    /// <summary>CanvasLayer for world-anchored text (names, chat bubbles, battle text) at native resolution; projection
    /// runs in the bridge's own _Process at priority 100 (not process_frame — probed: emitted before node _process), so after every world node.</summary>
    public partial class WorldTextBridge : CanvasLayer
    {
        /// Must exceed every world node's process priority (Character 0, MapManager's camera follow 50); lower priority runs first (text_bridge_order.gd).
        private const int ProjectionProcessPriority = 100;

        /// Text scale — the geometric mean of the UI factor and the world render scale, so labels
        /// stay proportioned between the UI and the characters when the two scales diverge.
        /// Named DisplayScale: `Scale` would shadow Node.Scale (CS0108).
        public float DisplayScale { get; private set; } = 1f;

        /// World render scale (world units → screen px): anchors' LocalOffsetWorld conversion only.
        public float WorldScale => _worldScale;

        private WorldViewport? _worldViewport;
        private float _uiFactor = 1f;
        private float _worldScale = 1f;

        // Mirrors the bridge-owned Position/Visible so unchanged values are never written back to the engine.
        private sealed class Entry
        {
            public CanvasItem Item = null!;
            public IBridgedText Element = null!;
            public Vector2 Position;
            public bool Visible;
        }
        private readonly System.Collections.Generic.List<Entry> _entries = new();

        public override void _EnterTree() => ProcessPriority = ProjectionProcessPriority;   // before the first _Process

        public void Attach(WorldViewport worldViewport)
        {
            _worldViewport = worldViewport;
            _worldViewport.ScaleChanged += OnWorldScaleChanged;
            if (UiScaleApplier.Instance != null)
                UiScaleApplier.Instance.FactorChanged += OnUiFactorChanged;
            // Seed from live state: the viewport may have applied its first layout (and fired
            // ScaleChanged) before this Attach, and the UI factor was applied at startup.
            OnWorldScaleChanged(_worldViewport.Layout.Scale);
            OnUiFactorChanged(UiScaleApplier.Instance?.Factor ?? 1f);
        }

        /// ApplyScale runs before AddChild: font metrics are correct off-tree, but GetMinimumSize()
        /// is stale same-frame after a font-size change (probed).
        public void Register<T>(T element, Character.Character owner) where T : CanvasItem, IBridgedText
        {
            element.AnchorOwner = owner;
            element.ApplyScale(DisplayScale, _worldScale);
            element.Visible = false;   // no (0,0) flash before the first projection
            AddChild(element);
            _entries.Add(new Entry { Item = element, Element = element, Position = element switch
            {
                Node2D n => n.Position,
                Control c => c.Position,
                _ => Vector2.Zero,
            } });
        }

        public override void _Process(double delta) => UpdateProjection();

        /// <summary>Hides the bridge and drops its elements. UpdateProjection early-returns while
        /// no world is attached, so without this the last frame's names/bubbles linger over the
        /// login screen. ChangeMap re-shows the bridge; characters re-register their elements.</summary>
        public void Clear()
        {
            Visible = false;
            for (int i = GetChildCount() - 1; i >= 0; i--)
                GetChild(i).QueueFree();
            _entries.Clear();
        }

        public override void _ExitTree()
        {
            if (_worldViewport != null) _worldViewport.ScaleChanged -= OnWorldScaleChanged;
            if (UiScaleApplier.Instance != null)
                UiScaleApplier.Instance.FactorChanged -= OnUiFactorChanged;
        }

        private void OnWorldScaleChanged(float s)
        {
            // 0 is the pre-first-map default Layout, not a real scale — a 0 would pin DisplayScale to 0.
            if (s <= 0f) return;
            _worldScale = s;
            DisplayScale = Mathf.Sqrt(_uiFactor * _worldScale);
            RescaleChildren();   // text scale and world-unit centering offsets both depend on it
        }

        private void OnUiFactorChanged(float s)
        {
            _uiFactor = s;
            DisplayScale = Mathf.Sqrt(_uiFactor * _worldScale);
            RescaleChildren();
        }

        private void RescaleChildren()
        {
            for (int i = 0; i < GetChildCount(); i++)
                if (GetChild(i) is IBridgedText e) e.ApplyScale(DisplayScale, _worldScale);
        }

        private void UpdateProjection()
        {
            // Current is null only pre-first-map, when the bridge is empty (Attach never clears it) — no state to reset.
            if (_worldViewport == null || _worldViewport.Current == null) return;
            // Offsets are world units, so their screen conversion uses the world scale —
            // DisplayScale (UI factor) sizes the text but would misplace the anchor otherwise.
            var o = _worldViewport.Layout.DisplayOrigin;
            var s = _worldViewport.Layout.DisplaySize;
            // No Rect2(Vector2I, …) ctor in GodotSharp — construct from component casts.
            var display = new Rect2(new Vector2((float)o.X, (float)o.Y), new Vector2((float)s.X, (float)s.Y));
            var current = _worldViewport.Current;
            for (int i = _entries.Count - 1; i >= 0; i--)   // backwards: pass may remove entries
            {
                var entry = _entries[i];
                var item = entry.Item;
                var element = entry.Element;
                if (!GodotObject.IsInstanceValid(item))
                {
                    _entries.RemoveAt(i);
                    continue;
                }
                if (element.AnchorOwner == null || !GodotObject.IsInstanceValid(element.AnchorOwner))
                {
                    item.QueueFree();
                    _entries.RemoveAt(i);
                    continue;
                }
                // Managed-only checks run before the engine calls below.
                if (element.AnchorOwner.IsHiddenFromViewer
                    // Roof band must draw above overhead text: the bridge's CanvasLayer can't be z-sorted
                    // under it, so hide the element when a visible roof tile covers the anchor's tile.
                    || element.AnchorOwner.IsRoofOccluded
                    || (element is Goose2Client.Overlays.BridgedNameLabel && !element.AnchorOwner.ShouldShowNameOverhead)
                    || (element is Goose2Client.Overlays.BridgedVitalsBars && !element.AnchorOwner.ShouldShowVitalsOverhead)
                    // Post-transition overlap: for ~2 frames the NEW map is Current while OLD-map characters
                    // are still alive (queued free) — don't project them through the new map's canvas transform.
                    || element.AnchorOwner.GetViewport() != current)
                {
                    SetVisible(entry, false);
                    continue;
                }
                var pos = _worldViewport.WorldToWindow(element.AnchorOwner.GlobalPosition)   // calls the shared forward transform (lockstep with WindowToWorld)
                    + element.LocalOffsetWorld * _worldScale;
                // Whole window pixels: the anchor projects integrally (snapped character, snapped
                // camera, integer scale), but the centering offsets do not — BridgedNameLabel's
                // -w/2 lands on a half pixel for any odd label width, which rasterizes the glyphs
                // softly and asymmetrically. Rounding here covers every element's offset at once.
                pos = pos.Round();
                var bounds = element.ScreenBounds;
                bool visible = !WorldTextProjection.IsCulled(new Rect2(pos + bounds.Position, bounds.Size), display);
                if (visible && pos != entry.Position)
                {
                    entry.Position = pos;
                    // No Position on CanvasItem — branch on the concrete base (elements are always one or the other):
                    if (item is Node2D n) n.Position = pos;
                    else if (item is Control c) c.Position = pos;
                }
                SetVisible(entry, visible);
            }
        }

        private static void SetVisible(Entry entry, bool visible)
        {
            if (entry.Visible == visible) return;
            entry.Visible = visible;
            entry.Item.Visible = visible;
        }
    }
}
