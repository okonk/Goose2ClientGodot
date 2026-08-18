namespace Goose2Client
{
    /// <summary>
    /// Maps <see cref="MakeWindowPacket.Buttons"/> (bool[5]) onto the Goose2
    /// <see cref="WindowButtons"/> enum.
    ///
    /// Wire order is Combine, Close, Back, Next, OK (same as the Aspereta client).
    /// Goose2's enum inserts <see cref="WindowButtons.Exit"/> at 0, so the packet
    /// index for a button is <c>(int)button - 1</c>.
    /// </summary>
    public static class WindowButtonFlags
    {
        /// <summary>
        /// Returns whether the server enabled <paramref name="button"/> in this
        /// MakeWindow's Buttons array. Null/short arrays yield false.
        /// </summary>
        public static bool IsEnabled(bool[]? buttons, WindowButtons button)
        {
            if (buttons == null) return false;

            // Exit has no packet slot; Combine..OK map to indices 0..4.
            int index = (int)button - 1;
            if (index < 0 || index >= buttons.Length) return false;

            return buttons[index];
        }
    }
}
