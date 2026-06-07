using Godot;
namespace Goose2Client.Overlays
{
    /// <summary>Base for a transient overlay parented to a Character/tile. Self-frees when its
    /// OverlayLifetime expires. Subclasses set Lifetime in _Ready and override Tick for visuals.</summary>
    public partial class WorldOverlay : Node2D
    {
        protected OverlayLifetime Lifetime;
        public override void _Process(double delta)
        {
            if (Lifetime == null) return;
            Lifetime.Advance(delta);
            Tick(delta);
            if (Lifetime.Expired) QueueFree();
        }
        protected virtual void Tick(double delta) { }
    }
}
