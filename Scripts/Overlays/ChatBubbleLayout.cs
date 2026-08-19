using Godot;

namespace Goose2Client.Overlays
{
    /// <summary>Pure logic for chat bubble layout: max width, padding, and lifetime constants.</summary>
    public static class ChatBubbleLayout
    {
        public const float MaxWidth = 250f;
        public static readonly Vector2 Padding = new Vector2(7f, 5f);
        public const double LifetimeSeconds = 3.0;

        /// <summary>Extra pixels between the top of the nameplate and the bottom of the bubble.
        /// Tuned 4px tighter than the reference client's gap for this project's nameplate size.</summary>
        public const float VerticalGap = 4f;

        /// <summary>Clamp text width to MaxWidth.</summary>
        public static float ClampWidth(float textWidth) => Mathf.Min(textWidth, MaxWidth);

        /// <summary>Compute the background Control size from measured text size (text + padding
        /// on every side, so the label is inset by exactly <see cref="Padding"/>).</summary>
        public static Vector2 BackgroundSize(Vector2 textSize) => textSize + Padding * 2;
    }
}
