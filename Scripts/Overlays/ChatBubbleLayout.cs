using Godot;

namespace Goose2Client.Overlays
{
    /// <summary>Pure logic for chat bubble layout: max width, padding, and lifetime constants.</summary>
    public static class ChatBubbleLayout
    {
        public const float MaxWidth = 250f;
        public static readonly Vector2 Padding = new Vector2(7f, 5f);
        public const double LifetimeSeconds = 3.0;

        /// <summary>Clamp text width to MaxWidth.</summary>
        public static float ClampWidth(float textWidth) => Mathf.Min(textWidth, MaxWidth);

        /// <summary>Compute the background Control size from measured text size (textSize + padding).</summary>
        public static Vector2 BackgroundSize(Vector2 textSize) => textSize + Padding;
    }
}
