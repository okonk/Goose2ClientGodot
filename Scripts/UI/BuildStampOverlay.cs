using Godot;

namespace Goose2Client.UI
{
    /// <summary>
    /// Always-on-top build identifier, drawn in the top-right corner of every screen.
    /// Owned by the GameManager autoload so it survives scene swaps.
    /// </summary>
    public partial class BuildStampOverlay : CanvasLayer
    {
        private const int Margin = 6;

        public override void _Ready()
        {
            Layer = 128;
            Name = "BuildStampOverlay";

            var label = new Label
            {
                Name = "BuildIdLabel",
                Text = BuildInfo.Id,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Right,
            };

            // Anchor to the top-right, inset by Margin. Wide enough for the longest id
            // form: <UTC>-<short-sha>-dirty.
            label.SetAnchorsPreset(Control.LayoutPreset.TopRight);
            label.GrowHorizontal = Control.GrowDirection.Begin;
            label.OffsetLeft = -320;
            label.OffsetRight = -Margin;
            label.OffsetTop = Margin;
            label.OffsetBottom = Margin + 20;

            label.Modulate = new Color(1, 1, 1, 0.45f);

            // Fixed by design — dev stamp must not scale; the font audit walks UiLayer only, this is outside it.
            label.AddThemeFontSizeOverride("font_size", 10);

            AddChild(label);
        }
    }
}
