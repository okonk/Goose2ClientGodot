using Godot;

namespace Goose2Client
{
    /// <summary>CanvasLayer for world-anchored text (names, chat bubbles, battle text) at native resolution; projection
    /// runs in the bridge's own _Process at priority 100 (not process_frame — probed: emitted before node _process), so after every world node.</summary>
    public partial class WorldTextBridge : CanvasLayer
    {
        /// Must exceed every world node's process priority (all default 0 today); lower priority runs first (text_bridge_order.gd).
        private const int ProjectionProcessPriority = 100;

        /// Current display scale — the single scale source for elements (T7: never read Layout.Scale here).
        /// Named DisplayScale: `Scale` would shadow Node.Scale (CS0108).
        public float DisplayScale { get; private set; } = 1f;

        private WorldViewport _worldViewport;

        public override void _EnterTree() => ProcessPriority = ProjectionProcessPriority;   // before the first _Process

        public void Attach(WorldViewport worldViewport)
        {
            _worldViewport = worldViewport;
            _worldViewport.ScaleChanged += OnScaleChanged;
        }

        /// ApplyScale runs before AddChild: font metrics are correct off-tree, but GetMinimumSize()
        /// is stale same-frame after a font-size change (probed).
        public void Register<T>(T element, Character.Character owner) where T : CanvasItem, IBridgedText
        {
            element.AnchorOwner = owner;
            element.ApplyScale(DisplayScale);
            element.Visible = false;   // no (0,0) flash before the first projection
            AddChild(element);
        }

        public override void _Process(double delta) => UpdateProjection();

        public override void _ExitTree()
        {
            if (_worldViewport != null) _worldViewport.ScaleChanged -= OnScaleChanged;
        }

        private void OnScaleChanged(float s)
        {
            DisplayScale = s;
            for (int i = 0; i < GetChildCount(); i++)
                if (GetChild(i) is IBridgedText e) e.ApplyScale(s);
        }

        private void UpdateProjection()
        {
            // Current is null only pre-first-map, when the bridge is empty (Attach never clears it) — no state to reset.
            if (_worldViewport == null || _worldViewport.Current == null) return;
            // T7: DisplayScale is the single scale source (ScaleChanged keeps it in lockstep with
            // Layout.Scale) — never read Layout.Scale here; only the display rect geometry comes from Layout.
            float scale = DisplayScale;
            var o = _worldViewport.Layout.DisplayOrigin;
            var s = _worldViewport.Layout.DisplaySize;
            // No Rect2(Vector2I, …) ctor in GodotSharp — construct from component casts.
            var display = new Rect2(new Vector2((float)o.X, (float)o.Y), new Vector2((float)s.X, (float)s.Y));
            for (int i = GetChildCount() - 1; i >= 0; i--)   // backwards: pass may QueueFree
            {
                var child = GetChild(i);
                if (child is not CanvasItem item || child is not IBridgedText element) continue;
                if (element.AnchorOwner == null || !GodotObject.IsInstanceValid(element.AnchorOwner))
                {
                    item.QueueFree();
                    continue;
                }
                // Post-transition overlap: for ~2 frames the NEW map is Current while OLD-map characters
                // are still alive (queued free) — don't project them through the new map's canvas transform.
                if (element.AnchorOwner.GetViewport() != _worldViewport.Current) { item.Visible = false; continue; }
                var pos = _worldViewport.WorldToWindow(element.AnchorOwner.GlobalPosition)   // calls the shared forward transform (lockstep with WindowToWorld)
                    + element.LocalOffsetWorld * scale;
                // No Position on CanvasItem — branch on the concrete base (elements are always one or the other):
                if (item is Node2D n) n.Position = pos;
                else if (item is Control c) c.Position = pos;
                item.Visible = !WorldTextProjection.IsCulled(
                    new Rect2(pos + element.ScreenBounds.Position, element.ScreenBounds.Size), display);
            }
        }
    }
}
