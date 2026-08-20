using Godot;

namespace Goose2Client
{
    /// <summary>CanvasLayer hosting world-anchored text at native resolution (names, chat bubbles,
    /// battle text). Placed in the tree between WorldViewport and UiLayer (GameManager._Ready), so
    /// on the default CanvasLayer Layer=1: above WorldTexture (root-canvas Control) and, by tree
    /// order, below UiLayer (HUD). Owns element Position/Visible per frame and their lifetime.
    /// Projection runs in the bridge's own _Process at ProcessPriority 100 — NOT from
    /// SceneTree.process_frame (probed: emitted BEFORE node _process → would lag a frame).
    /// At equal priority the tree order alone would suffice (the world nodes live under the
    /// earlier-sibling WorldViewport, so they process first); priority 100 additionally covers
    /// future world nodes that raise their own priority. It runs after every world node in the
    /// same stage, before rendering (T2; contract pinned by tools/tests/text_bridge_order.gd).</summary>
    public partial class WorldTextBridge : CanvasLayer
    {
        /// <summary>Must exceed every world node's process priority (all use the default 0 today —
        /// Character, MapManager, WorldOverlay). Lower priority runs first; see text_bridge_order.gd.</summary>
        private const int ProjectionProcessPriority = 100;

        /// <summary>Current display scale (T7: the only place elements learn S). 1 before first map.
        /// Named DisplayScale (not Scale — would shadow the inherited Node.Scale with a CS0108
        /// warning, the same trap the overlay elements' DisplayScale fields avoid).</summary>
        public float DisplayScale { get; private set; } = 1f;

        private WorldViewport _worldViewport;

        public override void _EnterTree() => ProcessPriority = ProjectionProcessPriority;   // before the first _Process

        /// <summary>Wire to the owning WorldViewport. Precondition: worldViewport in tree.
        /// Postcondition: scale changes propagate to all registered elements.</summary>
        public void Attach(WorldViewport worldViewport)
        {
            _worldViewport = worldViewport;
            _worldViewport.ScaleChanged += OnScaleChanged;
        }

        /// <summary>Publish an element for projection. Precondition: element not in any tree, owner valid.
        /// Postcondition: element is a child, scaled at the current Scale, projected from the next frame.
        /// Teardown needs no unregistration: the per-frame pass frees children with dead owners.
        /// Constraint is CanvasItem (the common base of Node2D and Control) — name labels are
        /// Labels/Controls, bubbles/battle text are Node2Ds. (Position must be set through the
        /// Node2D/Control branch in the pass — CanvasItem has no Position.)
        /// ORDER: ApplyScale BEFORE AddChild — name labels measure via font metrics, which are
        /// correct off-tree, whereas GetMinimumSize() is stale same-frame after a font-size
        /// change (probed). Bubble ApplyScale is a no-op pre-SetText (no message yet); its real
        /// measurement runs in-tree from ShowChatBubble, which needs an in-tree label anyway.
        /// Visible starts FALSE: Register lands in a mid-frame deferred flush (packet handling),
        /// which is AFTER this frame's _process but BEFORE its render — the element would
        /// otherwise draw one frame at (0,0) (window top-left). The next frame's pass sets
        /// Visible from the projection, so first appearance is already at the correct spot.</summary>
        public void Register<T>(T element, Character.Character owner) where T : CanvasItem, IBridgedText
        {
            element.Owner = owner;
            element.ApplyScale(DisplayScale);
            element.Visible = false;   // no (0,0) flash before the first projection
            AddChild(element);
        }

        public override void _Process(double delta) => UpdateProjection();

        public override void _ExitTree()
        {
            if (_worldViewport != null) _worldViewport.ScaleChanged -= OnScaleChanged;   // same delegate instance Attach connected
        }

        private void OnScaleChanged(float s)
        {
            DisplayScale = s;
            for (int i = 0; i < GetChildCount(); i++)
                if (GetChild(i) is IBridgedText e) e.ApplyScale(s);
        }

        private void UpdateProjection()
        {
            // Current is null only pre-first-map, when the bridge is empty (Attach never clears it),
            // so a plain return is correct here — there is no state to reset.
            if (_worldViewport == null || _worldViewport.Current == null) return;
            // T7: DisplayScale is the bridge's single scale source (kept in lockstep with
            // Layout.Scale by the same ScaleChanged event that feeds it) — never read
            // Layout.Scale here. Only the display rect geometry comes from Layout.
            float scale = DisplayScale;
            var o = _worldViewport.Layout.DisplayOrigin;
            var s = _worldViewport.Layout.DisplaySize;
            // No Rect2(Vector2I, …) ctor in GodotSharp — construct from component casts.
            var display = new Rect2(new Vector2((float)o.X, (float)o.Y), new Vector2((float)s.X, (float)s.Y));
            for (int i = GetChildCount() - 1; i >= 0; i--)   // backwards: pass may QueueFree
            {
                var child = GetChild(i);
                if (child is not CanvasItem item || child is not IBridgedText element) continue;   // CanvasItem: uniform for Control + Node2D elements
                if (element.Owner == null || !GodotObject.IsInstanceValid(element.Owner))
                {
                    item.QueueFree();   // T4: owner gone (char removed / map change) → element dies
                    continue;
                }
                // ChangeMap overlap guard: for ~2 frames after a transition the NEW map is Current
                // while OLD-map characters are still alive (queued free pending) — don't project
                // them through the new map's canvas transform.
                if (element.Owner.GetViewport() != _worldViewport.Current) { item.Visible = false; continue; }
                var pos = _worldViewport.WorldToWindow(element.Owner.GlobalPosition)   // calls the shared forward transform (lockstep with WindowToWorld)
                    + element.LocalOffsetWorld * scale;
                // No Position on CanvasItem — branch on the concrete base (elements are always one or the other):
                if (item is Node2D n) n.Position = pos;
                else if (item is Control c) c.Position = pos;
                item.Visible = !WorldTextProjection.IsCulled(
                    new Rect2(pos + element.ScreenBounds.Position, element.ScreenBounds.Size), display);   // T3
            }
        }
    }
}
